using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using VeyonSync.Core;
using VeyonSync.Teacher;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("O serviço Professor requer Windows.");
var dir = Files.DataDir("Teacher"); Files.RestrictDirectory(dir);
var config = Files.Read<TeacherConfig>(Path.Combine(dir, "config.json"));
if (!Validation.Label(config.Lab) || config.Port is < 1024 or > 65535 || string.IsNullOrWhiteSpace(config.Thumbprint))
    throw new InvalidOperationException("Instalação incompleta: laboratório, porta ou certificado inválido. Execute install-teacher.ps1.");
var db = new Store(Path.Combine(dir, "teacher.db"));
if (args.Contains("--new-code")) { Console.WriteLine(db.NewCode()); return 0; }
if (args.Contains("--request-collect")) { db.RequestCollect(); Console.WriteLine("Pedido registrado."); return 0; }
using var certificates = new X509Store(StoreName.My, StoreLocation.LocalMachine); certificates.Open(OpenFlags.ReadOnly);
var certificate = certificates.Certificates.Find(X509FindType.FindByThumbprint, config.Thumbprint, false)
    .OfType<X509Certificate2>().SingleOrDefault() ?? throw new InvalidOperationException("Certificado não encontrado.");
if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
    throw new InvalidOperationException("Certificado sem chave privada, expirado ou ainda inválido.");
if (args.Contains("--health-check"))
{
    try
    {
        using var http = CertificateTrust.Client(new AgentConfig { CertificateSha256 = certificate.GetCertHashString(HashAlgorithmName.SHA256) });
        using var response = await http.GetAsync($"https://{config.TeacherName}:{config.Port}/health");
        response.EnsureSuccessStatusCode();
        var health = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (health.GetProperty("status").GetString() != "ok" || health.GetProperty("lab").GetString() != config.Lab)
            throw new IOException("Resposta HTTPS diverge do laboratório configurado.");
        Console.WriteLine("HTTPS verificado com nome, validade, cadeia e impressão digital do certificado.");
        return 0;
    }
    catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
}
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "VeyonSyncTeacher");
builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 32 * 1024; o.ListenAnyIP(config.Port, l => l.UseHttps(certificate)); });
builder.Services.AddSingleton(config); builder.Services.AddSingleton(db);
builder.Services.AddSingleton(new VeyonIntegration(config, dir, new CommandRunner()));
builder.Services.AddHostedService<Administration>();
builder.Services.AddHostedService<Maintenance>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("pair", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("report", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var app = builder.Build(); app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ok", role = "Teacher", lab = config.Lab, version = "0.2.0" }));
app.MapPost("/api/pair", (PairRequest request) =>
{
    if (request.Code == null || request.AgentId == null || string.IsNullOrWhiteSpace(request.MachineName) || request.MachineName.Length > 63
        || !Guid.TryParse(request.AgentId, out _) || !Validation.Label(request.Lab)) return Results.BadRequest();
    if (!string.Equals(request.Lab, config.Lab, StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(403);
    var token = db.Pair(request.Code, request.AgentId, config.Lab);
    return token == null ? Results.Unauthorized() : Results.Ok(new PairResponse(token, config.Lab));
}).RequireRateLimiting("pair");
app.MapPost("/api/report", (HttpRequest request, Report report) =>
{
    if (!Validation.ValidReport(report)) return Results.BadRequest("Campos inválidos.");
    var auth = request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || request.Headers["X-Agent-Id"] != report.AgentId
        ) return Results.Unauthorized();
    if (!string.Equals(report.Lab, config.Lab, StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(403);
    var ack = db.ReceiveAuthorized(report, auth[7..], config.Lab);
    return ack == null ? Results.Unauthorized() : Results.Ok(ack);
}).RequireRateLimiting("report");
await app.RunAsync();

return 0;
