using Microsoft.Data.Sqlite;
using VeyonSync.Core;
using Xunit;

namespace VeyonSync.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "VeyonSync-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string id = Guid.NewGuid().ToString();
    private Store NewStore() => new(Path.Combine(dir, "teacher.db"));
    private Report Report(long sequence = 1) => new(id, "PC03", "B", "PC03", "ESCOLA\\aluno", "10.0.0.3", "AA-BB-CC-DD-EE-FF", DateTimeOffset.UtcNow, sequence, "0.2.0", "manual");
    [Fact]
    public void PairingIsSingleUseAndNewCodeInvalidatesPrevious()
    {
        var db = NewStore(); var old = db.NewCode(); var current = db.NewCode();
        Assert.Null(db.Pair(old, id, "B")); var token = db.Pair(current, id, "B"); Assert.NotNull(token);
        Assert.Null(db.Pair(current, Guid.NewGuid().ToString(), "B"));
        Assert.True(db.Authorized(id, token, "B")); Assert.False(db.Authorized(id, token, "A"));
        Assert.False(db.Authorized(id, "wrong", "B")); db.Revoke(id); Assert.False(db.Authorized(id, token, "B"));
    }
    [Fact]
    public void PairingCodeExpires()
    {
        var db = NewStore(); var code = db.NewCode();
        using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "teacher.db")}")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE codes SET expires='2000-01-01T00:00:00.0000000+00:00'"; cmd.ExecuteNonQuery(); }
        Assert.Null(db.Pair(code, id, "B"));
    }
    [Fact]
    public void StatePersistsAndOlderReportsCannotOverwriteInventory()
    {
        var db = NewStore(); Assert.True(db.Receive(Report(100)).Received);
        Assert.True(db.Receive(Report(101) with { Ip = "10.0.0.4", Mac = "11-22-33-44-55-66", User = null }).Received);
        Assert.True(db.Receive(Report(99)).Stale);
        var reopened = NewStore(); var machine = Assert.Single(reopened.Machines());
        Assert.Equal("10.0.0.4", machine.Ip); Assert.Equal(101, machine.Data.Seq); Assert.Null(machine.User);
        Assert.Contains(reopened.Events(), e => e.Kind == "Mudança de IP"); Assert.Contains(reopened.Events(), e => e.Kind == "Mudança de MAC");
        Assert.Contains(reopened.Events(), e => e.Kind == "Mudança de usuário"); Assert.True(reopened.Healthy());
    }
    [Fact]
    public void CollectionIsDeliveredOnceAndIntegrationStatusIsIndependent()
    {
        var db = NewStore(); var ack = db.Receive(Report()); Assert.False(ack.VeyonApplied);
        db.RequestCollect(); Assert.True(db.Receive(Report(2)).CollectRequested); Assert.False(db.Receive(Report(3)).CollectRequested);
        db.IntegrationResult(id, true); Assert.True(db.Receive(Report(4)).VeyonApplied);
        Assert.False(db.Receive(Report(5) with { Mac = "11-22-33-44-55-66" }).VeyonApplied);
    }
    [Fact]
    public void ReenrollmentRecoversSequenceWithoutLosingHistory()
    {
        var db = NewStore(); db.Receive(Report(10000)); var code = db.NewCode(); Assert.NotNull(db.Pair(code, id, "B"));
        Assert.False(db.Receive(Report(1)).Stale); Assert.Single(db.Machines()); Assert.Contains(db.Events(), e => e.Kind == "Pareamento");
    }
    [Fact]
    public void FailedWriteIsAtomic()
    {
        var db = NewStore(); db.Receive(Report());
        using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "teacher.db")}")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TRIGGER abort_history BEFORE INSERT ON history BEGIN SELECT RAISE(ABORT,'test failure'); END"; cmd.ExecuteNonQuery(); }
        Assert.Throws<SqliteException>(() => db.Receive(Report(2) with { Ip = "10.0.0.4" }));
        Assert.Equal("10.0.0.3", Assert.Single(db.Machines()).Ip);
    }
    [Fact]
    public void CounterAndPendingReportSurviveRestartAndClockChanges()
    {
        Directory.CreateDirectory(dir); var path = Path.Combine(dir, "state.json");
        var state = new AgentState { Sequence = 500, Pending = Report(500), Status = "Falhou" }; Files.Write(path, state);
        var loaded = Files.Read<AgentState>(path); loaded.Sequence++;
        loaded.Pending = Report(loaded.Sequence) with { CollectedAt = DateTimeOffset.UtcNow.AddDays(-2) }; Files.Write(path, loaded);
        var next = Files.Read<AgentState>(path); Assert.Equal(501, next.Sequence); Assert.Equal(501, next.Pending!.Seq);
    }
    [Fact]
    public void LegacySchemaIsRetained()
    {
        var db = NewStore(); db.Receive(Report());
        using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "teacher.db")}")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DROP TABLE integrations; DROP TABLE metadata"; cmd.ExecuteNonQuery(); }
        var migrated = NewStore(); Assert.Single(migrated.Machines()); Assert.Equal("Pendente", migrated.Machines()[0].Integration); Assert.NotEmpty(migrated.Events());
    }
    [Theory]
    [InlineData(null, "PC03")]
    [InlineData("B", null)]
    [InlineData("B", "=cmd")]
    [InlineData("B", "PC 03")]
    public void InvalidRequiredFieldsAreRejected(string? lab, string? position)
    {
        var report = Report() with { Lab = lab!, Position = position! }; Assert.False(Validation.ValidReport(report));
    }
    [Theory]
    [InlineData("http://teacher:5443")]
    [InlineData("https://user:pass@teacher:5443")]
    [InlineData("https://teacher:5443/api")]
    public void InvalidTeacherEndpointsAreRejected(string url) => Assert.Throws<ArgumentException>(() => Validation.TeacherUri(url));
    [Fact]
    public void CommunicationTransitionsAreRecordedOnceWithoutAssumingPowerState()
    {
        var db = NewStore(); db.Receive(Report()); db.UpdateCommunication();
        using (var c = new SqliteConnection($"Data Source={Path.Combine(dir, "teacher.db")}"))
        {
            c.Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE machines SET last_seen='2000-01-01T00:00:00.0000000+00:00'"; cmd.ExecuteNonQuery();
        }
        db.UpdateCommunication(); db.UpdateCommunication(); db.Receive(Report(2)); db.UpdateCommunication(); db.UpdateCommunication();
        Assert.Single(db.Events(), e => e.Kind == "Sem comunicação recente");
        Assert.Single(db.Events(), e => e.Kind == "Comunicação recuperada");
    }
    [Fact]
    public void NewInventoryDuringIntegrationDoesNotReceiveAnIncorrectAppliedStatus()
    {
        var db = NewStore(); var original = Report(); db.Receive(original);
        var changed = Report(2) with { Ip = "10.0.0.9" }; db.Receive(changed);
        Assert.False(db.CompleteIntegration(original)); Assert.NotEqual("Aplicado", Assert.Single(db.Machines()).Integration);
        Assert.True(db.CompleteIntegration(changed)); Assert.Equal("Aplicado", Assert.Single(db.Machines()).Integration);
    }
    [Fact]
    public void RevokedCredentialCannotWriteInventory()
    {
        var db = NewStore(); var token = db.Pair(db.NewCode(), id, "B")!;
        Assert.NotNull(db.ReceiveAuthorized(Report(), token, "B")); db.Revoke(id);
        Assert.Null(db.ReceiveAuthorized(Report(2) with { Ip = "10.0.0.9" }, token, "B"));
        Assert.Equal("10.0.0.3", Assert.Single(db.Machines()).Ip);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
