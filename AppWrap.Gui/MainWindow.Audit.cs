using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppWrap.Build;
using AppWrap.Build.Audit;
using AppWrap.Core;
using AppWrap.Core.Audit;
using Microsoft.Win32;

namespace AppWrap.Gui;

public partial class MainWindow
{
    // Portable-conversion audit panel: the wizard's route from a vendor
    // setup .exe/.msi or a portable .zip to a proper PortableApps.com
    // package. The static pass reads the source without executing it (zip
    // inventory, MSI tables, .reg files, exe family); the runtime pass is
    // a separate explicit button in the test harness's discipline — it
    // silently installs into a scratch folder, launches the app once and
    // diffs the registry. Findings synthesize a DRAFT portable profile
    // that opens in the profile workspace for review; like every import
    // it is untrusted until the user saves it there.
    private bool auditPanelOpen;
    private bool auditBusy;
    private string auditFile = "";
    private AuditReport? auditReport;
    private string? auditNote;
    private CancellationTokenSource? auditCancellation;

    private void OpenAuditPanel()
    {
        auditReport = null;
        auditNote = null;
        auditPanelOpen = true;
        Render();
    }

    private void RenderAuditPanel(StackPanel panel)
    {
        StepLabel.Text = "CONVERT TO PORTABLE";
        StepTopic.Text = "Audit a setup or zip";
        Question.Text = "Audit a setup file or portable zip";
        Hint.Text = "AppWrap reads the source to find where the app stores data and which registry entries it creates, then drafts a portable profile that cleans up after it. The static audit never runs the source. The runtime audit is a separate, explicit step: it installs silently into a scratch folder, runs the app once, and watches the registry.";
        NextButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        Formats.Visibility = Visibility.Collapsed;

        panel.Children.Add(Label("Setup .exe / .msi, or portable .zip"));
        var fileRow = new Grid { Margin = new(0, 0, 0, 10) };
        fileRow.ColumnDefinitions.Add(new());
        fileRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var fileText = Identify(new TextBox { Text = auditFile, ToolTip = "Type or paste the setup file or portable zip to audit, or use Choose file…", Style = null, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, FontFamily = MonoFont, IsEnabled = !auditBusy, Padding = new(0, 6, 8, 6), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center }, "AuditSourceFile");
        fileText.SetResourceReference(Control.ForegroundProperty, "Text");
        fileRow.Children.Add(fileText);
        WirePathCommit(fileText, text =>
        {
            if (auditBusy) return;
            var file = WizardPaths.NormalizePathText(text);
            if (WizardPaths.SamePath(file, auditFile)) { Render(); return; }
            auditFile = file; auditReport = null; auditNote = null; Render();
        });
        var choose = Identify(new Button { Content = "Choose file…", Padding = new(12, 6, 12, 6), MinHeight = 40, FontSize = 13, IsEnabled = !auditBusy }, "AuditChooseFile");
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose the setup file or portable zip to audit", Filter = "Setup and portable files|*.exe;*.msi;*.zip", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) { auditFile = dialog.FileName; auditReport = null; auditNote = null; Render(); }
        };
        Grid.SetColumn(choose, 1); fileRow.Children.Add(choose);
        var fileCard = new Border { Child = fileRow, CornerRadius = new(4), Padding = new(10, 4, 4, 4), BorderThickness = new(1), Margin = new(0, 0, 0, 10) };
        fileCard.SetResourceReference(Border.BackgroundProperty, "InputSurface"); fileCard.SetResourceReference(Border.BorderBrushProperty, "Border");
        panel.Children.Add(fileCard);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
        var runStatic = SmallButton(auditBusy ? "Auditing…" : "Run static audit", "AuditRunStatic");
        runStatic.IsEnabled = !auditBusy;
        runStatic.Click += (_, _) => RunStaticAuditAsync();
        bar.Children.Add(runStatic);
        var extension = Path.GetExtension(auditFile).ToLowerInvariant();
        if (extension is ".exe" or ".msi")
        {
            var runRuntime = SmallButton("Run runtime audit", "AuditRunRuntime");
            runRuntime.IsEnabled = !auditBusy;
            runRuntime.Click += (_, _) => RunRuntimeAuditAsync();
            bar.Children.Add(runRuntime);
        }
        if (auditBusy)
        {
            var cancel = SmallButton("Cancel audit", "AuditCancel");
            cancel.Click += (_, _) => auditCancellation?.Cancel();
            bar.Children.Add(cancel);
        }
        if (auditReport is not null && !auditBusy)
        {
            var draft = SmallButton("Review draft profile", "AuditReviewDraft");
            draft.Click += (_, _) => ReviewAuditDraft();
            bar.Children.Add(draft);
        }
        var back = SmallButton("Back", "AuditBack");
        back.IsEnabled = !auditBusy;
        back.Click += (_, _) => { auditPanelOpen = false; Render(); };
        bar.Children.Add(back);
        panel.Children.Add(bar);

