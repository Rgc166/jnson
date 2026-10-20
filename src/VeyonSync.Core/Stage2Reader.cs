using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace VeyonSync.Core;

/// <summary>Read-only adapter for the uploaded etapa 2 schema. Never creates or migrates its database.</summary>
public sealed class Stage2Reader(string configPath, string databasePath)
{
    public JsonNode Configuration()
    {
        if (!File.Exists(configPath)) throw new IOException("A etapa 2 do professor não foi encontrada. Instale o serviço antes da interface THC.");
        return JsonNode.Parse(File.ReadAllText(configPath), new JsonNodeOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Configuração da etapa 2 vazia.");
    }
    public TeacherConfig Settings()
    {
        var root = Configuration();
        return new TeacherConfig
        {
            Lab = root["Lab"]?.GetValue<string>() ?? throw new InvalidDataException("Laboratório não configurado."),
            Port = root["Port"]?.GetValue<int>() ?? 5443,
            Thumbprint = root["Tls"]?["Thumbprint"]?.GetValue<string>() ?? "",
            TeacherName = Environment.MachineName,
            VeyonCli = root["Veyon"]?["CliPath"]?.GetValue<string>() ?? ""
        };
    }
    private SqliteConnection Open()
    {
        if (!File.Exists(databasePath)) throw new IOException("Banco da etapa 2 não encontrado. Confira se o serviço VeyonSyncTeacher iniciou.");
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
        }.ToString());
        c.Open(); return c;
    }
    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public Snapshot Read(string serviceStatus)
    {
        var config = Settings();
        using var c = Open();
        using var tx = c.BeginTransaction(deferred: true);
        using (var schema = c.CreateCommand())
        {
            schema.Transaction = tx;
            schema.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('machines','history','veyon_queue','veyon_applied','settings')";
            if (Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture) != 5)
                throw new InvalidDataException("O banco encontrado não é da etapa 2. Este complemento não converte bancos de outras versões.");
        }
        var machines = new List<Machine>();
        using (var query = c.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = """
                SELECT m.agent_id,m.lab,m.name,m.position,m.user,m.ip,m.mac,m.last_seen,m.last_seq,m.version,
                       q.status,q.error,a.agent_id
                FROM machines m
                LEFT JOIN veyon_queue q ON q.agent_id=m.agent_id
                LEFT JOIN veyon_applied a ON a.agent_id=m.agent_id
                WHERE m.lab=@lab COLLATE NOCASE ORDER BY m.position,m.name
                """;
            query.Parameters.AddWithValue("@lab", config.Lab);
            using var r = query.ExecuteReader();
            while (r.Read())
            {
                var seen = DateTimeOffset.Parse(r.GetString(7), CultureInfo.InvariantCulture);
                var queue = Text(r, 10);
                var integration = queue == "erro" ? "Falhou" : queue != null ? "Pendente" : !r.IsDBNull(12) ? "Aplicado" : "Sem confirmação";
                machines.Add(new Machine(new Report(r.GetString(0), Text(r, 2) ?? "", r.GetString(1), Text(r, 3) ?? "",
                    Text(r, 4), Text(r, 5), Text(r, 6), seen, r.GetInt64(8), Text(r, 9) ?? "", "inventário"),
                    seen, integration, Text(r, 11)));
            }
        }
        var events = new List<Event>();
        using (var query = c.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = "SELECT id,agent_id,ts,kind,detail FROM history ORDER BY id DESC LIMIT 500";
            using var r = query.ExecuteReader();
            while (r.Read()) events.Add(new Event(r.GetInt64(0), Text(r, 1) ?? "", DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture),
                Text(r, 3) ?? "", Text(r, 4) ?? ""));
        }
        tx.Commit();
        return new Snapshot("Teacher", config.Lab, Environment.MachineName, serviceStatus, machines, events, null,
            [new Diagnostic("Serviço do professor", serviceStatus == "Em execução", serviceStatus),
             new Diagnostic("Etapa 2", true, "Inventário e histórico lidos do banco instalado, sem conversão de dados."),
             new Diagnostic("Veyon", false, "Aplicação no diretório e disponibilidade no Veyon Master são resultados diferentes.")]);
    }
    public string? AppliedIp(string agentId)
    {
        using var c = Open(); using var q = c.CreateCommand();
        q.CommandText = "SELECT host FROM veyon_applied WHERE agent_id=@a";
        q.Parameters.AddWithValue("@a", agentId); return q.ExecuteScalar() as string;
    }
}
