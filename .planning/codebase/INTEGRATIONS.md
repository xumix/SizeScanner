# External Integrations

**Analysis Date:** 2026-07-16

## APIs & External Services

**Windows Native Filesystem APIs:**
- `kernel32.dll!CreateFile` opens directory and volume handles in `ScannerCore/DirectoryScanner.cs` and `ScannerCore/VolumeParallelismPolicy.cs`.
  - SDK/Client: .NET P/Invoke through `System.Runtime.InteropServices` and `Microsoft.Win32.SafeHandles`.
  - Auth: Current Windows process token; inaccessible directories return `null` and are reported by `ScannerCore/DriveScanner.cs`.
- `ntdll.dll!NtQueryDirectoryFile` performs buffered native directory enumeration in `ScannerCore/DirectoryScanner.cs`.
  - SDK/Client: Direct P/Invoke with a pooled 1 MiB buffer and unsafe parsing in `ScannerCore/DirectoryScanner.cs`.
  - Auth: Current Windows process token; no separate credentials.
- `kernel32.dll!DeviceIoControl` queries `StorageDeviceSeekPenaltyProperty` to distinguish SSD-class storage from seek-penalty devices in `ScannerCore/VolumeParallelismPolicy.cs`.
  - SDK/Client: Direct P/Invoke with marshalled `STORAGE_PROPERTY_QUERY`-style structures.
  - Auth: Current Windows process token; failures conservatively select sequential scanning.

**Windows Shell and Desktop Services:**
- Windows Explorer selection is launched through `explorer.exe` in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`.
  - SDK/Client: `System.Diagnostics.Process`.
  - Auth: Current interactive Windows user.
- Recycle Bin deletion uses `Microsoft.VisualBasic.FileIO.FileSystem` in `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`; permanent deletion uses `System.IO`.
  - SDK/Client: .NET runtime APIs.
  - Auth: Current Windows process token and filesystem ACLs.
- UAC elevation uses the Windows Shell `runas` verb in `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.
  - SDK/Client: `ProcessStartInfo` with `UseShellExecute=true`.
  - Auth: Interactive UAC consent; cancellation error 1223 is handled without treating it as an application failure.
