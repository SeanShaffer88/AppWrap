using System.Windows;
using System.Windows.Threading;

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
        base.OnStartup(e);
    }

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
