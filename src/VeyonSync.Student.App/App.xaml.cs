using System.Windows;
namespace VeyonSync.Student.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, error) => { MessageBox.Show(error.Exception.Message, "VeyonSync", MessageBoxButton.OK, MessageBoxImage.Error); error.Handled = true; };
        MainWindow = new VeyonSync.Desktop.MainWindow("Student"); MainWindow.Show();
    }
}
