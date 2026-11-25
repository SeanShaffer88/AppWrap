using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AppWrap.Core;
using AppWrap.Core.PortableProfiles;
using AppWrap.Generators;
using Microsoft.Win32;

namespace AppWrap.Gui;

public partial class MainWindow
{
    private readonly ProfileWorkspaceState profileState = new();
    private bool configurePortable;
    private ProfileCategory profileCategory = ProfileCategory.OverviewLaunch;
    private int profilePageIndex;
    private string? profileSelectedRowId;
    private PortableDocumentKind profileTextKind = PortableDocumentKind.Launcher;
    private bool profileSyncingText;
    private TextBlock? profileDiagSummary;
    private TextBox? profileDiagAll;
    private const int ProfilePageSize = 3;

    private static readonly (string Label, PortableDocumentKind Kind)[] ProfileTextDocuments =
        [("Launcher configuration", PortableDocumentKind.Launcher),
         ("Installer configuration", PortableDocumentKind.Installer),
         ("Package details", PortableDocumentKind.Metadata)];

    private string ProfileAppId() => PortableAppsGenerator.PackageId(OutputInput(OutputMode.PortableApps, "package.exe"));

    private Button SmallButton(string content, string id)
    {
        var button = Identify(new Button { Content = content, FontSize = 12, MinHeight = 30, Padding = new(10, 4, 10, 4) }, id);
        button.Margin = new(0, 0, 6, 0);
        return button;
    }

