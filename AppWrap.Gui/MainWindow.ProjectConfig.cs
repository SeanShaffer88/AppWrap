using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AppWrap.Core;
using AppWrap.Core.Projects;

namespace AppWrap.Gui;

public partial class MainWindow
{
    // Phase 2 configuration surface: guided editors for the loaded project's
    // appinfo.ini, launcher INI and installer.ini, bound to the Phase 1 project
    // model through ProjectConfigWorkspace (the files on disk, parsed losslessly,
    // are the single source of truth). PafValidator runs over the whole project —
    // including unsaved edits — and its diagnostics surface here without ever
    // blocking navigation. Advanced mode (off by default, persisted per project)
    // only unlocks direct text editing of the two custom-code files; packaging
    // behaviour is untouched by anything on this panel.
    private enum ConfigTab { AppInfo, Launcher, Installer, Project, CustomCode }

    private ProjectConfigWorkspace? configWorkspace;
    private bool configOpen;
    private ConfigTab configTab = ConfigTab.AppInfo;
    private bool configCustomInstaller;
    private bool configSyncingText;
    private TextBlock? configDiagSummary;
    private TextBox? configDiagAll;

    private static readonly (string Label, ConfigTab Tab)[] ConfigTabs =
        [("App info", ConfigTab.AppInfo), ("Launcher", ConfigTab.Launcher), ("Installer", ConfigTab.Installer), ("Project", ConfigTab.Project), ("Custom code", ConfigTab.CustomCode)];

    private static ProjectDocumentKind DocumentOf(ConfigTab tab) => tab switch
    {
        ConfigTab.AppInfo => ProjectDocumentKind.AppInfo,
        ConfigTab.Launcher => ProjectDocumentKind.Launcher,
        _ => ProjectDocumentKind.Installer,
    };

