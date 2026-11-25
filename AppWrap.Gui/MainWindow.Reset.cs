using AppWrap.Core.PortableProfiles;
using AppWrap.Core.Projects;
using AppWrap.Core.TestHarness;

namespace AppWrap.Gui;

public partial class MainWindow
{
    // "Package another app" on the finished screen: returns the wizard to
    // step 1 with the same state a fresh launch has, so a second package
    // starts clean. This is pure in-memory state — resetting writes no
    // files (in particular it never saves the previous app's project) and
    // drops every reference to the previous app's project, profile and
    // test/template sessions, so none of it can leak into the next run.
    // Theme and filename style are deliberately kept: they are app
    // preferences persisted in window.json and restored at launch, not
    // per-package state.
    private void ResetWizard()
    {
        step = 0;
        executables = [];
        source = ""; name = ""; version = "1.0"; executable = ""; output = "";
        icon = null; sidebar = null;
        cleanupWarning = null;
        startMenu = true; desktop = false;
        busy = false; finished = false; closingAfterCancel = false;
        offerLaunch = true; offerStartup = false; offerWebsite = false;
        developerWebsite = "";

        loadedProject = null;

        profileState.Reset();
        configurePortable = false;
        profileCategory = ProfileCategory.OverviewLaunch;
        profilePageIndex = 0;
        profileSelectedRowId = null;
        profileTextKind = PortableDocumentKind.Launcher;

        configOpen = false; configWorkspace = null;
        configTab = ConfigTab.AppInfo; configCustomInstaller = false;

        templateOpen = false;
        templateKind = ProjectTemplateKind.BlankPafApp;
        templateName = ""; templateFolder = "";
        templateSpec = PafLayout.DefaultPafSpecVersion;
        templateAdvanced = false;
        templateNote = null; templateDiagnostics = null; pendingTemplate = null;

        testPanelOpen = false; testBusy = false;
        testMode = TestRunMode.Portable;
        testFolder = ""; testReport = null; testReportFile = null; testNote = null;

        outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        destinationChosen = false;
        suggestedOutput = "";
        outputLocationDisplay = null;
        filenamePreviews.Clear();

        StandardMode.IsChecked = true;
        PortableMode.IsChecked = false;
        BothMode.IsChecked = false;
        PafMode.IsChecked = false;
        Render();
    }
}
