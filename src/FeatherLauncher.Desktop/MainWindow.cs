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
    private static readonly string[] Pages = ["Home", "Instances", "Browse", "Mods", "Resource Packs", "Shaders", "Skins", "Capes", "Downloads", "Storage", "Settings", "Diagnostics"];
    private readonly ISettingsService settingsService; private readonly IAppPaths paths; private readonly ICacheService cache; private readonly ILogger logger;
    private readonly Grid content = new(); private LauncherSettings settings = new();
    public MainWindow(ISettingsService settingsService, IAppPaths paths, ICacheService cache, ILogger<MainWindow> logger)
    {
        this.settingsService = settingsService; this.paths = paths; this.cache = cache; this.logger = logger;
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
        else if (page == "Diagnostics") _ = BuildDiagnosticsAsync();
        else content.Children.Add(PageShell(page, new TextBlock { Text = "Not implemented yet", FontSize = 18, Foreground = Brushes.Gray }));
    }
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
        Row("Launcher version", typeof(App).Assembly.GetName().Version?.ToString() ?? "Unavailable"); Row("Operating system", RuntimeInformation.OSDescription); Row("Process architecture", RuntimeInformation.ProcessArchitecture.ToString()); Row("Current launcher memory usage", FormatBytes(process.WorkingSet64)); Row("Application-data path", paths.Data); Row("Logs path", paths.Logs); Row("Cache path", paths.Cache); Row("Total cache size", FormatBytes(size));
        content.Children.Clear(); content.Children.Add(PageShell("Diagnostics", rows));
    }
    private void ApplyTheme() { if (Application.Current is not null) Application.Current.RequestedThemeVariant = settings.Theme switch { LauncherTheme.Light => ThemeVariant.Light, LauncherTheme.System => ThemeVariant.Default, _ => ThemeVariant.Dark }; }
    private static void AddField(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) }); panel.Children.Add(control); }
    private static Control PageShell(string title, Control body) => new ScrollViewer { Content = new StackPanel { Margin = new Thickness(42, 36), Spacing = 22, Children = { new TextBlock { Text = title, FontSize = 32, FontWeight = FontWeight.Bold }, body } } };
    private static string FormatBytes(long value) { string[] units = ["B", "KiB", "MiB", "GiB"]; double size = value; var unit = 0; while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; } return $"{size:0.##} {units[unit]}"; }
}
