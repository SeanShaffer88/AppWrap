using System.IO;
using System.Windows;
using System.Windows.Controls;
using AppWrap.Build;
using AppWrap.Core;
using AppWrap.Core.TestHarness;
using Microsoft.Win32;

namespace AppWrap.Gui;

public partial class MainWindow
{
    // Phase 4 test panel: the runtime test harness is a separate, explicit
    // action — builds never run a packaged app. The panel copies the chosen
    // package folder into a throwaway sandbox with a redirected profile, runs
    // its launcher once, and shows the resulting test-report.json summary
    // (the full report is saved under the app's TestReports state folder).
    // Modes are Portable and PAF Package; testing a standard installer would
    // mean executing an installer, which stays out of scope.
    private bool testPanelOpen;
    private bool testBusy;
    private TestRunMode testMode = TestRunMode.Portable;
    private string testFolder = "";
    private TestReport? testReport;
    private string? testReportFile;
    private string? testNote;
    private CancellationTokenSource? testCancellation;

    private void OpenTestPanel()
    {
        testMode = IsPaf ? TestRunMode.PafPackage : TestRunMode.Portable;
        testFolder = IsPaf ? PafDestination : "";
        testReport = null;
        testReportFile = null;
        testNote = null;
        testPanelOpen = true;
        Render();
    }

    private void RenderTestPanel(StackPanel panel)
    {
        StepLabel.Text = "TEST PACKAGE";
        StepTopic.Text = "Test package";
        Question.Text = "Test a built package";
        Hint.Text = "Runs the app once in a throwaway sandbox with a redirected user profile and watches where it writes. Writes into the profile are portability violations. Nothing is installed, the sandbox is deleted afterwards, and builds never do this — testing only happens when you click Run test.";
        NextButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        Formats.Visibility = Visibility.Collapsed;

        panel.Children.Add(Label("Package format tested"));
        var modes = Identify(new ComboBox { ItemsSource = new[] { "Portable", "PAF Package" }, SelectedIndex = testMode == TestRunMode.PafPackage ? 1 : 0, FontSize = 13, MinHeight = 36, IsEnabled = !testBusy, Margin = new(0, 0, 0, 10) }, "TestMode");
        modes.SelectionChanged += (_, _) => { if (modes.SelectedIndex >= 0) testMode = modes.SelectedIndex == 1 ? TestRunMode.PafPackage : TestRunMode.Portable; };
        panel.Children.Add(modes);

        panel.Children.Add(Label("Package folder"));
        var folderRow = new Grid { Margin = new(0, 0, 0, 10) };
        folderRow.ColumnDefinitions.Add(new());
        folderRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var folderText = Identify(new TextBox { Text = testFolder, ToolTip = "Type or paste a built PAF Package folder, or a portable app folder extracted from a package — or use Choose folder…", Style = null, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, IsReadOnly = false, IsEnabled = !testBusy, Padding = new(0, 6, 8, 6), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center }, "TestPackageFolder");
        folderText.SetResourceReference(Control.ForegroundProperty, "Text");
        folderRow.Children.Add(folderText);
        WirePathCommit(folderText, text =>
        {
            if (testBusy) return;
            var folder = WizardPaths.NormalizePathText(text);
            if (WizardPaths.SamePath(folder, testFolder)) { Render(); return; }
            testFolder = folder; testReport = null; Render();
        });
        var choose = Identify(new Button { Content = "Choose folder…", Padding = new(12, 6, 12, 6), MinHeight = 40, FontSize = 13, IsEnabled = !testBusy }, "TestChooseFolder");
        choose.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose the package folder to test", Multiselect = false };
            if (dialog.ShowDialog(this) == true) { testFolder = dialog.FolderName; testReport = null; Render(); }
        };
        Grid.SetColumn(choose, 1); folderRow.Children.Add(choose);
        var folderCard = new Border { Child = folderRow, CornerRadius = new(8), Padding = new(10, 4, 4, 4), BorderThickness = new(1), Margin = new(0, 0, 0, 10) };
        folderCard.SetResourceReference(Border.BackgroundProperty, "InputSurface"); folderCard.SetResourceReference(Border.BorderBrushProperty, "Border");
        panel.Children.Add(folderCard);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
        var run = SmallButton(testBusy ? "Running…" : "Run test", "TestRun");
        run.IsEnabled = !testBusy;
        run.Click += (_, _) => RunTestAsync();
        bar.Children.Add(run);
        if (testBusy)
        {
            var cancel = SmallButton("Cancel test", "TestCancel");
            cancel.Click += (_, _) => testCancellation?.Cancel();
            bar.Children.Add(cancel);
        }
        var done = SmallButton("Done", "TestDone");
        done.IsEnabled = !testBusy;
        done.Click += (_, _) => { testPanelOpen = false; Render(); };
        bar.Children.Add(done);
        panel.Children.Add(bar);

