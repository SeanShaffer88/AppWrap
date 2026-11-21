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
    private enum BuildSelection { Standard, Portable, Both }
    private BuildSelection Selection => BothMode.IsChecked == true ? BuildSelection.Both : PortableMode.IsChecked == true ? BuildSelection.Portable : BuildSelection.Standard;
    private bool IsBoth => Selection == BuildSelection.Both;
    private bool UsesPortable => Selection != BuildSelection.Standard;
    private PackageNamingStyle namingStyle = PackageNamingStyle.NameVersionSetup;
    private string outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private bool destinationChosen;
    private TextBox? outputLocationDisplay;
    private readonly List<(OutputMode Mode, TextBlock Text)> filenamePreviews = [];

    private string GeneratedName(OutputMode mode) => PackageNaming.GetFileName(name.Trim(), version.Trim(), mode, namingStyle);
    private string SingleDestination => string.IsNullOrEmpty(output) ? Path.Combine(outputFolder, GeneratedName(Mode)) : output;
    private void InvalidateOutputName()
    {
        if (!string.IsNullOrEmpty(output)) outputFolder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        output = "";
    }

    private void RenderOutput(StackPanel panel)
    {
        filenamePreviews.Clear();
        Question.Text = IsBoth ? "Where should we save the packages?" : Mode == OutputMode.Standard ? "Where should the installer be saved?" : "Where should the portable package be saved?";
        Hint.Text = IsBoth ? "One build creates both files from the same app folder." : "Choose a filename style and location, then click Build.";
        var namingRow = new Grid { Margin = new(0, 0, 0, 8) };
        namingRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); namingRow.ColumnDefinitions.Add(new());
        var namingLabel = Label("Filename style"); namingLabel.Margin = new(0, 0, 14, 0); namingLabel.VerticalAlignment = VerticalAlignment.Center;
        namingRow.Children.Add(namingLabel);
        var styles = Identify(new ComboBox { ItemsSource = PackageNaming.Labels, SelectedIndex = (int)namingStyle, FontSize = 13, MinHeight = 38, Height = 40 }, "NamingStyle");
        styles.SelectionChanged += (_, _) => { if (styles.SelectedIndex < 0) return; namingStyle = (PackageNamingStyle)styles.SelectedIndex; InvalidateOutputName(); UpdateOutputPreview(); };
        Grid.SetColumn(styles, 1); namingRow.Children.Add(styles); panel.Children.Add(namingRow);
        var location = new Grid(); location.ColumnDefinitions.Add(new()); location.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var value = destinationChosen ? IsBoth ? outputFolder : SingleDestination : "Choose where to save your package";
        var path = Identify(new TextBox { Text = value, ToolTip = value, Style = null, BorderThickness = new(0), Background = Brushes.Transparent, IsReadOnly = true, Padding = new(0, 6, 8, 6), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center }, IsBoth ? "OutputFolder" : "OutputPath");
        path.SetResourceReference(Control.ForegroundProperty, "Text"); location.Children.Add(path);
        outputLocationDisplay = path;
        var choose = Identify(new Button { Content = IsBoth ? "Choose folder…" : "Save as…", Padding = new(12, 6, 12, 6), MinHeight = 40, FontSize = 13 }, IsBoth ? "BrowseOutputFolder" : "BrowseOutputPath");
        choose.Click += (_, _) => BrowseOutput(); Grid.SetColumn(choose, 1); location.Children.Add(choose);
        var locationCard = new Border { Child = location, CornerRadius = new(8), Padding = new(10, 4, 4, 4), BorderThickness = new(1), Margin = new(0, 0, 0, 8) };
        locationCard.SetResourceReference(Border.BackgroundProperty, "InputSurface"); locationCard.SetResourceReference(Border.BorderBrushProperty, "Border"); panel.Children.Add(locationCard);
        if (IsBoth) { FilenamePreview(panel, "Installer", GeneratedName(OutputMode.Standard), OutputMode.Standard); FilenamePreview(panel, "Portable", GeneratedName(OutputMode.PortableApps), OutputMode.PortableApps); }
        else FilenamePreview(panel, "Filename", Path.GetFileName(SingleDestination), Mode);
    }

    private void UpdateOutputPreview()
    {
        if (outputLocationDisplay != null) { outputLocationDisplay.Text = destinationChosen ? IsBoth ? outputFolder : SingleDestination : "Choose where to save your package"; outputLocationDisplay.ToolTip = outputLocationDisplay.Text; }
        foreach (var preview in filenamePreviews) { preview.Text.Text = IsBoth ? GeneratedName(preview.Mode) : Path.GetFileName(SingleDestination); preview.Text.ToolTip = preview.Text.Text; }
    }

    private void FilenamePreview(StackPanel panel, string label, string value, OutputMode mode)
    {
        var row = new Grid { Margin = new(0, 1, 0, 1) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(76) }); row.ColumnDefinitions.Add(new());
        var caption = Label(label); caption.FontSize = 12; caption.Margin = new(0); row.Children.Add(caption);
        var preview = new TextBlock { Text = value, ToolTip = value, FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        filenamePreviews.Add((mode, preview));
        Grid.SetColumn(preview, 1); row.Children.Add(preview); panel.Children.Add(row);
    }

    private AppWrapInput OutputInput(OutputMode mode, string path) => new(source, name.Trim(), version.Trim(), executable, startMenu, desktop, path, mode, mode == OutputMode.PortableApps ? icon : null, mode == OutputMode.PortableApps ? sidebar : null);

    private async Task<bool> BuildSelectionAsync()
    {
        if (!destinationChosen) throw new UserInputException("Choose a location for your finished package first.");
        var inputs = IsBoth
            ? new[] { OutputInput(OutputMode.Standard, Path.Combine(outputFolder, GeneratedName(OutputMode.Standard))), OutputInput(OutputMode.PortableApps, Path.Combine(outputFolder, GeneratedName(OutputMode.PortableApps))) }
            : new[] { OutputInput(Mode, SingleDestination) };
        foreach (var input in inputs) InputRules.Validate(input);
        OutputIdentity[] identities;
        Scanning(true);
        try { identities = await Task.Run(() => inputs.Select(i => OutputIdentity.Capture(i.OutputPath)).ToArray()); }
        finally { Scanning(false); }
        if (identities.Any(i => i.Exists))
        {
            var files = string.Join("\n", identities.Select(i => Path.GetFileName(i.Path) + (i.Exists ? " — replace existing file" : " — new file")));
            if (MessageBox.Show(this, "Build these packages?\n\n" + files, "Replace existing packages", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return false;
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
            else
            {
                var result = await Task.Run(() => service.BuildAsync(inputs[0], cancellation.Token, expectedOutput: identities[0]));
                cleanupWarning = result.CleanupWarning; output = result.OutputPath;
            }
            finished = true; return true;
        }
        finally { busy = false; cancellation.Dispose(); cancellation = null; }
    }
}