- Windows identity and administrator-role checks use `WindowsIdentity`, `WindowsPrincipal`, and `WindowsBuiltInRole.Administrator` in `ScannerCore/DriveScanner.cs` and `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.
  - SDK/Client: `System.Security.Principal.Windows`.
  - Auth: Current Windows access token.
- The native Avalonia storage provider opens the OS folder picker in `SizeScanner.Avalonia/Services/AvaloniaFolderPicker.cs`.
  - SDK/Client: `Avalonia.Platform.Storage`.
  - Auth: Current interactive Windows user.

**Runtime Network Services:**
- No HTTP client, remote service SDK, telemetry exporter, update service, or runtime API client is present in `ScannerCore/`, `SizeScanner.Avalonia/`, or `ScannerConsole/`.
- Links in `README.md` are documentation and image references only; no application code fetches them.

**Build-Time Package Service:**
- NuGet restores packages declared in `Directory.Packages.props` and project references in `**/*.csproj`.
  - SDK/Client: `dotnet restore` in `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, and `.github/workflows/codeql.yml`.
  - Auth: No repository-level private feed or `NuGet.Config` is present; any feed credentials must come from the developer or CI environment.

## Data Storage

**Databases:**
- Not detected; no database server, embedded database, ORM, or database client is referenced by `Directory.Packages.props` or production `*.csproj` files.
  - Connection: Not applicable.
  - Client: Not applicable.

**File Storage:**
- Local Windows filesystems only. `ScannerCore/DirectoryScanner.cs` reads directory metadata, while `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs` reveals or deletes user-selected paths.
- User preferences are serialized to `%AppData%\SizeScanner\settings.avalonia.json` by `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`.
- Settings serialization uses the AOT-safe source-generated context in `SizeScanner.Avalonia/Services/SizeScannerJsonContext.cs`.
- Scan results remain in memory as `ScannerCore/FsItem.cs` trees; no scan database or result file persistence is implemented in `ScannerCore/DriveScanner.cs`.

**Caching:**
- No external or persistent cache is present. `ScannerCore/DirectoryScanner.cs` temporarily rents enumeration buffers from `ArrayPool<byte>.Shared`.

## Authentication & Identity

**Auth Provider:**
- No application account system, OAuth/OIDC provider, API-key authentication, or authorization server is present in the production projects listed by `SizeScanner.slnx`.
  - Implementation: Windows identity is used only for privilege detection and filesystem access in `ScannerCore/DriveScanner.cs` and `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.
- The default manifest requests `asInvoker` in `SizeScanner.Avalonia/app.manifest`; users can explicitly relaunch elevated through `SizeScanner.Avalonia/Services/WindowsElevationService.cs`.

## Monitoring & Observability

**Error Tracking:**
- None; no Sentry, Application Insights, OpenTelemetry, or remote crash-reporting package appears in `Directory.Packages.props`.

**Logs:**
- Avalonia startup calls `LogToTrace()` in `SizeScanner.Avalonia/Program.cs`, routing framework diagnostics to trace listeners rather than a remote sink.
- Native enumeration failures emit `Debug.WriteLine` with the NTSTATUS code in `ScannerCore/DirectoryScanner.cs`.
- The development harness writes progress, failures, and inaccessible paths to the terminal through Spectre.Console in `ScannerConsole/Program.cs`.
- GitHub CI uploads TRX results and Cobertura coverage artifacts from `TestResults/` in `.github/workflows/dotnet-desktop.yml`.

## CI/CD & Deployment

**Hosting:**
- GitHub Releases hosts tagged release archives created by `.github/workflows/release.yml`.
- The release artifact is `SizeScanner-win-x64.zip`, built as a self-contained Windows x64 publication from `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`.
- No MSIX/WAP installer, store deployment, container image, cloud host, or code-signing integration is configured in `.github/workflows/release.yml`.

**CI Pipeline:**
- `.github/workflows/dotnet-desktop.yml` runs Debug and Release tests, collects XPlat coverage, restores `win-x64`, builds `SizeScanner.slnx`, and uploads Release artifacts on pushes and pull requests to `main` or `master`.
- `.github/workflows/release.yml` triggers on `v*` tags, performs a self-contained `win-x64` publish, creates a zip, and invokes `softprops/action-gh-release@v2` with generated release notes.
- `.github/workflows/codeql.yml` performs scheduled and branch/PR C# CodeQL analysis on `windows-latest`.
- `.github/dependabot.yml` groups weekly NuGet and GitHub Actions updates; `.github/workflows/dependabot-auto-merge.yml` enables squash auto-merge for patch and minor Dependabot pull requests.
- Git tag history is required by MinVer, so GitHub checkout uses `fetch-depth: 0` in `.github/workflows/dotnet-desktop.yml`, `.github/workflows/release.yml`, and `.github/workflows/codeql.yml`.
- GitLab CI is not present in the checked-out repository. `AGENTS.md` references `.gitlab-ci.yml`, but that file is not available, so no active GitLab pipeline can be verified.

## Environment Configuration

**Required env vars:**
- Production application: none detected in `ScannerCore/`, `SizeScanner.Avalonia/`, or `ScannerConsole/`.
- `SIZESCANNER_RUN_PERF_TESTS=1` optionally enables the opt-in speed test in `ScannerCore.Tests/DirectoryWalkEngineParallelSpeedTests.cs`.
- GitHub workflows define non-secret build variables such as `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_NOLOGO`, project paths, and configuration in `.github/workflows/*.yml`.
- Dependabot auto-merge maps the pull-request URL and GitHub token to `PR_URL` and `GH_TOKEN` in `.github/workflows/dependabot-auto-merge.yml`.

**Secrets location:**
- `GITHUB_TOKEN` is supplied by GitHub Actions through `${{ secrets.GITHUB_TOKEN }}` in `.github/workflows/dependabot-auto-merge.yml`.
- No `.env` files, repository credential files, or application secret configuration were detected; runtime code does not request secrets.

## Webhooks & Callbacks

**Incoming:**
- None in application code. GitHub event triggers in `.github/workflows/*.yml` are repository automation events, not public application endpoints.

**Outgoing:**
- None in application code; no HTTP, webhook, email, analytics, or notification integration is implemented under `ScannerCore/`, `SizeScanner.Avalonia/`, or `ScannerConsole/`.
- GitHub release and pull-request mutations are performed only inside `.github/workflows/release.yml` and `.github/workflows/dependabot-auto-merge.yml` using GitHub-provided actions and credentials.

---

*Integration audit: 2026-07-16*
