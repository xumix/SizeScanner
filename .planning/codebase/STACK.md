# Technology Stack

**Analysis Date:** 2026-07-16

## Languages

**Primary:**
- C# - Application, scanner engine, console harness, and tests in `ScannerCore/*.cs`, `SizeScanner.Avalonia/*.cs`, `ScannerConsole/Program.cs`, `ScannerCore.Tests/*.cs`, and `SizeScanner.Avalonia.Tests/*.cs`.
- The compiler language version follows the selected .NET 10 SDK default because no `LangVersion` override is present in `Directory.Build.props` or the project files.

**Secondary:**
- Avalonia XAML - Desktop views, resources, styles, and compiled bindings in `SizeScanner.Avalonia/App.axaml` and `SizeScanner.Avalonia/Views/*.axaml`.
- XML/MSBuild - SDK-style project and central package configuration in `SizeScanner.slnx`, `Directory.Build.props`, `Directory.Packages.props`, and `**/*.csproj`.
- YAML - GitHub Actions and dependency automation in `.github/workflows/*.yml` and `.github/dependabot.yml`.
- PowerShell command syntax - Windows CI and documented development commands in `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, and `README.md`.

## Runtime

**Environment:**
- .NET 10 for Windows, targeting `net10.0-windows` in all five project files under `ScannerCore/`, `ScannerCore.Tests/`, `ScannerConsole/`, `SizeScanner.Avalonia/`, and `SizeScanner.Avalonia.Tests/`.
- .NET SDK `10.0.100` is pinned with `rollForward: latestFeature` in `global.json`.
- Production runtime identifier is `win-x64` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`; the other projects also declare `win-x64`.

**Package Manager:**
- NuGet through the .NET SDK, with central package management enabled in `Directory.Packages.props`.
- Package versions are declared once in `Directory.Packages.props`; individual references remain in each `*.csproj`.
- Lockfile: missing; no `packages.lock.json` or repository `NuGet.Config` is present, so restores use configured/default NuGet sources without repository-level lock enforcement.

## Frameworks

**Core:**
- .NET 10 Base Class Library - Filesystem, threading, JSON, Windows identity, process, and interop primitives used throughout `ScannerCore/` and `SizeScanner.Avalonia/`.
- Avalonia `12.0.4` - Classic desktop lifetime, controls, storage picker, custom drawing, Fluent theme, and Inter font in `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/App.axaml`, and `SizeScanner.Avalonia/Views/`.
- CommunityToolkit.Mvvm `8.4.2` - Observable view-models and generated relay commands in `SizeScanner.Avalonia/ViewModels/`.
- Microsoft.Extensions.DependencyInjection `10.0.9` - Application composition and singleton registration in `SizeScanner.Avalonia/App.axaml.cs`.

**Testing:**
- xUnit v3 `3.2.2` with Visual Studio runner `3.1.5` - Unit and Windows integration tests in `ScannerCore.Tests/` and `SizeScanner.Avalonia.Tests/`.
- Microsoft.NET.Test.Sdk `18.6.0` - `dotnet test` discovery and execution in both test project files.
- coverlet.collector `10.0.1` - XPlat code coverage collection configured through test package references and `.github/workflows/dotnet-desktop.yml`.

**Build/Dev:**
- .NET SDK/MSBuild - Restore, build, test, watch, and publish operations defined in `README.md`, `.vscode/tasks.json`, and `.github/workflows/*.yml`.
- Native AOT and IL trimming - Enabled by `PublishAot`, `PublishTrimmed`, and `IsAotCompatible` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`; `ScannerCore/ScannerCore.csproj` is also marked AOT-compatible.
- Single-file publishing - Enabled by `PublishSingleFile` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- MinVer `7.0.0` - Derives application versions from Git tags prefixed with `v` through `MinVerTagPrefix` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- Visual Studio Code CoreCLR tooling - Build, publish, watch, launch, and attach configurations in `.vscode/tasks.json` and `.vscode/launch.json`.

## Key Dependencies

**Critical:**
- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, and `Avalonia.Fonts.Inter` `12.0.4` - The complete production desktop UI stack declared in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- `CommunityToolkit.Mvvm` `8.4.2` - Source-generated MVVM properties and commands used by `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- `Microsoft.Extensions.DependencyInjection` `10.0.9` - Explicit composition root in `SizeScanner.Avalonia/App.axaml.cs`.
- `MinVer` `7.0.0` - Build-only Git tag versioning dependency marked `PrivateAssets=all` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.

**Infrastructure:**
- `Spectre.Console` `0.57.0` - Manual scanner performance/progress harness output in `ScannerConsole/Program.cs`; it is not referenced by the production UI.
- `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio`, and `coverlet.collector` - Test and coverage infrastructure in `ScannerCore.Tests/ScannerCore.Tests.csproj` and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- `Microsoft.VisualBasic.FileIO` from the .NET runtime - Windows Recycle Bin deletion behavior in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`; no separate NuGet dependency is declared.

## Configuration

**Environment:**
- The production application requires no runtime environment variables; application settings persist as JSON through `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`.
- Optional performance tests are enabled only when `SIZESCANNER_RUN_PERF_TESTS=1` in `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`.
- Nullable reference types are enabled, implicit usings are disabled, analysis level is latest, and deterministic compilation is enabled repository-wide in `Directory.Build.props`.
- A source-generated `System.Text.Json` context in `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs` preserves trimming and native-AOT compatibility.

**Build:**
- `global.json` selects the SDK; `Directory.Build.props` supplies shared compiler properties; `Directory.Packages.props` supplies central versions.
- `SizeScanner.slnx` defines `Any CPU`, `x64`, and `x86` solution platforms and includes all five projects.
- `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` defines the `WinExe`, Windows support declaration, application manifest, icon, compiled Avalonia bindings, trimming, AOT, and single-file publication.
- `SizeScanner.Avalonia/app.manifest` requests `asInvoker`, advertises Windows 10/11 compatibility, and enables version 6 Windows common controls.

## Platform Requirements

**Development:**
- Windows 10 or newer with a .NET 10 SDK compatible with `global.json`, as documented in `README.md`.
- Native Windows APIs are required by `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs`, so the solution is not cross-platform even though Avalonia itself is.
- Unsafe code is enabled only in `ScannerCore/ScannerCore.csproj` for native directory buffer parsing in `ScannerCore/DirectoryScanner.cs`.

**Production:**
- Self-contained Windows x64 desktop deployment generated from `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`; no separately installed .NET Desktop Runtime is required.
- Release output is a trimmed, native-AOT, single-file-capable `win-x64` application; `.github/workflows/release.yml` publishes self-contained files and packages them as `SizeScanner-win-x64.zip`.
- The executable runs at the caller's privilege level by default via `SizeScanner.Avalonia/app.manifest`, with optional UAC relaunch implemented in `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.

---

*Stack analysis: 2026-07-16*
