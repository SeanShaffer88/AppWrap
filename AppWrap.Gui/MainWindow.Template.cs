using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppWrap.Core;
using AppWrap.Core.Projects;
using Microsoft.Win32;

namespace AppWrap.Gui;

public partial class MainWindow
{
    // Phase 5 "New Project from Template": the blank template scaffolds a
    // minimal valid PAF project (ProjectScaffolder) and loads it into the
    // wizard; the wrapped-binary template is the existing source-selection
    // flow, reached from here, with the template choice recorded in the
    // project file when the project is inferred. Scaffolding writes project
    // files only — it never runs a build.
    private bool templateOpen;
    private ProjectTemplateKind templateKind = ProjectTemplateKind.BlankPafApp;
    private string templateName = "";
    private string templateFolder = "";
    private string templateSpec = PafLayout.DefaultPafSpecVersion;
    private bool templateAdvanced;
    private string? templateNote;
    private IReadOnlyList<PafDiagnostic>? templateDiagnostics;
    private string? pendingTemplate;

    private void OpenTemplatePanel()
    {
        templateKind = ProjectTemplateKind.BlankPafApp;
        templateNote = null;
        templateDiagnostics = null;
        templateOpen = true;
        Render();
    }

    private void RenderTemplatePanel(StackPanel panel)
    {
        StepLabel.Text = "NEW PROJECT";
        StepTopic.Text = "New project from template";
        Question.Text = "Start a new project";
        Hint.Text = "A template creates the project files only — nothing is built. The blank template makes a minimal PortableApps.com Format skeleton with TODO placeholders to finish; the wrapped-binary template is the usual flow of choosing an existing application folder.";
        NextButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        Formats.Visibility = Visibility.Collapsed;

        panel.Children.Add(Label("Template"));
        var templates = Identify(new ComboBox { ItemsSource = new[] { "Blank PAF app", "Wrapped binary — wrap an existing folder" }, SelectedIndex = templateKind == ProjectTemplateKind.WrappedBinary ? 1 : 0, FontSize = 13, MinHeight = 36, Margin = new(0, 0, 0, 10) }, "TemplateKind");
        templates.SelectionChanged += (_, _) => { if (templates.SelectedIndex >= 0) { templateKind = templates.SelectedIndex == 1 ? ProjectTemplateKind.WrappedBinary : ProjectTemplateKind.BlankPafApp; templateNote = null; templateDiagnostics = null; Render(); } };
        panel.Children.Add(templates);

        if (templateKind == ProjectTemplateKind.WrappedBinary)
        {
            var about = new TextBlock { Text = "Choose the folder that contains your app and the files it needs. The wizard continues exactly as when you pick a folder on the first page; the project created for it records that it came from this template.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
            about.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(about);
            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            var choose = SmallButton("Choose application folder…", "TemplateChooseFolder");
            choose.Click += (_, _) => { pendingTemplate = ProjectScaffolder.WrappedBinaryTemplateName; templateOpen = false; BrowseSource(); };
            bar.Children.Add(choose);
            var backFromTemplate = SmallButton("Back", "TemplateBack");
            backFromTemplate.Click += (_, _) => { templateOpen = false; Render(); };
            bar.Children.Add(backFromTemplate);
            panel.Children.Add(bar);
            return;
        }

        panel.Children.Add(Label("App name"));
        var nameBox = Identify(new TextBox { Text = templateName, MaxLength = 120, FontSize = 13, MinHeight = 36, Margin = new(0, 0, 0, 10) }, "TemplateAppName");
        nameBox.TextChanged += (_, _) => templateName = nameBox.Text;
        panel.Children.Add(nameBox);

        panel.Children.Add(Label("New project folder"));
        var folderRow = new Grid { Margin = new(0, 0, 0, 10) };
        folderRow.ColumnDefinitions.Add(new());
        folderRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var folderText = Identify(new TextBox { Text = templateFolder, ToolTip = "Type or paste the new project folder, or use Choose folder…", Style = null, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, IsReadOnly = false, Padding = new(0, 6, 8, 6), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center }, "TemplateFolder");
        folderText.SetResourceReference(Control.ForegroundProperty, "Text");
        folderRow.Children.Add(folderText);
        WirePathCommit(folderText, text =>
        {
            var folder = WizardPaths.NormalizePathText(text);
            if (WizardPaths.SamePath(folder, templateFolder)) { Render(); return; }
            templateFolder = folder; templateNote = null; templateDiagnostics = null; Render();
        });
        var chooseFolder = Identify(new Button { Content = "Choose folder…", Padding = new(12, 6, 12, 6), MinHeight = 40, FontSize = 13 }, "TemplateChooseDestination");
        chooseFolder.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose the new project folder", Multiselect = false };
            if (dialog.ShowDialog(this) == true) { templateFolder = dialog.FolderName; templateNote = null; templateDiagnostics = null; Render(); }
        };
        Grid.SetColumn(chooseFolder, 1); folderRow.Children.Add(chooseFolder);
        var folderCard = new Border { Child = folderRow, CornerRadius = new(8), Padding = new(10, 4, 4, 4), BorderThickness = new(1), Margin = new(0, 0, 0, 10) };
        folderCard.SetResourceReference(Border.BackgroundProperty, "InputSurface"); folderCard.SetResourceReference(Border.BorderBrushProperty, "Border");
        panel.Children.Add(folderCard);

        panel.Children.Add(Label("PAF specification version"));
        var specBox = Identify(new TextBox { Text = templateSpec, MaxLength = 16, FontSize = 13, MinHeight = 36, Margin = new(0, 0, 0, 4) }, "TemplateSpecVersion");
        specBox.TextChanged += (_, _) => templateSpec = specBox.Text;
        panel.Children.Add(specBox);
        var specHint = new TextBlock { Text = $"AppWrap's toolchain targets PAF {PafLayout.DefaultPafSpecVersion}; another version is recorded in the project file, and the validator will flag the mismatch.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
        specHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(specHint);

        var advanced = Identify(new CheckBox { Content = "Advanced mode — also scaffold custom code starters", IsChecked = templateAdvanced, FontSize = 12, MinHeight = 24 }, "TemplateAdvancedMode");
        advanced.Checked += (_, _) => templateAdvanced = true;
        advanced.Unchecked += (_, _) => templateAdvanced = false;
        panel.Children.Add(advanced);
        var advancedHint = new TextBlock { Text = "Advanced mode is saved in the new project file. The starters are comment-only files with headers explaining how each is used; packaging behaviour is unchanged.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 10) };
        advancedHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(advancedHint);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
        var create = SmallButton("Create project", "TemplateCreate");
        create.Click += (_, _) => CreateBlankProject();
        actions.Children.Add(create);
        var back = SmallButton("Back", "TemplateBack");
        back.Click += (_, _) => { templateOpen = false; Render(); };
        actions.Children.Add(back);
        panel.Children.Add(actions);

        if (templateNote is not null)
        {
            var note = new TextBlock { Text = templateNote, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(note);
        }
        if (templateDiagnostics is { Count: > 0 } diagnostics)
        {
            var lines = diagnostics.Select(d => $"{d.Severity} · {d.FilePath} · {d.Message}");
            var box = Identify(new TextBox { Text = string.Join("\n", lines), IsReadOnly = true, FontSize = 12, TextWrapping = TextWrapping.NoWrap, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, "TemplateDiagnostics");
            panel.Children.Add(box);
        }
    }

    private void CreateBlankProject()
    {
        if (string.IsNullOrWhiteSpace(templateName)) { Error.Text = "Enter an app name for the new project."; return; }
        if (string.IsNullOrEmpty(templateFolder)) { Error.Text = "Choose a new, empty folder for the project."; return; }
        try
        {
            var result = ProjectScaffolder.CreateBlank(templateFolder, templateName, templateSpec, templateAdvanced);
            source = result.Root;
            loadedProject = new LoadedProject(result.Project, false);
            name = result.Project.Name;
            developerWebsite = "";
            executable = "";
            executables = [];
            InvalidateOutputName();
            templateDiagnostics = result.Diagnostics;
            var todos = result.Diagnostics.Count(d => d.Code == "TodoPlaceholder");
            templateNote = $"Project created in {result.Root}. It opens with the Project configuration… button on the first page. {todos} placeholder (TODO) item(s) to finish — they are listed below and shown as warnings in the configuration panel.";
            Render();
        }
        catch (Exception ex) { Render(); ShowError(ex); }
    }
}
