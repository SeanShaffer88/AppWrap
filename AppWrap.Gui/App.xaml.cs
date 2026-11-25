using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AppWrap.Cli;

namespace AppWrap.Gui;

public partial class App : Application
{
    private bool reportingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Last-resort net: an exception escaping a UI event handler must
        // never close the program without a word. UI-thread failures are
        // shown and marked handled so the wizard stays open; failures on
        // other threads cannot be recovered, only reported.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // Dual-mode entry: one executable is both the wizard and the CLI.
        // CliModeDetector decides from the raw command line; only an
        // unmistakable CLI invocation keeps the window from opening.
        var decision = CliModeDetector.Detect(e.Args);
        if (decision.Mode == CliMode.Cli)
        {
            Shutdown(RunCli(decision.CliArgs));
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Runs the CLI in-process and returns its exit code. The GUI is a
    /// window-subsystem executable, so the parent console is attached
    /// first and the standard streams reopened; without a parent console
    /// the host still runs (its output has nowhere to go) and the exit
    /// code still propagates. The host runs on a pool thread so its
    /// awaits never capture the UI dispatcher.
    /// </summary>
    private static int RunCli(string[] args)
    {
        AttachToParentConsole();
        try
        {
            return Task.Run(() => CliHost.Run(args, hostedInGui: true)).GetAwaiter().GetResult();
        }
        catch { return 4; }
    }

    private static void AttachToParentConsole()
    {
        if (!AttachConsole(AttachParentProcess)) return;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.OutputEncoding = Encoding.UTF8;
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportError(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Fire-and-forget work (background scans, builds) must not take
        // the process down when nobody awaited its failure.
        e.SetObserved();
        Dispatcher.InvokeAsync(() => ReportError(e.Exception));
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // The runtime will still end the process after this handler for
        // most exceptions; reporting is best effort so a genuinely fatal
        // failure at least says what happened instead of vanishing.
        if (e.ExceptionObject is Exception exception) ReportError(exception);
    }

    private void ReportError(Exception exception)
    {
        if (reportingError) return;
        reportingError = true;
        try
        {
            MessageBox.Show(
                "AppWrap hit an unexpected problem and could not finish that step.\n\n"
                + exception.Message + "\n\nDetails:\n" + exception,
                "AppWrap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* The report itself must never become the failure. */ }
        finally { reportingError = false; }
    }
}