    private void OpenProjectConfig()
    {
        var loaded = loadedProject;
        if (loaded is null || string.IsNullOrEmpty(source)) return;
        try
        {
            configWorkspace = ProjectConfigWorkspace.Open(source, loaded.Project, loaded.Inferred);
            configTab = ConfigTab.AppInfo;
            configOpen = true;
            Render();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RenderProjectConfig(StackPanel panel)
    {
        var workspace = configWorkspace!;
        StepLabel.Text = "PROJECT CONFIGURATION";
        StepTopic.Text = "Project configuration";
        Question.Text = "Project configuration";
        Hint.Text = "Edit this project's PortableApps configuration. Problems are listed as you work and never block you from moving around; packaging keeps its existing rules.";
        NextButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        Formats.Visibility = Visibility.Collapsed;

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
        foreach (var (label, tab) in ConfigTabs)
        {
            var button = Identify(new Button { Content = label, FontSize = 12, MinHeight = 30, Padding = new(10, 4, 10, 4), Margin = new(0, 0, 6, 0) }, "ConfigTab" + tab);
            if (tab == configTab)
            {
                button.SetResourceReference(BackgroundProperty, "Accent");
                button.SetResourceReference(ForegroundProperty, "AccentText");
            }
            var captured = tab;
            button.Click += (_, _) => { configTab = captured; Render(); };
            bar.Children.Add(button);
        }
        var validate = SmallButton("Validate all", "ConfigValidateAll");
        validate.Click += (_, _) => Render();
        bar.Children.Add(validate);
        var save = SmallButton("Save", "ConfigSave");
        save.Click += (_, _) =>
        {
            try { workspace.Save(); Render(); }
            catch (Exception ex) { ShowError(ex); }
        };
        bar.Children.Add(save);
        var done = SmallButton("Done", "ConfigDone");
        done.Click += (_, _) => { configOpen = false; configWorkspace = null; Render(); };
        bar.Children.Add(done);
        panel.Children.Add(bar);

        var advanced = Identify(new CheckBox
        {
            Content = "Advanced mode — edit custom code files directly",
            IsChecked = workspace.AdvancedMode,
            FontSize = 12,
            MinHeight = 24,
        }, "ConfigAdvancedMode");
        advanced.Checked += (_, _) => { workspace.AdvancedMode = true; Render(); };
        advanced.Unchecked += (_, _) => { workspace.AdvancedMode = false; Render(); };
        panel.Children.Add(advanced);
        var warning = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 2, 0, 8),
            Text = "Advanced mode is saved in this project file. It unlocks raw text editing of Custom.nsh and PortableApps.comInstallerCustom.nsh below. AppWrap does not check custom code, and packaging is unchanged: source build scripts in a payload are still rejected and generated installers still use only AppWrap's own hook. Leave this off unless you know NSH.",
        };
        warning.SetResourceReference(TextBlock.ForegroundProperty, workspace.AdvancedMode ? "Error" : "Muted");
        panel.Children.Add(warning);

        if (configTab == ConfigTab.CustomCode) RenderConfigCustomCode(panel, workspace);
        else if (configTab == ConfigTab.Project) RenderConfigImages(panel, workspace);
        else
        {
            RenderConfigFields(panel, workspace, DocumentOf(configTab));
            RenderConfigText(panel, workspace, DocumentOf(configTab));
        }

        var diagnostics = new StackPanel { Margin = new(0, 8, 0, 0) };
        panel.Children.Add(diagnostics);
        configDiagSummary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(configDiagSummary, AutomationLiveSetting.Assertive);
        Identify(configDiagSummary, "ConfigDiagnostics");
        diagnostics.Children.Add(configDiagSummary);
        var all = new Expander { Header = "All diagnostics", FontSize = 12, Margin = new(0, 4, 0, 0) };
        all.SetResourceReference(ForegroundProperty, "Muted");
        configDiagAll = new TextBox { IsReadOnly = true, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(configDiagAll, "ConfigDiagnosticsAll");
        all.Content = configDiagAll;
        diagnostics.Children.Add(all);
        RefreshConfigDiagnostics();
    }

    private void RenderConfigFields(StackPanel panel, ProjectConfigWorkspace workspace, ProjectDocumentKind kind)
    {
        var list = new StackPanel();
        foreach (var state in workspace.FieldStates(kind))
        {
            var field = state.Field;
            var row = new Grid { Margin = new(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new());
            var label = new TextBlock
            {
                Text = field.Label + (field.Required ? " *" : ""),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"[{field.Section}] {field.Key}" + (field.Hint.Length > 0 ? " — " + field.Hint : ""),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(label);
            if (field.Kind == ConfigFieldKind.Boolean)
            {
                var toggle = Identify(new CheckBox
                {
                    IsChecked = string.Equals(state.Value, "true", StringComparison.OrdinalIgnoreCase),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = field.Hint.Length > 0 ? field.Hint : $"[{field.Section}] {field.Key}",
                }, "ConfigField" + field.Section + field.Key);
                toggle.Checked += (_, _) => { workspace.SetField(field, "true"); Render(); };
                toggle.Unchecked += (_, _) => { workspace.SetField(field, "false"); Render(); };
                Grid.SetColumn(toggle, 1); row.Children.Add(toggle);
            }
            else
            {
                var box = Identify(new TextBox
                {
                    Text = state.Value ?? "",
                    FontSize = 12,
                    MinHeight = 28,
                    Padding = new(8, 3, 8, 3),
                    ToolTip = field.Hint.Length > 0 ? field.Hint : $"[{field.Section}] {field.Key}",
                }, "ConfigField" + field.Section + field.Key);
                box.LostFocus += (_, _) =>
                {
                    var text = box.Text.Trim();
                    var value = text.Length == 0 ? null : text;
                    if (!string.Equals(value, workspace.GetField(field), StringComparison.Ordinal))
                    {
                        workspace.SetField(field, value);
                        Render();
                    }
                };
                Grid.SetColumn(box, 1); row.Children.Add(box);
            }
            list.Children.Add(row);
            if (state.Diagnostics.Count > 0)
            {
                var first = state.Diagnostics[0];
                var caption = new TextBlock
                {
                    Text = first.Message,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new(190, -3, 0, 5),
                };
                caption.SetResourceReference(TextBlock.ForegroundProperty,
                    first.Severity == PafDiagnosticSeverity.Error ? "Error" : "Muted");
                list.Children.Add(caption);
            }
        }
        var scroller = new ScrollViewer
        {
            Content = list,
            MaxHeight = 168,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new(0, 0, 0, 6),
        };
        panel.Children.Add(scroller);
        var required = new TextBlock { Text = "* required by the PortableApps.com format checks.", FontSize = 11, Margin = new(0, 0, 0, 4) };
        required.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(required);
    }

    /// <summary>
    /// The Project tab: the project's first-class icon and sidebar image
    /// properties (appwrap.project.json, schema v3). Paths can be typed or
    /// pasted; a file inside the project is stored project-relative, one
    /// outside is stored absolute. The wizard's icon/sidebar steps and these
    /// fields are two views of the same properties: reopening the project
    /// restores the wizard picks from here, and a build records the wizard's
    /// picks back here.
    /// </summary>
    private void RenderConfigImages(StackPanel panel, ProjectConfigWorkspace workspace)
    {
        var intro = new TextBlock
        {
            Text = "These pictures belong to the project and are used by its builds. The sidebar image applies to the compiled portable package only — a PAF Package folder has no sidebar (the official installer keeps its sidebar in its own installation).",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 0, 0, 8),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(intro);
        foreach (var state in workspace.ImageFieldStates())
        {
            var row = new Grid { Margin = new(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = state.Label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = "appwrap.project.json — " + state.Key,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(label);
            var box = Identify(new TextBox
            {
                Text = state.ResolvedPath ?? "",
                FontSize = 12,
                MinHeight = 28,
                Padding = new(8, 3, 8, 3),
                ToolTip = state.StoredPath is null ? "No picture chosen; the default is used." : "Stored in the project file as: " + state.StoredPath,
            }, "ConfigProjectImage" + state.Key);
            var key = state.Key;
            box.LostFocus += (_, _) =>
            {
                var text = box.Text.Trim();
                // Compare in stored form; the workspace setter normalizes the
                // same way (relative entries resolve against the project root).
                string? candidate = text.Length == 0 ? null : ProjectStore.ToStoredPath(workspace.Root,
                    Path.IsPathRooted(text) ? text : Path.GetFullPath(Path.Combine(workspace.Root, text.Replace('/', Path.DirectorySeparatorChar))));
                var current = key == ProjectConfigWorkspace.IconPathKey ? workspace.IconPath : workspace.SidebarImagePath;
                if (!string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase))
                {
                    if (key == ProjectConfigWorkspace.IconPathKey) workspace.IconPath = text.Length == 0 ? null : text;
                    else workspace.SidebarImagePath = text.Length == 0 ? null : text;
                    Render();
                }
            };
            Grid.SetColumn(box, 1); row.Children.Add(box);
            var clear = SmallButton("Clear", "ConfigProjectImageClear" + state.Key);
            clear.IsEnabled = state.StoredPath is not null;
            clear.Margin = new(6, 0, 0, 0);
            clear.Click += (_, _) =>
            {
                if (key == ProjectConfigWorkspace.IconPathKey) workspace.IconPath = null;
                else workspace.SidebarImagePath = null;
                Render();
            };
            Grid.SetColumn(clear, 2); row.Children.Add(clear);
            panel.Children.Add(row);
            var captionText = state.StoredPath is null
                ? "Not set — builds use the default picture."
                : state.Exists
                    ? "Stored as " + state.StoredPath
                    : "Stored as " + state.StoredPath + " — the file no longer exists.";
            var caption = new TextBlock
            {
                Text = state.Diagnostics.Count > 0 ? state.Diagnostics[0].Message : captionText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(190, -3, 0, 5),
            };
            caption.SetResourceReference(TextBlock.ForegroundProperty,
                state.Diagnostics.Any(d => d.Severity == PafDiagnosticSeverity.Error) ? "Error" : "Muted");
            panel.Children.Add(caption);
        }
    }

    private void RenderConfigText(StackPanel panel, ProjectConfigWorkspace workspace, ProjectDocumentKind kind)
    {
        var label = new TextBlock { Text = "Configuration text — " + workspace.DisplayPathFor(kind), FontSize = 12, Margin = new(0, 2, 0, 4) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(label);
        var box = Identify(new TextBox
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 76,
            MaxHeight = 110,
        }, "ConfigConfigurationText");
        configSyncingText = true;
        box.Text = workspace.GetDocumentText(kind);
        configSyncingText = false;
        box.TextChanged += (_, _) =>
        {
            if (configSyncingText) return;
            try { workspace.SetDocumentText(kind, box.Text); }
            catch (UserInputException ex) { Error.Text = ex.Message; }
            RefreshConfigDiagnostics();
        };
        panel.Children.Add(box);
    }

    private void RenderConfigCustomCode(StackPanel panel, ProjectConfigWorkspace workspace)
    {
        var selector = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 6) };
        foreach (var (label, installer) in new[] { ("Launcher Custom.nsh", false), ("Installer custom code", true) })
        {
            var button = Identify(new Button { Content = label, FontSize = 12, MinHeight = 30, Padding = new(10, 4, 10, 4), Margin = new(0, 0, 6, 0) },
                installer ? "ConfigCustomInstaller" : "ConfigCustomLauncher");
            if (installer == configCustomInstaller)
            {
                button.SetResourceReference(BackgroundProperty, "Accent");
                button.SetResourceReference(ForegroundProperty, "AccentText");
            }
            button.Click += (_, _) => { configCustomInstaller = installer; Render(); };
            selector.Children.Add(button);
        }
        panel.Children.Add(selector);

        var kind = configCustomInstaller ? CustomCodeKind.Installer : CustomCodeKind.Launcher;
        var document = workspace.ReadCustomCode(kind);
        var pathLabel = new TextBlock
        {
            Text = document.DisplayPath + (document.Exists ? "" : " (not created yet)"),
            FontSize = 12,
            Margin = new(0, 0, 0, 4),
        };
        pathLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        panel.Children.Add(pathLabel);
        if (!workspace.AdvancedMode)
        {
            var note = new TextBlock
            {
                Text = "Advanced mode is off, so custom code is read-only here. The no-custom-scripts packaging posture is unchanged either way.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 0, 0, 6),
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(note);
        }
        var box = Identify(new TextBox
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 120,
            MaxHeight = 170,
            IsReadOnly = !workspace.AdvancedMode,
            Text = document.Text,
        }, "ConfigCustomCodeText");
        panel.Children.Add(box);
        var save = SmallButton("Save custom code", "ConfigCustomSave");
        save.IsEnabled = workspace.AdvancedMode;
        save.Margin = new(0, 6, 0, 0);
        save.Click += (_, _) =>
        {
            try
            {
                var outcome = workspace.WriteCustomCode(kind, box.Text);
                if (!outcome.Written) { Error.Text = outcome.Message; return; }
                Render();
            }
            catch (Exception ex) { ShowError(ex); }
        };
        panel.Children.Add(save);
    }

    private void RefreshConfigDiagnostics()
    {
        var workspace = configWorkspace;
        if (workspace is null || configDiagSummary is null || configDiagAll is null) return;
        var diagnostics = workspace.ValidateAll();
        var errors = diagnostics.Count(d => d.Severity == PafDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == PafDiagnosticSeverity.Warning);
        configDiagSummary.Text = diagnostics.Count == 0
            ? "No problems found. Evidence level: configuration validated."
            : $"{errors} error(s), {warnings} warning(s). First: [{diagnostics[0].Code}] {diagnostics[0].Message}";
        configDiagSummary.SetResourceReference(TextBlock.ForegroundProperty, errors > 0 ? "Error" : "Muted");
        configDiagAll.Text = string.Join("\n", diagnostics.Select(d =>
            $"{d.Severity} [{d.Code}] {d.FilePath}" + (d.Section is not null ? $" [{d.Section}] {d.Key}" : "") + $": {d.Message} — {d.Hint}"));
    }
}
