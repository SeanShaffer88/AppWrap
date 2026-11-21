# Independent lifecycle evidence review

Status: in progress. Genuine fixture trust review and standard install/running-upgrade/uninstall evidence pass within this fixture. Narrow portable diagnostic/staging fixes are separately approved. Portable running-update attribution/removal and final cleanup remain pending.

## Standard v1 observation

Read exact run evidence under `work/lifecycle-20261121-9f6a82c1`: baseline.json, artifacts-standard.json, standard-v1-result.json and logs/standard-v1-install.log plus the recorded probe launch result.

Baseline declares absence of the unique default per-user install root, exact Desktop/Start Menu links, exact uninstall key in both views and portable package root, matching the Auditor's independent earlier absence reads. Source inventory contains only the native probe, version resource text, root resource and v1 obsolete asset; it contains no precreated Data/settings, so later user marker creation is not confused with shipped configuration.

Standard v1 setup artifact: 2223483 bytes, SHA256 `A5C035FA1ACEBF20E2A5E49D00F87FFA9CFACA5FC691AD8FF162677C856EEC33`. v2 setup also exists as separately hashed preparation evidence, not yet counted as an upgraded installation.

Actual v1 setup exits **0**. Installer log records Inno7.1.1, user privileges None, administrative install mode No, HKEY_CURRENT_USER root, default unique LocalAppData Programs destination, `/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART` and exact owned log. It records successful file/shortcut/uninstall-key creation and no Windows restart requirement. This establishes logged installation actions; final shortcut metadata/registry value checks are separate gates.

Installed probe hash recorded in result is `E3B06492AADCC9FC859086B11C877A12496DB59F69CA416A286E626549864A4B`, matching independently approved v1 source artifact. Actual direct probe process exits **37**, requested 37. Recorded version is 1.0.0, resourcesOk true, cwd is selected installed `bin`, arguments retain `two words` and `quote "inner"` separately, and the probe creates its unshipped `Data/user-settings.txt` marker. Its short normal run closes with reason completed, not an upgrade closure.

## Upgrade proof requirements

Do not infer running-instance closure from eventual PID disappearance: hold mode has a 300-second maximum. Require exact owned PID/start/path, hold record, installer action timestamps/Restart Manager evidence, and close reason wm-close/end-session/query-end-session or comparable observed installer-driven event before the lifetime deadline. A maximum-lifetime or manual close after a blocked update is not an automatic-close pass.

Preserve marker bytes across upgrades; distinguish known tracked v1 obsolete content from untracked user data. Verify uninstall registry/shortcuts/tracked payload absence before any separate manual cleanup so cleanup cannot mask uninstaller residue.

## Confirmed portable diagnostic issue

Read raw ASCII-compatible nsis-encoding-repro.log containing a deliberate invalid command and the decoded last-build-failure.txt. The old unconditional UTF16LE log reader is wrong for compiler-created `/O` logs; wizard validation errors use a separate no-BOM UTF16LE writer. A bounded encoding-aware diagnostic fix is reviewable independently from the actual compile failure.

The decoded actual portable error is NSIS `File: failed opening file` for the generated root portable launcher at installer source line1795. This is not explained by display encoding. Long duplicated staging/package identifiers are a hypothesis requiring exact file/path-length and shorter-control reproduction before changing product staging behavior. No portable installer execution is counted yet; failed paired preparation publishes neither output, as designed.

## Standard running upgrade and uninstall review

Read standard-running-identity.json, logs/standard-v1-hold.json, standard-upgrade-result.json, standard-v2-registration.json, standard-shortcuts.json, standard-shortcut-launch.json, standard-uninstall-result.json and the actual upgrade/uninstall logs.

- Owned held v1 PID420 starts 22:04:21.185 UTC; probe start record 22:04:21.194 UTC. Upgrade log 16:04:21 local (UTC-06) records Restart Manager finding the owned application, then shutting down applications at 16:04:21.553. Probe closes with **end-session**, heartbeat0, exit0; this occurs far before its 300-second lifetime and is supported by the installer shutdown log. Upgrade exits0, installs approved v2 hash `F5190D3D3CBD64C7D955B6273BAC4DF58B71EBCB90E7CFDC8415DD2CA4447071`, restarts no probe, preserves the modified user marker and untracked file.
- Both shortcut metadata records point to the selected installed bin executable and bin working directory; actual shortcut launch records v1 resources/cwd correctly. v2 registration records the same exact uninstall identity/path with DisplayVersion2.0.0.
- Known v1 obsolete payload remains after upgrade; this is explicitly observed Inno tracked-file upgrade behavior, not a claim that arbitrary obsolete files are removed. The uninstall log later deletes this known tracked obsolete file.
- Exact uninstaller exits0, logs nonadministrative HKCU uninstall and known tracked file/shortcut/key deletion, no reboot. Its self-executable disappears after settling. User Data/settings and untracked user file remain intentionally; initial failure to remove a nonempty install directory is consistent with that preservation, not an app-file orphan claim.
- Auditor independently re-read only exact host targets after uninstall: both unique shortcuts absent, exact uninstall key absent in Registry32/Registry64, and exactly three intentionally retained files: user-untracked.txt, Data/probe-4404.json, Data/user-settings.txt. No tracked probe/resource/uninstaller/obsolete asset remained. This observation preceded separate manual cleanup and therefore is not masked by it. Read-only command exited0.

