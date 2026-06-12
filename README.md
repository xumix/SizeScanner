# SizeScanner

A Windows disk-usage visualizer inspired by [Steffen Gerlach's Scanner](http://www.steffengerlach.de/freeware/) and [@AgentMC SizeScanner](https://github.com/AgentMC/SizeScanner).

Pick a drive or folder, wait for the scan, and explore where space goes through nested doughnut charts. Hover for names and sizes; right-click a slice to open it in Explorer or delete it.

![](https://raw.githubusercontent.com/xumix/SizeScanner/master/Img/main_window.png)

Compared with the original Scanner2 tool, SizeScanner:

- Reports **inaccessible** data
- Handles **symlinks and reparse points** correctly, including OneDrive "online-only" placeholders
- Scans **only when you ask** (click a drive button or browse a folder), not automatically at startup
- Lets you tune the chart with **free-space visibility** and a **size filter** so huge trees stay readable

## Requirements

- **Windows 10+** (version supported by [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0))
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** - needed to build from source
- ~~**[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** - needed to run~~ Self-contained

## Quick start

From the repository root:

```powershell
dotnet restore SizeScanner.slnx
dotnet build SizeScanner.slnx -c Debug
dotnet run --project .\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj
```

When the window opens, click a drive letter in the toolbar (for example `C:\`) or **Browse...** to pick a folder, then wait for the progress bar to finish.

### Console harness (for development purposes)

```powershell
# Optional path; defaults to your user profile folder
dotnet run --project .\ScannerConsole\ScannerConsole.csproj -- C:\some\folder
```

Use this to validate scanner changes without launching the chart UI.

## License

[GNU Affero General Public License v3.0](LICENSE)
