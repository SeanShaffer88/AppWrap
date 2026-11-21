using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AppWrap.Build;
using AppWrap.Core;
using Microsoft.Win32;

namespace AppWrap.Gui;

public partial class MainWindow : Window
{
    private int step;
    private bool scanning;
    private IReadOnlyList<string> executables = [];
    private string theme = "AppWrap";
    private string source = "", name = "", version = "1.0", executable = "", output = "";
    private string? icon, sidebar;
    private string? cleanupWarning;
    private bool startMenu = true, desktop, busy, finished, closingAfterCancel;
    private CancellationTokenSource? cancellation;
    private string StateDirectory => Path.Combine(AppContext.BaseDirectory, "Data");
    private OutputMode Mode => PortableMode.IsChecked == true ? OutputMode.PortableApps : OutputMode.Standard;
    private bool UsesStandard => Selection != BuildSelection.Portable;
    private int IconStep => IsBoth ? 5 : 4;
    private int TotalSteps => IsBoth ? 8 : UsesPortable ? 7 : 6;
    private bool offerLaunch = true, offerStartup, offerWebsite;
    private string developerWebsite = "";

    public MainWindow()
    {
        InitializeComponent();
        ThemePicker.ItemsSource = ThemeCatalog.Names;
        Loaded += (_, _) => { RestoreWindow(); ThemePicker.SelectedItem = theme; ThemeCatalog.Apply(theme, Application.Current.Resources); Render(); };
        Closing += OnClosing;
        SystemParameters.StaticPropertyChanged += SystemPreferenceChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged;
    }
    private void SystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.Invoke(() => ThemeCatalog.Apply(theme, Application.Current.Resources)); }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (Page == null) return;
        step = 0; InvalidateOutputName(); Render();
    }

    private static T Identify<T>(T control, string id) where T : DependencyObject
    { AutomationProperties.SetAutomationId(control, id); AutomationProperties.SetName(control, id switch { "SourceFolder" => "Application folder", "AppName" => "App name", "AppVersion" => "Version", "MainExecutable" => "Application launch file", "OutputPath" => "Finished package", "BrowseSourceFolder" => "Choose application folder", "BrowseOutputPath" => "Choose save location", "StartMenuShortcut" => "Start Menu shortcut", "DesktopShortcut" => "Desktop shortcut", "LauncherIcon" => "App icon", "BrowseLauncherIcon" => "Choose app icon", "SidebarImage" => "Sidebar image", "BrowseSidebarImage" => "Choose sidebar image", "NamingStyle" => "Filename style", "OutputFolder" => "Output folder", "BrowseOutputFolder" => "Choose output folder", "OfferLaunch" => "Offer to open the app (optional)", "OfferStartup" => "Offer Start with Windows (optional)", "OfferWebsite" => "Offer the developer website (optional)", "DeveloperWebsite" => "Developer website (optional)", _ => id }); return control; }

    private StackPanel Panel() { var panel = new StackPanel(); Page.Content = panel; return panel; }
    private TextBlock Label(string text) { var label = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 8) }; label.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return label; }
    private void Scanning(bool value)
    { scanning = value; Page.IsEnabled = !value; Formats.IsEnabled = !value; NextButton.IsEnabled = !value; BackButton.IsEnabled = !value; }
    private void SelectFile(StackPanel panel, string label, string id, string? selected, Action choose, bool optional = false)
    {
        panel.Children.Add(Label(label));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(optional ? "M2,2H26V26H2Z M5,22L11,13 16,18 20,11 25,22 M7,7A2,2 0 1 0 11,7A2,2 0 1 0 7,7" : "M2,7H11L15,11H28V26H2Z"), StrokeThickness = 1.6, Width = 28, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted"); grid.Children.Add(glyph);
        if (optional && !string.IsNullOrEmpty(selected))
        {
            try { var thumbnail = new Image { Source = AppWrap.Imaging.ImageAssets.Load(selected), Width = 34, Height = 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Selected image preview" }; glyph.Visibility = Visibility.Collapsed; grid.Children.Add(thumbnail); }
            catch (Exception) { Error.Text = "This picture could not be opened. Choose another image or use the default."; }
        }
        var path = Identify(new TextBox { Text = string.IsNullOrEmpty(selected) ? optional ? "Use the default, or pick a picture" : step == 0 ? "Choose a folder to get started" : "Choose where to save your package" : selected, IsReadOnly = true, Style = null, BorderThickness = new(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = selected, Padding = new(0, 8, 8, 8), Margin = new(0, 0, 12, 0) }, id);
        path.SetResourceReference(Control.ForegroundProperty, "Text");
        Grid.SetColumn(path, 1); grid.Children.Add(path);
        var button = Identify(new Button { Content = optional ? "Choose image…" : step == 0 ? "Choose folder…" : "Save as…", MinWidth = 138, VerticalAlignment = VerticalAlignment.Center }, "Browse" + id);
        button.Click += (_, _) => choose(); Grid.SetColumn(button, 2); grid.Children.Add(button);
        var card = new Border { CornerRadius = new(12), Padding = new(14, 12, 12, 12), BorderThickness = new(1), Child = grid };
        card.SetResourceReference(Border.BackgroundProperty, "InputSurface"); card.SetResourceReference(Border.BorderBrushProperty, "Border"); panel.Children.Add(card);
        if (optional && !string.IsNullOrEmpty(selected))
        {
            var remove = Identify(new Button { Content = "Use default", MinHeight = 24, FontSize = 12, Padding = new(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, -4, 0, 4) }, "Clear" + id);
            remove.Click += (_, _) => { if (step == IconStep) icon = null; else sidebar = null; Render(); };
            var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var currentLabel = panel.Children[0]; panel.Children.RemoveAt(0); heading.Children.Add(currentLabel); Grid.SetColumn(remove, 1); heading.Children.Add(remove); panel.Children.Insert(0, heading);
        }
    }

    private void Render()
    {
        Error.Text = ""; Details.Visibility = Visibility.Collapsed;
        var compactFinish = step == 4 && UsesStandard && !busy && !finished;
        Hint.Margin = compactFinish ? new(0, 6, 0, 8) : new(0, 8, 0, 16);
        Steps.Margin = compactFinish ? new(0, 8, 0, 10) : new(0, 8, 0, 18);
        Formats.IsEnabled = !busy && !finished; Formats.Visibility = step == 0 && !busy && !finished ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step > 0 && !busy && !finished ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = busy || finished ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = step == TotalSteps - 1 ? IsBoth ? "Build both" : "Build" : "Next";
        CancelButton.Content = finished ? "Close" : busy ? "Cancel build" : "Cancel";
        StepLabel.Text = finished ? "READY TO SHARE" : busy ? "BUILDING YOUR PACKAGE" : $"STEP {step + 1} OF {TotalSteps}";
        StepTopic.Text = finished || busy ? "" : step switch { 0 => "Application", 1 => "App details", 2 => "Launch file", 3 => "Shortcuts", 4 when UsesStandard => "Finish options", var s when UsesPortable && s == IconStep => "App icon", var s when UsesPortable && s == IconStep + 1 => "Sidebar image", _ => "Save & build" };
        Steps.Maximum = TotalSteps; Steps.Value = step + 1; Steps.IsIndeterminate = busy;
        var panel = Panel();
        if (finished) { Question.Text = IsBoth ? "Your packages are ready." : "Your package is ready."; Hint.Text = IsBoth ? "Your installer and portable package are ready to share." : "You can share this file with other Windows users."; panel.Children.Add(Identify(new TextBox { Text = output, IsReadOnly = true, TextWrapping = TextWrapping.Wrap }, "FinishedOutput")); if (cleanupWarning != null) { Error.Text = "Your package is ready. Some temporary files could not be removed; see details."; DetailsText.Text = cleanupWarning; Details.Visibility = Visibility.Visible; } return; }
        if (busy) { Question.Text = "Wrapping your app…"; Hint.Text = "This can take a little while. Your original app stays where it is."; return; }
        switch (step)
        {
            case 0:
                Question.Text = "Where is your application?";
                Hint.Text = !UsesPortable ? "Choose the folder that contains your app and the files it needs." : "Choose an app that is already portable. Packaging cannot move its settings or remove its dependencies.";
                SelectFile(panel, "Application folder", "SourceFolder", source, BrowseSource);
                break;
            case 1:
                Question.Text = "What is your app called?"; Hint.Text = "Use the name and version people should see when they install it.";
                var information = new Grid(); information.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) }); information.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var namePanel = new StackPanel { Margin = new(0, 0, 16, 0) }; var versionPanel = new StackPanel(); Grid.SetColumn(versionPanel, 1);
                namePanel.Children.Add(Label("App name"));
                var appName = Identify(new TextBox { Text = name, MaxLength = 120 }, "AppName"); appName.TextChanged += (_, _) => { if (name != appName.Text) developerWebsite = ""; name = appName.Text; InvalidateOutputName(); }; namePanel.Children.Add(appName);
                versionPanel.Children.Add(Label("Version"));
                var appVersion = Identify(new TextBox { Text = version, MaxLength = 23 }, "AppVersion"); appVersion.TextChanged += (_, _) => { version = appVersion.Text; InvalidateOutputName(); }; versionPanel.Children.Add(appVersion);
                information.Children.Add(namePanel); information.Children.Add(versionPanel); panel.Children.Add(information);
                break;
            case 2:
                Question.Text = "Which file launches the app?"; Hint.Text = "Choose the application people normally open.";
                try
                {
                    var choices = executables;
                    var combo = Identify(new ComboBox { ItemsSource = choices, SelectedItem = executable, MinHeight = 44, FontSize = 16 }, "MainExecutable");
                    combo.SelectionChanged += (_, _) => executable = combo.SelectedItem as string ?? ""; panel.Children.Add(combo);
                    if (choices.Count == 0) Error.Text = "We couldn’t find an application file in this folder. Go back and pick the folder containing your app’s .exe.";
                }
                catch (Exception ex) { ShowError(ex); }
                break;
            case 3:
                Question.Text = "Where should shortcuts go?"; Hint.Text = UsesStandard ? "Choose initial selections. The installing user can change them.\nThese choices apply only to the standard installer." : "Your package appears in the portable app menu.\nWindows shortcuts apply only to standard installers.";
                panel.Children.Add(Label("Shortcut defaults (optional)"));
                var menu = Identify(new CheckBox { Content = "Start Menu (optional)", IsChecked = startMenu, IsEnabled = UsesStandard }, "StartMenuShortcut"); menu.Checked += (_, _) => startMenu = true; menu.Unchecked += (_, _) => startMenu = false; panel.Children.Add(menu);
                var desk = Identify(new CheckBox { Content = "Desktop (optional)", IsChecked = desktop, IsEnabled = UsesStandard }, "DesktopShortcut"); desk.Checked += (_, _) => desktop = true; desk.Unchecked += (_, _) => desktop = false; panel.Children.Add(desk);
                break;
            case 4 when UsesStandard:
                RenderFinishOptions(panel); break;
            case var s when UsesPortable && s == IconStep:
                Question.Text = "Choose an icon for your app."; Hint.Text = "Pick a picture, or click Next to use a simple default icon.";
                SelectFile(panel, "App icon (optional)", "LauncherIcon", icon, () => BrowseImage(true), true); break;
            case var s when UsesPortable && s == IconStep + 1:
                Question.Text = "Choose a sidebar image."; Hint.Text = "This picture appears beside the installation steps. Click Next to keep the default.";
                SelectFile(panel, "Sidebar image (optional)", "SidebarImage", sidebar, () => BrowseImage(false), true); break;
            default: RenderOutput(panel); break;
        }
    }

    private async void BrowseSource()
    {
        if (scanning || busy) return;
        var dialog = new OpenFolderDialog { Title = "Choose your application folder", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        source = dialog.FolderName; name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source)); developerWebsite = ""; InvalidateOutputName(); executable = "";
        try { Scanning(true); executables = await Task.Run(() => InputRules.Executables(source)); if (executables.Count == 1) executable = executables[0]; Render(); }
        catch (Exception ex) { Render(); ShowError(ex); }
        finally { Scanning(false); }
    }

    private void BrowseImage(bool isIcon)
    {
        var dialog = new OpenFileDialog { Title = isIcon ? "Choose an app icon" : "Choose a sidebar image", Filter = "Pictures|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { _ = AppWrap.Imaging.ImageAssets.Load(dialog.FileName); if (isIcon) icon = dialog.FileName; else sidebar = dialog.FileName; Render(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void BrowseOutput()
    {
        if (IsBoth)
        {
            var folder = new OpenFolderDialog { Title = "Choose a folder for both packages", InitialDirectory = outputFolder };
            if (folder.ShowDialog(this) == true) { outputFolder = folder.FolderName; output = ""; destinationChosen = true; Render(); }
            return;
        }
        var dialog = new SaveFileDialog { Title = "Save your package", Filter = "Windows package|*.exe", DefaultExt = ".exe", AddExtension = true, OverwritePrompt = false,
            FileName = GeneratedName(Mode), InitialDirectory = outputFolder };
        if (dialog.ShowDialog(this) == true) { output = dialog.FileName; outputFolder = Path.GetDirectoryName(output)!; destinationChosen = true; Render(); }
    }
    private async void NextClick(object sender, RoutedEventArgs e)
    {
        if (scanning || busy || finished) return;
        try
        {
            if (step == 0) { Scanning(true); try { executables = await Task.Run(() => InputRules.Executables(source)); if (executables.Count == 0) throw new UserInputException("We couldn’t find an application file. Pick the folder containing your app’s .exe."); } finally { Scanning(false); } }
            if (step == 1) { PackageNaming.ValidateName(name.Trim()); InputRules.PackageVersion(version); }
            if (step == 2 && string.IsNullOrEmpty(executable)) throw new UserInputException("Choose the file that opens your app.");
            if (step == 4 && UsesStandard) StandardInstallerOptions.Normalize(CurrentStandardOptions);
            if (step < TotalSteps - 1) { step++; Render(); return; }
            if (!await BuildSelectionAsync()) return;
            Render(); if (closingAfterCancel) Close();
        }
        catch (OperationCanceledException) { Render(); Error.Text = "Build cancelled. Your original files and any existing package are unchanged."; if (closingAfterCancel) Close(); }
        catch (Exception ex) { Render(); ShowError(ex); if (closingAfterCancel) Close(); }
    }

    private void ShowError(Exception error)
    {
        Error.Text = error is UserInputException or BuildFailureException ? error.Message : error is UnauthorizedAccessException ? "AppWrap cannot write here. Move AppWrap to a writable folder and choose a location you can save to." : "AppWrap couldn’t finish this step. Check that the folder and files are available, then try again.";
        DetailsText.Text = error is BuildFailureException failure ? failure.Details : error.ToString();
        Details.Visibility = error is UserInputException ? Visibility.Collapsed : Visibility.Visible;
    }
    private void BackClick(object sender, RoutedEventArgs e) { if (step > 0) { step--; Render(); } }
    private void CancelClick(object sender, RoutedEventArgs e) { if (busy) cancellation?.Cancel(); else Close(); }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    { if (ThemePicker.SelectedItem is string selected) { theme = selected; ThemeCatalog.Apply(theme, Application.Current.Resources); } }
    private sealed record SavedWindow(double Left, double Top, double Width, double Height, bool Maximized, string Theme = "AppWrap", PackageNamingStyle NamingStyle = PackageNamingStyle.NameVersionSetup);
    private void RestoreWindow()
    {
        try
        {
            var statePath = Path.Combine(StateDirectory, "window.json");
            InputRules.SafePath(statePath);
            if (new FileInfo(statePath).Length > 65536) return;
            var saved = JsonSerializer.Deserialize<SavedWindow>(File.ReadAllText(statePath));
            if (saved == null) return;
            theme = ThemeCatalog.Names.Contains(saved.Theme) ? saved.Theme : "AppWrap";
            namingStyle = Enum.IsDefined(saved.NamingStyle) ? saved.NamingStyle : PackageNamingStyle.NameVersionSetup;
            var restored = WindowPlacement.Clamp(new Rect(saved.Left, saved.Top, saved.Width, saved.Height), MinWidth, MinHeight);
            MinWidth = Math.Min(MinWidth, restored.Width); MinHeight = Math.Min(MinHeight, restored.Height);
            Width = restored.Width; Height = restored.Height; Left = restored.Left; Top = restored.Top;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (saved.Maximized) WindowState = System.Windows.WindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or UserInputException) { }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; closingAfterCancel = true; cancellation?.Cancel(); return; }
        try
        {
            InputRules.SafePath(Path.Combine(StateDirectory, "window.json"));
            Directory.CreateDirectory(StateDirectory);
            var bounds = RestoreBounds;
            var saved = new SavedWindow(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == System.Windows.WindowState.Maximized, theme, namingStyle);
            var temporary = Path.Combine(StateDirectory, "window-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(saved));
            File.Move(temporary, Path.Combine(StateDirectory, "window.json"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UserInputException) { }
    }
}
