using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace VeyonSync.Core;

public sealed class Store
{
    private readonly string connectionString;
    private readonly object gate = new();
    public Store(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 10 }.ToString();
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL");
        foreach (var sql in new[] {
            "CREATE TABLE IF NOT EXISTS machines(agent_id TEXT PRIMARY KEY, lab TEXT, name TEXT, position TEXT, user TEXT, ip TEXT, mac TEXT, last_seen TEXT, last_seq INTEGER, version TEXT)",
            "CREATE TABLE IF NOT EXISTS tokens(agent_id TEXT PRIMARY KEY, hash TEXT, lab TEXT)",
            "CREATE TABLE IF NOT EXISTS codes(hash TEXT PRIMARY KEY, expires TEXT)",
            "CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY AUTOINCREMENT, agent_id TEXT, ts TEXT, kind TEXT, detail TEXT)",
            "CREATE TABLE IF NOT EXISTS collect_requests(agent_id TEXT PRIMARY KEY)",
            "CREATE TABLE IF NOT EXISTS veyon_queue(agent_id TEXT PRIMARY KEY, status TEXT, created TEXT)",
            "CREATE TABLE IF NOT EXISTS integrations(agent_id TEXT PRIMARY KEY, status TEXT NOT NULL DEFAULT 'Pendente', error TEXT)",
            "CREATE INDEX IF NOT EXISTS ix_history_ts ON history(ts)",
            "CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY, value TEXT)",
            "CREATE TABLE IF NOT EXISTS communication(agent_id TEXT PRIMARY KEY, recent INTEGER NOT NULL)"
        }) Exec(c, sql);
        Exec(c, "INSERT OR REPLACE INTO metadata VALUES('schema','1')");
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    private static int Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string, object?)[] values)
    {
        using var m = c.CreateCommand(); m.CommandText = sql; m.Transaction = tx;
        foreach (var (key, value) in values) m.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return m.ExecuteNonQuery();
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Now() => DateTimeOffset.UtcNow.ToString("o");
    private static void History(SqliteConnection c, SqliteTransaction? tx, string id, string kind, string detail) =>
        Exec(c, "INSERT INTO history(agent_id,ts,kind,detail) VALUES(@a,@t,@k,@d)", tx,
            ("@a", id), ("@t", Now()), ("@k", kind), ("@d", detail));
    public string NewCode()
    {
        lock (gate)
        {
            using var c = Open();
            Exec(c, "DELETE FROM codes WHERE expires < @t", null, ("@t", Now()));
            // Only one live code: explicitly generating a new code cancels the old one.
            var code = RandomNumberGenerator.GetInt32(0, 100000000).ToString("D8");
            using var tx = c.BeginTransaction(); Exec(c, "DELETE FROM codes", tx);
            Exec(c, "INSERT INTO codes VALUES(@h,@e)", tx, ("@h", Hash(code)), ("@e", DateTimeOffset.UtcNow.AddMinutes(15).ToString("o")));
            tx.Commit(); return code;
        }
    }
    public string? Pair(string code, string id, string lab)
    {
        if (!Guid.TryParse(id, out _) || !Validation.Label(lab) || code.Length != 8 || !code.All(char.IsAsciiDigit)) return null;
        lock (gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            Exec(c, "DELETE FROM codes WHERE expires < @t", tx, ("@t", Now()));
            if (Exec(c, "DELETE FROM codes WHERE hash=@h", tx, ("@h", Hash(code))) != 1) return null;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Exec(c, "INSERT OR REPLACE INTO tokens VALUES(@a,@h,@l)", tx, ("@a", id), ("@h", Hash(token)), ("@l", lab));
            // A new enrollment permits a fresh monotonic sequence, retaining inventory/history.
            Exec(c, "UPDATE machines SET last_seq=0 WHERE agent_id=@a", tx, ("@a", id));
            History(c, tx, id, "Pareamento", "Agente autorizado; credencial individual emitida."); tx.Commit(); return token;
        }
    }
    public bool Authorized(string id, string token, string lab)
    {
        if (token.Length > 256) return false;
        lock (gate)
        {
            using var c = Open(); using var m = c.CreateCommand();
            m.CommandText = "SELECT hash,lab FROM tokens WHERE agent_id=@a"; m.Parameters.AddWithValue("@a", id);
            using var r = m.ExecuteReader();
            return r.Read() && string.Equals(r.GetString(1), lab, StringComparison.OrdinalIgnoreCase)
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(r.GetString(0)), Encoding.ASCII.GetBytes(Hash(token)));
        }
    }
    public void Revoke(string id)
    {
        lock (gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            Exec(c, "DELETE FROM tokens WHERE agent_id=@a", tx, ("@a", id)); History(c, tx, id, "Revogação", "Autorização do agente revogada."); tx.Commit();
        }
    }
    public void RequestCollect(string? id = null)
    {
        lock (gate)
        {
            using var c = Open();
            if (id == null) Exec(c, "INSERT OR IGNORE INTO collect_requests SELECT agent_id FROM machines");
            else Exec(c, "INSERT OR IGNORE INTO collect_requests VALUES(@a)", null, ("@a", id));
        }
    }
    public ReportAck? ReceiveAuthorized(Report report, string token, string lab)
    {
        lock (gate)
        {
            return Authorized(report.AgentId, token, lab) ? Receive(report) : null;
        }
    }
    public void UpdateCommunication()
    {
        lock (gate)
        {
            var machines = Machines();
            using var c = Open(); using var tx = c.BeginTransaction();
            foreach (var machine in machines)
            {
                var recent = DateTimeOffset.UtcNow - machine.LastSeen < TimeSpan.FromMinutes(3) ? 1 : 0;
                using var q = c.CreateCommand(); q.Transaction = tx;
                q.CommandText = "SELECT recent FROM communication WHERE agent_id=@a";
                q.Parameters.AddWithValue("@a", machine.Data.AgentId);
                var previous = q.ExecuteScalar();
                if (previous != null && Convert.ToInt32(previous) != recent)
                    History(c, tx, machine.Data.AgentId, recent == 1 ? "Comunicação recuperada" : "Sem comunicação recente",
                        recent == 1 ? "O agente voltou a enviar relatórios." : "Nenhum relatório recente; isso não comprova que o PC está desligado.");
                Exec(c, "INSERT OR REPLACE INTO communication VALUES(@a,@r)", tx, ("@a", machine.Data.AgentId), ("@r", recent));
            }
            tx.Commit();
        }
    }
    public ReportAck Receive(Report report)
    {
        if (!Validation.ValidReport(report)) throw new ArgumentException("Relatório inválido.");
        lock (gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            long previous = -1; string? ip = null, mac = null, user = null, position = null, name = null;
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx; q.CommandText = "SELECT last_seq,ip,mac,user,position,name FROM machines WHERE agent_id=@a";
                q.Parameters.AddWithValue("@a", report.AgentId); using var r = q.ExecuteReader();
                if (r.Read()) { previous = r.GetInt64(0); ip = Text(r, 1); mac = Text(r, 2); user = Text(r, 3); position = Text(r, 4); name = Text(r, 5); }
            }
            bool stale = report.Seq <= previous;
            if (stale) Exec(c, "UPDATE machines SET last_seen=@t WHERE agent_id=@a", tx, ("@t", Now()), ("@a", report.AgentId));
            else
            {
                Exec(c, "INSERT OR REPLACE INTO machines VALUES(@a,@l,@n,@p,@u,@i,@m,@t,@s,@v)", tx,
                    ("@a", report.AgentId), ("@l", report.Lab), ("@n", report.MachineName), ("@p", report.Position),
                    ("@u", report.User), ("@i", report.Ip), ("@m", report.Mac), ("@t", Now()), ("@s", report.Seq), ("@v", report.AgentVersion));
                if (previous < 0) History(c, tx, report.AgentId, "Primeiro cadastro", report.MachineName);
                else
                {
                    if (ip != report.Ip) History(c, tx, report.AgentId, "Mudança de IP", $"{ip} → {report.Ip}");
                    if (mac != report.Mac) History(c, tx, report.AgentId, "Mudança de MAC", $"{mac} → {report.Mac}");
                    if (user != report.User) History(c, tx, report.AgentId, "Mudança de usuário", $"{user ?? "Sem usuário"} → {report.User ?? "Sem usuário"}");
                }
                if (previous < 0 || ip != report.Ip || mac != report.Mac || position != report.Position || name != report.MachineName)
                {
                    Exec(c, "INSERT OR REPLACE INTO veyon_queue VALUES(@a,'pendente',@t)", tx, ("@a", report.AgentId), ("@t", Now()));
                    Exec(c, "INSERT OR REPLACE INTO integrations VALUES(@a,'Pendente',NULL)", tx, ("@a", report.AgentId));
                }
            }
            bool collect = Exec(c, "DELETE FROM collect_requests WHERE agent_id=@a", tx, ("@a", report.AgentId)) > 0;
            string integration = "Sem confirmação";
            using (var q = c.CreateCommand()) { q.Transaction = tx; q.CommandText = "SELECT status FROM integrations WHERE agent_id=@a"; q.Parameters.AddWithValue("@a", report.AgentId); integration = q.ExecuteScalar()?.ToString() ?? integration; }
            tx.Commit(); return new(true, stale, integration == "Aplicado", integration, collect);
        }
    }
    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public List<Machine> Machines()
    {
        lock (gate)
        {
            using var c = Open(); using var q = c.CreateCommand();
            q.CommandText = "SELECT m.*,i.status,i.error FROM machines m LEFT JOIN integrations i ON m.agent_id=i.agent_id ORDER BY m.position,m.name";
            using var r = q.ExecuteReader(); var list = new List<Machine>();
            while (r.Read()) { var time = DateTimeOffset.Parse(r.GetString(7)); list.Add(new(new(r.GetString(0), r.GetString(2), r.GetString(1), r.GetString(3), Text(r, 4), Text(r, 5), Text(r, 6), time, r.GetInt64(8), r.GetString(9), "inventário"), time, Text(r, 10) ?? "Pendente", Text(r, 11))); }
            return list;
        }
    }
    public List<Event> Events(int limit = 500)
    {
        lock (gate)
        {
            using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT id,agent_id,ts,kind,detail FROM history ORDER BY id DESC LIMIT @n"; q.Parameters.AddWithValue("@n", Math.Clamp(limit, 1, 10000));
            using var r = q.ExecuteReader(); var result = new List<Event>(); while (r.Read()) result.Add(new(r.GetInt64(0), r.GetString(1), DateTimeOffset.Parse(r.GetString(2)), r.GetString(3), r.GetString(4))); return result;
        }
    }
    public bool CompleteIntegration(Report applied)
    {
        lock (gate)
        {
            var current = Machines().SingleOrDefault(m => m.Data.AgentId == applied.AgentId)?.Data;
            if (current == null || current.Ip != applied.Ip || current.Mac != applied.Mac || current.Position != applied.Position || current.MachineName != applied.MachineName)
            {
                IntegrationResult(applied.AgentId, false, "Novos dados chegaram durante a aplicação. Gere outra prévia.");
                return false;
            }
            IntegrationResult(applied.AgentId, true);
            return true;
        }
    }
    public void IntegrationResult(string id, bool ok, string? error = null)
    {
        lock (gate)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            Exec(c, "INSERT OR REPLACE INTO integrations VALUES(@a,@s,@e)", tx, ("@a", id), ("@s", ok ? "Aplicado" : "Falhou"), ("@e", error));
            if (ok) Exec(c, "DELETE FROM veyon_queue WHERE agent_id=@a", tx, ("@a", id));
            History(c, tx, id, "Integração Veyon", ok ? "Cadastro aplicado e verificado; disponibilidade do Veyon Master não verificada." : error ?? "Falhou"); tx.Commit();
        }
    }
    public void Prune(int days) { lock (gate) { using var c = Open(); Exec(c, "DELETE FROM history WHERE ts<@t", null, ("@t", DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 7, 3650)).ToString("o"))); } }
    public bool Healthy() { using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "PRAGMA quick_check"; return q.ExecuteScalar()?.ToString() == "ok"; }
}