    private void RenderPortableBehavior(StackPanel panel)
    {
        Question.Text = "How does your app handle its settings?";
        Hint.Text = "Choose “Already portable” if the app keeps everything in its own folder.\nChoose “Configure data and cleanup” to teach the launcher where the app stores files and settings, so they travel with the package.";
        profileState.WizardAppId = ProfileAppId();
        var modes = new Border { CornerRadius = new(12), Padding = new(4), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 12) };
        modes.SetResourceReference(Border.BackgroundProperty, "InputSurface"); modes.SetResourceReference(Border.BorderBrushProperty, "Border");
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        var already = Identify(new RadioButton { Content = "Already portable — package as supplied", IsChecked = !configurePortable, GroupName = "PortableBehavior" }, "PortableAlready");
        already.SetResourceReference(StyleProperty, "FormatChoice");
        already.Checked += (_, _) => { if (configurePortable) { configurePortable = false; profileState.SetMode(PortableExecutionKind.PackagingOnly); Render(); } };
        var configure = Identify(new RadioButton { Content = "Configure data and cleanup", IsChecked = configurePortable, GroupName = "PortableBehavior" }, "PortableConfigure");
        configure.SetResourceReference(StyleProperty, "FormatChoice");
        configure.Checked += (_, _) => { if (!configurePortable) { configurePortable = true; profileState.SetMode(PortableExecutionKind.Pal); Render(); } };
        choices.Children.Add(already); choices.Children.Add(configure); modes.Child = choices; panel.Children.Add(modes);
        if (!configurePortable)
        {
            var note = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            note.Text = profileState.HasWorkingEdits
                ? "Your portable profile is kept for this session. It will not be used unless you choose “Configure data and cleanup”."
                : "The app is packaged exactly as supplied; AppWrap does not move its files or settings, or clean anything up.";
            panel.Children.Add(note);
            return;
        }
        RenderProfileWorkspace(panel);
    }

    private void RenderProfileWorkspace(StackPanel panel)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new());
        panel.Children.Add(root);

        var header = new Grid();
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Portable profile", FontSize = 15, FontWeight = FontWeights.SemiBold });
        var status = new TextBlock { Text = "Applies to the portable package · Engine PAL 2.2.9 (pinned)", FontSize = 12 };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); title.Children.Add(status);
        header.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var newButton = SmallButton("New", "ProfileNew");
        newButton.Click += (_, _) => { profileState.NewProfile(); profileCategory = ProfileCategory.OverviewLaunch; profilePageIndex = 0; profileSelectedRowId = null; Render(); };
        var loadButton = SmallButton("Load…", "ProfileLoad");
        loadButton.Click += (_, _) => LoadProfileFile();
        var saveButton = SmallButton("Save…", "ProfileSave");
        saveButton.Click += (_, _) => SaveProfileFile();
        actions.Children.Add(newButton); actions.Children.Add(loadButton); actions.Children.Add(saveButton);
        Grid.SetColumn(actions, 1); header.Children.Add(actions);
        root.Children.Add(header);

        var main = new Grid { Margin = new(0, 8, 0, 0) };
        main.ColumnDefinitions.Add(new() { Width = new GridLength(148) });
        main.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        main.ColumnDefinitions.Add(new());
        Grid.SetRow(main, 1); root.Children.Add(main);

        var categories = new StackPanel();
        foreach (var info in ProfileWorkspaceState.Categories)
        {
            var button = new Button { Content = info.Title, FontSize = 12, MinHeight = 30, Padding = new(8, 4, 8, 4), Margin = new(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
            Identify(button, "ProfileCategory" + info.Category);
            if (info.Category == profileCategory)
            {
                button.SetResourceReference(BackgroundProperty, "Accent");
                button.SetResourceReference(ForegroundProperty, "AccentText");
            }
            var captured = info.Category;
            button.Click += (_, _) => { profileCategory = captured; profilePageIndex = 0; profileSelectedRowId = null; Render(); };
            categories.Children.Add(button);
        }
        main.Children.Add(categories);

        var right = new StackPanel();
        Grid.SetColumn(right, 2); main.Children.Add(right);
        var categoryInfo = ProfileWorkspaceState.Categories.Single(c => c.Category == profileCategory);
        var description = new TextBlock { Text = categoryInfo.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        right.Children.Add(description);

        if (profileCategory == ProfileCategory.OverviewLaunch)
        {
            var summary = new TextBox { Text = string.Join("\n", profileState.PlanSummaryLines()), IsReadOnly = true, FontSize = 12, MaxHeight = 56, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 6) };
            Identify(summary, "ProfilePlanSummary");
            right.Children.Add(summary);
        }

        ProfileRuleRow? selected = null;
        if (profileCategory != ProfileCategory.ConfigurationText)
        {
            selected = RenderProfileGrid(right, categoryInfo);
            RenderProfilePager(right, categoryInfo);
        }
        else
        {
            var note = new TextBlock { Text = "Edit the configuration text below. The rule pages update from the same text, and problems appear in the diagnostics row.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            right.Children.Add(note);
        }

        if (profileState.HasForceCleanupEntries && profileCategory != ProfileCategory.ConfigurationText)
        {
            var acknowledge = Identify(new CheckBox { Content = "I understand: force cleanup deletes the listed keys and folders on exit, even when they are not empty.", IsChecked = profileState.ForceCleanupAcknowledged, FontSize = 12, MinHeight = 24, Margin = new(0, 2, 0, 2) }, "ProfileForceCleanupAck");
            acknowledge.Checked += (_, _) => { profileState.ForceCleanupAcknowledged = true; Render(); };
            acknowledge.Unchecked += (_, _) => { profileState.ForceCleanupAcknowledged = false; Render(); };
            right.Children.Add(acknowledge);
        }

        if (profileCategory == ProfileCategory.TrustedExtensions) RenderExtensionEditor(right, selected);
        else if (profileCategory != ProfileCategory.ConfigurationText) RenderProfileRowEditor(right, selected);

        RenderProfileTextEditor(right);

        var diagnostics = new StackPanel { Margin = new(0, 8, 0, 0) };
        Grid.SetRow(diagnostics, 2); root.Children.Add(diagnostics);
        profileDiagSummary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(profileDiagSummary, AutomationLiveSetting.Assertive);
        Identify(profileDiagSummary, "ProfileDiagnostics");
        diagnostics.Children.Add(profileDiagSummary);
        var all = new Expander { Header = "All diagnostics", FontSize = 12, Margin = new(0, 4, 0, 0) };
        all.SetResourceReference(ForegroundProperty, "Muted");
        profileDiagAll = new TextBox { IsReadOnly = true, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(profileDiagAll, "ProfileDiagnosticsAll");
        all.Content = profileDiagAll;
        diagnostics.Children.Add(all);
        RefreshProfileDiagnostics();
    }

    private ProfileRuleRow? RenderProfileGrid(StackPanel right, ProfileCategoryInfo categoryInfo)
    {
        var page = profileState.Page(profileCategory, profilePageIndex, ProfilePageSize);
        var effective = Math.Clamp(profilePageIndex, 0, page.PageCount - 1);
        if (effective != profilePageIndex) { profilePageIndex = effective; page = profileState.Page(profileCategory, profilePageIndex, ProfilePageSize); }

        var headerGrid = new Grid { Margin = new(2, 0, 2, 2) };
        headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1.5, GridUnitType.Star) });
        foreach (var _ in categoryInfo.Columns) headerGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        AddGridCell(headerGrid, 0, "Rule", 11, true);
        for (var i = 0; i < categoryInfo.Columns.Count; i++) AddGridCell(headerGrid, i + 1, categoryInfo.Columns[i], 11, true);
        right.Children.Add(headerGrid);

        ProfileRuleRow? selected = null;
        foreach (var row in page.Rows)
        {
            if (row.Id == profileSelectedRowId) selected = row;
            var card = new Border { CornerRadius = new(6), Padding = new(8, 5, 8, 5), BorderThickness = new(1), Margin = new(0, 0, 0, 4), Focusable = true, Cursor = Cursors.Hand };
            card.SetResourceReference(Border.BackgroundProperty, "InputSurface");
            card.SetResourceReference(Border.BorderBrushProperty, row.Id == profileSelectedRowId ? "StepAccent" : "Border");
            AutomationProperties.SetAutomationId(card, "ProfileRow");
            AutomationProperties.SetName(card, row.Display);
            var content = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var display = new TextBlock { Text = row.Display, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = row.Display };
            top.Children.Add(display);
            var badgeText = row.IsBlocked ? "Blocked" : row.Status == CapabilityStatus.Supported ? "" : row.Status.ToString();
            if (badgeText.Length > 0)
            {
                var badge = new TextBlock { Text = badgeText, FontSize = 11, Margin = new(8, 0, 0, 0) };
                badge.SetResourceReference(TextBlock.ForegroundProperty, row.IsBlocked || row.Status == CapabilityStatus.Unsupported ? "Error" : "Muted");
                Grid.SetColumn(badge, 1); top.Children.Add(badge);
            }
            content.Children.Add(top);
            var cells = new Grid { Margin = new(0, 2, 0, 0) };
            cells.ColumnDefinitions.Add(new() { Width = new GridLength(1.5, GridUnitType.Star) });
            foreach (var _ in categoryInfo.Columns) cells.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            AddGridCell(cells, 0, $"[{row.Section}]", 11, false);
            for (var i = 0; i < categoryInfo.Columns.Count && i < row.Cells.Count; i++) AddGridCell(cells, i + 1, row.Cells[i], 11, false);
            content.Children.Add(cells);
            card.Child = content;
            var captured = row.Id;
            card.MouseLeftButtonDown += (_, _) => { profileSelectedRowId = captured; Render(); };
            card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { profileSelectedRowId = captured; Render(); e.Handled = true; } };
            right.Children.Add(card);
        }
        if (page.TotalCount == 0)
        {
            var empty = new TextBlock { Text = "No rules yet. Add one below, or write it in the configuration text.", FontSize = 12, Margin = new(2, 2, 0, 6) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            right.Children.Add(empty);
        }
        return selected ?? (profileSelectedRowId == null ? null : profileState.Rows(profileCategory).FirstOrDefault(r => r.Id == profileSelectedRowId));
    }

    private static void AddGridCell(Grid grid, int column, string text, double fontSize, bool muted)
    {
        var cell = new TextBlock { Text = text, FontSize = fontSize, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) };
        if (muted) cell.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void RenderProfilePager(StackPanel right, ProfileCategoryInfo categoryInfo)
    {
        var page = profileState.Page(profileCategory, profilePageIndex, ProfilePageSize);
        var bar = new Grid { Margin = new(0, 2, 0, 6) };
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new());
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var previous = SmallButton("Previous", "ProfilePagePrevious");
        previous.IsEnabled = profilePageIndex > 0;
        previous.Click += (_, _) => { profilePageIndex--; Render(); };
        bar.Children.Add(previous);
        var info = new TextBlock { Text = $"Page {Math.Clamp(profilePageIndex, 0, page.PageCount - 1) + 1} of {page.PageCount} · {page.TotalCount} rules", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 0, 8, 0) };
        info.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(info, 1); bar.Children.Add(info);
        var next = SmallButton("Next page", "ProfilePageNext");
        next.IsEnabled = profilePageIndex < page.PageCount - 1;
        next.Click += (_, _) => { profilePageIndex++; Render(); };
        Grid.SetColumn(next, 2); bar.Children.Add(next);

        var presets = ProfileWorkspaceState.Presets.Where(p => p.Category == profileCategory).ToList();
        if (profileCategory != ProfileCategory.TrustedExtensions)
        {
            var presetBox = Identify(new ComboBox { FontSize = 12, MinHeight = 30, Width = 180, Margin = new(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }, "ProfilePreset");
            presetBox.Items.Add("Blank rule");
            foreach (var preset in presets) presetBox.Items.Add(preset.Label);
            presetBox.SelectedIndex = 0;
            Grid.SetColumn(presetBox, 4); bar.Children.Add(presetBox);
            var add = SmallButton("Add", "ProfileAddRule");
            add.Click += (_, _) =>
            {
                string? newId;
                if (presetBox.SelectedIndex > 0)
                {
                    var preset = presets[presetBox.SelectedIndex - 1];
                    newId = preset.CreatesFileWriteSection ? profileState.AddFileWriteRule() : profileState.AddEntry(preset.Document, preset.Section, preset.Key, preset.Value);
                }
                else newId = profileState.AddDefaultRow(profileCategory);
                if (newId != null)
                {
                    profileSelectedRowId = newId;
                    var count = profileState.Rows(profileCategory).Count;
                    profilePageIndex = Math.Max(0, (count - 1) / ProfilePageSize);
                }
                Render();
            };
            Grid.SetColumn(add, 5); bar.Children.Add(add);
        }
        var remove = SmallButton("Remove", "ProfileRemoveRule");
        remove.IsEnabled = profileSelectedRowId != null;
        remove.Click += (_, _) =>
        {
            if (profileSelectedRowId != null && profileState.RemoveRow(profileSelectedRowId)) profileSelectedRowId = null;
            Render();
        };
        Grid.SetColumn(remove, 6); bar.Children.Add(remove);
        right.Children.Add(bar);
    }

    private void RenderProfileRowEditor(StackPanel right, ProfileRuleRow? selected)
    {
        var card = new Border { CornerRadius = new(8), Padding = new(10, 8, 10, 8), BorderThickness = new(1), Margin = new(0, 0, 0, 6) };
        card.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        right.Children.Add(card);
        if (selected is null)
        {
            var hint = new TextBlock { Text = "Select a rule to edit it here. Expert editing is always available in the configuration text below.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            card.Child = hint;
            return;
        }
        var editor = new StackPanel();
        card.Child = editor;
        if (selected.IsBlocked)
        {
            var banner = new TextBlock { Text = "Blocked: " + selected.StatusNote, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
            banner.SetResourceReference(TextBlock.ForegroundProperty, "Error");
            editor.Children.Add(banner);
        }
        else if (selected.StatusNote.Length > 0)
        {
            var note = new TextBlock { Text = selected.StatusNote, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Error");
            editor.Children.Add(note);
        }
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(200) });
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
        fields.ColumnDefinitions.Add(new());
        fields.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var keyBox = Identify(new TextBox { Text = selected.Key, FontSize = 12, MinHeight = 32, Padding = new(8, 4, 8, 4), IsReadOnly = !selected.CanEditKey, ToolTip = selected.CanEditKey ? "The exact key name" : "This key name is fixed by the format" }, "ProfileRowKey");
        keyBox.LostFocus += (_, _) =>
        {
            if (!selected.CanEditKey || keyBox.Text == selected.Key) return;
            try { profileSelectedRowId = profileState.UpdateRowKey(selected.Id, keyBox.Text); Render(); }
            catch (UserInputException ex) { Error.Text = ex.Message; Render(); }
        };
        fields.Children.Add(keyBox);
        var valueBox = Identify(new TextBox { Text = selected.Value, FontSize = 12, MinHeight = 32, Padding = new(8, 4, 8, 4), ToolTip = "The exact value" }, "ProfileRowValue");
        valueBox.TextChanged += (_, _) =>
        {
            profileState.UpdateRowValue(selected.Id, valueBox.Text);
            RefreshProfileDiagnostics();
        };
        Grid.SetColumn(valueBox, 2); fields.Children.Add(valueBox);
        var up = SmallButton("↑", "ProfileRowUp");
        up.IsEnabled = selected.CanReorder;
        up.ToolTip = "Move this rule up";
        up.Click += (_, _) => { if (profileState.MoveRow(selected.Id, -1)) Render(); };
        Grid.SetColumn(up, 3); fields.Children.Add(up);
        var down = SmallButton("↓", "ProfileRowDown");
        down.IsEnabled = selected.CanReorder;
        down.ToolTip = "Move this rule down";
        down.Click += (_, _) => { if (profileState.MoveRow(selected.Id, 1)) Render(); };
        Grid.SetColumn(down, 4); fields.Children.Add(down);
        editor.Children.Add(fields);
        var caption = new TextBlock { Text = $"[{selected.Section}] · line {selected.Line} · {selected.Status}", FontSize = 11, Margin = new(0, 4, 0, 0) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        editor.Children.Add(caption);
    }

    private void RenderExtensionEditor(StackPanel right, ProfileRuleRow? selected)
    {
        var card = new Border { CornerRadius = new(8), Padding = new(10, 8, 10, 8), BorderThickness = new(1), Margin = new(0, 0, 0, 6) };
        card.SetResourceReference(Border.BackgroundProperty, "InputSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        right.Children.Add(card);
        var editor = new StackPanel();
        card.Child = editor;
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(1.2, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(1.6, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
        fields.ColumnDefinitions.Add(new());
        fields.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var pathBox = Identify(new TextBox { FontSize = 12, MinHeight = 32, Padding = new(8, 4, 8, 4), ToolTip = "Path inside the profile folder, for example Other\\Source\\Custom.nsh" }, "ProfileExtensionPath");
        fields.Children.Add(pathBox);
        var hashBox = Identify(new TextBox { FontSize = 12, MinHeight = 32, Padding = new(8, 4, 8, 4), ToolTip = "The file's SHA-256 (64 hexadecimal characters)" }, "ProfileExtensionHash");
        Grid.SetColumn(hashBox, 2); fields.Children.Add(hashBox);
        var purposeBox = Identify(new TextBox { FontSize = 12, MinHeight = 32, Padding = new(8, 4, 8, 4), ToolTip = "Why this source is part of the package" }, "ProfileExtensionPurpose");
        Grid.SetColumn(purposeBox, 4); fields.Children.Add(purposeBox);
        var add = SmallButton("Add extension", "ProfileExtensionAdd");
        add.Click += (_, _) =>
        {
            try { profileState.AddExtension(pathBox.Text, hashBox.Text, purposeBox.Text); Render(); }
            catch (UserInputException ex) { Error.Text = ex.Message; Render(); }
        };
        Grid.SetColumn(add, 5); fields.Children.Add(add);
        editor.Children.Add(fields);
        if (selected is not null && selected.Id.StartsWith("ext:", StringComparison.Ordinal)
            && int.TryParse(selected.Id.AsSpan(4), out var index) && index < profileState.Extensions.Count)
        {
            var extension = profileState.Extensions[index];
            var trust = Identify(new CheckBox { Content = "Trust this exact file (this path and hash) as launcher source", IsChecked = extension.Trusted, FontSize = 12, MinHeight = 24, Margin = new(0, 6, 0, 0) }, "ProfileExtensionTrust");
            trust.Checked += (_, _) => { profileState.SetExtensionTrusted(index, true); Render(); };
            trust.Unchecked += (_, _) => { profileState.SetExtensionTrusted(index, false); Render(); };
            editor.Children.Add(trust);
            var note = new TextBlock { Text = "Trusting lets this source run when the launcher is compiled and when the portable app runs. Editing or re-importing the file removes the trust.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            editor.Children.Add(note);
        }
    }

    private void RenderProfileTextEditor(StackPanel right)
    {
        var labelRow = new Grid { Margin = new(0, 2, 0, 4) };
        labelRow.ColumnDefinitions.Add(new());
        labelRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = new TextBlock { Text = ProfileTextLabel(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        labelRow.Children.Add(label);
        var selector = Identify(new ComboBox { FontSize = 12, MinHeight = 30, Width = 200 }, "ProfileTextDocument");
        foreach (var (text, _) in ProfileTextDocuments) selector.Items.Add(text);
        selector.SelectedIndex = Array.FindIndex(ProfileTextDocuments, d => d.Kind == profileTextKind);
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedIndex >= 0 && ProfileTextDocuments[selector.SelectedIndex].Kind != profileTextKind)
            {
                profileTextKind = ProfileTextDocuments[selector.SelectedIndex].Kind;
                Render();
            }
        };
        Grid.SetColumn(selector, 1); labelRow.Children.Add(selector);
        right.Children.Add(labelRow);

        var box = Identify(new TextBox
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 72,
        }, "ProfileConfigurationText");
        profileSyncingText = true;
        box.Text = profileState.GetDocumentText(profileTextKind);
        profileSyncingText = false;
        box.TextChanged += (_, _) =>
        {
            if (profileSyncingText) return;
            try { profileState.SetDocumentText(profileTextKind, box.Text); }
            catch (UserInputException ex) { Error.Text = ex.Message; }
            RefreshProfileDiagnostics();
        };
        right.Children.Add(box);
    }

    private string ProfileTextLabel() => profileTextKind switch
    {
        PortableDocumentKind.Launcher => $"Launcher configuration — App/AppInfo/Launcher/{ProfileAppId()}.ini",
        PortableDocumentKind.Installer => "Installer configuration — App/AppInfo/installer.ini",
        _ => "Package details — App/AppInfo/appinfo.ini",
    };

    private void RefreshProfileDiagnostics()
    {
        if (profileDiagSummary == null || profileDiagAll == null) return;
        var diagnostics = profileState.Diagnostics;
        var errors = diagnostics.Count(d => d.Severity == ProfileSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == ProfileSeverity.Warning);
        profileDiagSummary.Text = diagnostics.Count == 0
            ? "No problems found. Evidence level: configuration validated."
            : $"{errors} error(s), {warnings} warning(s). First: [{diagnostics[0].Code}] {diagnostics[0].Message}";
        profileDiagSummary.SetResourceReference(TextBlock.ForegroundProperty, errors > 0 ? "Error" : "Muted");
        profileDiagAll.Text = string.Join("\n", diagnostics.Select(d => $"{d.Severity} [{d.Code}] {d.DocumentPath}: {d.Message}"));
        if (UsesPortable && step == BehaviorStep && configurePortable) NextButton.IsEnabled = errors == 0;
    }

    private void LoadProfileFile()
    {
        var dialog = new OpenFileDialog { Title = "Load a portable profile", Filter = "AppWrap portable profile|*.json|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            profileState.ImportProfileText(File.ReadAllText(dialog.FileName));
            profileCategory = ProfileCategory.OverviewLaunch;
            profilePageIndex = 0;
            profileSelectedRowId = null;
            Render();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SaveProfileFile()
    {
        var dialog = new SaveFileDialog { Title = "Save the portable profile", Filter = "AppWrap portable profile|*.json", DefaultExt = ".json", AddExtension = true, FileName = "portable-profile.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, profileState.ExportProfileText()); }
        catch (Exception ex) { ShowError(ex); }
    }
}
