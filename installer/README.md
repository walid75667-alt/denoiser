# Windows installer

`MicDenoiser-Setup-x64.exe` packages the self-contained Windows 10/11 x64
application, .NET runtime, DeepFilterNet3 model, native libraries, instructions
and licenses. No model download is needed at application startup. VB-Cable
remains a separate vendor driver installation.

Build after `scripts/build.ps1 -Publish`, using NSIS 3:

```powershell
./scripts/build-installer.ps1 -Makensis 'C:/Program Files (x86)/NSIS/makensis.exe'
```

Setup uses the current user's `%LocalAppData%/Programs/MicDenoiser` folder,
without requesting administrator access. It creates Start menu shortcuts, an
optional desktop shortcut and a Windows Apps uninstall entry. The first
installation sets the application's language to the selected installer
language. Upgrades keep existing settings; uninstall also keeps settings in
`%AppData%/MicDenoiser`.

The uninstaller deletes a generated list of published files and empty
subdirectories. It never recursively deletes the installation folder or
AppData. Close the application before installing or uninstalling.

The installer was compiled with NSIS 3.11 on Linux. Payload hashes and
extraction were checked. Actual installation, language selection, shortcuts,
upgrade, uninstall, and Windows audio still require a Windows test. This
installer is unsigned.
