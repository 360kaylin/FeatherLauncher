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
        services.AddSingleton<IAuthenticationConfigurationStore>(sp => new JsonAuthenticationConfigurationStore(Path.Combine(sp.GetRequiredService<IAppPaths>().Data, "authentication.json")));
        services.AddSingleton<IAuthenticationConfigurationProvider>(sp => sp.GetRequiredService<IAuthenticationConfigurationStore>());
        services.AddSingleton(sp => new ManualAuthenticationVerificationStore(Path.Combine(sp.GetRequiredService<IAppPaths>().Data, "authentication-verification.json")));
        services.AddSingleton(sp => new AuthenticationChecklistStore(Path.Combine(sp.GetRequiredService<IAppPaths>().Data, "authentication-checklist.json"), sp.GetRequiredService<ILogRedactor>()));
        services.AddSingleton<AuthenticationDiagnosticsExporter>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }); services.AddSingleton<IMinecraftMetadataService, MinecraftMetadataService>();
        services.AddSingleton<IXboxAuthenticationService, XboxAuthenticationService>(); services.AddSingleton<IXstsAuthorizationService, XstsAuthorizationService>(); services.AddSingleton<IMinecraftAuthenticationService, MinecraftAuthenticationService>(); services.AddSingleton<IMinecraftEntitlementService, MinecraftEntitlementService>(); services.AddSingleton<IMinecraftProfileService, MinecraftProfileService>();
        services.AddSingleton<ISecureTokenStorage>(sp => OperatingSystem.IsWindows() ? new WindowsDpapiTokenStorage(Path.Combine(sp.GetRequiredService<IAppPaths>().Data, "credentials")) : new UnsupportedPlatformTokenStorage());
        services.AddSingleton<IMicrosoftAuthenticationService>(sp => { var configuration = sp.GetRequiredService<IAuthenticationConfigurationProvider>().Get(); return configuration.IsConfigured ? new MsalAuthenticationService(configuration, sp.GetRequiredService<ISecureTokenStorage>(), sp.GetRequiredService<IXboxAuthenticationService>(), sp.GetRequiredService<IXstsAuthorizationService>(), sp.GetRequiredService<IMinecraftAuthenticationService>(), sp.GetRequiredService<IMinecraftEntitlementService>(), sp.GetRequiredService<IMinecraftProfileService>(), sp.GetRequiredService<ILogger<MsalAuthenticationService>>()) : new DisabledMicrosoftAuthenticationService(); });
        services.AddLogging(builder => { builder.SetMinimumLevel(LogLevel.Information); builder.Services.AddSingleton<ILoggerProvider, RedactingFileLoggerProvider>(); });
        var provider = services.BuildServiceProvider(); provider.GetRequiredService<IAppPaths>().EnsureCreated();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<IAppPaths>(), provider.GetRequiredService<ICacheService>(), provider.GetRequiredService<IAuthenticationConfigurationStore>(), provider.GetRequiredService<IMicrosoftAuthenticationService>(), provider.GetRequiredService<IMinecraftMetadataService>(), provider.GetRequiredService<ManualAuthenticationVerificationStore>(), provider.GetRequiredService<AuthenticationChecklistStore>(), provider.GetRequiredService<AuthenticationDiagnosticsExporter>(), provider.GetRequiredService<ISecureTokenStorage>(), provider.GetRequiredService<ILogger<MainWindow>>());
            desktop.MainWindow = window; await window.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
