using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using VeyonSync.Core;

namespace VeyonSync.Teacher;

public sealed class Administration(Store store, TeacherConfig config, VeyonIntegration veyon, ILogger<Administration> log) : BackgroundService
{
    private static T Data<T>(RpcRequest r) => JsonSerializer.Deserialize<T>(r.Data ?? "null", Files.Json) ?? throw new ArgumentException("Dados ausentes.");
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Retention on every start; the UI timer never mutates inventory/history.
        store.Prune(config.RetentionDays);
        await LocalRpc.Serve("Teacher", Handle, ct);
    }
    private Task<object?> Handle(RpcRequest request, CancellationToken ct) => Dispatch(request, ct);
    private async Task<object?> Dispatch(RpcRequest r, CancellationToken ct)
    {
        switch (r.Action)
        {
            case "snapshot": return new Snapshot("Teacher", config.Lab, Environment.MachineName, "Em execução", store.Machines(), store.Events(), null, Diagnostics());
            case "new-code": return new PairingCode(store.NewCode(), DateTimeOffset.UtcNow.AddMinutes(15), config.Lab, config.TeacherName);
            case "collect": store.RequestCollect(r.Data == null ? null : Data<string>(r)); return "Pedido criado; aguarda a próxima comunicação do agente.";
            case "revoke": store.Revoke(Data<string>(r)); return "Agente revogado.";
            case "preview":
                {
                    var id = Data<string>(r); var machine = store.Machines().SingleOrDefault(m => m.Data.AgentId == id) ?? throw new ArgumentException("Computador não encontrado.");
                    return (await veyon.Preview([machine], ct)).Single();
                }
            case "apply":
                {
                    var plan = Data<IntegrationPreview>(r); var machine = store.Machines().SingleOrDefault(m => m.Data.AgentId == plan.AgentId) ?? throw new ArgumentException("Computador não encontrado.");
                    try { var backup = await veyon.Apply(machine.Data, plan, ct); var current = store.CompleteIntegration(machine.Data); return current ? "Aplicado e verificado. Backup: " + backup : "Cadastro aplicado, mas chegaram novos dados. Gere outra prévia. Backup: " + backup; }
                    catch (Exception e) { store.IntegrationResult(plan.AgentId, false, e.Message); log.LogWarning("Integração Veyon falhou para {Agent}: {Error}", plan.AgentId, e.Message); throw; }
                }
            case "backups": return veyon.Backups();
            case "restore":
                {
                    await veyon.Restore(Data<string>(r), ct);
                    foreach (var machine in store.Machines()) store.IntegrationResult(machine.Data.AgentId, false, "Backup restaurado. Gere uma nova prévia para conferir o cadastro.");
                    return "Backup restaurado; cadastros precisam de nova conferência.";
                }
            case "settings": return config;
            case "save-settings":
                {
                    var next = Data<TeacherConfig>(r);
                    if (next.Lab != config.Lab || next.Thumbprint != config.Thumbprint || next.Port != config.Port || next.TeacherName != config.TeacherName)
                        throw new InvalidOperationException("Laboratório, certificado, nome e porta exigem configuração explícita pelo instalador.");
                    if (!Path.IsPathFullyQualified(next.VeyonCli) || !File.Exists(next.VeyonCli)) throw new ArgumentException("Selecione o executável veyon-cli instalado.");
                    config.VeyonCli = next.VeyonCli; config.RetentionDays = Math.Clamp(next.RetentionDays, 7, 3650);
                    Files.Write(Path.Combine(Files.DataDir("Teacher"), "config.json"), config); store.Prune(config.RetentionDays); return "Configurações salvas.";
                }
            default: throw new ArgumentException("Ação desconhecida.");
        }
    }
    private List<Diagnostic> Diagnostics()
    {
        using var certs = new X509Store(StoreName.My, StoreLocation.LocalMachine); certs.Open(OpenFlags.ReadOnly);
        var cert = certs.Certificates.Find(X509FindType.FindByThumbprint, config.Thumbprint, false).OfType<X509Certificate2>().FirstOrDefault();
        return [new("Serviço",true,"Canal administrativo respondeu; serviço em execução."),
            new("Certificado TLS",cert!=null&&cert.HasPrivateKey&&cert.NotAfter>DateTime.Now.AddDays(30),cert==null?"Certificado ausente.":$"Nome: {cert.GetNameInfo(X509NameType.DnsName,false)} · Validade: {cert.NotAfter:d}"),
            new("HTTPS",true,$"Porta configurada: {config.Port}. Teste a conexão a partir de um aluno para conferir DNS, firewall e TLS."),
            new("Banco de dados",store.Healthy(),"SQLite: verificação de integridade executada."),
            new("Veyon CLI",File.Exists(config.VeyonCli),config.VeyonCli),
            new("Comunicação",store.Machines().Any(m=>DateTimeOffset.UtcNow-m.LastSeen<TimeSpan.FromMinutes(3)),"Ausência de comunicação recente não prova que o computador está desligado.")];
    }
}
