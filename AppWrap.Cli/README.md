# AppWrap.Cli

The AppWrap command-line interface: every capability of the wizard and the
Development Suite as commands, for scripts and local AI operators. It is a
thin shell over AppWrap.Core and AppWrap.Build — the same operations, the
same validation, the same typed errors — with argument parsing
(`CliArguments.cs`) and command execution (`CommandRouter.cs`) kept free of
console I/O so both are check-covered; `Program.cs` is the only console
surface.

Commands: `build`, `project validate|info|create`, `config list|get|set`,
`profile validate`, `test`, `gui`. Every command accepts `--json` (a single
envelope object: ok, command, exitCode, result, diagnostics, error) and
`--verbose` (progress on stderr). Exit codes: 0 success, 2 usage error,
3 validation failed (nothing written), 4 build/test failure.

The CLI resolves its packaging backends from its own folder exactly like
the GUI (the `third_party` tree beside the executable) and stages builds in
`Data` beside it. Full reference: `docs/CLI.md`.
