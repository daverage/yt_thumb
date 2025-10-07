using System.IO;
using System.Windows;
using System;

namespace ThumbPick.Gui;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");
        File.AppendAllText(logPath, $"[{DateTime.Now:O}] OnStartup entered\n");

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
