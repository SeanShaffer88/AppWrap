using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AppWrap.Build;
using AppWrap.Core;
using Microsoft.Win32;

namespace AppWrap.Gui;

public partial class MainWindow
{
    private enum BuildSelection { Standard, Portable, Both, PafPackage }
    private BuildSelection Selection => BothMode.IsChecked == true ? BuildSelection.Both : PafMode.IsChecked == true ? BuildSelection.PafPackage : PortableMode.IsChecked == true ? BuildSelection.Portable : BuildSelection.Standard;
    private bool IsBoth => Selection == BuildSelection.Both;
    private bool IsPaf => Selection == BuildSelection.PafPackage;
    private bool UsesPortable => Selection != BuildSelection.Standard;
    private bool FolderDestination => IsBoth || IsPaf;
    private string PafFolderName => PackageNaming.GetFolderName(name.Trim());
    private string PafDestination => Path.Combine(outputFolder, PafFolderName);
    private PackageNamingStyle namingStyle = PackageNamingStyle.NameVersionSetup;
    private string outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private bool destinationChosen;
    private TextBox? outputLocationDisplay;
    private readonly List<(OutputMode Mode, TextBlock Text)> filenamePreviews = [];

    private string GeneratedName(OutputMode mode) => PackageNaming.GetFileName(name.Trim(), version.Trim(), mode, namingStyle);
    private string SingleDestination => string.IsNullOrEmpty(output) ? Path.Combine(outputFolder, GeneratedName(Mode)) : output;
    /// <summary>The destination AppWrap suggested at the last render — the baseline the reconciliation rule compares a chosen destination against.</summary>
    private string suggestedOutput = "";
    private string SuggestedDestination() => Path.Combine(outputFolder, GeneratedName(Mode));
    private void InvalidateOutputName()
    {
        // Name, version, mode or filename style changed, so the suggested
        // destination moved. A chosen destination (Save as… or typed) that
        // merely echoes the previous suggestion follows it; a diverged
        // custom path is the user's own text and is left alone, keeping
        // its folder. With no override there is nothing to reconcile.
        var next = SuggestedDestination();
        if (!string.IsNullOrEmpty(output))
        {
            var reconciled = WizardPaths.ReconcileSuggestion(output, suggestedOutput, next);
            output = WizardPaths.SamePath(reconciled, next) ? "" : reconciled;
            var basis = string.IsNullOrEmpty(output) ? next : output;
            if (Path.GetDirectoryName(Path.GetFullPath(basis)) is { Length: > 0 } folder) outputFolder = folder;
        }
        suggestedOutput = next;
    }

    /// <summary>
    /// Commits a typed/pasted destination from the output step's editable
    /// box. For folder destinations (Both, PAF Package) the text is the
    /// destination folder; otherwise it is the full package file path.
    /// Relative text resolves against the current output folder. Build
    /// still runs the usual destination validation afterwards.
    /// </summary>
    private void CommitOutputText(string text)
    {
        var typed = WizardPaths.NormalizePathText(text);
        if (typed.Length == 0) { Render(); return; }
        try
        {
            var full = Path.GetFullPath(Path.IsPathRooted(typed) ? typed : Path.Combine(outputFolder, typed));
            if (FolderDestination)
            {
                if (WizardPaths.SamePath(full, outputFolder) && destinationChosen) { Render(); return; }
                outputFolder = full;
            }
            else
            {
                if (WizardPaths.SamePath(full, SingleDestination) && destinationChosen) { Render(); return; }
                output = full;
                if (Path.GetDirectoryName(full) is { Length: > 0 } folder) outputFolder = folder;
            }
            destinationChosen = true;
            Render();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Render();
            ShowError(new UserInputException("That location is not a valid path. Check it and try again."));
        }
    }

