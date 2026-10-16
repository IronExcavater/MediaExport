# Development

Run the current source build from PowerShell at the repository root:

```powershell
.\eng\Run-Dev.ps1
```

The launcher builds an unpackaged, self-contained Windows App SDK executable and starts that exact output. It does not use or copy files into a registered `AppX` installation, so the window always represents the source just built. Pass `-Platform x86` or `-Platform ARM64` when developing for another configured architecture.

Release packaging remains separate: build the project normally through Visual Studio or the packaging workflow when testing MSIX behavior.
