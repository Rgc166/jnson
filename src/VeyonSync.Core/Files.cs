using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.Versioning;

namespace VeyonSync.Core;

public static class Files
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static string DataDir(string role) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VeyonSync", role);
    public static T Read<T>(string path) where T : new() => File.Exists(path)
        ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Arquivo JSON vazio.")
        : new T();
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Json); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    [SupportedOSPlatform("windows")]
    public static void RestrictDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Pasta de dados não pode ser um link.");
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        acl.SetOwner(admin);
        foreach (var sid in new[] { admin, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
    }
}