    private void RenderOutput(StackPanel panel)
    {
        filenamePreviews.Clear();
        Question.Text = IsBoth ? "Where should we save the packages?" : IsPaf ? "Where should the PAF package folder be created?" : Mode == OutputMode.Standard ? "Where should the installer be saved?" : "Where should the portable package be saved?";
        Hint.Text = IsBoth ? "One build creates both files from the same app folder." : IsPaf ? "A PAF Package is a folder, not a file: the official PortableApps.com Installer turns it into the final package. AppWrap's own installers remain the default choices." : "Choose a filename style and location, then click Build.";
        if (!IsPaf)
        {
            var namingRow = new Grid { Margin = new(0, 0, 0, 8) };
            namingRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); namingRow.ColumnDefinitions.Add(new());
            var namingLabel = Label("Filename style"); namingLabel.Margin = new(0, 0, 14, 0); namingLabel.VerticalAlignment = VerticalAlignment.Center;
            namingRow.Children.Add(namingLabel);
            var styles = Identify(new ComboBox { ItemsSource = PackageNaming.Labels, SelectedIndex = (int)namingStyle, FontSize = 13, MinHeight = 38, Height = 40 }, "NamingStyle");
            styles.SelectionChanged += (_, _) => { if (styles.SelectedIndex < 0) return; namingStyle = (PackageNamingStyle)styles.SelectedIndex; InvalidateOutputName(); UpdateOutputPreview(); };
            Grid.SetColumn(styles, 1); namingRow.Children.Add(styles); panel.Children.Add(namingRow);
        }
        var location = new Grid(); location.ColumnDefinitions.Add(new()); location.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var value = FolderDestination ? outputFolder : SingleDestination;
        suggestedOutput = SuggestedDestination();
        var path = Identify(new TextBox { Text = value, ToolTip = "Type or paste a path, or use the button to browse", Style = null, BorderThickness = new(0), Background = Brushes.Transparent, IsReadOnly = false, Padding = new(0, 6, 8, 6), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center }, FolderDestination ? "OutputFolder" : "OutputPath");
        path.SetResourceReference(Control.ForegroundProperty, "Text"); location.Children.Add(path);
        WirePathCommit(path, CommitOutputText);
        outputLocationDisplay = path;
        var choose = Identify(new Button { Content = FolderDestination ? "Choose folder…" : "Save as…", Padding = new(12, 6, 12, 6), MinHeight = 40, FontSize = 13 }, FolderDestination ? "BrowseOutputFolder" : "BrowseOutputPath");
        choose.Click += (_, _) => BrowseOutput(); Grid.SetColumn(choose, 1); location.Children.Add(choose);
        var locationCard = new Border { Child = location, CornerRadius = new(8), Padding = new(10, 4, 4, 4), BorderThickness = new(1), Margin = new(0, 0, 0, 8) };
        locationCard.SetResourceReference(Border.BackgroundProperty, "InputSurface"); locationCard.SetResourceReference(Border.BorderBrushProperty, "Border"); panel.Children.Add(locationCard);
        if (IsBoth) { FilenamePreview(panel, "Installer", GeneratedName(OutputMode.Standard), OutputMode.Standard); FilenamePreview(panel, "Portable", GeneratedName(OutputMode.PortableApps), OutputMode.PortableApps); }
        else if (IsPaf) FilenamePreview(panel, "Folder", PafFolderName, OutputMode.PafPackage);
        else FilenamePreview(panel, "Filename", Path.GetFileName(SingleDestination), Mode);
    }

    private void UpdateOutputPreview()
    {
        if (outputLocationDisplay != null) { outputLocationDisplay.Text = FolderDestination ? outputFolder : SingleDestination; outputLocationDisplay.ToolTip = "Type or paste a path, or use the button to browse"; }
        suggestedOutput = SuggestedDestination();
        foreach (var preview in filenamePreviews) { preview.Text.Text = IsBoth ? GeneratedName(preview.Mode) : IsPaf ? PafFolderName : Path.GetFileName(SingleDestination); preview.Text.ToolTip = preview.Text.Text; }
    }

    private void FilenamePreview(StackPanel panel, string label, string value, OutputMode mode)
    {
        var row = new Grid { Margin = new(0, 1, 0, 1) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(76) }); row.ColumnDefinitions.Add(new());
        var caption = Label(label); caption.FontSize = 12; caption.Margin = new(0); row.Children.Add(caption);
        var preview = new TextBlock { Text = value, ToolTip = value, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        filenamePreviews.Add((mode, preview));
        Grid.SetColumn(preview, 1); row.Children.Add(preview); panel.Children.Add(row);
    }

    private AppWrapInput OutputInput(OutputMode mode, string path) => new(source, name.Trim(), version.Trim(), executable, startMenu, desktop, path, mode, mode is OutputMode.PortableApps or OutputMode.PafPackage ? icon : null, mode == OutputMode.PortableApps ? sidebar : null, mode == OutputMode.Standard ? StandardInstallerOptions.Normalize(CurrentStandardOptions) : null, mode is OutputMode.PortableApps or OutputMode.PafPackage ? profileState.EffectiveProfile : null);

    private async Task<bool> BuildSelectionAsync()
    {
        if (!destinationChosen) throw new UserInputException("Choose a location for your finished package first.");
        var inputs = IsBoth
            ? new[] { OutputInput(OutputMode.Standard, Path.Combine(outputFolder, GeneratedName(OutputMode.Standard))), OutputInput(OutputMode.PortableApps, Path.Combine(outputFolder, GeneratedName(OutputMode.PortableApps))) }
            : IsPaf
            ? new[] { OutputInput(OutputMode.PafPackage, PafDestination) }
            : new[] { OutputInput(Mode, SingleDestination) };
        foreach (var input in inputs) InputRules.Validate(input);
        var projectWarning = await SaveProjectBeforeBuildAsync();
        OutputIdentity[] identities = [];
        if (!IsPaf)
        {
            Scanning(true);
            try { identities = await Task.Run(() => inputs.Select(i => OutputIdentity.Capture(i.OutputPath)).ToArray()); }
            finally { Scanning(false); }
        }
        var targets = IsPaf
            ? new[] { PafFolderName + (Directory.Exists(PafDestination) ? " — replace existing folder" : " — new folder") }
            : identities.Select(i => Path.GetFileName(i.Path) + (i.Exists ? " — replace existing file" : " — new file")).ToArray();
        if (IsPaf ? Directory.Exists(PafDestination) : identities.Any(i => i.Exists))
        {
            if (MessageBox.Show(this, "Build these packages?\n\n" + string.Join("\n", targets), "Replace existing packages", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return false;
        }
        busy = true; cancellation = new(); Render();
        try
        {
            var service = new BuildService(AppContext.BaseDirectory, StateDirectory);
            if (IsBoth)
            {
                var result = await Task.Run(() => service.BuildBothAsync(inputs[0], inputs[1], cancellation.Token, overwriteExisting: true, expectedOutputs: identities));
                cleanupWarning = result.CleanupWarning; output = result.Standard.OutputPath + "\n" + result.Portable.OutputPath;
            }
            else if (IsPaf)
            {
                var result = await Task.Run(() => service.BuildAsync(inputs[0], cancellation.Token));
                cleanupWarning = result.CleanupWarning; output = result.OutputPath;
            }
            else
            {
                var result = await Task.Run(() => service.BuildAsync(inputs[0], cancellation.Token, expectedOutput: identities[0]));
                cleanupWarning = result.CleanupWarning; output = result.OutputPath;
            }
            var recordWarning = await RecordProjectBuildAsync(inputs.Select(i => i.Mode));
            var warnings = new[] { projectWarning, recordWarning, cleanupWarning }.Where(w => !string.IsNullOrEmpty(w)).ToList();
            cleanupWarning = warnings.Count == 0 ? null : string.Join("\n", warnings);
            finished = true; return true;
        }
        finally { busy = false; cancellation.Dispose(); cancellation = null; }
    }
}
