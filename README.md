# MediaExport

MediaExport is a Windows app for finding media and exporting it with yt-dlp and FFmpeg.

## Architecture

The solution is split into four projects:

- `MediaExport` — WinUI 3 views and Windows integrations.
- `MediaExport.Application` — view models and application workflows.
- `MediaExport.Core` — media providers, exporting, settings, storage, and tool management.
- `MediaExport.Tests` — NUnit unit tests for application and core behaviour.

Dependencies point inward: the WinUI app uses the application and core projects, while the core project does not depend on the interface.

## Developer setup

You need:

- Windows 10 version 1809 or newer.
- Visual Studio 2026 with the **WinUI application development** workload.
- The .NET 10 SDK.
- Developer Mode enabled in Windows Settings.

Clone the repository, open `MediaExport.slnx`, select `MediaExport` as the startup project, choose `x64`, and run the packaged or unpackaged launch profile.

To build and test from a terminal:

```powershell
dotnet restore MediaExport.slnx
dotnet build MediaExport.slnx -p:Platform=x64
dotnet test MediaExport.Tests/MediaExport.Tests.csproj
```

MediaExport can use yt-dlp and FFmpeg already installed on the computer. Missing tools can also be installed and managed from Settings.

## License

MediaExport is licensed under the [Mozilla Public License 2.0](LICENSE).
