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
using FeatherLauncher.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;

namespace FeatherLauncher.Desktop;

public sealed class MainWindow : Window
{
    private static readonly string[] Pages = ["Home", "Account", "Authentication Setup", "Versions", "Instances", "Browse", "Mods", "Resource Packs", "Shaders", "Skins", "Capes", "Downloads", "Storage", "Settings", "Diagnostics"];
    private readonly ISettingsService settingsService; private readonly IAppPaths paths; private readonly ICacheService cache; private readonly IAuthenticationConfigurationStore auth; private readonly IMicrosoftAuthenticationService authentication; private readonly IMinecraftMetadataService metadata; private readonly ManualAuthenticationVerificationStore verification; private readonly AuthenticationChecklistStore checklist; private readonly AuthenticationDiagnosticsExporter diagnosticsExporter; private readonly ISecureTokenStorage tokenStorage; private readonly ILogger logger;
    private CancellationTokenSource? signInCancellation;
    private readonly Grid content = new(); private LauncherSettings settings = new();
    public MainWindow(ISettingsService settingsService, IAppPaths paths, ICacheService cache, IAuthenticationConfigurationStore auth, IMicrosoftAuthenticationService authentication, IMinecraftMetadataService metadata, ManualAuthenticationVerificationStore verification, AuthenticationChecklistStore checklist, AuthenticationDiagnosticsExporter diagnosticsExporter, ISecureTokenStorage tokenStorage, ILogger<MainWindow> logger)
    {
        this.settingsService = settingsService; this.paths = paths; this.cache = cache; this.auth = auth; this.authentication = authentication; this.metadata = metadata; this.verification = verification; this.checklist = checklist; this.diagnosticsExporter = diagnosticsExporter; this.tokenStorage = tokenStorage; this.logger = logger;
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
        else if (page == "Authentication Setup") _ = BuildAuthenticationSetupAsync();
        else if (page == "Versions") _ = BuildVersionsAsync();
        else if (page == "Storage") _ = BuildStorageAsync();
        else if (page == "Diagnostics") _ = BuildDiagnosticsAsync();
        else content.Children.Add(PageShell(page, new TextBlock { Text = "Not implemented yet", FontSize = 18, Foreground = Brushes.Gray }));
    }
    private Control BuildAccount()
    {
        if (!auth.Get().IsConfigured) return PageShell("Account", new TextBlock { Text = "Authentication configuration is missing or invalid. Complete Authentication Setup; sign-in remains disabled.", FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { Text = "Signed out", TextWrapping = TextWrapping.Wrap }; var details = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        var signIn = new Button { Content = "Sign in with Microsoft" }; var cancel = new Button { Content = "Cancel sign-in", IsEnabled = false }; var signOut = new Button { Content = "Sign out" }; var switchAccount = new Button { Content = "Switch account" }; var copyCode = new Button { Content = "Copy temporary code", IsEnabled = false }; string? currentCode = null;
        signIn.Click += async (_, _) => { signInCancellation = new(); cancel.IsEnabled = true; try { await authentication.BeginSignInAsync(signInCancellation.Token); } catch (InvalidOperationException) { status.Text = "A sign-in is already in progress."; } finally { cancel.IsEnabled = false; } };
        cancel.Click += (_, _) => signInCancellation?.Cancel(); signOut.Click += async (_, _) => await authentication.SignOutAsync(); switchAccount.Click += async (_, _) => { await authentication.SignOutAsync(); signIn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); };
        copyCode.Click += async (_, _) => { if (currentCode is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(currentCode); };
        authentication.DeviceCodeReceived += (_, code) => Dispatcher.UIThread.Post(() => { currentCode = code.UserCode; copyCode.IsEnabled = true; details.Text = $"Open {code.VerificationUrl} in your normal browser and enter the temporary code. Feather Launcher never asks for your password. Code expires {code.ExpiresAt:u}."; DispatcherTimer.RunOnce(() => { currentCode = null; copyCode.IsEnabled = false; details.Text = "The temporary device code expired and was cleared."; }, code.ExpiresAt > DateTimeOffset.UtcNow ? code.ExpiresAt - DateTimeOffset.UtcNow : TimeSpan.Zero); });
        _ = Task.Run(async () => { await foreach (var state in authentication.ObserveAccountStateAsync()) Dispatcher.UIThread.Post(() => { status.Text = state switch { SigningInAccount => "Signing in…", MicrosoftAuthenticatedAccount => "Microsoft authenticated", XboxAuthenticatedAccount => "Xbox authenticated", MinecraftAuthenticatedAccount => "Minecraft authenticated", OwnershipConfirmedAccount => "Minecraft: Java Edition ownership confirmed", ProfileLoadedAccount => "Minecraft profile loaded", SignedInAccount signed => $"Signed in and ready. Session expires {signed.Expiry.ExpiresAt:u}.", AuthenticationFailed failed => $"Sign-in failed ({failed.Code}): {failed.SafeMessage}", SigningOutAccount => "Signing out…", _ => "Signed out" }; }); });
        return PageShell("Account", new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "Authentication is configured. Check Authentication Setup for this build's manual-verification status.", TextWrapping = TextWrapping.Wrap }, status, details, copyCode, new TextBlock { Text = "Only an entitlement reported by the official Minecraft service confirms Java ownership; owning another edition alone is not sufficient.", TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { signIn, cancel, signOut, switchAccount } } } });
    }
    private async Task BuildAuthenticationSetupAsync()
    {
        var configuration = auth.Get(); var fingerprint = auth.Fingerprint(configuration); var record = await verification.LoadAsync(fingerprint, typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable"); var savedChecklist = await checklist.LoadAsync();
        var enabled = new CheckBox { Content = "Authentication enabled", IsChecked = configuration.FeatureEnabled };
        var clientId = new TextBox { Text = configuration.ClientId, Watermark = "Application (client) ID GUID" };
        var authority = new TextBox { Text = configuration.Authority }; var scopes = new TextBox { Text = string.Join(' ', configuration.RequiredScopes) };
        var deviceCode = new CheckBox { Content = "Use device-code flow", IsChecked = configuration.UseDeviceCode }; var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void RefreshSummary()
        {
            var candidate = new AuthenticationConfiguration(enabled.IsChecked == true, clientId.Text, null, (scopes.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries), authority.Text ?? string.Empty, deviceCode.IsChecked == true);
            summary.Text = $"Enabled: {(candidate.FeatureEnabled ? "yes" : "no")}\nClient ID present: {(candidate.HasClientId ? "yes" : "no")}\nClient ID valid GUID: {(candidate.HasValidClientId ? "yes" : "no")}\nAuthority: {candidate.Authority}\nScopes: {string.Join(", ", candidate.RequiredScopes)}\nDevice-code flow enabled: {(candidate.UseDeviceCode ? "yes" : "no")}\nSecure storage available: {(OperatingSystem.IsWindows() ? "yes" : "no")}\nLive verification recorded: {(record.Verified ? "yes" : "no")}\n\n{(candidate.IsConfigured ? record.Verified ? "Authentication is configured and has a local verification record for these settings." : "Authentication is configured but has not been manually verified on this build." : "Authentication configuration is missing or invalid; sign-in is disabled.")}";
        }
        RefreshSummary(); enabled.IsCheckedChanged += (_, _) => RefreshSummary(); clientId.TextChanged += (_, _) => RefreshSummary(); authority.TextChanged += (_, _) => RefreshSummary(); scopes.TextChanged += (_, _) => RefreshSummary(); deviceCode.IsCheckedChanged += (_, _) => RefreshSummary();
        var save = new Button { Content = "Save non-secret configuration" }; var saveStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        save.Click += async (_, _) => { var updated = new AuthenticationConfiguration(enabled.IsChecked == true, clientId.Text?.Trim(), null, (scopes.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries), authority.Text?.Trim() ?? string.Empty, deviceCode.IsChecked == true); var changed = auth.Fingerprint(updated) != fingerprint; await auth.SaveAsync(updated); if (changed) await verification.SaveAsync(ManualAuthenticationVerification.NotVerified(auth.Fingerprint(updated), typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable")); saveStatus.Text = updated.IsConfigured ? "Configuration saved. Restart Feather Launcher to activate it. Material changes reset live verification." : "Configuration saved, but it is invalid. Sign-in remains disabled."; };
        var checklistPanel = new StackPanel { Spacing = 8 }; var editors = new List<(AuthenticationChecklistItem Item, ComboBox Result, TextBox Note)>();
        foreach (var item in savedChecklist.Items) { var result = new ComboBox { ItemsSource = Enum.GetValues<VerificationResult>(), SelectedItem = item.Result, Width = 130 }; var note = new TextBox { Text = item.Note, Watermark = "Short non-sensitive note", Width = 360 }; editors.Add((item, result, note)); checklistPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = item.Scenario, Width = 270, VerticalAlignment = VerticalAlignment.Center }, result, note } }); }
        var saveChecklist = new Button { Content = "Save checklist" }; var checklistStatus = new TextBlock();
        saveChecklist.Click += async (_, _) => { var now = DateTimeOffset.UtcNow; var updated = new AuthenticationChecklist(editors.Select(x => x.Item with { Result = (VerificationResult)(x.Result.SelectedItem ?? VerificationResult.NotTested), Timestamp = (VerificationResult)(x.Result.SelectedItem ?? VerificationResult.NotTested) == VerificationResult.NotTested ? null : now, Note = x.Note.Text ?? string.Empty }).ToArray()); await checklist.SaveAsync(updated); var verified = updated.Items.All(x => x.Result == VerificationResult.Pass); await verification.SaveAsync(verified ? new(true, now, typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable", updated.Items.Select(x => x.Scenario).ToArray(), auth.Fingerprint(auth.Get())) : ManualAuthenticationVerification.NotVerified(auth.Fingerprint(auth.Get()), typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable")); checklistStatus.Text = verified ? "Checklist saved; live verification recorded locally for this configuration." : "Checklist saved after redacting sensitive note content. Verification is incomplete."; };
        var confirmClear = new CheckBox { Content = "I understand authentication sessions, cache, state, device-code state, and authentication logs will be cleared." }; var clear = new Button { Content = "Clear authentication data" }; var clearStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        clear.Click += async (_, _) => { if (confirmClear.IsChecked != true) { clearStatus.Text = "Confirm the clear action first."; return; } signInCancellation?.Cancel(); try { await new AuthenticationDataClearer(authentication, tokenStorage, paths.Logs).ClearAsync(); clearStatus.Text = "Authentication data cleared. Unrelated launcher settings and Minecraft data were not changed."; } catch (Exception) { clearStatus.Text = "Authentication data was cleared where supported; secure storage reported a safe local error."; } };
        var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(summary); AddField(panel, "Microsoft public client ID (never a client secret)", clientId); AddField(panel, "Authority", authority); AddField(panel, "Required scopes (space-separated)", scopes); panel.Children.Add(enabled); panel.Children.Add(deviceCode); panel.Children.Add(save); panel.Children.Add(saveStatus); panel.Children.Add(new TextBlock { Text = "Guided live-test checklist", FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 0) }); panel.Children.Add(checklistPanel); panel.Children.Add(saveChecklist); panel.Children.Add(checklistStatus); panel.Children.Add(confirmClear); panel.Children.Add(clear); panel.Children.Add(clearStatus);
        content.Children.Clear(); content.Children.Add(PageShell("Authentication Setup", panel));
    }
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
        var fingerprint = auth.Fingerprint(authenticationConfiguration); var verified = await verification.LoadAsync(fingerprint, typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable"); var scenarios = (await checklist.LoadAsync()).Items.Select(x => x.Scenario).ToArray();
        Row("Authentication configured", authenticationConfiguration.IsConfigured ? "yes" : "no"); Row("Selected authentication flow", authenticationConfiguration.UseDeviceCode ? "Device code" : "System browser"); Row("Current high-level authentication state", "See Account page"); Row("Secure storage available", OperatingSystem.IsWindows() ? "yes (Windows DPAPI)" : "no"); Row("Last safe authentication error category", "None recorded this run"); Row("Live authentication manually verified", verified.Verified ? "yes — local operator record" : "no — local operator record only");
        Row("Launcher version", typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable"); Row("Operating system", RuntimeInformation.OSDescription); Row("Process architecture", RuntimeInformation.ProcessArchitecture.ToString()); Row("Current launcher memory usage", FormatBytes(process.WorkingSet64)); Row("Application-data path", paths.Data); Row("Logs path", paths.Logs); Row("Cache path", paths.Cache); Row("Total cache size", FormatBytes(size));
        var export = new Button { Content = "Export redacted authentication diagnostics" }; var exportStatus = new TextBlock(); export.Click += async (_, _) => { var report = AuthenticationDiagnosticsExporter.Create(authenticationConfiguration, OperatingSystem.IsWindows(), "See Account page", "None", verified.Verified, scenarios); await diagnosticsExporter.ExportAsync(Path.Combine(paths.Data, "authentication-diagnostics-redacted.json"), report); exportStatus.Text = "Redacted report exported as authentication-diagnostics-redacted.json in launcher application data."; }; rows.Children.Add(export); rows.Children.Add(exportStatus);
        content.Children.Clear(); content.Children.Add(PageShell("Diagnostics", rows));
    }
    private void ApplyTheme() { if (Application.Current is not null) Application.Current.RequestedThemeVariant = settings.Theme switch { LauncherTheme.Light => ThemeVariant.Light, LauncherTheme.System => ThemeVariant.Default, _ => ThemeVariant.Dark }; }
    private static void AddField(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }); panel.Children.Add(control); }
    private static Control PageShell(string title, Control body) => new ScrollViewer { Content = new StackPanel { Margin = new Thickness(42, 36), Spacing = 22, Children = { new TextBlock { Text = title, FontSize = 32, FontWeight = FontWeight.Bold }, body } } };
    private static string FormatBytes(long value) { string[] units = ["B", "KiB", "MiB", "GiB"]; double size = value; var unit = 0; while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; } return $"{size:0.##} {units[unit]}"; }
}
