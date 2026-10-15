using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace VeyonSync.Core;

public sealed class TeacherConfig
{
    public string Lab { get; set; } = "A";
    public int Port { get; set; } = 5443;
    public string Thumbprint { get; set; } = "";
    public string TeacherName { get; set; } = Environment.MachineName;
    public string VeyonCli { get; set; } = @"C:\Program Files\Veyon\veyon-cli.exe";
    public int RetentionDays { get; set; } = 90;
}
public sealed class AgentConfig
{
    public string AgentId { get; set; } = Guid.NewGuid().ToString();
    public string MachineGuid { get; set; } = "";
    public string TeacherUrl { get; set; } = "";
    public string Lab { get; set; } = "A";
    public string Position { get; set; } = "";
    public bool AutoSend { get; set; } = true;
    public int PresenceSeconds { get; set; } = 60;
    public string? TokenProtected { get; set; }
    // Public certificate fingerprint, never a credential. Trust is scoped to this application.
    public string? CertificateSha256 { get; set; }
    [SupportedOSPlatform("windows")]
    public string? GetToken() => TokenProtected == null ? null : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(TokenProtected), null, DataProtectionScope.LocalMachine));
    [SupportedOSPlatform("windows")]
    public void SetToken(string value) => TokenProtected = Convert.ToBase64String(ProtectedData.Protect(
        Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
}
