namespace VeyonSync.Core;

public record PairRequest(string Code, string AgentId, string Lab, string MachineName);
public record PairResponse(string Token, string Lab);
public record Report(string AgentId, string MachineName, string Lab, string Position, string? User,
    string? Ip, string? Mac, DateTimeOffset CollectedAt, long Seq, string AgentVersion, string Reason);
public record ReportAck(bool Received, bool Stale, bool VeyonApplied, string VeyonAvailability, bool CollectRequested);
public record Machine(Report Data, DateTimeOffset LastSeen, string Integration, string? IntegrationError)
{
    public string Name => Data.MachineName;
    public string Position => Data.Position;
    public string? Ip => Data.Ip;
    public string? User => Data.User;
    public string Communication => DateTimeOffset.UtcNow - LastSeen < TimeSpan.FromMinutes(3)
        ? "Comunicação recente" : "Sem comunicação recente";
}
public record Event(long Id, string AgentId, DateTimeOffset Time, string Kind, string Detail);
public record Diagnostic(string Name, bool Ok, string Detail);
public record Snapshot(string Role, string Lab, string Computer, string Service, List<Machine> Machines,
    List<Event> Events, AgentState? Agent, List<Diagnostic> Diagnostics);
public record PairingCode(string Code, DateTimeOffset Expires, string Lab, string Teacher);
public record IntegrationPreview(string AgentId, string Name, string? OldIp, string NewIp, string? Mac, string Location, string Action);
public record RpcRequest(string Action, string? Data = null);
public record RpcResponse(bool Ok, string? Data = null, string? Error = null);

public sealed class AgentState
{
    public long Sequence { get; set; }
    public Report? Pending { get; set; }
    public Report? Current { get; set; }
    public string Status { get; set; } = "Aguardando pareamento";
    public DateTimeOffset? LastConfirmed { get; set; }
    public string? LastError { get; set; }
    public bool Force { get; set; }
    public bool VeyonApplied { get; set; }
    public string VeyonAvailability { get; set; } = "Sem confirmação";
}
