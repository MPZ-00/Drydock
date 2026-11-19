using System.Windows;
using System.Windows.Threading;
using Drydock.App.ViewModels;
using Drydock.Core;

namespace Drydock.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settings = Settings.Load();
        ApplyTheme(settings.Theme);
        var gameDir = GameInstall.Find(Environment.GetEnvironmentVariable("SE2_GAME_DIR") ?? settings.GameDir);
        if (gameDir == null)
        {
            var picker = new GameDirWindow();
            if (picker.ShowDialog() != true || picker.GameDir == null)
            {
                Shutdown();
                return;
            }
            gameDir = picker.GameDir;
            settings = settings with { GameDir = gameDir };
            settings.Save();
        }
        // before any window touches a type from the game's assemblies
        GameInstall.Register(gameDir);

        var vm = new MainViewModel(settings);
        vm.ThemeChanged += ApplyTheme;
        var window = new MainWindow(vm);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    static void ApplyTheme(string theme) =>
        Current.ThemeMode = theme switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ErrorWindow.Show(MainWindow, e.Exception);
    }
}
