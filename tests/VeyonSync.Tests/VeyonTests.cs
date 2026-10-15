using System.Text.Json.Nodes;
using VeyonSync.Core;
using Xunit;

namespace VeyonSync.Tests;

public sealed class VeyonTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "VeyonSync-veyon-" + Guid.NewGuid().ToString("N"));
    private readonly string id = Guid.NewGuid().ToString();
    private Report Report(string ip = "10.0.0.3") => new(id, "PC03", "B", "PC03", null, ip, "AA-BB-CC-DD-EE-FF", DateTimeOffset.UtcNow, 1, "0.2.0", "manual");
    private sealed class FakeCli : ICommandRunner
    {
        public JsonObject Config { get; } = JsonNode.Parse("""{"BuiltinDirectory":{"NetworkObjects":[{"Type":2,"Name":"Unrelated","Uid":"other","ParentUid":""}]},"Other":{"keep":true}}""")!.AsObject();
        public JsonArray Objects => Config["BuiltinDirectory"]!["NetworkObjects"] is JsonArray array ? array : Config["BuiltinDirectory"]!["NetworkObjects"]!["JsonStoreArray"]!.AsArray();
        public bool FailImport { get; set; }
        public List<string> Commands { get; } = [];
        public Task<string> Run(string executable, IReadOnlyList<string> args, CancellationToken ct)
        {
            Commands.Add(string.Join(' ', args));
            if (args[0] == "config" && args[1] == "export") File.WriteAllText(args[2], Config.ToJsonString());
            else if (args[0] == "config" && args[1] == "import") { var saved = JsonNode.Parse(File.ReadAllText(args[2]))!; Config.Clear(); foreach (var kv in saved.AsObject()) Config[kv.Key] = kv.Value?.DeepClone(); }
            else if (args[0] == "config" && args[1] == "get") return Task.FromResult(Objects.ToJsonString());
            else if (args[0] == "config" && args[1] == "set")
            {
                if (FailImport) throw new IOException("Injected CLI failure");
                if (Config["BuiltinDirectory"]!["NetworkObjects"] is JsonArray) Config["BuiltinDirectory"]!["NetworkObjects"] = JsonNode.Parse(args[3]);
                else Config["BuiltinDirectory"]!["NetworkObjects"]!["JsonStoreArray"] = JsonNode.Parse(args[3]);
            }
            return Task.FromResult("");
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntegrationBacksUpUpdatesWithoutDuplicatesAndPreservesUnrelatedObjects(bool wrapped)
    {
        var runner = new FakeCli();
        if (wrapped) { var array = runner.Objects.DeepClone(); runner.Config["BuiltinDirectory"]!["NetworkObjects"] = new JsonObject { ["JsonStoreArray"] = array }; }
        var integration = new VeyonIntegration(new() { Lab = "B" }, dir, runner);
        var machine = new Machine(Report(), DateTimeOffset.UtcNow, "Pendente", null);
        var plan = Assert.Single(await integration.Preview([machine], default)); await integration.Apply(machine.Data, plan, default);
        var count = runner.Objects.Count;
        var uid = runner.Objects.Single(x => x!["Name"]!.GetValue<string>() == plan.Name)!["Uid"]!.GetValue<string>();
        var next = machine with { Data = Report("10.0.0.9") }; var change = Assert.Single(await integration.Preview([next], default));
        Assert.Equal("10.0.0.3", change.OldIp); await integration.Apply(next.Data, change, default);
        Assert.Equal(count, runner.Objects.Count);
        Assert.Equal(uid, runner.Objects.Single(x => x!["Name"]!.GetValue<string>() == plan.Name)!["Uid"]!.GetValue<string>());
        Assert.True(runner.Config["Other"]!["keep"]!.GetValue<bool>()); Assert.Equal(2, integration.Backups().Length);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("clear"));
    }
    [Fact]
    public async Task FailureRetainsBackupAndDoesNotClaimSuccess()
    {
        var runner = new FakeCli { FailImport = true }; var integration = new VeyonIntegration(new() { Lab = "B" }, dir, runner);
        var machine = new Machine(Report(), DateTimeOffset.UtcNow, "Pendente", null); var plan = Assert.Single(await integration.Preview([machine], default));
        await Assert.ThrowsAsync<IOException>(() => integration.Apply(machine.Data, plan, default)); Assert.Single(integration.Backups());
    }
    [Fact]
    public async Task ChangedDataRequiresNewApprovalPreview()
    {
        var runner = new FakeCli(); var integration = new VeyonIntegration(new() { Lab = "B" }, dir, runner);
        var machine = new Machine(Report(), DateTimeOffset.UtcNow, "Pendente", null); var plan = Assert.Single(await integration.Preview([machine], default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => integration.Apply(Report("10.0.0.8"), plan, default));
        Assert.DoesNotContain(runner.Commands, c => c.StartsWith("config set"));
    }
    [Fact]
    public async Task UnownedNameCollisionIsBlocked()
    {
        var runner = new FakeCli(); runner.Objects.Add(new JsonObject { ["Type"] = 3, ["Name"] = VeyonIntegration.ManagedName("B", id), ["Uid"] = "unowned" });
        var integration = new VeyonIntegration(new() { Lab = "B" }, dir, runner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => integration.Preview([new(Report(), DateTimeOffset.UtcNow, "Pendente", null)], default));
    }
    [Fact]
    public async Task UnknownSchemaFailsClosed()
    {
        var runner = new FakeCli(); runner.Config.Clear(); var integration = new VeyonIntegration(new() { Lab = "B" }, dir, runner);
        await Assert.ThrowsAsync<InvalidDataException>(() => integration.Preview([new(Report(), DateTimeOffset.UtcNow, "Pendente", null)], default));
    }
    [Fact]
    public async Task RestoreChecksTheBackupAndRejectsPathTraversal()
    {
        var runner = new FakeCli(); var integration = new VeyonIntegration(new() { Lab = "B" }, dir, runner);
        var machine = new Machine(Report(), DateTimeOffset.UtcNow, "Pendente", null); var plan = Assert.Single(await integration.Preview([machine], default));
        await integration.Apply(machine.Data, plan, default); var backup = Assert.Single(integration.Backups());
        await integration.Restore(backup, default); Assert.Single(runner.Objects);
        await Assert.ThrowsAsync<ArgumentException>(() => integration.Restore("../not-a-backup.json", default));
    }
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
