using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FeatherLauncher.Core.Models;
using FeatherLauncher.Core.Services;
using Microsoft.Extensions.Logging;

namespace FeatherLauncher.Desktop;

public sealed class MainWindow : Window
{
    private static readonly string[] Pages = ["Home", "Account", "Versions", "Instances", "Browse", "Mods", "Resource Packs", "Shaders", "Skins", "Capes", "Downloads", "Storage", "Settings", "Diagnostics"];
    private readonly ISettingsService settingsService; private readonly IAppPaths paths; private readonly ICacheService cache; private readonly IAuthenticationConfigurationProvider auth; private readonly IMicrosoftAuthenticationService authentication; private readonly IMinecraftMetadataService metadata; private readonly ILogger logger;
    private CancellationTokenSource? signInCancellation;
    private readonly Grid content = new(); private LauncherSettings settings = new();
    public MainWindow(ISettingsService settingsService, IAppPaths paths, ICacheService cache, IAuthenticationConfigurationProvider auth, IMicrosoftAuthenticationService authentication, IMinecraftMetadataService metadata, ILogger<MainWindow> logger)
    {
        this.settingsService = settingsService; this.paths = paths; this.cache = cache; this.auth = auth; this.authentication = authentication; this.metadata = metadata; this.logger = logger;
        Title = "Feather Launcher"; Width = 1120; Height = 720; MinWidth = 900; MinHeight = 600; Background = new SolidColorBrush(Color.Parse("#101319"));
        var nav = new StackPanel { Width = 210, Spacing = 3, Margin = new Thickness(14) };
        nav.Children.Add(new TextBlock { Text = "FEATHER", FontSize = 24, FontWeight = FontWeight.Bold, Margin = new Thickness(8, 10, 8, 22), Foreground = new SolidColorBrush(Color.Parse("#86A8FF")) });
        foreach (var page in Pages) { var button = new Button { Content = page, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 9), Tag = page }; button.Click += (_, _) => ShowPage((string)button.Tag!); nav.Children.Add(button); }
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") }; root.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#171B24")), Child = nav }); Grid.SetColumn(content, 1); root.Children.Add(content); Content = root;
    }
    public async Task InitializeAsync() { settings = await settingsService.LoadAsync(); ApplyTheme(); ShowPage("Home"); logger.LogInformation("Launcher UI initialized"); }
    private void ShowPage(string page)
    {
        content.Children.Clear();
        if (page == "Home") content.Children.Add(PageShell("Home", new StackPanel { Spacing = 10, Children = { new TextBlock { Text = "Welcome to Feather Launcher", FontSize = 30, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = "A free, lightweight and unofficial Minecraft: Java Edition launcher.", FontSize = 16 }, new Border { Margin = new Thickness(0, 18), Padding = new Thickness(18), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.Parse("#1D2330")), Child = new TextBlock { Text = "Phase 1 foundation • Game launching is not implemented yet." } } } }));
        else if (page == "Settings") content.Children.Add(BuildSettings());
        else if (page == "Account") content.Children.Add(BuildAccount());
        else if (page == "Versions") _ = BuildVersionsAsync();
        else if (page == "Storage") _ = BuildStorageAsync();
        else if (page == "Diagnostics") _ = BuildDiagnosticsAsync();
        else content.Children.Add(PageShell(page, new TextBlock { Text = "Not implemented yet", FontSize = 18, Foreground = Brushes.Gray }));
    }
    private Control BuildAccount()
    {
        if (!auth.Get().IsConfigured) return PageShell("Account", new TextBlock { Text = "Microsoft sign-in is not configured yet.", FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { Text = "Signed out", TextWrapping = TextWrapping.Wrap }; var details = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        var signIn = new Button { Content = "Sign in with Microsoft" }; var cancel = new Button { Content = "Cancel sign-in", IsEnabled = false }; var signOut = new Button { Content = "Sign out" }; var switchAccount = new Button { Content = "Switch account" }; var copyCode = new Button { Content = "Copy temporary code", IsEnabled = false }; string? currentCode = null;
        signIn.Click += async (_, _) => { signInCancellation = new(); cancel.IsEnabled = true; try { await authentication.BeginSignInAsync(signInCancellation.Token); } catch (InvalidOperationException) { status.Text = "A sign-in is already in progress."; } finally { cancel.IsEnabled = false; } };
        cancel.Click += (_, _) => signInCancellation?.Cancel(); signOut.Click += async (_, _) => await authentication.SignOutAsync(); switchAccount.Click += async (_, _) => { await authentication.SignOutAsync(); signIn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); };
        copyCode.Click += async (_, _) => { if (currentCode is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(currentCode); };
        authentication.DeviceCodeReceived += (_, code) => Dispatcher.UIThread.Post(() => { currentCode = code.UserCode; copyCode.IsEnabled = true; details.Text = $"Open {code.VerificationUrl} in your normal browser and enter temporary code {code.UserCode}. Feather Launcher never asks for your password. Code expires {code.ExpiresAt:u}."; });
        _ = Task.Run(async () => { await foreach (var state in authentication.ObserveAccountStateAsync()) Dispatcher.UIThread.Post(() => { status.Text = state switch { SigningInAccount => "Signing in…", MicrosoftAuthenticatedAccount => "Microsoft authenticated", XboxAuthenticatedAccount => "Xbox authenticated", MinecraftAuthenticatedAccount => "Minecraft authenticated", OwnershipConfirmedAccount => "Minecraft: Java Edition ownership confirmed", ProfileLoadedAccount loaded => $"Profile loaded: {loaded.Profile.Name} ({FormatUuid(loaded.Profile.Id)})", SignedInAccount signed => $"Signed in and ready: {signed.Profile?.Name}. Session expires {signed.Expiry.ExpiresAt:u}.", AuthenticationFailed failed => $"Sign-in failed ({failed.Code}): {failed.SafeMessage}", SigningOutAccount => "Signing out…", _ => "Signed out" }; }); });
        return PageShell("Account", new StackPanel { Spacing = 12, Children = { status, details, copyCode, new TextBlock { Text = "Only an entitlement reported by the official Minecraft service confirms Java ownership; owning another edition alone is not sufficient.", TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { signIn, cancel, signOut, switchAccount } } } });
    }
    private static string FormatUuid(string id) => id.Length == 32 ? $"{id[..8]}-{id[8..12]}-{id[12..16]}-{id[16..20]}-{id[20..]}" : "Unavailable";
    private async Task BuildVersionsAsync()
    {
        var panel = new StackPanel { Spacing = 10 }; var status = new TextBlock { Text = "Loading official Minecraft metadata…" }; panel.Children.Add(status); content.Children.Clear(); content.Children.Add(PageShell("Versions", panel));
        try
        {
            var result = await metadata.GetManifestAsync(); var boxes = Enum.GetValues<MinecraftVersionType>().ToDictionary(t => t, t => new CheckBox { Content = t switch { MinecraftVersionType.OldBeta => "Old beta", MinecraftVersionType.OldAlpha => "Old alpha", _ => t.ToString() }, IsChecked = t == MinecraftVersionType.Release });
            var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; foreach (var box in boxes.Values) filters.Children.Add(box); var rows = new StackPanel { Spacing = 5 };
            void Render() { rows.Children.Clear(); foreach (var version in result.Manifest.Versions.Where(v => boxes[v.Type].IsChecked == true).Take(250)) rows.Children.Add(new SelectableTextBlock { Text = $"{version.Id}  |  {version.Type}  |  Released {version.ReleaseTime:u}  |  Updated {version.UpdatedTime:u}" }); }
            foreach (var box in boxes.Values) box.IsCheckedChanged += (_, _) => Render(); panel.Children.Clear(); panel.Children.Add(filters); panel.Children.Add(new TextBlock { Text = result.Cache.UsedOfflineFallback ? "Offline: using validated cached metadata." : "Official metadata loaded.", Foreground = Brushes.Gray }); panel.Children.Add(rows); Render();
        }
        catch (Exception ex) { status.Text = $"Version metadata unavailable: {ex.Message}"; logger.LogWarning("Version page could not load metadata: {FailureType}", ex.GetType().Name); }
    }
    private async Task BuildStorageAsync()
    {
        var status = await metadata.GetCacheStatusAsync(); var text = new TextBlock { Text = CacheText(status) }; var refresh = new Button { Content = "Refresh version manifest" }; var clear = new Button { Content = "Clear version metadata cache" };
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { var result = await metadata.GetManifestAsync(true); text.Text = CacheText(result.Cache); } finally { refresh.IsEnabled = true; } };
        clear.Click += async (_, _) => { await metadata.ClearCacheAsync(); text.Text = CacheText(await metadata.GetCacheStatusAsync()); };
        content.Children.Clear(); content.Children.Add(PageShell("Storage", new StackPanel { Spacing = 12, Children = { text, refresh, clear } }));
    }
    private static string CacheText(MetadataCacheStatus status) => !status.Exists ? "Version metadata cache: empty (offline cache unavailable)" : $"Version metadata cache: {FormatBytes(status.SizeBytes)} • age {status.Age:hh\\:mm\\:ss} • {(status.IsExpired ? "stale" : "valid")} • offline cache available";
    private Control BuildSettings()
    {
        var theme = new ComboBox { ItemsSource = Enum.GetValues<LauncherTheme>(), SelectedItem = settings.Theme };
        var behavior = new ComboBox { ItemsSource = Enum.GetValues<GameStartBehavior>(), SelectedItem = settings.GameStartBehavior };
        var updates = new CheckBox { Content = "Check automatically for launcher updates", IsChecked = settings.CheckForUpdatesAutomatically };
        var cacheLimit = new NumericUpDown { Minimum = 128, Maximum = 1_048_576, Value = settings.CacheSizeLimitMb, Increment = 128 };
        var instances = new TextBox { Text = settings.DefaultInstanceLocation };
        var status = new TextBlock(); var save = new Button { Content = "Save settings" }; var reset = new Button { Content = "Reset settings" };
        save.Click += async (_, _) => { settings = settings with { Theme = (LauncherTheme)(theme.SelectedItem ?? LauncherTheme.Dark), GameStartBehavior = (GameStartBehavior)(behavior.SelectedItem ?? GameStartBehavior.KeepOpen), CheckForUpdatesAutomatically = updates.IsChecked == true, CacheSizeLimitMb = (int)(cacheLimit.Value ?? LauncherSettings.DefaultCacheSizeLimitMb), DefaultInstanceLocation = instances.Text ?? paths.Instances }; await settingsService.SaveAsync(settings); ApplyTheme(); status.Text = "Settings saved locally."; };
        reset.Click += async (_, _) => { settings = await settingsService.ResetAsync(); ApplyTheme(); ShowPage("Settings"); };
        var panel = new StackPanel { Spacing = 8 }; AddField(panel, "Theme", theme); AddField(panel, "When Minecraft starts", behavior); panel.Children.Add(updates); AddField(panel, "Cache size limit (MiB)", cacheLimit); AddField(panel, "Default Minecraft instance location", instances); panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 14), Children = { save, reset, status } }); return PageShell("Settings", panel);
    }
    private async Task BuildDiagnosticsAsync()
    {
        var size = await cache.GetSizeBytesAsync(); var process = Process.GetCurrentProcess();
        var rows = new StackPanel { Spacing = 10 }; void Row(string label, string value) => rows.Children.Add(new SelectableTextBlock { Text = $"{label}:  {value}", TextWrapping = TextWrapping.Wrap });
        var authenticationConfiguration = auth.Get();
        Row("Authentication configured", authenticationConfiguration.IsConfigured ? "yes" : "no"); Row("Selected authentication flow", authenticationConfiguration.UseDeviceCode ? "Device code" : "System browser"); Row("Current high-level authentication state", "See Account page"); Row("Token expiry time", "Available only for the active ready session on Account page"); Row("Secure storage available", OperatingSystem.IsWindows() ? "yes (Windows DPAPI)" : "no"); Row("Last safe authentication error category", "None recorded this run"); Row("Live authentication manually verified", "no — local operator record only");
        Row("Launcher version", typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable"); Row("Operating system", RuntimeInformation.OSDescription); Row("Process architecture", RuntimeInformation.ProcessArchitecture.ToString()); Row("Current launcher memory usage", FormatBytes(process.WorkingSet64)); Row("Application-data path", paths.Data); Row("Logs path", paths.Logs); Row("Cache path", paths.Cache); Row("Total cache size", FormatBytes(size));
        content.Children.Clear(); content.Children.Add(PageShell("Diagnostics", rows));
    }
    private void ApplyTheme() { if (Application.Current is not null) Application.Current.RequestedThemeVariant = settings.Theme switch { LauncherTheme.Light => ThemeVariant.Light, LauncherTheme.System => ThemeVariant.Default, _ => ThemeVariant.Dark }; }
    private static void AddField(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }); panel.Children.Add(control); }
    private static Control PageShell(string title, Control body) => new ScrollViewer { Content = new StackPanel { Margin = new Thickness(42, 36), Spacing = 22, Children = { new TextBlock { Text = title, FontSize = 32, FontWeight = FontWeight.Bold }, body } } };
    private static string FormatBytes(long value) { string[] units = ["B", "KiB", "MiB", "GiB"]; double size = value; var unit = 0; while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; } return $"{size:0.##} {units[unit]}"; }
}