        if (testBusy)
        {
            var running = new TextBlock { Text = "Running the test… the app is launched in its sandbox; this panel updates when it exits or the time limit is reached.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            running.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(running);
            return;
        }
        if (testNote is not null)
        {
            var note = new TextBlock { Text = testNote, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(note);
        }
        if (testReport is { } report)
        {
            var verdict = new TextBlock
            {
                Text = report.Verdict == TestVerdict.Pass ? "Verdict: PASS — the app kept its writes inside the package." : "Verdict: FAIL — see the detected writes below.",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 4, 0, 4),
            };
            verdict.SetResourceReference(TextBlock.ForegroundProperty, report.Verdict == TestVerdict.Pass ? "Text" : "Error");
            panel.Children.Add(verdict);
            var summary = new TextBlock
            {
                Text = $"Exit code: {(report.ExitCode?.ToString() ?? "none (killed)")}{(report.TimedOut ? " · timed out" : "")} · Duration: {report.DurationSeconds:0.0}s · Detected writes: {report.DetectedWrites.Count} · Violations: {report.Violations.Count}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(0, 0, 0, 8),
            };
            summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(summary);
            if (report.DetectedWrites.Count > 0)
            {
                var lines = report.DetectedWrites.Select(w =>
                    (w.Violation ? "VIOLATION · " : "") + $"{w.Root} · {w.Path} · {w.Change} · {CategoryName(w.Category)}");
                var writes = Identify(new TextBox { Text = string.Join("\n", lines), IsReadOnly = true, FontSize = 12, TextWrapping = TextWrapping.NoWrap, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, "TestReportWrites");
                panel.Children.Add(writes);
            }
            if (testReportFile is not null)
            {
                var path = new TextBlock { Text = "Full report: " + testReportFile, ToolTip = testReportFile, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
                path.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                panel.Children.Add(path);
            }
        }
    }

    private static string CategoryName(WriteCategory category) => category switch
    {
        WriteCategory.PortableData => "Portable data",
        WriteCategory.PackageRoot => "Package root",
        WriteCategory.ProfileLeakage => "Profile leakage",
        _ => "Temporary",
    };

    private async void RunTestAsync()
    {
        if (testBusy) return;
        if (string.IsNullOrEmpty(testFolder) || !Directory.Exists(testFolder)) { Error.Text = "Choose the package folder to test first."; return; }
        testBusy = true;
        testReport = null;
        testReportFile = null;
        testNote = null;
        testCancellation = new();
        Render();
        try
        {
            var reports = Path.Combine(StateDirectory, "TestReports");
            Directory.CreateDirectory(reports);
            var reportPath = Path.Combine(reports, "test-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            var appName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(testFolder)) : name.Trim();
            var request = new TestRunRequest(appName, testMode, testFolder, reportPath);
            var token = testCancellation.Token;
            var result = await Task.Run(() => AppWrap.Build.TestHarness.RunAsync(request, token));
            testReport = result.Report;
            testReportFile = result.ReportPath;
            testNote = result.CleanupWarning;
            testBusy = false;
            Render();
        }
        catch (OperationCanceledException)
        {
            testBusy = false;
            testNote = "Test cancelled. The sandbox was removed.";
            Render();
        }
        catch (Exception ex)
        {
            testBusy = false;
            Render();
            ShowError(ex);
        }
        finally { testCancellation?.Dispose(); testCancellation = null; }
    }
}
