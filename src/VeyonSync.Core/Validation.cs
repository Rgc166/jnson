using System.Net;
using System.Text.RegularExpressions;

namespace VeyonSync.Core;

public static partial class Validation
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$")]
    private static partial Regex LabelRegex();
    [GeneratedRegex("^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex MacRegex();
    public static bool Label(string? value) => value != null && LabelRegex().IsMatch(value);
    public static bool ValidReport(Report d) => Guid.TryParse(d.AgentId, out _) && Label(d.Lab)
        && !string.IsNullOrWhiteSpace(d.MachineName) && d.MachineName.Length <= 63
        && Label(d.Position) && (d.User?.Length ?? 0) <= 128 && d.Seq > 0
        && (d.Ip == null || IPAddress.TryParse(d.Ip, out _))
        && (d.Mac == null || MacRegex().IsMatch(d.Mac))
        && !string.IsNullOrWhiteSpace(d.AgentVersion) && d.AgentVersion.Length <= 32
        && !string.IsNullOrWhiteSpace(d.Reason) && d.Reason.Length <= 32;
    public static Uri TeacherUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.UserInfo != "" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "")
            throw new ArgumentException("Informe https://NOME-DO-PROFESSOR:5443, sem caminho ou credenciais.");
        return uri;
    }
}
