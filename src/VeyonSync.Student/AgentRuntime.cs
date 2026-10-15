using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using VeyonSync.Core;

namespace VeyonSync.Student;

public record Enrollment(string TeacherUrl, string Lab, string Position, string CertificatePath, string Code, bool ConfirmReplacement);
public sealed class AgentRuntime
{
    private readonly string dir = Files.DataDir("Student");
    private readonly SemaphoreSlim gate = new(1, 1);
    private AgentConfig config;
    private AgentState state;
    private List<Event> events;
    private Collected? last;
    private bool first = true;
    private DateTimeOffset lastSent = DateTimeOffset.MinValue, retry = DateTimeOffset.MinValue;
    private int failures;
    public AgentRuntime()
    {
        Files.RestrictDirectory(dir);
        config = Files.Read<AgentConfig>(Path.Combine(dir, "config.json"));
        state = Files.Read<AgentState>(Path.Combine(dir, "state.json"));
        events = Files.Read<List<Event>>(Path.Combine(dir, "events.json"));
        var machine = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null)?.ToString()
            ?? throw new InvalidOperationException("Não foi possível identificar esta instalação do Windows.");
        if (config.MachineGuid != "" && config.MachineGuid != machine)
        {
            config.AgentId = Guid.NewGuid().ToString(); config.TokenProtected = null; state = new();
            AddEvent("Identidade", "Instalação do Windows diferente; novo pareamento necessário.");
        }
        config.MachineGuid = machine;
        // Migration from etapa1: establish a floor once, then persist a counter independent of wall-clock changes.
        if (state.Sequence == 0) state.Sequence = Math.Max(state.Pending?.Seq ?? 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        SaveConfig(); SaveState();
    }
    private void SaveConfig() => Files.Write(Path.Combine(dir, "config.json"), config);
    private void SaveState() => Files.Write(Path.Combine(dir, "state.json"), state);
    private void AddEvent(string kind, string detail)
    {
        var next = events.Count == 0 ? 1 : events.Max(e => e.Id) + 1;
        events.Insert(0, new(next, config.AgentId, DateTimeOffset.UtcNow, kind, detail));
        if (events.Count > 500) events.RemoveRange(500, events.Count - 500);
        Files.Write(Path.Combine(dir, "events.json"), events);
    }
    public async Task Tick(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var token = config.GetToken();
            if (token == null) { state.Status = "Aguardando pareamento"; SaveState(); return; }
            if (!config.AutoSend && !state.Force) { state.Status = "Modo manual — aguardando envio"; SaveState(); return; }
            var now = DateTimeOffset.UtcNow;
            if (now < retry && !state.Force) return;
            var current = Collector.Collect(config.TeacherUrl);
            bool changed = first || last?.Ip != current.Ip || last?.Mac != current.Mac || last?.User != current.User;
            first = false; last = current;
            if (!state.Force && !changed && state.Pending == null && now - lastSent < TimeSpan.FromSeconds(Math.Clamp(config.PresenceSeconds, 15, 3600))) return;
            state.Pending = new(config.AgentId, Environment.MachineName, config.Lab, config.Position, current.User, current.Ip, current.Mac,
                now, checked(++state.Sequence), "0.2.0", state.Force ? "manual" : changed ? "evento" : "presença");
            state.Current = state.Pending; state.Status = "Enviando"; SaveState();
            using var http = CertificateTrust.Client(config);
            using var message = new HttpRequestMessage(HttpMethod.Post, config.TeacherUrl.TrimEnd('/') + "/api/report") { Content = JsonContent.Create(state.Pending, options: Files.Json) };
            message.Headers.Authorization = new("Bearer", token); message.Headers.Add("X-Agent-Id", config.AgentId);
            using var response = await http.SendAsync(message, ct);
            if ((int)response.StatusCode == 401) throw new InvalidOperationException("Agente não autorizado. Verifique a revogação ou faça novo pareamento.");
            response.EnsureSuccessStatusCode(); var ack = await response.Content.ReadFromJsonAsync<ReportAck>(Files.Json, ct);
            if (ack is not { Received: true }) throw new IOException("Professor não confirmou o recebimento.");
            if (ack.Stale) throw new InvalidOperationException("Professor recusou a sequência antiga. Novo pareamento é necessário para recuperar a sequência.");
            if (state.LastError != null) AddEvent("Recuperação", "Comunicação com o professor restabelecida.");
            state.Status = "Confirmado pelo professor"; state.LastConfirmed = now; state.LastError = null;
            state.VeyonApplied = ack.VeyonApplied; state.VeyonAvailability = ack.VeyonAvailability;
            state.Pending = null; state.Force = ack.CollectRequested; failures = 0; retry = DateTimeOffset.MinValue; lastSent = now; SaveState();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var detail = e is HttpRequestException ? "Falha HTTPS: confira DNS, rede, certificado, nome do professor e horário do Windows. " + e.Message : e.Message;
            if (state.LastError != detail) AddEvent("Falha no envio", detail);
            state.Status = "Falhou"; state.LastError = detail; state.Force = false;
            retry = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(++failures, 6)))); SaveState();
        }
        finally { gate.Release(); }
    }
    public async Task<object> Action(string action, string? data, CancellationToken ct)
    {
        T Read<T>() => System.Text.Json.JsonSerializer.Deserialize<T>(data ?? "null", Files.Json) ?? throw new ArgumentException("Dados ausentes.");
        await gate.WaitAsync(ct);
        try
        {
            switch (action)
            {
                case "snapshot":
                    return new Snapshot("Student", config.Lab, Environment.MachineName, "Em execução", [], events.ToList(), state,
                    [new("Serviço",true,"Serviço respondeu pelo canal administrativo."),
                     new("Pareamento",config.TokenProtected!=null,config.TokenProtected==null?"Este aluno ainda precisa de autorização.":"Credencial individual armazenada com DPAPI e ACL restrita."),
                     new("Professor",!string.IsNullOrEmpty(config.TeacherUrl),config.TeacherUrl),
                     new("Envio automático",config.AutoSend,config.AutoSend?"Ativado":"Modo manual: solicitações do professor aguardam um envio manual.")]);
                case "settings": return PublicSettings();
                case "send": state.Force = true; retry = DateTimeOffset.MinValue; SaveState(); return "Envio solicitado. Aguarde a confirmação do professor.";
                case "save-settings":
                    {
                        var next = Read<AgentConfig>();
                        if (next.AgentId != config.AgentId || next.TeacherUrl != config.TeacherUrl || next.Lab != config.Lab || next.CertificateSha256 != config.CertificateSha256)
                            throw new ArgumentException("Identidade, laboratório e professor só podem mudar pelo assistente de pareamento.");
                        if (!Validation.Label(next.Position)) throw new ArgumentException("Posição: use letras, números, hífen ou sublinhado, até 32 caracteres.");
                        config.Position = next.Position; config.AutoSend = next.AutoSend; config.PresenceSeconds = Math.Clamp(next.PresenceSeconds, 15, 3600);
                        SaveConfig(); AddEvent("Configuração", "Preferências de envio atualizadas."); return "Configurações salvas.";
                    }
                case "diagnose":
                    {
                        if (string.IsNullOrWhiteSpace(config.TeacherUrl)) throw new InvalidOperationException("Configure o professor no assistente de pareamento.");
                        using var http = CertificateTrust.Client(config); using var response = await http.GetAsync(config.TeacherUrl.TrimEnd('/') + "/health", ct);
                        response.EnsureSuccessStatusCode(); var health = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Files.Json, ct);
                        if (health.GetProperty("role").GetString() != "Teacher" || !string.Equals(health.GetProperty("lab").GetString(), config.Lab, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("O destino não corresponde ao professor deste laboratório.");
                        return "DNS/rede, porta HTTPS e certificado verificados; professor respondeu com o laboratório correto. Isso não confirma aplicação no Veyon.";
                    }
                case "pair":
                    {
                        var enrollment = Read<Enrollment>();
                        if (!Validation.Label(enrollment.Lab) || !Validation.Label(enrollment.Position)) throw new ArgumentException("Laboratório ou posição inválidos.");
                        var uri = Validation.TeacherUri(enrollment.TeacherUrl);
                        if (config.TokenProtected != null && !enrollment.ConfirmReplacement) throw new InvalidOperationException("Confirme a substituição do pareamento existente.");
                        using var certificate = X509CertificateLoader.LoadCertificateFromFile(enrollment.CertificatePath);
                        if (certificate.HasPrivateKey) throw new ArgumentException("Selecione somente o certificado público do professor (.cer).");
                        var next = new AgentConfig
                        {
                            AgentId = config.AgentId,
                            MachineGuid = config.MachineGuid,
                            Lab = enrollment.Lab,
                            Position = enrollment.Position,
                            TeacherUrl = uri.GetLeftPart(UriPartial.Authority),
                            AutoSend = config.AutoSend,
                            CertificateSha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256)
                        };
                        using var http = CertificateTrust.Client(next);
                        using var response = await http.PostAsJsonAsync(next.TeacherUrl + "/api/pair", new PairRequest(enrollment.Code, next.AgentId, next.Lab, Environment.MachineName), Files.Json, ct);
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Pareamento recusado ({(int)response.StatusCode}). Confira laboratório, código de uso único e validade de 15 minutos.");
                        var paired = await response.Content.ReadFromJsonAsync<PairResponse>(Files.Json, ct) ?? throw new IOException("Resposta de pareamento vazia.");
                        if (!string.Equals(paired.Lab, next.Lab, StringComparison.OrdinalIgnoreCase)) throw new IOException("Laboratório divergente.");
                        next.SetToken(paired.Token); config = next; SaveConfig();
                        state = new AgentState { Sequence = state.Sequence, Status = "Pareado — aguardando primeiro envio", Force = config.AutoSend };
                        SaveState(); first = true; retry = DateTimeOffset.MinValue; AddEvent("Pareamento", "Autorização recebida do professor."); return "Pareamento concluído. O código foi consumido.";
                    }
                default: throw new ArgumentException("Ação desconhecida.");
            }
        }
        finally { gate.Release(); }
    }
    private AgentConfig PublicSettings() => new()
    {
        AgentId = config.AgentId,
        MachineGuid = config.MachineGuid,
        TeacherUrl = config.TeacherUrl,
        Lab = config.Lab,
        Position = config.Position,
        AutoSend = config.AutoSend,
        PresenceSeconds = config.PresenceSeconds,
        CertificateSha256 = config.CertificateSha256
    };
}
