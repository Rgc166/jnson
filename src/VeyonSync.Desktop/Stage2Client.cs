using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VeyonSync.Core;

namespace VeyonSync.Desktop;

/// <summary>Administrative companion to the existing etapa 2 service; never replaces its binaries or database.</summary>
public sealed class Stage2Client
{
    private readonly string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VeyonSync", "Teacher");
    private readonly string data = Files.DataDir("Teacher");
    private readonly string secretDirectory = Files.DataDir("THC");
    private Stage2Reader Reader => new(Path.Combine(app, "appsettings.json"), Path.Combine(data, "teacher.db"));
    private string Executable => Path.Combine(app, "VeyonSync.Teacher.exe");
    public async Task<T> Call<T>(string action, object? input = null, CancellationToken ct = default)
    {
        object? result = action switch
        {
            "snapshot" => await Task.Run(() => Reader.Read(ServiceStatus()), ct),
            "settings" => Reader.Settings(),
            "new-code" => await NewCode(ct),
            "collect" => await Command(["--request-collect"], ct),
            "export-link" => await Command(["--gerar-vsync", input as string ?? throw new ArgumentException("Escolha o destino do arquivo de ligação.")], ct),
            "connect-admin" => await Connect(input as string ?? "", false, ct),
            "generate-admin" => await Connect("", true, ct),
            "preview" => await Preview(input as string ?? "", ct),
            "apply" => await Apply((IntegrationPreview)input!, ct),
            "start-service" => await StartService(ct),
            _ => throw new InvalidOperationException("Esta operação não está disponível no complemento da etapa 2.")
        };
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(result, Files.Json), Files.Json)!;
    }
    private static string ServiceStatus()
    {
        try { using var service = new ServiceController("VeyonSyncTeacher"); return service.Status == ServiceControllerStatus.Running ? "Em execução" : "Parado: " + service.Status; }
        catch (InvalidOperationException) { return "Não instalado"; }
    }
    private async Task<string> Command(string[] args, CancellationToken ct)
    {
        if (!File.Exists(Executable)) throw new IOException("Executável da etapa 2 não encontrado. Este pacote instala somente a interface THC.");
        var info = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = app, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException("Não foi possível executar a ação do professor.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch { if (!p.HasExited) p.Kill(true); throw; }
        var text = await stdout; var error = await stderr;
        if (p.ExitCode != 0) throw new IOException("O professor recusou a operação. " + error[..Math.Min(error.Length, 500)]);
        return text.Trim();
    }
    private async Task<PairingCode> NewCode(CancellationToken ct)
    {
        var output = await Command(["--new-code"], ct);
        var match = Regex.Match(output, @"(?<!\d)\d{8}(?!\d)");
        if (!match.Success) throw new IOException("Não foi possível interpretar o código gerado pelo professor.");
        return new PairingCode(match.Value, DateTimeOffset.UtcNow.AddMinutes(15), Reader.Settings().Lab, Environment.MachineName);
    }
    private string SecretPath => Path.Combine(secretDirectory, "admin-key.json");
    private void SaveKey(string key)
    {
        Files.RestrictDirectory(secretDirectory);
        Files.Write(SecretPath, Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.LocalMachine)));
    }
    private string Key() => File.Exists(SecretPath)
        ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String((JsonSerializer.Deserialize<string>(File.ReadAllText(SecretPath), Files.Json) ?? throw new InvalidDataException("Chave administrativa local inválida."))), null, DataProtectionScope.LocalMachine))
        : throw new InvalidOperationException("Vincule uma chave administrativa na tela Configurações para usar a integração Veyon.");
    private HttpClient Client()
    {
        var config = Reader.Settings();
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
        var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, config.Thumbprint, false).OfType<X509Certificate2>().FirstOrDefault()
            ?? throw new IOException("Certificado configurado no professor não encontrado.");
        return CertificateTrust.Client(new AgentConfig { CertificateSha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256) });
    }
    private async Task<JsonElement> Api(string path, object? body, CancellationToken ct, string? key = null)
    {
        var config = Reader.Settings(); using var http = Client();
        http.Timeout = TimeSpan.FromSeconds(body == null ? 15 : 180);
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post,
            $"https://{Environment.MachineName}:{config.Port}{path}");
        request.Headers.Add("X-Admin-Key", key ?? Key());
        if (body != null) request.Content = JsonContent.Create(body, options: Files.Json);
        using var response = await http.SendAsync(request, ct);
        if ((int)response.StatusCode == 401) throw new InvalidOperationException("Chave administrativa recusada. Vincule a chave atual em Configurações.");
        if ((int)response.StatusCode == 429) throw new InvalidOperationException("Muitas tentativas inválidas. Aguarde um minuto antes de tentar novamente.");
        response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(Files.Json, ct);
    }
    private async Task<string> Connect(string key, bool generate, CancellationToken ct)
    {
        if (generate)
        {
            var output = await Command(["--new-admin-key"], ct);
            var match = Regex.Match(output, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{48}(?![0-9A-Fa-f])");
            if (!match.Success) throw new IOException("Não foi possível interpretar a chave administrativa gerada.");
            key = match.Value;
        }
        if (!Regex.IsMatch(key, @"\A[0-9A-Fa-f]{48}\z")) throw new ArgumentException("A chave administrativa deve conter 48 caracteres hexadecimais.");
        if (generate) SaveKey(key);
        _ = await Api("/api/admin/pending", null, ct, key);
        if (!generate) SaveKey(key);
        return "Interface vinculada à API administrativa local. A chave não é exibida nem incluída nos diagnósticos.";
    }
    private async Task<IntegrationPreview> Preview(string id, CancellationToken ct)
    {
        var current = Reader.Read(ServiceStatus()).Machines.SingleOrDefault(m => m.Data.AgentId == id)
            ?? throw new ArgumentException("Computador não encontrado.");
        var pending = await Api("/api/admin/pending", null, ct);
        if (!pending.GetProperty("veyonReady").GetBoolean()) throw new InvalidOperationException(pending.GetProperty("veyonInfo").GetString() ?? "Veyon não validado.");
        var item = pending.GetProperty("items").EnumerateArray().FirstOrDefault(e => e.GetProperty("agentId").GetString() == id);
        if (item.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Este computador não tem alteração pendente no professor.");
        return new IntegrationPreview(id, item.GetProperty("friendlyName").GetString() ?? current.Name, Reader.AppliedIp(id),
            current.Ip ?? throw new InvalidOperationException("Computador sem IP."), current.Data.Mac,
            Reader.Configuration()["Veyon"]?["Location"]?.GetValue<string>() ?? "Lab " + current.Data.Lab, "Aplicar alteração pendente");
    }
    private async Task<string> Apply(IntegrationPreview approved, CancellationToken ct)
    {
        var now = await Preview(approved.AgentId, ct);
        if (now != approved) throw new InvalidOperationException("Os dados mudaram. Gere outra prévia antes de aplicar.");
        var result = await Api("/api/admin/integrate", new { dryRun = false, onlyAgentIds = new[] { approved.AgentId } }, ct);
        if (result.TryGetProperty("fatalError", out var fatal) && fatal.ValueKind == JsonValueKind.String) throw new IOException(fatal.GetString());
        var items = result.GetProperty("items").EnumerateArray().ToArray();
        var item = items.SingleOrDefault(i => i.GetProperty("agentId").GetString() == approved.AgentId);
        if (item.ValueKind == JsonValueKind.Undefined) throw new IOException("O serviço não retornou resultado para este computador.");
        if (item.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) throw new IOException(error.GetString());
        if (!item.GetProperty("verifiedInDirectory").GetBoolean()) throw new IOException("O resultado não foi verificado no diretório Veyon.");
        return "Cadastro aplicado e verificado pelo serviço da etapa 2. Disponibilidade no Veyon Master: sem confirmação.";
    }
    private static async Task<string> StartService(CancellationToken ct)
    {
        await Task.Run(() => { using var s = new ServiceController("VeyonSyncTeacher"); s.Start(); s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); }, ct);
        return "Serviço iniciado.";
    }
}
