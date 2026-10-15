using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using VeyonSync.Core;
using Validation = VeyonSync.Core.Validation;

namespace VeyonSync.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel vm;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string page = "Dashboard", search = "", filter = "Todos", selectedId = "", theme = "Escuro";
    private bool collapsed, tiles, closed, wizardOpened;
    private int ticks;
    private Action? refreshContents;
    private long lastEvent;
    private readonly string prefs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VeyonSync", "ui.json");
    private readonly Dictionary<string, string> icons = new() { { "Dashboard", "◫" }, { "Computadores", "▤" }, { "Laboratórios", "▦" }, { "Integração", "⇄" }, { "Histórico", "◷" }, { "Pareamento", "⊕" }, { "Diagnóstico", "⊙" }, { "Configurações", "⚙" } };
    public MainWindow(string role)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/VeyonSync.Desktop;component/Themes.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "Canvas"); SetResourceReference(ForegroundProperty, "Ink");
        vm = new(role); DataContext = vm; RoleLabel.Text = role == "Teacher" ? "Professor · Administração" : "Aluno · Conexão";
        Title = "VeyonSync · " + (role == "Teacher" ? "Professor" : "Aluno");
        if (role == "Student") { Width = 980; Height = 710; }
        try { theme = Files.Read<Dictionary<string, string>>(prefs).GetValueOrDefault("theme", "Escuro"); } catch (IOException) { }
        ApplyTheme(); BuildNavigation();
        Loaded += async (_, _) => { await Refresh(); timer.Start(); };
        timer.Tick += async (_, _) =>
        {
            vm.Clock = DateTime.Now.ToString("HH:mm:ss");
            if (++ticks % 5 == 0) await Refresh();
            if (page == "Pareamento" && pairCountdown != null && activeCode != null)
                pairCountdown.Text = DateTimeOffset.UtcNow >= activeCode.Expires ? "Expirado — gere um novo código." : $"Validade restante: {(activeCode.Expires - DateTimeOffset.UtcNow):mm\\:ss} · uso único";
        };
        SystemEvents.UserPreferenceChanged += WindowsPreferencesChanged;
        Closed += (_, _) => { closed = true; timer.Stop(); SystemEvents.UserPreferenceChanged -= WindowsPreferencesChanged; };
    }
    private void WindowsPreferencesChanged(object sender, UserPreferenceChangedEventArgs e) { if (theme == "Windows") Dispatcher.Invoke(ApplyTheme); }
    private bool Light() => theme == "Claro" || (theme == "Windows" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) == 1);
    private void ApplyTheme()
    {
        var colors = Light() ? new Dictionary<string, string> { { "Canvas", "#F8FAFC" }, { "Sidebar", "#FFFFFF" }, { "Card", "#FFFFFF" }, { "Primary", "#2563EB" }, { "Ink", "#0F172A" }, { "Muted", "#475569" }, { "Line", "#CBD5E1" } } :
            new Dictionary<string, string> { { "Canvas", "#0B1120" }, { "Sidebar", "#111827" }, { "Card", "#1E293B" }, { "Primary", "#3B82F6" }, { "Ink", "#F8FAFC" }, { "Muted", "#94A3B8" }, { "Line", "#334155" } };
        foreach (var (key, color) in colors)
        {
            var target = (Color)ColorConverter.ConvertFromString(color);
            var previous = (TryFindResource(key) as SolidColorBrush)?.Color ?? target;
            var brush = new SolidColorBrush(target);
            if (SystemParameters.ClientAreaAnimation)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous, target, TimeSpan.FromMilliseconds(240)));
            Resources[key] = brush;
        }
    }
    private void ChangeTheme(object sender, RoutedEventArgs e)
    {
        theme = theme switch { "Escuro" => "Claro", "Claro" => "Windows", _ => "Escuro" }; ApplyTheme();
        Files.Write(prefs, new Dictionary<string, string> { { "theme", theme } }); vm.Message = "Tema: " + theme;
    }
    private void OpenSettings(object sender, RoutedEventArgs e) => Navigate("Configurações");
    private void CollapseMenu(object sender, RoutedEventArgs e)
    {
        collapsed = !collapsed;
        var previousWidth = NavColumn.ActualWidth;
        var targetWidth = collapsed ? 76 : 220;
        NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        NavColumn.Width = new GridLength(targetWidth);
        if (SystemParameters.ClientAreaAnimation)
            NavColumn.BeginAnimation(ColumnDefinition.WidthProperty, new GridLengthAnimation { From = previousWidth, To = targetWidth, Duration = TimeSpan.FromMilliseconds(220) });
        Brand.Text = collapsed ? "VS" : "VEYONSYNC"; RoleLabel.Visibility = VersionLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; BuildNavigation();
    }
    private void BuildNavigation()
    {
        Navigation.Children.Clear();
        var names = vm.Role == "Teacher" ? icons.Keys.ToArray() : new[] { "Dashboard", "Histórico", "Pareamento", "Diagnóstico", "Configurações" };
        foreach (var name in names)
        {
            var button = Button(collapsed ? icons[name] : $"{icons[name]}   {(name == "Dashboard" && vm.Role == "Student" ? "Visão geral" : name)}", () => { Navigate(name); return Task.CompletedTask; });
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            if (name == page) button.SetResourceReference(BackgroundProperty, "Primary"); Navigation.Children.Add(button);
        }
    }
    private void Navigate(string name)
    {
        page = name; PageTitle.Text = name == "Dashboard" ? (vm.Role == "Teacher" ? "Visão geral do laboratório" : "Seu computador") : name; BuildNavigation(); Render();
        if (SystemParameters.ClientAreaAnimation) { PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))); }
    }
    private async Task Refresh()
    {
        if (closed || vm.Busy) return;
        await vm.Refresh();
        if (closed) return;
        if (vm.Connected && vm.Snapshot != null)
        {
            var newest = vm.Snapshot.Events.FirstOrDefault()?.Id ?? 0;
            if (lastEvent > 0 && newest > lastEvent) vm.Message = $"{vm.Snapshot.Events.Count(e => e.Id > lastEvent)} novos eventos. Consulte o histórico.";
            lastEvent = newest;
            if (activeCode != null && vm.Snapshot.Events.Any(e => e.Kind == "Pareamento" && e.Time >= activeCode.Expires.AddMinutes(-15)))
            {
                activeCode = null; if (codeText != null) codeText.Text = "Código utilizado"; if (pairCountdown != null) pairCountdown.Text = "Aluno pareado. Gere outro código para o próximo computador.";
            }
            if (vm.Role == "Student" && !wizardOpened && vm.Snapshot.Diagnostics.Any(d => d.Name == "Pareamento" && !d.Ok))
            {
                wizardOpened = true; Navigate("Dashboard"); await Wizard(); return;
            }
        }
        if (page is "Computadores" or "Histórico" && refreshContents != null) refreshContents();
        else if (page == "Dashboard" && vm.Role == "Teacher" && PageContent.IsKeyboardFocusWithin && refreshContents != null) refreshContents();
        else if (page is "Dashboard" or "Diagnóstico" or "Laboratórios") Render();
        else if (PageContent.Content == null) Render();
    }
    private void Render()
    {
        refreshContents = null;
        PageTitle.Text = page == "Dashboard" ? (vm.Role == "Teacher" ? "Visão geral do laboratório" : "Seu computador") : page;
        switch (page)
        {
            case "Dashboard": if (vm.Role == "Teacher") Computers(true); else StudentHome(); break;
            case "Computadores": Computers(false); break;
            case "Histórico": History(); break;
            case "Diagnóstico": Diagnostics(); break;
            case "Laboratórios": Laboratories(); break;
            case "Pareamento": Pairing(); break;
            case "Integração": Integration(); break;
            case "Configurações": _ = Attempt(Settings); break;
        }
    }
    private async Task Attempt(Func<Task> operation)
    {
        try { await operation(); } catch (Exception e) { vm.Message = e is OperationCanceledException ? "A operação excedeu o tempo de espera. Confira o serviço e o diagnóstico." : e.Message; }
    }
    private Button Button(string label, Func<Task> action, bool primary = false)
    {
        var b = new Button { Content = label }; if (primary) { b.SetResourceReference(BackgroundProperty, "Primary"); b.Foreground = Brushes.White; }
        b.Click += async (_, _) => { b.IsEnabled = false; try { await Attempt(action); } finally { b.IsEnabled = true; } }; return b;
    }
    private static TextBlock Text(string value, int size = 14) => new() { Text = value, FontSize = size, Margin = new Thickness(0, 0, 0, 10) };
    private Border Card(UIElement child) { var b = new Border { Child = child, CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Margin = new Thickness(0, 0, 12, 12) }; b.SetResourceReference(BackgroundProperty, "Card"); return b; }
    private static ScrollViewer Scroll(UIElement element) => new() { Content = element, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static void DockTop(DockPanel root, UIElement child) { DockPanel.SetDock(child, Dock.Top); root.Children.Add(child); }
    private static DataGrid Table(params (string title, string path)[] columns)
    {
        var table = new DataGrid(); foreach (var (title, path) in columns) table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); return table;
    }
    private List<Machine> VisibleMachines() => (vm.Snapshot?.Machines ?? []).Where(m =>
        ($"{m.Name} {m.Position} {m.Ip}".Contains(search, StringComparison.OrdinalIgnoreCase)) &&
        (filter == "Todos" || filter == m.Communication || filter == "Falha na integração" && m.Integration == "Falhou"))
        .OrderBy(m => m.Position, StringComparer.OrdinalIgnoreCase).ToList();
    private void Computers(bool dashboard)
    {
        var root = new DockPanel();
        if (dashboard)
        {
            var stats = new WrapPanel(); var all = vm.Snapshot?.Machines ?? [];
            foreach (var (label, value) in new[]{("Cadastrados",all.Count.ToString()),("Comunicação recente",all.Count(m=>m.Communication=="Comunicação recente").ToString()),
                ("Sem comunicação recente",all.Count(m=>m.Communication!="Comunicação recente").ToString()),("Falhas de integração",all.Count(m=>m.Integration=="Falhou").ToString()),
                ("Última comunicação",all.Count==0?"Sem dados":all.Max(m=>m.LastSeen).ToLocalTime().ToString("HH:mm:ss"))})
            {
                var panel = new StackPanel { MinWidth = 125 }; panel.Children.Add(Text(value, 24)); panel.Children.Add(Text(label, 12)); stats.Children.Add(Card(panel));
            }
            DockTop(root, stats);
        }
        var actions = new WrapPanel();
        var find = new TextBox { Text = search, Width = 210, ToolTip = "Buscar nome, posição ou IP" };
        // Preserve focus and caret across polling refreshes by updating only the result area while typing.
        actions.Children.Add(find);
        var states = new ComboBox { ItemsSource = new[] { "Todos", "Comunicação recente", "Sem comunicação recente", "Falha na integração" }, SelectedItem = filter }; actions.Children.Add(states);
        actions.Children.Add(Button(tiles ? "Mostrar lista" : "Mostrar grade", () => { tiles = !tiles; Computers(dashboard); return Task.CompletedTask; }));
        actions.Children.Add(Button("Exportar CSV", () => { ExportMachines(VisibleMachines()); return Task.CompletedTask; }));
        actions.Children.Add(Button("Solicitar coleta", async () => { vm.Message = await vm.Run<string>("collect"); })); DockTop(root, actions);
        var result = new ContentControl(); root.Children.Add(result);
        void Fill()
        {
            var machines = VisibleMachines();
            if (machines.Count == 0) { result.Content = Text(vm.Connected ? "Nenhum computador encontrado. Use Pareamento para autorizar o primeiro aluno." : "Serviço indisponível. Nenhum dado atual confirmado.", 16); return; }
            if (tiles) { var wrap = new WrapPanel(); foreach (var machine in machines) { var stack = new StackPanel { Width = 220 }; stack.Children.Add(Text(machine.Position + " · " + machine.Name, 17)); stack.Children.Add(Text(machine.Ip ?? "IP indisponível")); stack.Children.Add(Text(machine.Data.Mac ?? "MAC indisponível", 12)); stack.Children.Add(Text(machine.Communication)); stack.Children.Add(Text("Integração: " + machine.Integration, 12)); stack.Children.Add(Text(machine.LastSeen.ToLocalTime().ToString("g"), 12)); stack.Children.Add(Button("Ver detalhes", () => Details(machine))); wrap.Children.Add(Card(stack)); } result.Content = Scroll(wrap); }
            else
            {
                var table = Table(("Posição", "Position"), ("Computador", "Name"), ("Usuário", "User"), ("IP", "Ip"), ("Comunicação", "Communication"), ("Veyon", "Integration")); table.ItemsSource = machines; table.SelectedItem = machines.FirstOrDefault(m => m.Data.AgentId == selectedId);
                table.SelectionChanged += (_, _) => { if (table.SelectedItem is Machine m) selectedId = m.Data.AgentId; };
                table.MouseDoubleClick += async (_, _) => { if (table.SelectedItem is Machine m) await Attempt(() => Details(m)); };
                table.KeyDown += async (_, e) => { if (e.Key == Key.Enter && table.SelectedItem is Machine m) await Attempt(() => Details(m)); }; result.Content = table;
            }
        }
        find.TextChanged += (_, _) => { search = find.Text; Fill(); }; states.SelectionChanged += (_, _) => { filter = (string)states.SelectedItem; Fill(); }; Fill(); refreshContents = Fill; PageContent.Content = root;
        if (Keyboard.FocusedElement is TextBox && search != "") { find.Focus(); find.CaretIndex = find.Text.Length; }
    }
    private Window Dialog(string title, UIElement content, int width = 640, int height = 580)
    {
        var window = new Window { Owner = this, Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content, Padding = new Thickness(24) };
        window.Resources.MergedDictionaries.Add(Resources); window.SetResourceReference(BackgroundProperty, "Canvas"); window.SetResourceReference(ForegroundProperty, "Ink"); return window;
    }
    private Task Details(Machine machine)
    {
        var panel = new StackPanel(); panel.Children.Add(Text(machine.Position + " · " + machine.Name, 24));
        panel.Children.Add(Text($"Laboratório {machine.Data.Lab}\nIdentidade: {machine.Data.AgentId}\nUsuário: {machine.User ?? "Sem usuário"}\nIP: {machine.Ip}\nMAC: {machine.Data.Mac}\nÚltima comunicação: {machine.LastSeen.ToLocalTime():G}\nIntegração: {machine.Integration}\n{machine.IntegrationError}"));
        panel.Children.Add(Button("Solicitar nova coleta", async () => vm.Message = await vm.Run<string>("collect", machine.Data.AgentId), true));
        panel.Children.Add(Button("Revogar autorização", async () => { if (MessageBox.Show(this, "Este aluno precisará de um novo código para voltar a enviar dados. Revogar?", "Revogar agente", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) vm.Message = await vm.Run<string>("revoke", machine.Data.AgentId); }));
        panel.Children.Add(Text("Histórico deste computador", 18));
        foreach (var ev in (vm.Snapshot?.Events ?? []).Where(e => e.AgentId == machine.Data.AgentId)) panel.Children.Add(Text($"{ev.Time.ToLocalTime():g} · {ev.Kind}\n{ev.Detail}", 12));
        Dialog("Detalhes do computador", Scroll(panel)).ShowDialog(); return Task.CompletedTask;
    }
    private void StudentHome()
    {
        var panel = new StackPanel(); var state = vm.Snapshot?.Agent;
        panel.Children.Add(Text(state?.Status ?? "Aguardando serviço", 24));
        var card = new StackPanel();
        card.Children.Add(Text($"Laboratório: {vm.Snapshot?.Lab ?? "—"}\nComputador: {Environment.MachineName}\nÚltima confirmação: {state?.LastConfirmed?.ToLocalTime().ToString("G") ?? "Nenhum envio confirmado"}\nVeyon: {state?.VeyonAvailability ?? "Sem confirmação"}"));
        if ((state?.Current ?? state?.Pending) is { } pending) card.Children.Add(Text($"Última coleta: {pending.Position} · {pending.User ?? "Sem usuário"}\nIP: {pending.Ip ?? "Indisponível"} · MAC: {pending.Mac ?? "Indisponível"}"));
        if (state?.LastError is { } error) { var text = Text(error); text.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); card.Children.Add(text); }
        panel.Children.Add(Card(card));
        panel.Children.Add(Button("Sincronizar agora", async () => { vm.Message = await vm.Run<string>("send"); await Refresh(); }, true));
        panel.Children.Add(Button("Ver detalhes da conexão", async () => { var cfg = await vm.Run<AgentConfig>("settings"); vm.Message = $"Professor: {cfg.TeacherUrl} · Posição: {cfg.Position} · Automático: {(cfg.AutoSend ? "ligado" : "desligado")}"; }));
        panel.Children.Add(Button("Testar conexão segura", async () => { vm.Message = await vm.Run<string>("diagnose"); }));
        panel.Children.Add(Text("Fechar esta janela mantém o serviço funcionando. Confirmação do professor e aplicação no Veyon são resultados separados.", 12)); PageContent.Content = Scroll(panel);
    }
    private string historySearch = "", historyKind = "Todos";
    private DateTime? historyFrom;
    private void History()
    {
        var root = new DockPanel(); var bar = new WrapPanel(); var find = new TextBox { Text = historySearch, Width = 210, ToolTip = "Buscar computador, identidade ou detalhe" };
        var kinds = new ComboBox { ItemsSource = new[] { "Todos" }.Concat((vm.Snapshot?.Events ?? []).Select(e => e.Kind).Distinct()).ToArray(), SelectedItem = historyKind };
        var date = new DatePicker { SelectedDate = historyFrom, ToolTip = "Data inicial", Margin = new Thickness(0, 4, 8, 12), Width = 150 }; bar.Children.Add(find); bar.Children.Add(kinds); bar.Children.Add(date);
        var table = Table(("Quando", "Time"), ("Identidade", "AgentId"), ("Evento", "Kind"), ("Detalhes", "Detail"));
        List<Event> Current() => (vm.Snapshot?.Events ?? []).Where(e => (historyKind == "Todos" || e.Kind == historyKind)
            && (historyFrom == null || e.Time.LocalDateTime.Date >= historyFrom.Value.Date)
            && ($"{e.AgentId} {e.Detail} {vm.Snapshot?.Machines.FirstOrDefault(m => m.Data.AgentId == e.AgentId)?.Name}".Contains(historySearch, StringComparison.OrdinalIgnoreCase))).ToList();
        void Fill() => table.ItemsSource = Current();
        find.TextChanged += (_, _) => { historySearch = find.Text; Fill(); }; kinds.SelectionChanged += (_, _) => { historyKind = kinds.SelectedItem?.ToString() ?? "Todos"; Fill(); }; date.SelectedDateChanged += (_, _) => { historyFrom = date.SelectedDate; Fill(); };
        bar.Children.Add(Button("Exportar CSV", () => { SaveCsv("historico.csv", new[] { new[] { "Horário", "Identidade", "Evento", "Detalhes" } }.Concat(Current().Select(e => new[] { e.Time.ToLocalTime().ToString("O"), e.AgentId, e.Kind, e.Detail }))); return Task.CompletedTask; }));
        DockTop(root, bar); DockTop(root, Text("Exibindo os últimos 500 eventos disponíveis. A retenção do professor é configurável; o aluno mantém até 500 eventos locais.", 12)); root.Children.Add(table); Fill(); refreshContents = Fill; PageContent.Content = root;
    }
    private void Diagnostics()
    {
        var panel = new StackPanel();
        foreach (var check in vm.Snapshot?.Diagnostics ?? []) panel.Children.Add(Card(Text($"{(check.Ok ? "✓" : "!")} {check.Name}\n{check.Detail}")));
        panel.Children.Add(Button("Exportar diagnóstico TXT", () =>
        {
            var picker = new SaveFileDialog { FileName = "VeyonSync-diagnostico.txt", Filter = "Texto|*.txt" };
            if (picker.ShowDialog(this) == true) { var s = vm.Snapshot; File.WriteAllText(picker.FileName, $"VeyonSync 0.2.0 · {DateTimeOffset.Now:O}\n{vm.Heading}\n{vm.Service}\n" + string.Join("\n", s?.Diagnostics.Select(d => $"{d.Name}: {d.Ok} · {d.Detail}") ?? []) + "\n" + string.Join("\n", s?.Events.Where(e => e.Kind.Contains("Falha") || e.Kind.Contains("Integração")).Take(50).Select(e => $"{e.Time:O} {e.Kind}: {e.Detail}") ?? []), new UTF8Encoding(true)); vm.Message = "Diagnóstico exportado. Revise nomes de usuários e endereços antes de compartilhar."; }
            return Task.CompletedTask;
        }));
        if (vm.Role == "Student") panel.Children.Add(Button("Testar HTTPS do professor", async () => vm.Message = await vm.Run<string>("diagnose"), true));
        panel.Children.Add(Text("A validação de certificados permanece ativa. Não importe chaves privadas no aluno.", 12)); PageContent.Content = Scroll(panel);
    }
    private void Laboratories()
    {
        var panel = new StackPanel(); panel.Children.Add(Text("Laboratório " + (vm.Snapshot?.Lab ?? "—"), 24));
        panel.Children.Add(Text("Este professor administra somente o laboratório configurado na instalação. O nome do Windows não define a sala física."));
        panel.Children.Add(Text("Para mudar o laboratório ou o professor, faça uma reconfiguração administrativa explícita e pareie novamente os alunos. Nenhum certificado ou aluno será transferido automaticamente.")); PageContent.Content = Scroll(panel);
    }
    private PairingCode? activeCode;
    private TextBlock? pairCountdown, codeText;
    private void Pairing()
    {
        var panel = new StackPanel();
        if (vm.Role == "Student") { panel.Children.Add(Text("Autorizar este computador", 22)); panel.Children.Add(Text("Separe o certificado público e um código de uso único gerado no professor do laboratório correto.")); panel.Children.Add(Button("Abrir assistente de pareamento", Wizard, true)); }
        else
        {
            panel.Children.Add(Text("Adicionar um aluno", 22)); panel.Children.Add(Text("Cada código autoriza um único computador e expira em 15 minutos. Gerar outro código cancela o anterior."));
            var code = Text(activeCode?.Code ?? "Nenhum código gerado", 36); codeText = code; pairCountdown = Text("");
            panel.Children.Add(Card(code)); panel.Children.Add(pairCountdown);
            panel.Children.Add(Button("Gerar código de pareamento", async () => { activeCode = await vm.Run<PairingCode>("new-code"); code.Text = activeCode.Code; vm.Message = $"Código gerado para {activeCode.Teacher} · Laboratório {activeCode.Lab}. Não compartilhe em logs."; }, true));
            panel.Children.Add(Text("A confirmação de novos pareamentos aparece no histórico. O código fica somente nesta tela enquanto válido.", 12));
        }
        PageContent.Content = Scroll(panel);
    }
    private void Integration()
    {
        var panel = new StackPanel(); panel.Children.Add(Text("Prévia antes de aplicar", 22));
        panel.Children.Add(Text("A integração usa uma localização VeyonSync dedicada e nomes estáveis por agente. Cadastros existentes fora desse controle são preservados. Feche o Configurator durante a aplicação."));
        var machines = vm.Snapshot?.Machines ?? [];
        var choice = new ComboBox { ItemsSource = machines, DisplayMemberPath = "Position" }; panel.Children.Add(choice);
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 }; panel.Children.Add(preview); IntegrationPreview? plan = null;
        var apply = Button("Criar backup e aplicar", async () =>
        {
            if (plan == null) return;
            if (MessageBox.Show(this, $"Aplicar o cadastro {plan.Name} em {plan.Location}? Um backup da configuração Veyon será criado antes.", "Confirmar integração", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            vm.Message = await vm.Run<string>("apply", plan); plan = null; preview.Text = "Operação concluída. Gere outra prévia para aplicar novamente."; await Refresh();
        }, true); apply.IsEnabled = false;
        choice.SelectionChanged += (_, _) => { plan = null; apply.IsEnabled = false; preview.Text = "Gere uma prévia para o computador selecionado."; };
        panel.Children.Add(Button("Gerar prévia", async () =>
        {
            if (choice.SelectedItem is not Machine machine) throw new InvalidOperationException("Selecione um computador.");
            plan = await vm.Run<IntegrationPreview>("preview", machine.Data.AgentId);
            preview.Text = $"{plan.Action}: {plan.Name}\nLocalização: {plan.Location}\nIP atual: {plan.OldIp ?? "Novo cadastro"}\nNovo IP: {plan.NewIp}\nMAC: {plan.Mac}"; apply.IsEnabled = true;
        })); panel.Children.Add(apply);
        panel.Children.Add(Button("Restaurar backup…", async () =>
        {
            var backups = await vm.Run<string[]>("backups"); if (backups.Length == 0) throw new InvalidOperationException("Nenhum backup disponível.");
            var content = new StackPanel(); content.Children.Add(Text("Restaura toda a configuração do Veyon presente no backup, inclusive alterações de outros administradores feitas depois dele. Um backup de segurança será criado."));
            var list = new ComboBox { ItemsSource = backups, SelectedIndex = 0 }; content.Children.Add(list); Window? dialog = null;
            content.Children.Add(Button("Confirmar restauração completa", async () => { if (MessageBox.Show(this, "Restaurar toda a configuração do Veyon selecionada?", "Restaurar", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) { vm.Message = await vm.Run<string>("restore", (string)list.SelectedItem); dialog?.Close(); } }));
            dialog = Dialog("Restaurar configuração Veyon", content, 660, 420); dialog.ShowDialog();
        }));
        panel.Children.Add(Text("Cadastro aplicado não confirma disponibilidade no Veyon Master. A versão instalada do CLI ainda precisa ser validada no Windows.", 12)); PageContent.Content = Scroll(panel);
    }
    private async Task Settings()
    {
        var panel = new StackPanel();
        if (vm.Role == "Teacher")
        {
            var cfg = await vm.Run<TeacherConfig>("settings"); panel.Children.Add(Text($"Laboratório {cfg.Lab} · {cfg.TeacherName} · Porta {cfg.Port}"));
            panel.Children.Add(Text("Executável do Veyon CLI")); var path = new TextBox { Text = cfg.VeyonCli }; panel.Children.Add(path);
            panel.Children.Add(Button("Selecionar executável", () => { var file = new OpenFileDialog { Filter = "Veyon CLI|veyon-cli.exe" }; if (file.ShowDialog(this) == true) path.Text = file.FileName; return Task.CompletedTask; }));
            panel.Children.Add(Text("Retenção do histórico em dias (7–3650)")); var days = new TextBox { Text = cfg.RetentionDays.ToString() }; panel.Children.Add(days);
            panel.Children.Add(Button("Salvar configurações", async () => { if (!int.TryParse(days.Text, out var value)) throw new ArgumentException("Informe um número de dias."); cfg.VeyonCli = path.Text; cfg.RetentionDays = value; vm.Message = await vm.Run<string>("save-settings", cfg); }, true));
        }
        else
        {
            var cfg = await vm.Run<AgentConfig>("settings"); panel.Children.Add(Text($"Professor: {cfg.TeacherUrl}\nLaboratório: {cfg.Lab}\nIdentidade: {cfg.AgentId}"));
            panel.Children.Add(Text("Posição física")); var position = new TextBox { Text = cfg.Position }; panel.Children.Add(position);
            var auto = new CheckBox { Content = "Envio automático", IsChecked = cfg.AutoSend }; panel.Children.Add(auto);
            panel.Children.Add(Text("Intervalo de presença em segundos (15–3600)")); var interval = new TextBox { Text = cfg.PresenceSeconds.ToString() }; panel.Children.Add(interval);
            panel.Children.Add(Button("Salvar configurações", async () => { if (!int.TryParse(interval.Text, out var value)) throw new ArgumentException("Informe um intervalo em segundos."); cfg.Position = position.Text; cfg.AutoSend = auto.IsChecked == true; cfg.PresenceSeconds = value; vm.Message = await vm.Run<string>("save-settings", cfg); }, true));
            panel.Children.Add(Text("Com o envio automático desligado, pedidos do professor só serão buscados no próximo envio manual.", 12));
        }
        panel.Children.Add(Text("Tema: " + theme + ". Use o botão Tema na barra superior para alternar entre escuro, claro e Windows.", 12)); PageContent.Content = Scroll(panel);
    }
    private Task Wizard()
    {
        var panel = new StackPanel(); var heading = Text("", 23); panel.Children.Add(heading); var progress = new ProgressBar { Minimum = 0, Maximum = 6, Height = 5, Margin = new Thickness(0, 0, 0, 22) }; panel.Children.Add(progress);
        var body = new StackPanel(); panel.Children.Add(body); var error = Text(""); error.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); panel.Children.Add(error);
        var lab = new TextBox { Text = vm.Snapshot?.Lab ?? "A" }; var url = new TextBox { Text = "https://PROFESSOR-LAB-A:5443" };
        var certificate = new TextBox { IsReadOnly = true }; var fingerprint = Text("Selecione um certificado público recebido do professor por um meio confiável.", 12);
        var position = new TextBox { Text = "PC01" }; var code = new PasswordBox(); var replacement = new CheckBox { Content = "Confirmo a substituição de um pareamento anterior, caso exista." };
        var footer = new WrapPanel(); int step = 0; Window? dialog = null;
        Button? back = null, next = null;
        back = Button("Voltar", () => { if (step > 0) { step--; Show(); } return Task.CompletedTask; });
        next = Button("Continuar", async () =>
        {
            try
            {
                error.Text = "";
                if (step == 0 && !Validation.Label(lab.Text)) throw new ArgumentException("Informe um laboratório válido, por exemplo B.");
                if (step == 1) _ = Validation.TeacherUri(url.Text.Trim());
                if (step == 2 && !File.Exists(certificate.Text)) throw new ArgumentException("Selecione o certificado público do professor.");
                if (step == 3 && !Validation.Label(position.Text)) throw new ArgumentException("Informe a posição, por exemplo PC03.");
                if (step == 4 && (code.Password.Length != 8 || !code.Password.All(char.IsAsciiDigit))) throw new ArgumentException("O código deve conter oito números.");
                if (step < 5) { step++; Show(); return; }
                var result = await vm.Run<string>("pair", new { TeacherUrl = url.Text.Trim(), Lab = lab.Text.Trim(), Position = position.Text.Trim(), CertificatePath = certificate.Text, Code = code.Password, ConfirmReplacement = replacement.IsChecked == true });
                code.Clear(); vm.Message = result; dialog?.Close(); await Refresh();
            }
            catch (Exception e) { error.Text = e.Message; }
        }, true);
        footer.Children.Add(back); footer.Children.Add(next); panel.Children.Add(footer);
        void Show()
        {
            body.Children.Clear(); error.Text = ""; progress.Value = step + 1; back!.IsEnabled = step > 0; next!.Content = step == 5 ? "Testar e concluir pareamento" : "Continuar";
            heading.Text = $"Etapa {step + 1} de 6 · " + new[] { "Laboratório", "Professor", "Certificado público", "Posição", "Código temporário", "Conferir e conectar" }[step];
            switch (step)
            {
                case 0: body.Children.Add(Text("Qual laboratório este computador pertence?")); body.Children.Add(lab); break;
                case 1: body.Children.Add(Text("Endereço HTTPS do professor. Um IP exige certificado válido para esse IP.")); body.Children.Add(url); break;
                case 2:
                    body.Children.Add(certificate); body.Children.Add(Button("Selecionar certificado .cer", () =>
                    {
                        var file = new OpenFileDialog { Filter = "Certificado público|*.cer;*.crt" };
                        if (file.ShowDialog(dialog) == true) { using var cert = X509CertificateLoader.LoadCertificateFromFile(file.FileName); certificate.Text = file.FileName; fingerprint.Text = $"Nome: {cert.GetNameInfo(X509NameType.DnsName, false)}\nValidade: {cert.NotAfter:d}\nSHA-256: {cert.GetCertHashString(HashAlgorithmName.SHA256)}\nConfira esta impressão digital com o administrador do professor."; }
                        return Task.CompletedTask;
                    })); body.Children.Add(fingerprint); break;
                case 3: body.Children.Add(Text("Posição física, por exemplo PC03.")); body.Children.Add(position); break;
                case 4: body.Children.Add(Text("Digite o código de uso único gerado no professor. Ele vale 15 minutos e não será salvo em logs.")); body.Children.Add(code); break;
                case 5: body.Children.Add(Text($"Laboratório {lab.Text} · {position.Text}\nProfessor: {url.Text}\nCertificado: {Path.GetFileName(certificate.Text)}")); body.Children.Add(replacement); body.Children.Add(Text("A conexão será testada com validação do nome, validade e certificado selecionado. O certificado não será instalado como raiz global do Windows.", 12)); break;
            }
        }
        Show(); dialog = Dialog("Parear aluno", Scroll(panel), 720, 620); dialog.ShowDialog(); return Task.CompletedTask;
    }
    private static string Cell(string? text)
    {
        text ??= "";
        if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private void SaveCsv(string name, IEnumerable<string?[]> rows)
    {
        var file = new SaveFileDialog { FileName = name, Filter = "CSV|*.csv" }; if (file.ShowDialog(this) != true) return;
        File.WriteAllLines(file.FileName, rows.Select(row => string.Join(";", row.Select(Cell))), new UTF8Encoding(true)); vm.Message = "Exportação concluída.";
    }
    private void ExportMachines(IEnumerable<Machine> machines) => SaveCsv("computadores.csv", new[] { new[] { "Identidade", "Computador", "Posição", "Laboratório", "IP", "MAC", "Usuário", "Última comunicação", "Integração" } }.Concat(machines.Select(m => new[] { m.Data.AgentId, m.Name, m.Position, m.Data.Lab, m.Ip, m.Data.Mac, m.User, m.LastSeen.ToLocalTime().ToString("O"), m.Integration })));
}
