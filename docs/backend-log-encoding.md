# Pinned portable backend log encoding

Read-only diagnosis support, not a compiler-failure cause determination. Actual compiler error must be decoded before changing path/layout behavior.

Upstream `Other/Source/InstallerWizard.nsi` uses two different writers for `Data/PortableApps.comInstallerLog.txt`:

- `WriteErrorToLog` lines254–256 opens append and `FileWriteUTF16LE`; it does not emit a BOM. This produces known no-BOM UTF16LE for prevalidation errors.
- Compiler invocation lines1369/1379 calls bundled `makensis.exe /O<log>` without `/OUTPUTCHARSET`. The compiler creates its own log rather than using the wizard's FileWrite encoding.

Actual lifecycle reproduction log `work/lifecycle-20261121-9f6a82c1/logs/nsis-encoding-repro.log` begins hexadecimal `50-72-6F-63-65-73-73-69-6E-67` (ASCII Processing), not UTF16LE. UTF8 decoding exposes the deliberate invalid-command diagnostic correctly. This proves that forcing `Encoding.Unicode` for all backend logs is incorrect; it does not prove the compiler's non-ASCII default charset on every host.

Recommended bounded decoder contract: preserve original byte evidence; honor UTF8/UTF16LE/UTF16BE BOM; handle known no-BOM UTF16LE wizard output by expected alternating-NUL text pattern; otherwise decode ASCII-compatible compiler text using explicit known encoding when available, or strict UTF8 then documented configured Windows codepage fallback. Do not silently replace undecodable bytes and present that as exact output. Keep read/log-size bounds and truncation indication. A StreamReader constructed with Unicode default plus detectBOM=true still forces Unicode when no BOM exists, so is insufficient.

Actual bundled makensis `/?` recognizes `/OUTPUTCHARSET <ACP|OEM|CP#|UTF8[SIG]|UTF16<LE|BE>[BOM]>`. An explicitly chosen compiler output charset can make future logs deterministic, but that is a separate targeted modification after the raw failure is understood; rebuild/hash/retest affected backend if changed. No source-path limit or other compile cause is inferred from garbled text.

Primary current source: https://nsis.sourceforge.io/Docs/Chapter3.html section3.1.1 documents `/O` and `/OUTPUTCHARSET` for redirected output. Exact bundled compiler help observed2026-11-21; help exits1 for `/?` despite printing supported options, so do not report it as a passing build.

## Bounded staging-name contract research

After the actual decoded compiler error identified opening the root launcher (`File` at generated installer line1795), controller requested checking a shorter private package staging directory name. Static inspection found no normal-app source-folder-basename equality check against metadata AppID:

- Wizard AppID comes from Details/AppID at lines655–665; generated artifact name derives from AppID/DisplayVersion at748. `strInstallAppDirectory` is independently supplied as the source root. Output directory is its parent at1355.
- Installer `OutFile` at79 is three parents above Source with metadata FILENAME; runtime `InstallDir` at83 uses metadata AppID. Payload inclusion1795–1800 uses source-relative root EXE/HTML and App tree, not the source folder basename.
- Installer's folder basename/AppID comparison at1039–1046 is guarded by COMMONFILESPLUGIN and concerns an installed destination, not ordinary-app compile staging.

Replacing the private package source folder component with short fixed `portable` retains metadata AppID, root launcher name, output naming and installed directory identity. Subsequent controller/implementer evidence reported the long-path mechanism confirmed, the short-folder fix approved, and same-original-state paired builds producing four artifacts successfully (`run220800244`). This supersedes the earlier candidate status; exact command/artifact evidence belongs to that run's lifecycle/build records. Keep the stage within AppWrap's existing owned portable Data/state boundary; no global temp relocation, long-path prefix substitution or host registry/policy setting is needed.