## Narrow source-fix approval

Final BackendLog decoder recognizes known no-BOM UTF16LE `ERROR: ` prefix, honors strict BOM encodings, reports unknown/invalid encoding with byte-preserving fallback, and bounds file reads/truncation. Auditor reproduced long-CJK-path misdecoding on the first implementation (isolated test exit1, DevTools run `20261121T220614892Z-4c14b6598cbe4f72a0f7a467327d9529`) and verified the same exact assertion passes with final copied source (exit0, `20261121T221903922Z-84cab42eb75c4ac6be465dbb3075f0cf`). Canonical source was never edited by Auditor.

The one-off decoder assertion project, copied decoder snapshots and all compiled evidence were preserved together and relocated from the repository docs scratch directory to `C:/Users/Sean Shaffer/Documents/Codex/2026-11-21/c/work/audit-lifecycle-decoder`. Native PowerShell Move-Item ran only after checking both exact resolved paths remain under this workspace work root, rejecting linked ancestors and an existing destination. Move command exited0. Historical run logs retain the original execution-time path correctly; this scratch implementation is not shipped as a duplicate canonical module.

Portable private package staging now uses short fixed `portable` directory rather than duplicating the long public application identifier in its internal folder. Public AppID, root launcher filename, payload layout and installer scripts are unchanged. Reviewed real paired v1/v2 build success at the original fixture scope (DevTools `20261121T220800244Z-921e66bcbb7c44a18e42421334a01f19`, exit0, artifacts.json hash907736AEAC436BB41D3923AD0EA81EFCEBC2E0CBB4CBBABB958859D00AC8FAD5), and actual long-stage regression exercising legacy raw path >260 while fixed private layout compiles.

**Approved these two narrow source fixes for an atomic commit**, independently of remaining lifecycle gates. Actual **179-check suite exit0**: `20261121T221506116Z-de07f62058f14274a53cda6993b11d4f`; includes seven decoder and three real long-stage checks plus prior regressions. Latest four packed artifacts consumed separately with **13 passing checks**, result Passed true at `work/AppWrap-packed-consumer/bda449ef297d488cafacdcf791042045/result.json` and run-consumer.log. No additional finding remains in those fixes.

## Portable held-close attribution is unresolved

Auditor performed **no UI/process actions** during this phase: no CUA calls, fixture/installer launches, WM_CLOSE/CloseMainWindow, process stopping or termination. Implementer is the sole host operator.

Read portable-running-identities.json, portable-hold-request.json, logs/portable-v1-hold.json and baseline updater result. Child7528/root12236 start about22:12:37.8 UTC; child argv contains --hold, last heartbeat61 (about30.5 seconds) and closeReason **wm-close**. v2 installer15284 starts22:13:02.138 UTC and exits1 without driver timeout/stop. These observations do not establish a successful update or its close-message sender.

Probe source does not turn timer expiry into wm-close: lifetime WM_TIMER assigns maximum-lifetime/completed before posting WM_CLOSE, heartbeat logging does not alter reason, and WM_CLOSE assigns wm-close only when reason is empty. Therefore the recorded wm-close is not explained by its own lifetime/heartbeat branch. Current records do not capture the external message sender; cannot attribute it to updater, Windows, physical input or automation from this evidence. Upstream inspected running-check branches display warnings/abort but the reviewed snippets do not prove sender behavior. Repeat controlled observation before adding a process guard or claiming automatic close.

All observations are scoped to this owned fixture/current host. No global zero-footprint, arbitrary-payload portability or general clean-machine qualification is claimed.

## Optional-action source and isolated regression review

Reviewed additive Core options, URL validation, generated optional tasks/run entries, exact startup-link ownership/migration/cache/writeback and new GUI step wiring against the approved optional-installer-options contract. Pre-host approval is restricted to the declared native-probe fixture, canonical same installed root and local owned website listener. Unknown/foreign/malformed/reparse shortcut state remains preserved. Cross-install-root relocation cleanup remains an explicit limitation; no general ownership inference from a basename or old absolute path is claimed.

Independently viewed native-three-finish-defaults.png: all three completion choices are visible without overlap, application checked and website/startup unchecked. Native-shortcut-defaults.png shows Start Menu checked and Desktop unchecked. Startup-only finish layout and actual action/ownership outcomes remain separate host gates.

Meaningful isolated mutation experiment: work/audit-standard-options-mutation copied only the options validator into a minimal test project outside the repository. The exact credential-bearing HTTPS URI is rejected in baseline (exit0). Removing only the copied UserInfo rejection makes the assertion fail (exit1); finally restores the copied source. result.json confirms canonicalSourceEdited=false and isolatedCopyRestored=true. This verifies that the credential-rejection branch is exercised; it does not replace the other compiler/host tests.

