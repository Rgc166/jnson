using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VeyonSync.Core;

public interface ICommandRunner { Task<string> Run(string executable, IReadOnlyList<string> args, CancellationToken ct); }
public sealed class CommandRunner : ICommandRunner
{
    public async Task<string> Run(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException("Não foi possível iniciar o Veyon CLI.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var output = p.StandardOutput.ReadToEndAsync(timeout.Token); var error = p.StandardError.ReadToEndAsync(timeout.Token);
        try { await p.WaitForExitAsync(timeout.Token); } catch { if (!p.HasExited) p.Kill(true); throw; }
        var text = await output; var err = await error;
        if (p.ExitCode != 0) throw new IOException($"Veyon CLI retornou {p.ExitCode}: {err[..Math.Min(err.Length, 1000)]}");
        return text;
    }
}
public sealed class VeyonIntegration(TeacherConfig config, string dataDir, ICommandRunner runner)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private string OwnershipPath => Path.Combine(dataDir, "veyon-owned.json");
    public static string ManagedName(string lab, string id) => $"VS-{lab}-{Guid.Parse(id):N}";
    private static JsonArray Objects(JsonNode root)
    {
        var value = root["BuiltinDirectory"]?["NetworkObjects"];
        return value as JsonArray ?? (value as JsonObject)?["JsonStoreArray"] as JsonArray
            ?? throw new InvalidDataException("Formato do Veyon não reconhecido: BuiltinDirectory/NetworkObjects ausente. Integração bloqueada.");
    }
    private static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? "";
    private async Task<JsonNode> Export(string file, CancellationToken ct)
    {
        await runner.Run(config.VeyonCli, ["config", "export", file], ct);
        return JsonNode.Parse(await File.ReadAllTextAsync(file, ct)) ?? throw new InvalidDataException("Exportação vazia.");
    }
    private async Task<JsonNode> Read(CancellationToken ct)
    {
        Directory.CreateDirectory(dataDir); var path = Path.Combine(dataDir, "inspect-" + Guid.NewGuid().ToString("N") + ".json");
        try { return await Export(path, ct); } finally { if (File.Exists(path)) File.Delete(path); }
    }
    private IntegrationPreview Plan(Report report, JsonNode root)
    {
        if (string.IsNullOrEmpty(report.Ip) || string.IsNullOrEmpty(report.Mac)) throw new InvalidOperationException("IP e MAC são necessários para aplicar no Veyon.");
        var name = ManagedName(config.Lab, report.AgentId); var objects = Objects(root);
        var owned = Files.Read<Dictionary<string, string>>(OwnershipPath);
        var matches = objects.Where(x => x != null && Text(x, "Name") == name).ToList();
        if (matches.Count > 1) throw new InvalidOperationException("Nome duplicado no Veyon; resolva o conflito antes de continuar.");
        var existing = matches.SingleOrDefault();
        if (existing != null && (!owned.TryGetValue(report.AgentId, out var uid) || Text(existing, "Uid") != uid))
            throw new InvalidOperationException("Já existe um objeto com esse nome fora do controle do VeyonSync. Nenhum cadastro foi alterado.");
        if (existing != null && existing["Type"]?.GetValue<int>() != 3) throw new InvalidOperationException("O objeto existente não é um computador.");
        var location = "VeyonSync-" + config.Lab;
        var locations = objects.Where(x => x != null && Text(x, "Name") == location).ToList();
        if (locations.Count > 1 || locations.Any(x => x!["Type"]?.GetValue<int>() != 2)) throw new InvalidOperationException("Conflito na localização do laboratório.");
        if (existing != null && (locations.Count != 1 || Text(existing, "ParentUid") != Text(locations[0]!, "Uid")))
            throw new InvalidOperationException("Objeto movido para outro laboratório no Veyon. Operação bloqueada.");
        return new(report.AgentId, name, existing == null ? null : Text(existing, "HostAddress"), report.Ip, report.Mac, location,
            existing == null ? "Cadastrar" : "Atualizar");
    }
    public async Task<List<IntegrationPreview>> Preview(IEnumerable<Machine> machines, CancellationToken ct)
    {
        await mutex.WaitAsync(ct); try { var root = await Read(ct); return machines.Select(m => Plan(m.Data, root)).ToList(); } finally { mutex.Release(); }
    }
    public async Task<string> Apply(Report report, IntegrationPreview approved, CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {

            var backups = Path.Combine(dataDir, "backups"); Directory.CreateDirectory(backups);
            var backup = Path.Combine(backups, $"veyon-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            var before = await Export(backup, ct); var plan = Plan(report, before);
            if (plan != approved) throw new InvalidOperationException("Os dados mudaram desde a prévia. Gere outra prévia antes de aplicar.");
            // Recheck immediately before writing; do not run Configurator edits concurrently.
            var check = await Read(ct);
            if (!JsonNode.DeepEquals(before, check)) throw new InvalidOperationException("O Veyon foi alterado durante a operação; tente novamente.");
            var updatedObjects = (JsonArray)Objects(before).DeepClone();
            var group = updatedObjects.SingleOrDefault(x => x != null && Text(x, "Name") == plan.Location);
            if (group == null)
            {
                group = new JsonObject { ["Type"] = 2, ["Name"] = plan.Location, ["Uid"] = Guid.NewGuid().ToString("B"), ["ParentUid"] = Guid.Empty.ToString("B"), ["HostAddress"] = "", ["MacAddress"] = "", ["DirectoryAddress"] = "" };
                updatedObjects.Add(group);
            }
            var target = updatedObjects.SingleOrDefault(x => x != null && Text(x, "Name") == plan.Name);
            if (target == null)
            {
                target = new JsonObject { ["Type"] = 3, ["Name"] = plan.Name, ["Uid"] = Guid.NewGuid().ToString("B"), ["ParentUid"] = Text(group, "Uid"), ["DirectoryAddress"] = "" };
                updatedObjects.Add(target);
            }
            // Preserve the existing UUID and every unrelated field. CSV import derives a new UUID from IP/MAC.
            target["HostAddress"] = plan.NewIp; target["MacAddress"] = plan.Mac;
            var payload = updatedObjects.ToJsonString();
            if (payload.Length + payload.Count(c => c == '"' || c == '\\') + config.VeyonCli.Length + 512 > 30000)
                throw new InvalidOperationException("Diretório grande demais para o limite seguro da linha de comando Windows. Nenhum cadastro foi alterado.");
            var current = await runner.Run(config.VeyonCli, ["config", "get", "BuiltinDirectory/NetworkObjects"], ct);
            if (!JsonNode.DeepEquals(Objects(before), JsonNode.Parse(current))) throw new IOException("CLI e exportação divergem; aplicação bloqueada.");
            await runner.Run(config.VeyonCli, ["config", "set", "BuiltinDirectory/NetworkObjects", payload, "json"], ct);
            var after = await Read(ct); var objects = Objects(after);
            var updated = objects.SingleOrDefault(x => x != null && Text(x, "Name") == plan.Name)
                ?? throw new IOException("Cadastro não encontrado após a importação. Backup preservado.");
            if (Text(updated, "HostAddress") != plan.NewIp || Text(updated, "MacAddress").Replace(":", "-").ToUpperInvariant() != plan.Mac!.Replace(":", "-").ToUpperInvariant())
                throw new IOException("O resultado do Veyon diverge da prévia. Backup preservado.");
            foreach (var previous in Objects(before).Where(x => x != null && Text(x, "Name") != plan.Name))
            {
                var same = objects.SingleOrDefault(x => x != null && Text(x, "Uid") == Text(previous!, "Uid"));
                if (!JsonNode.DeepEquals(previous, same)) throw new IOException("Um objeto não relacionado mudou. Confira o backup; nenhuma restauração automática foi executada.");
            }
            if (!JsonNode.DeepEquals(updatedObjects, objects)) throw new IOException("Diretório final diverge do plano aprovado. Backup preservado.");
            var oldOther = before.DeepClone(); var newOther = after.DeepClone();
            oldOther["BuiltinDirectory"]!.AsObject().Remove("NetworkObjects"); newOther["BuiltinDirectory"]!.AsObject().Remove("NetworkObjects");
            if (!JsonNode.DeepEquals(oldOther, newOther)) throw new IOException("Configurações externas ao diretório mudaram. Confira o backup.");
            var owned = Files.Read<Dictionary<string, string>>(OwnershipPath); owned[report.AgentId] = Text(updated, "Uid"); Files.Write(OwnershipPath, owned);
            return Path.GetFileName(backup);
        }
        finally { mutex.Release(); }
    }
    public string[] Backups() => Directory.Exists(Path.Combine(dataDir, "backups"))
        ? Directory.GetFiles(Path.Combine(dataDir, "backups"), "veyon-*.json").Select(Path.GetFileName).OfType<string>().OrderDescending().ToArray() : [];
    public async Task Restore(string name, CancellationToken ct)
    {
        if (Path.GetFileName(name) != name || !Backups().Contains(name)) throw new ArgumentException("Selecione um backup gerado pelo VeyonSync.");
        await mutex.WaitAsync(ct); try
        {
            var path = Path.Combine(dataDir, "backups", name);
            var wanted = JsonNode.Parse(await File.ReadAllTextAsync(path, ct))!; _ = Objects(wanted);
            var safety = Path.Combine(dataDir, "backups", $"veyon-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            await Export(safety, ct);
            await runner.Run(config.VeyonCli, ["config", "import", path], ct);
            var restored = await Read(ct);
            if (!JsonNode.DeepEquals(Objects(wanted), Objects(restored))) throw new IOException("A restauração não passou na conferência.");
        }
        finally { mutex.Release(); }
    }
}
