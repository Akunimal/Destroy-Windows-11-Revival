using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DWS.GUI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DWS.GUI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        Program.Log("App.Initialize started.");
        AvaloniaXamlLoader.Load(this);
        Program.Log("App.Initialize completed.");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Program.Log("OnFrameworkInitializationCompleted started.");
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Program.Log("Creating MainWindow and resolving MainViewModel...");
                var vm = Services.GetRequiredService<ViewModels.MainViewModel>();
                Program.Log($"MainViewModel resolved. Rules count: {vm.AvailableRules.Count}");
                var win = new MainWindow
                {
                    DataContext = vm
                };
                desktop.MainWindow = win;
                Program.Log("desktop.MainWindow assigned.");
                win.Show();
                Program.Log("win.Show() called.");
                win.Activate();
                Program.Log("win.Activate() called.");
            }

            base.OnFrameworkInitializationCompleted();
            Program.Log("OnFrameworkInitializationCompleted completed.");
        }
        catch (Exception ex)
        {
            Program.Log($"ERROR in OnFrameworkInitializationCompleted: {ex}");
            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.MainViewModel>();
        services.AddSingleton<Services.DashboardClient>();
        services.AddSingleton<Core.Rules.SecureRuleEngine>();
        services.AddSingleton<Core.Logging.TransactionJournal>();
        services.AddSingleton<Core.EnterprisePipelineCoordinator>();
    }
}
