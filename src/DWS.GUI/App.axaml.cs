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
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ViewModels.MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
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