        if (auditBusy)
        {
            var running = new TextBlock { Text = "Auditing… the static pass only reads the source; the runtime pass installs into a scratch folder and runs the app once.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            running.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(running);
            return;
        }
        if (auditNote is not null)
        {
            var note = new TextBlock { Text = auditNote, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(note);
        }
        if (auditReport is { } report)
        {
            var summary = new TextBlock
            {
                Text = $"{report.Findings.Count} finding(s)" + (report.HasHardWarnings ? $" — {report.Findings.Count(f => f.HardWarning)} HARD WARNING(S): services/drivers cannot be made portable by the launcher." : "."),
                FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4),
            };
            summary.SetResourceReference(TextBlock.ForegroundProperty, report.HasHardWarnings ? "Error" : "Text");
            panel.Children.Add(summary);
            foreach (var finding in report.Findings.Take(60))
            {
                var line = new TextBlock { Text = $"{finding.Kind}: {finding.Subject} — {finding.Detail}", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 4) };
                line.SetResourceReference(TextBlock.ForegroundProperty, finding.HardWarning ? "Error" : "Text");
                panel.Children.Add(line);
            }
            if (report.Findings.Count > 60)
            {
                var more = new TextBlock { Text = $"…and {report.Findings.Count - 60} more finding(s). The draft profile uses all of them.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
                more.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                panel.Children.Add(more);
            }
        }
    }

    private async void RunStaticAuditAsync()
    {
        if (auditBusy) return;
        if (string.IsNullOrEmpty(auditFile) || !File.Exists(auditFile)) { Error.Text = "Choose the setup file or portable zip to audit first."; return; }
        auditBusy = true; auditReport = null; auditNote = null; auditCancellation = new();
        Render();
        try
        {
            var file = auditFile;
            var token = auditCancellation.Token;
            var extension = Path.GetExtension(file).ToLowerInvariant();
            auditReport = extension switch
            {
                ".zip" => await Task.Run(() => StaticAuditor.AuditZip(file, Path.Combine(StateDirectory, "Audit", Path.GetFileNameWithoutExtension(file) + "-" + Guid.NewGuid().ToString("N")[..8]), token), token),
                ".msi" => await Task.Run(() => StaticAuditor.AuditMsi(file, new WindowsMsiReader()), token),
                ".exe" => await Task.Run(() => StaticAuditor.AuditExe(file), token),
                _ => throw new UserInputException("Choose a setup .exe or .msi, or a portable .zip."),
            };
            auditNote = "Static audit complete. Review the findings, then review the draft profile — it stays untrusted until you save it in the profile workspace.";
        }
        catch (OperationCanceledException) { auditNote = "Audit cancelled."; }
        catch (Exception error) when (error is UserInputException or IOException or UnauthorizedAccessException) { Error.Text = error.Message; }
        finally { auditBusy = false; auditCancellation = null; Render(); }
    }

    private async void RunRuntimeAuditAsync()
    {
        if (auditBusy) return;
        if (string.IsNullOrEmpty(auditFile) || !File.Exists(auditFile)) { Error.Text = "Choose the setup file to audit first."; return; }
        auditBusy = true; auditNote = null; auditCancellation = new();
        Render();
        try
        {
            var token = auditCancellation.Token;
            var runtime = await RuntimeAuditor.RunAsync(new RuntimeAuditRequest(auditFile), token);
            auditReport = runtime.Report;
            auditNote = $"Runtime audit complete: installer exit {(runtime.InstallerExitCode?.ToString() ?? "unknown")}, {runtime.RegistryChanges.Count} registry change(s) observed. The scratch install folder was removed{(runtime.CleanupWarning is null ? "." : ": " + runtime.CleanupWarning)}";
        }
        catch (OperationCanceledException) { auditNote = "Runtime audit cancelled."; }
        catch (Exception error) when (error is UserInputException or BuildFailureException or IOException or UnauthorizedAccessException) { Error.Text = error.Message; }
        finally { auditBusy = false; auditCancellation = null; Render(); }
    }

    /// <summary>
    /// Synthesizes the draft profile from the current findings and loads it
    /// into the profile workspace (imports never grant trust), pointing the
    /// wizard at the audited payload when one is on disk. The user reviews
    /// and saves the draft in the workspace before it can build.
    /// </summary>
    private void ReviewAuditDraft()
    {
        if (auditReport is not { } report) return;
        var draft = AuditProfileSynthesizer.Synthesize(report);
        profileState.LoadProfile(draft.Profile);
        configurePortable = true;
        if (report.PayloadFolder is { } payload && Directory.Exists(payload))
        {
            source = payload;
            executables = [];
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(report.SourcePath);
        }
        auditPanelOpen = false;
        step = 0;
        auditNote = null;
        Render();
    }
}