External WPF layout harness source reviewed before execution: real App/MainWindow and native Next/Back events, bounded owned-process screenshots, invalid URL/back-retention/theme checks, no build/install/payload/browser invocation. Approved only for the declared B2 host-operation lease and copied test state; normal application state writes still count in the fixture footprint. Actual harness outcome remains pending.

## Live-discovered uninstall ownership defect and correction

Live standard-options testing exposed a real defect in the prior generated [UninstallDelete] Check approach: Inno recorded the setup-time-owned deletion for later uninstall, so replacing that link with a foreign link did not protect it. Prior source/compiler approval did not establish runtime ownership semantics. This invalidated the previous optional-startup uninstall gate.

Reviewed targeted correction: remove that section entirely; retain early InitializeUninstall metadata cache; CurUninstallStepChanged(usUninstall) evaluates current StartupLinkOwned immediately before exact StartupName DeleteFile, logging failure. Canonical path/reparse and exact target/cwd/no-arguments checks remain intact. No broad deletion or forced cleanup added. An existing old installer log is not claimed to lose its previously recorded unconditional deletion through this change; old unpublished test histories were removed before constructing corrected fresh-history tests.

All four regenerated fixture executables and scripts independently hash-match updated standard-options/artifacts.json (compiler0; audit inspection command exit0). Actual generated scripts contain the runtime ownership gate and no [UninstallDelete] section (the name appears only in a comment). Preexecution approval granted for corrected-history owned-to-foreign replacement and normal owned cleanup, with runtime outcomes still pending. New package hashes: v1 4B7AB762C620409CE83069412FA765D2248DE7AA30714FCAE78CCE9133C705D0; v2 4BBFBA8FC8CB3BF900564FFBC5B47C145E58C3FAC4D5B72189D4A2312B88B360; v3 EA5D0065DB9FB9B6F8D75FDE741697EF961C49AA49E72A06EDCF7BA9BEAEA290; v4 681054EA0D0D886F3DAEA05231D3F433934367540FE85ABCD15C2D82D09A1C62.

## Corrected standard native host verification

Independently read native-verification.json (all19checks true), raw corrected fixed-foreign-result, fixed-owned-cleanup-uninstall-result, fixed-malformed-result, fixed-reparse-result and matching uninstall logs. A corrected fresh-history owned link was replaced by foreign link E4183895F1BEA215F90A8D3428E908C054C23E12CDF73750974E9C56304696A3; upgrade and uninstall preserved its exact hash. The runtime uninstall log explicitly reports preservation. A separate genuine owned-link uninstall exited0, logged removal during uninstall, removed startup/registration and retained user settings. Malformed bytes6345AFC3CEDF26D69487F2FFE32970015E5811D9053817D4999022D1072E5776 and a directory reparse target survived uninstall. Host-cleanup-result shows startup/desktop/StartMenu/registration absent, listener port free, settings and reparse target preserved. Exact owned test junction was subsequently removed by a literal nonrecursive command, distinct from earlier policy-blocked recursive cleanup.

Selected Finish now includes actual probe4936 output as well as exact startup target/cwd/noargs and observed owned loopback website request. Silent upgrade refreshes owned bin-to-bin-next link without app/browser launch; absent remains absent. Interactive/default/no-offer/explicit-disable cases are included in consolidated19checks. Failed Finish launches neither action and preserves foreign hash; retry evidence shows one app execution and one browser execution, avoiding duplicated external actions. Native startup-only screenshot independently viewed: checkbox visible and unchecked at ordinary completion position, no RunList overlap. Full-three defaults/shortcut visuals were separately inspected earlier.

Approved the corrected generated standard native feature within this exact owned fixture/current host scope. WPF minimum-size/theme harness and final regression/package review remain separate gates. No general clean-machine/accessibility/other-Windows qualification is inferred.

## Standard-options final checkpoint approval

Final narrow GUI diff against checkpoint0d6b70e adjusts only Finish-card label/card padding and36px URL vertical alignment; compact Finish-only hint/step margins preserve the other pages. Independently inspected Standard-AppWrap, Both-AppWrap, Standard-Gruvbox-Light and Both-OLED-Black screenshots at680x560: text/URL glyphs, controls, inline validation error and footer are visible without clipping or overlap. Actual outputs/AppWrap-options-layout-final-v2/result.json records110 passing entries (70 initial plus40 text-bound checks), zero failed entries and semantic exit0. The owned fixture runner also exited0 at20261121T225821322Z-35de8820ccd24223bca86f21aa10f9d2; ten screenshots cover Standard/Both across all five themes, native step counts, validation and Back retention. No installer/build/payload execution was performed by that layout harness.

Final standard feature regression232checks exit0 is retained at work/AppWrap-verification/20261121T225242-8d8fc6, runner20261121T225239859Z-45da1e5df806464c94b89e3f4ef11768. Combined with the corrected19 native host checks, isolated credential-validator mutation and source review, **the Standard optional-actions checkpoint is approved**. This is a scoped feature checkpoint, not an approval of forthcoming portable-profile implementation, a new distribution archive, clean-machine behavior or accessibility certification. Standard UI may now advance under the separately approved new profile specification.
