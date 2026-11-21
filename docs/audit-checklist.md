# AppWrap acceptance and security audit

Status: implementation pending; no release or runtime qualification claimed.

Scope: supplied AppWrap workflow blueprint; independent review of final source, real build/test evidence, and distributable inventory. Review uses `C:/Users/Sean Shaffer/.agents/skills/code-review-and-quality/SKILL.md` (five axes and verify-the-verification) and `C:/Users/Sean Shaffer/.agents/skills/security-and-hardening/SKILL.md` (trust boundaries, input encoding, destructive paths, supply chain).

## Trust boundaries and assets

Untrusted: selected folder names/content, executable names, text fields, existing portable metadata/custom scripts, selected images, compiler stdout/stderr, and destination filesystem state. Trusted execution: explicitly pinned compiler and builder toolchains only. Assets: source tree, existing output, unrelated filesystem contents, user data/settings, process lifetime, and local privacy.

## Required gates

| Gate | Acceptance | Evidence required |
|---|---|---|
| Wizard | Linear five/seven question paths; required validation; skip icon/sidebar works; no backend names or script editor; indeterminate progress and recoverable errors | UI smoke through both modes, screenshots, error and cancel paths |
| Standard generation | Correct Inno compile contract; grammar-safe quoted values including braces and quotes; recursion and launch/shortcut paths match selected exe | Generator edge cases and actual compiler output |
| Portable generation | Documented official builder contract and actual supported layout; valid app metadata/version/icon assets; safe sidebar customization; existing metadata preserved only as safe data | Official contract references and actual official-builder output |
| Portability honesty | Packaging ordinary executables does not promise redirecting registry/profile settings or removing machine dependencies | UI/help copy and generated launcher behavior |
| Source safety | Never run the chosen application; do not compile source-supplied arbitrary scripts or macros; source unchanged | Code inspection and sentinel/hash fixture |
| Path safety | Reject escaping/absolute main-exe paths, reparse-point traversal, source/output overlap, reserved names and invalid destinations; recursion bounded or cancellable | Adversarial file-tree fixtures |
| Atomic output | Compile into owned staging, verify new expected output exists and is nonempty, commit only on success; preserve previous destination on cancellation/failure | Existing-output hash checked before/after failed and cancelled builds |
| Process lifetime | Shell-free argv; hidden process; bounded logs; cancellation/timeout stops owned backend work, with no orphan children | Execution tests and owned-process evidence |
| Temporary state | Unique app-owned paths, no broad cleanup; refuse unexpected ownership/path state; failures reported; logs only in details | Success/failure/cancel state inventory |
| Imaging | Decode actual image; input limits; correct ICO sizes/color depths and PNG alpha; correctly sized uncompressed 24-bit sidebar BMP; image errors recoverable | Asset parser checks and actual builder acceptance |
| Dependencies | Pinned versions, authoritative download URLs, hashes, license notices and redistribution record; no runtime downloads or hidden telemetry | Packaged inventory/provenance and source scan |
| Desktop behavior | Remember window size/position safely across off-screen/DPI state; resizing preserves controls; close during build has defined cancellation | UI smoke and placement test |
| Distribution | Real configured self-contained Windows publish; exact packaged artifact identity; accurate unperformed checks | Build exit/log/hash and static package check |
| Review effectiveness | Tests catch a meaningful inverted guard, with mutation restored; final source reviewed after fixes | Mutation command/output and final review record |

## Initial blueprint assumptions requiring technical resolution

1. The PortableApps builder is not a general app-portabilization engine. A launcher and portable settings behavior must be explicit; a package filename alone proves neither.
2. Existing `.nsh`, `.nsi`, `.iss` or installer configuration can contain executable build directives. Copying data is different from allowing it to influence compilation.
3. Preserving an existing `appinfo.ini` wholesale may preserve malformed/injected control values. Parse data, validate allowed fields, serialize known grammar safely.
4. Quiet compiler flags and silent installation flags are different interfaces. Only the former belongs in build invocation.
5. Sidebar customization and fallback icons must use verified official hooks/assets, not guessed filenames or defines.
6. A compiler exit of zero is insufficient if an expected fresh artifact is absent. Existing stale outputs cannot count as success.
7. Static checks and compilation do not establish installer install/upgrade/uninstall, real portable settings isolation, clean-machine behavior, mixed-DPI hardware behavior, code-signing trust, or trademark clearance.

## Final verdict

Pending implementation and evidence. Findings will distinguish required correctness/security changes from optional improvements; unperformed checks remain explicit.
