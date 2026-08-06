# Technology Stack

**Analysis Date:** 2026-08-06

## Languages

**Primary:**
- C# - Application, scanner, console harness, and tests in `ScannerCore/`, `SizeScanner.Avalonia/`, `ScannerConsole/`, `ScannerCore.Tests/`, and `SizeScanner.Avalonia.Tests/`. The language version is not explicitly pinned; compilation uses the default supplied by the .NET 10 SDK in `global.json`.

**Secondary:**
- Avalonia XAML/XML - Desktop application resources, controls, data templates, compiled bindings, and Fluent theme composition in `SizeScanner.Avalonia/App.axaml` and `SizeScanner.Avalonia/Views/*.axaml`.
- MSBuild XML - Project and central build/package configuration in `Directory.Build.props`, `Directory.Packages.props`, and `**/*.csproj`.
- YAML - GitHub Actions and Dependabot automation in `.github/workflows/*.yml` and `.github/dependabot.yml`.
- JSON/JSON-with-comments - SDK and IDE configuration in `global.json`, `.vscode/tasks.json`, and `.vscode/launch.json`; runtime settings are serialized as JSON by `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`.
- PowerShell command syntax - Windows-oriented build, test, packaging, and run commands in `README.md` and `.github/workflows/*.yml`.

## Runtime

**Environment:**
- .NET 10 on Windows, targeting `net10.0-windows` in every project: `ScannerCore/ScannerCore.csproj`, `ScannerConsole/ScannerConsole.csproj`, `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`, `ScannerCore.Tests/ScannerCore.Tests.csproj`, and `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`.
- .NET SDK `10.0.100` is pinned with `rollForward: latestFeature` in `global.json`.
- `win-x64` is the project runtime identifier. The solution advertises Any CPU, x64, and x86 configurations in `SizeScanner.slnx`, but deployment and debug paths are concretely x64.
- Production is a self-contained, single-file, trimmed Native AOT Windows executable through `PublishSingleFile`, `PublishTrimmed`, and `PublishAot` in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.

**Package Manager:**
- NuGet through the .NET CLI/MSBuild.
- Central package management is enabled in `Directory.Packages.props`; project files declare package references without local version numbers.
- Lockfile: missing. No canonical `packages.lock.json` is present, so restore resolves from central version declarations rather than a checked-in dependency graph.

## Frameworks

**Core:**
- .NET Base Class Library 10 - Filesystem access, threading, channels, spans, JSON serialization, process launching, Windows identity, and safe native handles throughout `ScannerCore/` and `SizeScanner.Avalonia/`.
- Avalonia `12.1.1` - Classic desktop application lifetime, XAML UI, custom drawing, storage picker, Fluent theme, and Inter font in `SizeScanner.Avalonia/`.
- CommunityToolkit.Mvvm `8.4.2` - Observable view-model state and generated relay commands in `SizeScanner.Avalonia/ViewModels/`.
- Microsoft.Extensions.DependencyInjection `10.0.10` - Composition root and singleton service/view-model registration in `SizeScanner.Avalonia/App.axaml.cs`.
- System.Text.Json source generation - AOT-safe settings serialization through `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`; this is part of .NET rather than a separate NuGet package.

**Testing:**
- xUnit v3 `3.2.2` - Unit and integration test framework in `ScannerCore.Tests/` and `SizeScanner.Avalonia.Tests/`.
- Microsoft.NET.Test.Sdk `18.8.1` - Test discovery and execution for both test projects.
- xunit.runner.visualstudio `3.1.5` - IDE and `dotnet test` adapter, private to the test projects.
- coverlet.collector `10.0.1` - XPlat coverage collection in CI, private to the test projects.

