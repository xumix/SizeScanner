# External Integrations

**Analysis Date:** 2026-08-06

## APIs & External Services

**Runtime Network Services:**
- None detected. `ScannerCore/`, `SizeScanner.Avalonia/`, and `ScannerConsole/` contain no HTTP client, socket client, cloud SDK, database client, telemetry SDK, or remote API integration.
- Scans and settings operate against the local Windows machine; the production application does not require an internet connection.

**Package Distribution:**
- NuGet - Build-time dependency restore for package references declared in `**/*.csproj` and versioned in `Directory.Packages.props`.
  - SDK/Client: .NET CLI and MSBuild NuGet restore.
  - Auth: None configured in the repository; no canonical `NuGet.config` or private-feed credential configuration is detected.

**GitHub Platform:**
- GitHub Actions - Build, test, coverage, CodeQL, release, and dependency automation under `.github/workflows/`.
  - SDK/Client: GitHub-hosted actions and `gh` CLI in `.github/workflows/dependabot-auto-merge.yml`.
  - Auth: Repository-provided `GITHUB_TOKEN` is used only by the Dependabot auto-merge workflow; the release workflow uses its job-level `contents: write` permission.
- GitHub Releases - `v*` tags publish `SizeScanner-win-x64.zip` through `softprops/action-gh-release` in `.github/workflows/release.yml`.
  - SDK/Client: `softprops/action-gh-release@v3`.
  - Auth: GitHub Actions job token via `contents: write`.
- GitHub CodeQL - Scheduled and change-triggered C# static analysis in `.github/workflows/codeql.yml`.
  - SDK/Client: `github/codeql-action/init@v4` and `github/codeql-action/analyze@v4`.
  - Auth: GitHub Actions permissions `actions: read`, `contents: read`, and `security-events: write`.
- Dependabot - Weekly NuGet and GitHub Actions dependency updates configured in `.github/dependabot.yml`, with patch/minor auto-merge in `.github/workflows/dependabot-auto-merge.yml`.
  - SDK/Client: `dependabot/fetch-metadata@v3` and GitHub CLI.
  - Auth: Repository-provided `GITHUB_TOKEN`.

**Documentation Assets:**
- The screenshot in `README.md` is embedded from `raw.githubusercontent.com`; this is a documentation-only external asset and is not fetched by application code.

## Data Storage

**Databases:**
- Not detected. There is no relational, document, embedded, or cloud database dependency in `Directory.Packages.props` or application project files.
- No connection strings, ORM contexts, migrations, or database configuration are present in canonical source.

**File Storage:**
- Local filesystem only.
- User settings are serialized with source-generated System.Text.Json metadata to `%AppData%\SizeScanner\settings.avalonia.json` by `SizeScanner.Avalonia/Services/JsonSettingsStore.cs` and `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`.
- Scanned directory data remains in memory as `ScannerCore/FsItem.cs` trees; no scan-result persistence layer is present.
- Filesystem discovery and mutations target paths selected from local/Windows-visible volumes through `ScannerCore/`, `SizeScanner.Avalonia/Services/DriveProvider.cs`, `SizeScanner.Avalonia/Services/AvaloniaFolderPicker.cs`, and `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`.

**Caching:**
- No external or persistent cache.
- Scan state, chart state, progress, and retained tree nodes are process-local in `ScannerCore/` and `SizeScanner.Avalonia/ViewModels/`.

## Authentication & Identity

**Auth Provider:**
- None. The application has no user accounts, sign-in flow, token validation, authorization middleware, or remote identity provider.

**Windows Identity and Elevation:**
- The process checks membership in the local Administrators group with `WindowsIdentity`, `WindowsPrincipal`, and `WindowsBuiltInRole` in `ScannerCore/DriveScanner.cs` and `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.
- Optional elevation relaunch uses Windows ShellExecute with the `runas` verb in `SizeScanner.Avalonia/Services/WindowsElevationService.cs`, allowing Windows UAC to present the consent prompt.
- The normal application manifest runs as the invoking user (`asInvoker`) in `SizeScanner.Avalonia/app.manifest`; administrator access is not requested at startup.
- Filesystem authorization is delegated to Windows access control. Inaccessible paths are recorded by scanner behavior rather than authenticated through an application-specific security layer.

## Monitoring & Observability

**Error Tracking:**
- None. No Sentry, Application Insights, OpenTelemetry, or other remote error/metrics SDK is referenced by canonical projects.

**Logs:**
- Avalonia diagnostics are sent to trace listeners via `.LogToTrace()` in `SizeScanner.Avalonia/Program.cs`.
- Native directory enumeration and scan-engine fallback diagnostics use `Debug.WriteLine` in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/ScanEngineSelector.cs`.
- The development harness writes rich console status and failures through Spectre.Console in `ScannerConsole/Program.cs`.
- Runtime errors are generally surfaced in the UI or converted to result/fallback state; no log-file sink or centralized collector is configured.
- CI exports TRX test results and Cobertura coverage as GitHub Actions artifacts in `.github/workflows/dotnet-desktop.yml`.

## CI/CD & Deployment

