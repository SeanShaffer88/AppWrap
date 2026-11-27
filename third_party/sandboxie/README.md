# Sandboxie-Plus backend

`tools/Prepare-SandboxieBackend.ps1` prepares the pinned x64 Sandboxie-Plus
1.18.8 installer, the official release checksum file, the GPL-3.0 license,
and the source archive for commit `bde0890108a563f5d72eb8e57085be2b37177218`.
It verifies the checksum-file digest published by GitHub, verifies the
installer against Sandboxie's own checksum file, checks the source archive's
SHA-256, and checks that the tag still resolves to the pinned commit.

The generated files are not committed. AppWrap setup embeds the installer
and runs it from disk, so installing AppWrap does not need an Internet
connection. The source archive and license are retained with the installed
tool bundle for license compliance and provenance.
