using Microsoft.Data.Sqlite;
using System.Text.Json;
using VeyonSync.Core;
using Xunit;

namespace VeyonSync.Tests;

public sealed class Stage2ReaderTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "VeyonSync-stage2-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(dir, "appsettings.json");
    private string Database => Path.Combine(dir, "teacher.db");
    private Stage2Reader Reader => new(Config, Database);
    private readonly string id = Guid.NewGuid().ToString();
    public Stage2ReaderTests()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Config, """{"Lab":"B","Port":5443,"Tls":{"Thumbprint":"PUBLIC"},"Veyon":{"Location":"Lab B","CliPath":""}}""");
        using var c = new SqliteConnection($"Data Source={Database};Pooling=False"); c.Open();
        foreach (var sql in new[] {
            "CREATE TABLE machines(agent_id TEXT PRIMARY KEY,lab TEXT,name TEXT,position TEXT,user TEXT,ip TEXT,mac TEXT,last_seen TEXT,last_seq INTEGER,version TEXT)",
            "CREATE TABLE history(id INTEGER PRIMARY KEY,agent_id TEXT,ts TEXT,kind TEXT,detail TEXT)",
            "CREATE TABLE veyon_queue(agent_id TEXT PRIMARY KEY,status TEXT,created TEXT,error TEXT)",
            "CREATE TABLE veyon_applied(agent_id TEXT PRIMARY KEY,name TEXT,host TEXT,mac TEXT,ts TEXT)",
            "CREATE TABLE settings(k TEXT PRIMARY KEY,v TEXT)"
        }) { using var q = c.CreateCommand(); q.CommandText = sql; q.ExecuteNonQuery(); }
        using var insert = c.CreateCommand();
        insert.CommandText = "INSERT INTO machines VALUES(@id,'B','PC03','PC03','ESCOLA\\aluno','10.0.0.3','AA-BB-CC-DD-EE-FF',@time,123,'0.2.0')";
        insert.Parameters.AddWithValue("@id", id); insert.Parameters.AddWithValue("@time", DateTimeOffset.UtcNow.ToString("O")); insert.ExecuteNonQuery();
    }
    private void Execute(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Database};Pooling=False"); c.Open(); using var q = c.CreateCommand();
        q.CommandText = sql; q.Parameters.AddWithValue("@id", id); q.ExecuteNonQuery();
    }
    [Fact] public void ReadsActualStage2InventoryWithoutChangingTheDatabase()
    {
        var before = File.ReadAllBytes(Database); var config = File.ReadAllBytes(Config);
        var snapshot = Reader.Read("Em execução"); var machine = Assert.Single(snapshot.Machines);
        Assert.Equal("PC03", machine.Name); Assert.Equal("10.0.0.3", machine.Ip); Assert.Equal("B", snapshot.Lab);
        Assert.Equal("Sem confirmação", machine.Integration);
        Assert.Equal(before, File.ReadAllBytes(Database)); Assert.Equal(config, File.ReadAllBytes(Config));
    }
    [Fact] public void IntegrationStateComesFromQueueAndAppliedTables()
    {
        Execute("INSERT INTO veyon_applied VALUES(@id,'LAB-B-PC03','10.0.0.2','AA-BB-CC-DD-EE-FF','2026-10-20T10:00:00+00:00')");
        Assert.Equal("Aplicado", Assert.Single(Reader.Read("Em execução").Machines).Integration);
        Assert.Equal("10.0.0.2", Reader.AppliedIp(id));
        Execute("INSERT INTO veyon_queue VALUES(@id,'pendente','2026-10-20T10:00:00+00:00',NULL)");
        Assert.Equal("Pendente", Assert.Single(Reader.Read("Em execução").Machines).Integration);
        Execute("UPDATE veyon_queue SET status='erro',error='Veyon indisponível' WHERE agent_id=@id");
        var machine = Assert.Single(Reader.Read("Em execução").Machines);
        Assert.Equal("Falhou", machine.Integration); Assert.Equal("Veyon indisponível", machine.IntegrationError);
    }
    [Fact] public void FiltersInventoryByInstalledLabAndUsesRealServiceStatus()
    {
        Execute("INSERT INTO machines SELECT 'other','A','OUTRO','PC01',NULL,ip,mac,last_seen,1,version FROM machines WHERE agent_id=@id");
        var snapshot = Reader.Read("Parado: Stopped");
        Assert.Single(snapshot.Machines); Assert.False(snapshot.Diagnostics.First(d => d.Name == "Serviço do professor").Ok);
    }
    [Fact] public void MissingDatabaseIsNotCreated()
    {
        File.Delete(Database); Assert.Throws<IOException>(() => Reader.Read("Parado")); Assert.False(File.Exists(Database));
    }
    [Fact] public void RejectsOtherVersionsWithoutMigratingThem()
    {
        Execute("DROP TABLE veyon_applied"); var bytes = File.ReadAllBytes(Database);
        Assert.Throws<InvalidDataException>(() => Reader.Read("Em execução")); Assert.Equal(bytes, File.ReadAllBytes(Database));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
}