**Build/Dev:**
- .NET CLI and MSBuild - Restore, build, test, watch, run, and publish workflows defined by `README.md`, `.vscode/tasks.json`, and `.github/workflows/*.yml`.
- MinVer `7.0.0` - Git-tag-derived application versioning; tags use the `v` prefix configured in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- Native AOT toolchain - Release publishing is configured for trimming, ahead-of-time compilation, and self-contained output in `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- GitHub Actions - Windows build/test/publish automation and CodeQL analysis in `.github/workflows/`.
- Visual Studio Code CoreCLR integration - Build, publish, watch, launch, and attach definitions in `.vscode/tasks.json` and `.vscode/launch.json`.

## Key Dependencies

**Critical:**
- Avalonia `12.1.1` - Supplies the production desktop shell, rendering primitives, controls, storage provider, compiled bindings, Fluent theme, and platform detection used by `SizeScanner.Avalonia/Program.cs`, `SizeScanner.Avalonia/App.axaml`, and `SizeScanner.Avalonia/Views/`.
- Avalonia.Desktop `12.1.1` - Hosts the classic Windows desktop lifetime started by `SizeScanner.Avalonia/Program.cs`.
- Avalonia.Themes.Fluent `12.1.1` - Supplies `<FluentTheme />` and dynamic system resources in `SizeScanner.Avalonia/App.axaml`.
- Avalonia.Fonts.Inter `12.1.1` - Supplies `.WithInterFont()` during application bootstrap in `SizeScanner.Avalonia/Program.cs`.
- CommunityToolkit.Mvvm `8.4.2` - Generates observable properties and command plumbing for `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` and `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`.
- Microsoft.Extensions.DependencyInjection `10.0.10` - Wires platform service interfaces, view models, and `MainWindow` in `SizeScanner.Avalonia/App.axaml.cs`.

**Infrastructure:**
- Spectre.Console `0.57.2` - Rich progress, exception, and table output for the development-only scanner harness in `ScannerConsole/Program.cs`.
- MinVer `7.0.0` - Derives assembly/package version data from Git history and `v*` tags for the production application.
- Windows native libraries - `kernel32.dll` supplies `CreateFile` and `DeviceIoControl`; `ntdll.dll` supplies `NtQueryDirectoryFile`. P/Invoke declarations live in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs`.
- Microsoft.VisualBasic.FileIO - Framework-provided Windows recycle-bin operations in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`; it is not a separate package reference.

## Configuration

**Environment:**
- No runtime environment variables are required by application code.
- User preferences persist to `%AppData%\SizeScanner\settings.avalonia.json`, with the path and fallback behavior defined in `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`.
- CI disables .NET CLI telemetry and the logo through `DOTNET_CLI_TELEMETRY_OPTOUT` and `DOTNET_NOLOGO` in `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, and `.github/workflows/codeql.yml`.
- No `.env`-based application configuration system, database connection configuration, or application secret provider is detected in the canonical repository.

**Build:**
- `global.json` pins the SDK feature band and controls roll-forward.
- `Directory.Build.props` enables nullable reference types, latest analyzer rules, deterministic builds, AGPL license metadata, and disables implicit usings for all projects.
- `Directory.Packages.props` is the single source of NuGet package versions.
- `SizeScanner.slnx` is the solution entry point and includes five projects.
- `ScannerCore/ScannerCore.csproj` enables unsafe code and AOT compatibility for native directory enumeration.
- `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj` configures Windows-only deployment, compiled Avalonia bindings, app resources, Native AOT, trimming, single-file publishing, application manifest, icon, and MinVer.
- `SizeScanner.Avalonia/app.manifest` requests `asInvoker`, declares Windows 10/11 compatibility, and enables Windows common controls v6.
- `.editorconfig` sets UTF-8, final newlines, four-space C# indentation, braces on new lines, and System-first using sorting.
- `.vscode/tasks.json` and `.vscode/launch.json` provide local build/publish/watch and CoreCLR debug configuration.
- `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, and `.github/workflows/codeql.yml` use Windows runners and restore against `win-x64`.

## Platform Requirements

**Development:**
- Windows 10 or later on a version supported by .NET 10, as documented in `README.md`.
- .NET 10 SDK compatible with `global.json`; the pinned baseline is `10.0.100`.
- Windows/MSBuild native toolchain capable of linking the configured Native AOT publish output; CI establishes MSBuild with `microsoft/setup-msbuild` in `.github/workflows/*.yml`.
- x64 is the validated runtime target. Native struct layout, unsafe buffer parsing, and P/Invoke ABI assumptions in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs` must remain compatible with Windows.

**Production:**
- Windows 10 or later, x64.
- Self-contained `win-x64` Native AOT release; a separately installed .NET Desktop Runtime is not required.
- Local access to scanned files/directories and Windows native APIs is required. Administrator rights are optional and requested only through the explicit relaunch flow in `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.
- No server runtime, container, browser, database, or network service is required for application operation.

---

*Stack analysis: 2026-08-06*