**Hosting:**
- No hosted application service. SizeScanner is distributed as a Windows desktop executable.
- GitHub Releases hosts the versioned `SizeScanner-win-x64.zip` produced by `.github/workflows/release.yml`.
- The release is self-contained and unpackaged: no MSIX/WAP installer, Microsoft Store integration, container image, deployment server, or code-signing service is configured.

**CI Pipeline:**
- GitHub Actions is the detected canonical CI/CD platform.
- `.github/workflows/dotnet-desktop.yml` runs Debug and Release tests/builds on `windows-latest`, collects coverage, and uploads test/build artifacts for pushes and pull requests to `main` or `master`.
- `.github/workflows/codeql.yml` restores and manually builds the C# solution on `windows-latest`, then runs CodeQL on pushes, pull requests, and a weekly schedule.
- `.github/workflows/release.yml` restores and publishes a self-contained `win-x64` Native AOT build on `v*` tags, compresses it, uploads the build artifact, and creates a GitHub Release.
- `.github/workflows/dependabot-auto-merge.yml` runs on Dependabot pull requests and enables squash auto-merge for semantic-version patch and minor updates.
- No GitLab CI configuration is detected in the canonical repository root.

**Build Service Integrations:**
- `actions/checkout@v7` checks out full Git history so MinVer can derive versions in `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, and `.github/workflows/codeql.yml`.
- `actions/setup-dotnet@v6` installs the SDK selected by `global.json` and enables package caching.
- `microsoft/setup-msbuild@v3` exposes the Windows MSBuild toolchain required by the desktop/AOT build.
- `actions/upload-artifact@v7` stores test, coverage, build, and publish outputs.
- `softprops/action-gh-release@v3` creates the tagged GitHub Release.

## Environment Configuration

**Required env vars:**
- Application runtime: none.
- GitHub Actions defines `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_NOLOGO`, solution/project paths, and build configuration inside workflow YAML; these are CI convenience values rather than production requirements.
- Dependabot auto-merge maps the pull request URL and repository-provided GitHub token into the GitHub CLI environment in `.github/workflows/dependabot-auto-merge.yml`.

**Secrets location:**
- No application secrets, credential files, or secret-management integration are required by runtime source.
- GitHub-hosted automation relies on the repository-scoped token generated by GitHub Actions; only the token name is referenced in `.github/workflows/dependabot-auto-merge.yml`.
- No private NuGet feed credentials, signing certificate configuration, cloud deployment credentials, or checked-in secret configuration are detected.

## Webhooks & Callbacks

**Incoming:**
- Application endpoints: none. This is a desktop process with no HTTP server or callback listener.
- Repository automation responds to GitHub push, pull-request, tag, and schedule events declared in `.github/workflows/*.yml`; these are CI event triggers, not application webhooks.

**Outgoing:**
- Application callbacks/webhooks: none.
- Release and dependency workflows call GitHub platform operations through hosted actions and the GitHub CLI in `.github/workflows/release.yml` and `.github/workflows/dependabot-auto-merge.yml`.

## Windows Platform Integrations

**Native Directory Enumeration:**
- `ScannerCore/DirectoryScanner.cs` opens directories with `kernel32.dll!CreateFile` and streams entries from `ntdll.dll!NtQueryDirectoryFile`.
- Unsafe parsing reads Windows `FILE_DIRECTORY_INFORMATION` records directly, including allocation size, logical size, attributes, and names.
- Reparse points are skipped unless marked offline so OneDrive online-only placeholders remain visible; this behavior depends on Windows file-attribute semantics.

**Storage Device Policy:**
- `ScannerCore/VolumeParallelismPolicy.cs` opens local fixed-volume device paths through `kernel32.dll!CreateFile`.
- The same service calls `kernel32.dll!DeviceIoControl` with `IOCTL_STORAGE_QUERY_PROPERTY` to detect seek penalty and enable top-level parallel scanning only for SSD-class storage.
- UNC paths, non-fixed drives, inaccessible devices, and failed property queries conservatively remain sequential.

**Drive and Filesystem Shell:**
- `System.IO.DriveInfo` supplies ready-drive discovery and free/total space in `SizeScanner.Avalonia/Services/DriveProvider.cs` and `ScannerCore/DriveScanner.cs`.
- Avalonia's `StorageProvider.OpenFolderPickerAsync` opens the platform folder picker in `SizeScanner.Avalonia/Services/AvaloniaFolderPicker.cs`.
- `explorer.exe /select` reveals a chart item in Windows Explorer from `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`.
- `Microsoft.VisualBasic.FileIO.FileSystem` sends files and directories to the Windows Recycle Bin; permanent deletion uses `System.IO.File` and `System.IO.Directory` in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`.

**Desktop Shell and UI:**
- `SizeScanner.Avalonia/Program.cs` uses Avalonia platform detection and the classic desktop lifetime.
- `SizeScanner.Avalonia/App.axaml` loads Avalonia's Fluent theme and Windows-compatible system resources.
- `SizeScanner.Avalonia/app.manifest` declares Windows 10/11 compatibility and Windows common-controls v6.

---

*Integration audit: 2026-08-06*
