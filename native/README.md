# Native components

## AppWrap.Cli.exe — dual-mode CLI shim

`AppWrapCliShim.c` is the source of the shipped `AppWrap.Cli.exe`. Since
v0.2.3 the wizard and the CLI are one self-contained executable
(`AppWrap.exe`, with the CLI hosted in-process as a library); the shim
exists so scripts keep a console-subsystem entry point named
`AppWrap.Cli.exe`. It locates `AppWrap.exe` in its own directory, starts it
with the `--cli` marker prepended to the caller's arguments (forwarded
verbatim, preserving quoting), inherits the console, waits, and exits with
the child's exit code. When `AppWrap.exe` is missing or cannot be started
it exits 4 with an explanatory message. It keeps no state and writes no
files.

Build routes (both produce a dependency-free x64 console executable —
imports limited to system DLLs):

- **MSVC (Windows):** `BuildCliShim.cmd` — static CRT (`/MT`), console
  subsystem, ASLR and DEP.
- **MinGW-w64 (cross-compile):**
  `x86_64-w64-mingw32-gcc -O2 -static -municode -o AppWrap.Cli.exe AppWrapCliShim.c`

The binary in this folder was cross-compiled with MinGW-w64 GCC.

## UpgradeGuard

See `UpgradeGuard/README.md` for the preinstall guard's source, build and
provenance.
