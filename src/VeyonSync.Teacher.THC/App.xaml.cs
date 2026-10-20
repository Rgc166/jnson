using System.IO;
using System.Windows;
using System.Windows.Threading;
namespace VeyonSync.Teacher.THC;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool smoke = e.Args.Contains("--smoke-test");
        DispatcherUnhandledException += (_, error) => {
            error.Handled = true;
            if (smoke) { File.WriteAllText("thc-smoke-error.txt", error.Exception.ToString()); Shutdown(1); }
            else MessageBox.Show(error.Exception.Message, "VeyonSync THC", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        try {
            MainWindow = new VeyonSync.Desktop.MainWindow("Teacher", stage2: true);
            MainWindow.Show();
            if (smoke) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                var ok = MainWindow.IsLoaded && MainWindow.IsVisible && MainWindow.Title.Contains("THC");
                MainWindow.Close(); Shutdown(ok ? 0 : 1);
            }));
        } catch (Exception error) {
            if (smoke) File.WriteAllText("thc-smoke-error.txt", error.ToString());
            else MessageBox.Show(error.Message, "VeyonSync THC", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
