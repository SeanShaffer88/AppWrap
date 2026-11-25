# AppWrap.Cli

The AppWrap command-line interface: every capability of the wizard and the
Development Suite as commands, for scripts and local AI operators. It is a
thin shell over AppWrap.Core and AppWrap.Build — the same operations, the
same validation, the same typed errors — with argument parsing
(`CliArguments.cs`) and command execution (`CommandRouter.cs`) kept free of
console I/O so both are check-covered.

Since the dual-mode restructure this project builds as a **library**, not
an executable. `Program.cs` hosts `CliHost.Run`, which the GUI executable
calls in-process when `CliModeDetector` routes a command line to the CLI
(no arguments, or an unrecognized first argument, still opens the wizard).
The shipped `AppWrap.Cli.exe` is a tiny native shim
(`native/AppWrapCliShim.c`) that forwards to `AppWrap.exe --cli …` and
propagates its exit code, so the two entry points are interchangeable;
when the router's `gui` command runs hosted in-process it reports that the
GUI is this executable and exits 0 rather than launching a sibling.

Commands: `build`, `project validate|info|create`, `config list|get|set`,
`profile validate`, `test`, `gui`. The config commands also reach the
project document (`config … project Images IconPath|SidebarImagePath`) —
the project's first-class image properties — and `build --project`
defaults the icon/sidebar from them under any explicit flags. Every
command accepts `--json` (a single
envelope object: ok, command, exitCode, result, diagnostics, error) and
`--verbose` (progress on stderr). Exit codes: 0 success, 2 usage error,
3 validation failed (nothing written), 4 build/test failure.

The CLI resolves its packaging backends from its own folder exactly like
the GUI (the `third_party` tree beside the executable) and stages builds in
`Data` beside it. Full reference: `docs/CLI.md`.
