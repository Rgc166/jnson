using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VeyonSync.Core;

namespace VeyonSync.Desktop;

public sealed class ShellViewModel(string role) : INotifyPropertyChanged
{
    public string Role { get; } = role;
    public Snapshot? Snapshot { get; private set; }
    private string heading = "Conectando ao serviço…", message = "", clock = "", service = "Aguardando serviço";
    private bool busy;
    public string Heading { get => heading; private set { heading = value; Changed(); } }
    public string Message { get => message; set { message = value; Changed(); } }
    public string Clock { get => clock; set { clock = value; Changed(); } }
    public string Service { get => service; private set { service = value; Changed(); } }
    public bool Busy { get => busy; private set { busy = value; Changed(); } }
    public bool Connected { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public async Task Refresh()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            Snapshot = await LocalRpc.Call<Snapshot>(Role, "snapshot", ct: timeout.Token);
            Connected = true; Heading = $"Laboratório {Snapshot.Lab}  ·  {Snapshot.Computer}"; Service = "● Serviço em execução";
        }
        catch (Exception e)
        {
            Connected = false; Service = "Serviço indisponível";
            Heading = "Não foi possível consultar o serviço";
            Message = "Confira a instalação e o serviço do Windows. Dados anteriores podem estar desatualizados. " + e.Message;
        }
        finally { Busy = false; Changed(nameof(Snapshot)); Changed(nameof(Connected)); }
    }
    public async Task<T> Run<T>(string action, object? data = null)
    {
        if (!Connected) throw new InvalidOperationException("O serviço está indisponível. Confira a instalação e o diagnóstico.");
        if (Busy) throw new InvalidOperationException("Aguarde a operação atual terminar.");
        Busy = true;
        try { return await LocalRpc.Call<T>(Role, action, data); } finally { Busy = false; }
    }
}
