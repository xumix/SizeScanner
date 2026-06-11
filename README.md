# SizeScanner

A Windows disk-usage visualizer inspired by [Steffen Gerlach's Scanner](http://www.steffengerlach.de/freeware/). Pick a drive or folder, wait for the scan, and explore where space goes through nested doughnut charts.

![](https://raw.githubusercontent.com/xumix/SizeScanner/master/Img/SSSS01.png)

## What it does

SizeScanner walks a drive's directory tree and draws each level as a ring in a sunburst-style chart. Larger slices mean more space. Hover for names and sizes; right-click a slice to open it in Explorer or delete it.

Compared with the original Scanner2 tool, SizeScanner:

- Reports **inaccessible** data — total size plus a list of folders you could not read
- Handles **symlinks and reparse points** correctly, including OneDrive "online-only" placeholders
- Scans **only when you ask** (click a drive button or browse a folder), not automatically at startup
- Lets you tune the chart with **free-space visibility** and a **size filter** so huge trees stay readable

## Requirements

- **Windows** (version supported by [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0))
- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** — needed to build from source; the published app only needs the .NET 10 **desktop runtime** on the machine

## Quick start

From the repository root:

```powershell
dotnet restore SizeScanner.sln
dotnet build SizeScanner.sln -c Debug
dotnet run --project .\ScannerUiWinForms\ScannerUiWinForms.csproj
```

When the window opens, click a drive letter in the toolbar (for example `C:\`) or **Browse...** to pick a folder, then wait for the progress bar to finish.

### Run tests

```powershell
dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release
```

### Publish a runnable folder

To build a framework-dependent app you can copy to another PC (that PC must have the .NET 10 desktop runtime installed):

```powershell
dotnet publish .\ScannerUiWinForms\ScannerUiWinForms.csproj -c Release -r win-x64 --self-contained false
```

The executable is under `ScannerUiWinForms\bin\Release\net10.0-windows\win-x64\publish\`.

## Using the app

| Control | What it does |
|---------|----------------|
| **Browse...** | Pick a folder to scan (directory scan; no free-space slice). Shortcut: **Ctrl+O** |
| **Rescan** | Repeat the last drive or folder scan. Shortcut: **F5** |
| **Drive buttons** (`C:\`, `D:\`, …) | Start a full-drive scan. Controls are disabled until the scan completes. |
| **Progress bar** | Scan progress; the status bar shows the folder currently being read. |
| **Cancel** | Stop an in-progress scan. Shortcut: **Esc** |
| **Show / Hide free space** | Include or exclude the `[Free space]` slice from drive scans. |
| **Filter** | Hide small items below a percentage of total size. Higher values draw faster and look coarser. Default is **1%**. |
| **`[]\|\|` button** | Show or hide the **Inaccessible objects** pane on the right. |
| **Chart hover** | Tooltip with the path chain and size for the slice under the cursor. |
| **Right-click a slice** | **Show** — open that file or folder in Explorer with it selected. **Delete** — send to Recycle Bin (with confirmation). **Delete permanently** — remove without Recycle Bin. |

**Tips**

- Start with the default filter (1%). **No threshold** renders every file and can be very slow on large drives.
- Cream-colored slices are aggregated "everything smaller than the filter" placeholders, not real folders.
- After deleting something, press **F5** (Rescan) to refresh the chart.

### Settings

Filter, free-space, window size, splitter position, and inaccessible-pane visibility are saved to:

`%AppData%\SizeScanner\settings.json`

## Projects in this repo

| Folder | Purpose |
|--------|---------|
| `ScannerUiWinForms/` | WinForms UI — what you run day to day |
| `ScannerCore/` | Scan engine and filesystem tree model |
| `ScannerCore.Tests/` | Automated tests for `ScannerCore` |
| `ScannerConsole/` | Developer harness for timing and progress checks (not shipped) |

For architecture, scanning behavior, and contribution conventions, see [AGENTS.md](AGENTS.md).

### Console harness (developers)

```powershell
# Optional path; defaults to your user profile folder
dotnet run --project .\ScannerConsole\ScannerConsole.csproj -- C:\some\folder
```

Use this to validate scanner changes without launching the chart UI.

## CI

GitLab CI (`.gitlab-ci.yml`) requires a **Windows** runner (`tags: [windows]`). Pipelines build the solution, run `ScannerCore.Tests`, and publish release artifacts when a git tag is pushed.

## License

[GNU Affero General Public License v3.0](LICENSE)
