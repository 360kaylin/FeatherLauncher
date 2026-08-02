using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FeatherLauncher.Core.Services;
using FeatherLauncher.Infrastructure.Authentication;
using FeatherLauncher.Infrastructure.Logging;
using FeatherLauncher.Infrastructure.Minecraft;
using FeatherLauncher.Infrastructure.Paths;
using FeatherLauncher.Infrastructure.Settings;
using FeatherLauncher.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FeatherLauncher.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths, AppPaths>(); services.AddSingleton<ILogRedactor, LogRedactor>(); services.AddSingleton<ISettingsService, JsonSettingsService>(); services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<IAuthenticationConfigurationProvider, EnvironmentAuthenticationConfigurationProvider>(); services.AddSingleton<IMicrosoftAuthenticationService, DisabledMicrosoftAuthenticationService>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }); services.AddSingleton<IMinecraftMetadataService, MinecraftMetadataService>();
        services.AddLogging(builder => { builder.SetMinimumLevel(LogLevel.Information); builder.Services.AddSingleton<ILoggerProvider, RedactingFileLoggerProvider>(); });
        var provider = services.BuildServiceProvider(); provider.GetRequiredService<IAppPaths>().EnsureCreated();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<IAppPaths>(), provider.GetRequiredService<ICacheService>(), provider.GetRequiredService<IAuthenticationConfigurationProvider>(), provider.GetRequiredService<IMinecraftMetadataService>(), provider.GetRequiredService<ILogger<MainWindow>>());
            desktop.MainWindow = window; await window.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
