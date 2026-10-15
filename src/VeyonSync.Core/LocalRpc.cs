using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;

namespace VeyonSync.Core;

public static class LocalRpc
{
    public static string PipeName(string role) => "VeyonSync." + role + ".v1";
    public static async Task<T> Call<T>(string role, string action, object? data = null, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var pipe = new NamedPipeClientStream(".", PipeName(role), PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new RpcRequest(action,
            data == null ? null : JsonSerializer.Serialize(data, Files.Json)), Files.Json));
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Serviço encerrou a conexão.");
        var response = JsonSerializer.Deserialize<RpcResponse>(line, Files.Json)!;
        if (!response.Ok) throw new InvalidOperationException(response.Error ?? "Operação recusada.");
        return JsonSerializer.Deserialize<T>(response.Data ?? "null", Files.Json)!;
    }
    [SupportedOSPlatform("windows")]
    public static async Task Serve(string role, Func<RpcRequest, CancellationToken, Task<object?>> handler, CancellationToken ct)
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        while (!ct.IsCancellationRequested)
        {
            using var pipe = NamedPipeServerStreamAcl.Create(PipeName(role), PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(80));
                var token = requestTimeout.Token;
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                RpcResponse response;
                try
                {
                    var line = await reader.ReadLineAsync(token) ?? throw new IOException("Pedido vazio.");
                    if (line.Length > 64 * 1024) throw new InvalidDataException("Pedido muito grande.");
                    var request = JsonSerializer.Deserialize<RpcRequest>(line, Files.Json) ?? throw new InvalidDataException("Pedido inválido.");
                    response = new(true, JsonSerializer.Serialize(await handler(request, token), Files.Json));
                }
                catch (Exception e) when (e is not OperationCanceledException) { response = new(false, Error: e.Message); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Files.Json));
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { if (ct.IsCancellationRequested) break; }
        }
    }
}
