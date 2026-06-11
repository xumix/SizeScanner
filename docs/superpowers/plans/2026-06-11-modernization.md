# SizeScanner Modernization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modernize SizeScanner with engineering foundation (tests, CI, code quality) then UX improvements (browse, rescan, settings, recycle-bin delete), without changing the core scanner algorithm or chart interaction model.

**Architecture:** Keep UI → Core dependency flow. Add `ScannerCore.Tests`, harden `DriveScanner` API with named metadata and `IProgress<ScanProgress>`, extract `ChartMapper` and `FileSystemActions` from `Form1.cs`, persist settings as JSON, add GitLab CI and release artifacts.

**Tech Stack:** .NET 10, xUnit, GitLab CI, MinVer, WinForms, `WinForms.DataVisualization`, `Microsoft.VisualBasic.FileIO` from the .NET runtime (recycle bin), System.Text.Json (settings).

**Design spec:** `docs/superpowers/specs/2026-06-11-modernization-design.md`

---

## File map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `ScannerCore.Tests/ScannerCore.Tests.csproj` | xUnit test project |
| Create | `ScannerCore.Tests/HumanizeTests.cs` | Size/FsItem unit tests |
| Create | `ScannerCore.Tests/DriveScannerTests.cs` | Threshold + scan fixture tests |
| Create | `ScannerCore.Tests/TemporaryDirectory.cs` | Temp tree helper |
| Create | `.editorconfig` | Formatting/analyzer rules |
| Create | `global.json` | SDK version pin |
| Create | `.gitlab-ci.yml` | Build + test + publish |
| Create | `ScannerCore/DriveScanMetadata.cs` | Synthetic entry constants |
| Create | `ScannerCore/ScanProgress.cs` | Progress record |
| Create | `ScannerUiWinForms/ChartMapper.cs` | Chart series building |
| Create | `ScannerUiWinForms/FileSystemActions.cs` | Explorer + delete |
| Create | `ScannerUiWinForms/ScanSession.cs` | Scan lifecycle state |
| Create | `ScannerUiWinForms/UserSettings.cs` | JSON settings load/save |
| Create | `LICENSE` | AGPL 3.0 license |
| Modify | `Directory.Build.props` | Shared nullable/analyzer props |
| Modify | `Directory.Packages.props` | xUnit, MinVer, coverlet versions |
| Modify | `ScannerCore/DriveScanner.cs` | Metadata + IProgress |
| Modify | `ScannerCore/ScannerCore.csproj` | Nullable enable |
| Modify | `ScannerUiWinForms/Form1.cs` | Slim orchestration + UX |
| Modify | `ScannerUiWinForms/Form1.Designer.cs` | Browse + Rescan toolbar controls |
| Modify | `ScannerConsole/Program.cs` | IProgress consumption |
| Modify | `SizeScanner.sln` | Add test project |
| Modify | `README.md`, `AGENTS.md` | Test/CI/docs |
| Delete | `ScannerCore/Properties/AssemblyInfo.cs` | After GenerateAssemblyInfo unified |
| Delete | `ScannerConsole/Properties/AssemblyInfo.cs` | Same |
| Delete | `ScannerUiWinForms/Properties/AssemblyInfo.cs` | Same (if redundant) |

---

# Phase 1: Engineering Foundation

### Task 1: Test project scaffold

**Files:**
- Create: `ScannerCore.Tests/ScannerCore.Tests.csproj`
- Modify: `Directory.Packages.props`
- Modify: `SizeScanner.sln`

- [ ] **Step 1: Add package versions**

Add to `Directory.Packages.props` inside `<ItemGroup>`:

```xml
<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
<PackageVersion Include="xunit" Version="2.9.3" />
<PackageVersion Include="xunit.runner.visualstudio" Version="3.0.2" />
<PackageVersion Include="coverlet.collector" Version="6.0.4" />
```

- [ ] **Step 2: Create test project file**

`ScannerCore.Tests/ScannerCore.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IsPackable>false</IsPackable>
    <RootNamespace>ScannerCore.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ScannerCore\ScannerCore.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add project to solution**

Run: `dotnet sln SizeScanner.sln add ScannerCore.Tests/ScannerCore.Tests.csproj`

- [ ] **Step 4: Verify build**

Run: `dotnet build ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug`

Expected: Build succeeded (0 tests yet)

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props ScannerCore.Tests/ScannerCore.Tests.csproj SizeScanner.sln
git commit -m "test: add ScannerCore.Tests xUnit project"
```

---

### Task 2: Humanize unit tests

**Files:**
- Create: `ScannerCore.Tests/HumanizeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class HumanizeTests
{
    [Theory]
    [InlineData(0, "0.00 Byte(s)")]
    [InlineData(512, "512.00 Byte(s)")]
    [InlineData(1024, "1024.00 Byte(s)")]
    [InlineData(1536, "1.50 KByte(s)")]
    [InlineData(1048576, "1024.00 KByte(s)")]
    public void Size_formats_expected_suffixes(long bytes, string expected)
    {
        Assert.Equal(expected, Humanize.Size(bytes));
    }

    [Fact]
    public void FsItem_denied_directory_returns_access_denied()
    {
        var item = new FsItem("secret", 0, isDir: true) { Items = null };
        Assert.Equal("<Access Denied>", Humanize.FsItem(item));
    }

    [Fact]
    public void FsItem_zero_size_returns_empty()
    {
        var item = new FsItem("empty.txt", 0, isDir: false);
        Assert.Equal("<Empty>", Humanize.FsItem(item));
    }

    [Fact]
    public void FsItem_nonzero_returns_size_string()
    {
        var item = new FsItem("a.bin", 1024, isDir: false);
        Assert.Equal("1024.00 Byte(s)", Humanize.FsItem(item));
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug --filter HumanizeTests -v n`

Expected: All 8 tests PASS (tests document existing behavior; no production change)

- [ ] **Step 3: Commit**

```bash
git add ScannerCore.Tests/HumanizeTests.cs
git commit -m "test: cover Humanize size and FsItem formatting"
```

---

### Task 3: Temporary directory fixture

**Files:**
- Create: `ScannerCore.Tests/TemporaryDirectory.cs`

- [ ] **Step 1: Create helper**

```csharp
using System;

namespace ScannerCore.Tests;

public sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SizeScanner.Tests." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string CreateFile(string relativePath, int sizeBytes)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var dir = System.IO.Path.GetDirectoryName(fullPath)!;
        System.IO.Directory.CreateDirectory(dir);
        using var fs = System.IO.File.Create(fullPath);
        if (sizeBytes > 0)
            fs.SetLength(sizeBytes);
        return fullPath;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Path, recursive: true); }
        catch { /* best effort */ }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add ScannerCore.Tests/TemporaryDirectory.cs
git commit -m "test: add temporary directory fixture for scan tests"
```

---

### Task 4: DriveScanner integration tests

**Files:**
- Create: `ScannerCore.Tests/DriveScannerTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using System.Threading;
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DriveScannerTests
{
    [Fact]
    public void ScanDirectory_sums_file_sizes()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("a.txt", 100);
        temp.CreateFile("sub/b.txt", 250);

        var scanner = new DriveScanner();
        var root = scanner.ScanDirectory(temp.Path, CancellationToken.None);

        Assert.Equal(350, root.Size);
        Assert.Equal(2, root.Items!.Count);
    }

    [Fact]
    public void GetDisplayThreshold_returns_zero_for_directory_scan_when_free_space_is_excluded()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("big.bin", 1000);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        var threshold = scanner.GetDisplayThreshold(0.1f, includeFreeSpace: false);
        Assert.Equal(0, threshold);
    }

    [Fact]
    public void GetDisplayThreshold_uses_scanned_total_for_directory_scan_when_free_space_is_included()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("big.bin", 1000);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        var threshold = scanner.GetDisplayThreshold(0.1f, includeFreeSpace: true);
        Assert.Equal(100, threshold);
    }

    [Fact]
    public void ScanDirectory_sets_current_target_to_requested_path()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("ok.txt", 10);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        Assert.Equal(temp.Path, scanner.CurrentTarget);
    }

    [Fact]
    public void Inaccessible_starts_empty_for_readable_tree()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("ok.txt", 10);

        var scanner = new DriveScanner();
        scanner.ScanDirectory(temp.Path, CancellationToken.None);

        Assert.Empty(scanner.Inaccessible);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Debug -v n`

Expected: All tests PASS

- [ ] **Step 3: Commit**

```bash
git add ScannerCore.Tests/DriveScannerTests.cs
git commit -m "test: add DriveScanner directory scan integration tests"
```

---

### Task 5: Editorconfig and SDK pin

**Files:**
- Create: `.editorconfig`
- Create: `global.json`

- [ ] **Step 1: Create `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
insert_final_newline = true
trim_trailing_whitespace = true

[*.{cs,csx}]
indent_size = 4
indent_style = space

# C# formatting
csharp_new_line_before_open_brace = all
dotnet_sort_system_directives_first = true
```

- [ ] **Step 2: Create `global.json`**

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

Adjust version to match installed SDK: `dotnet --version`

- [ ] **Step 3: Verify build**

Run: `dotnet build SizeScanner.sln -c Release`

Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add .editorconfig global.json
git commit -m "chore: add editorconfig and pin .NET SDK"
```

---

### Task 6: GitLab CI

**Files:**
- Create: `.gitlab-ci.yml`

- [ ] **Step 1: Add pipeline**

```yaml
stages:
  - build
  - test
  - publish

variables:
  DOTNET_CLI_TELEMETRY_OPTOUT: "1"
  DOTNET_NOLOGO: "true"

build:
  stage: build
  tags:
    - windows
  script:
    - dotnet restore SizeScanner.sln
    - dotnet build SizeScanner.sln -c Release --no-restore

test:
  stage: test
  tags:
    - windows
  needs: [build]
  script:
    - dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release -v n

publish:
  stage: publish
  tags:
    - windows
  needs: [test]
  rules:
    - if: $CI_COMMIT_TAG
  script:
    - dotnet publish ScannerUiWinForms/ScannerUiWinForms.csproj -c Release -r win-x64 --self-contained false -o publish/
  artifacts:
    paths:
      - publish/
```

If no Windows runner is available yet, document `tags: [windows]` requirement in README and use a self-hosted runner.

- [ ] **Step 2: Commit**

```bash
git add .gitlab-ci.yml
git commit -m "ci: add GitLab build, test, and tag publish pipeline"
```

---

### Task 7: Enable nullable in ScannerCore

**Files:**
- Modify: `ScannerCore/ScannerCore.csproj`
- Modify: `ScannerCore/FsItem.cs`
- Modify: `ScannerCore/DriveScanner.cs`

- [ ] **Step 1: Enable nullable in project**

In `ScannerCore/ScannerCore.csproj` add inside `<PropertyGroup>`:

```xml
<Nullable>enable</Nullable>
```

- [ ] **Step 2: Fix nullable warnings in FsItem**

```csharp
public string Name { get; }
public List<FsItem>? Items { get; set; }
```

Update constructor to assign `Name` without `private set` if needed.

- [ ] **Step 3: Fix DriveScanner nullability**

Add null-forgiving or guards where `item.Items` is accessed after null check. Build until 0 warnings in ScannerCore.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release`

Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add ScannerCore/
git commit -m "refactor: enable nullable reference types in ScannerCore"
```

---

### Task 8: Unify assembly info generation

**Files:**
- Modify: `ScannerCore/ScannerCore.csproj` — set `GenerateAssemblyInfo` to `true` (or remove `false`)
- Delete: `ScannerCore/Properties/AssemblyInfo.cs`
- Delete: `ScannerConsole/Properties/AssemblyInfo.cs`
- Modify: `ScannerConsole/ScannerConsole.csproj` — `GenerateAssemblyInfo` true
- Modify: `AGENTS.md`

- [ ] **Step 1: Remove manual AssemblyInfo files and rebuild**

Run: `dotnet build SizeScanner.sln -c Release`

Expected: Build succeeded

- [ ] **Step 2: Commit**

```bash
git add ScannerCore/ ScannerConsole/ AGENTS.md
git commit -m "chore: use SDK-generated assembly info"
```

---

# Phase 2: Scanner API Hardening

### Task 9: DriveScanMetadata

**Files:**
- Create: `ScannerCore/DriveScanMetadata.cs`
- Modify: `ScannerCore/DriveScanner.cs`
- Create: `ScannerCore.Tests/DriveScanMetadataTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using ScannerCore;
using Xunit;

namespace ScannerCore.Tests;

public sealed class DriveScanMetadataTests
{
    [Fact]
    public void Constants_match_existing_synthetic_entry_contract()
    {
        Assert.Equal("[Free space]", DriveScanMetadata.FreeSpaceName);
        Assert.Equal("[Inaccessible]", DriveScanMetadata.InaccessibleName);
        Assert.Equal(0, DriveScanMetadata.FreeSpaceIndex);
        Assert.Equal(1, DriveScanMetadata.InaccessibleIndex);
        Assert.Equal(2, DriveScanMetadata.SyntheticEntryCount);
    }

    [Fact]
    public void GetInaccessibleEntry_returns_second_synthetic_child()
    {
        var root = new FsItem("C:\\", 1000, isDir: true)
        {
            Items =
            [
                new FsItem(DriveScanMetadata.FreeSpaceName, 100, false),
                new FsItem(DriveScanMetadata.InaccessibleName, 50, false),
                new FsItem("Windows", 850, true)
            ]
        };

        var inaccessible = DriveScanMetadata.GetInaccessibleEntry(root);
        Assert.Equal(DriveScanMetadata.InaccessibleName, inaccessible.Name);
        Assert.Equal(50, inaccessible.Size);
    }

    [Fact]
    public void GetFreeSpaceEntry_returns_first_synthetic_child()
    {
        var root = new FsItem("C:\\", 1000, isDir: true)
        {
            Items =
            [
                new FsItem(DriveScanMetadata.FreeSpaceName, 100, false),
                new FsItem(DriveScanMetadata.InaccessibleName, 50, false),
                new FsItem("Windows", 850, true)
            ]
        };

        var freeSpace = DriveScanMetadata.GetFreeSpaceEntry(root);
        Assert.Equal(DriveScanMetadata.FreeSpaceName, freeSpace.Name);
        Assert.Equal(100, freeSpace.Size);
    }
}
```

- [ ] **Step 2: Run test — expect FAIL**

Run: `dotnet test ScannerCore.Tests --filter DriveScanMetadataTests -v n`

Expected: FAIL — type not found

- [ ] **Step 3: Implement**

`ScannerCore/DriveScanMetadata.cs`:

```csharp
namespace ScannerCore;

public static class DriveScanMetadata
{
    public const string FreeSpaceName = "[Free space]";
    public const string InaccessibleName = "[Inaccessible]";
    public const int FreeSpaceIndex = 0;
    public const int InaccessibleIndex = 1;
    public const int SyntheticEntryCount = 2;

    public static FsItem GetInaccessibleEntry(FsItem driveRoot) =>
        driveRoot.Items![InaccessibleIndex];

    public static FsItem GetFreeSpaceEntry(FsItem driveRoot) =>
        driveRoot.Items![FreeSpaceIndex];
}
```

Update `DriveScanner.ScanDrive` to use `DriveScanMetadata.FreeSpaceName` and `DriveScanMetadata.InaccessibleName` instead of string literals.

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add ScannerCore/DriveScanMetadata.cs ScannerCore/DriveScanner.cs ScannerCore.Tests/DriveScanMetadataTests.cs
git commit -m "feat: add DriveScanMetadata for synthetic drive scan entries"
```

---

### Task 10: ScanProgress and IProgress

**Files:**
- Create: `ScannerCore/ScanProgress.cs`
- Modify: `ScannerCore/DriveScanner.cs`
- Modify: `ScannerUiWinForms/Form1.cs`
- Modify: `ScannerConsole/Program.cs`

- [ ] **Step 1: Add ScanProgress record**

```csharp
namespace ScannerCore;

public sealed record ScanProgress(
    string CurrentPath,
    long BytesScanned,
    float? PercentComplete,
    bool IsDriveScan);
```

- [ ] **Step 2: Add optional IProgress overloads**

Add to `DriveScanner`:

```csharp
public FsItem ScanDrive(string driveName, CancellationToken cancellationToken = default, IProgress<ScanProgress>? progress = null)
public FsItem ScanDirectory(string path, CancellationToken cancellationToken, IProgress<ScanProgress>? progress = null)
```

Refactor existing methods to call new overloads with `progress: null`. Report progress inside `ScanChildren` when `progress != null`, throttled to 300 ms via `Stopwatch`.

- [ ] **Step 3: Update Form1 to use Progress<ScanProgress>**

Replace `scanProgressTimer` tick body:

```csharp
private void OnScanProgress(ScanProgress p)
{
    if (InvokeRequired) { BeginInvoke(new Action<ScanProgress>(OnScanProgress), p); return; }
    if (p.PercentComplete.HasValue)
        scanProgressBar.Value = Math.Min((int)(p.PercentComplete.Value * 10), scanProgressBar.Maximum);
    SetStatusDetails(p.CurrentPath);
}
```

Pass `new Progress<ScanProgress>(OnScanProgress)` to `ScanDrive` / `ScanDirectory`. Remove `scanProgressTimer` start/stop for progress (keep timer field until Designer cleanup in Task 16).

- [ ] **Step 4: Update ScannerConsole**

Pass `IProgress<ScanProgress>` to Spectre status callback instead of polling `CurrentScanned`.

- [ ] **Step 5: Run tests + manual UI smoke**

Run: `dotnet test ScannerCore.Tests -c Release`

- [ ] **Step 6: Commit**

```bash
git add ScannerCore/ScanProgress.cs ScannerCore/DriveScanner.cs ScannerUiWinForms/Form1.cs ScannerConsole/Program.cs
git commit -m "feat: add IProgress ScanProgress reporting to DriveScanner"
```

---

# Phase 3: UI Architecture

### Task 11: ChartMapper extraction

**Files:**
- Create: `ScannerUiWinForms/ChartMapper.cs`
- Modify: `ScannerUiWinForms/Form1.cs`

- [ ] **Step 1: Create ChartMapper skeleton**

Move these from `Form1.cs` into `ChartMapper`:
- `PlaceholderTag` static field
- `LoadChartDataCollection`, `AddOrExtendPlaceHolder`, `ApplySliceStyle`
- `GetLevelBaseColor`, `GetChildShade`, `ColorFromHsb`
- `AlignDoughnuts`, `TryGetDataSeries`, `AddPoint`
- `_totals` dictionary — becomes instance state on `ChartMapper`

Public API:

```csharp
internal sealed class ChartMapper
{
    public void RefreshChart(Chart chart, FsItem root, long filterThreshold, bool includeFreeSpace);
}
```

- [ ] **Step 2: Update Form1.RefreshChart**

```csharp
private readonly ChartMapper _chartMapper = new();

private void RefreshChart()
{
    if (_scanRoot == null || _scanner == null) return;
    var percent = 0.0025f * filterThresholdComboBox.SelectedIndex;
    var includeFreeSpace = freeSpaceComboBox.SelectedIndex == 0;
    _filterThreshold = _scanner.GetDisplayThreshold(percent, includeFreeSpace);
    var chartRoot = includeFreeSpace ? _scanRoot : GetChartRootWithoutSyntheticEntries();
    _chartMapper.RefreshChart(usageChart, chartRoot, _filterThreshold, includeFreeSpace);
}
```

- [ ] **Step 3: Build and manually verify chart**

Run: `dotnet run --project ScannerUiWinForms/ScannerUiWinForms.csproj`

Scan a drive; confirm colors, placeholders, drill-down rings unchanged.

- [ ] **Step 4: Commit**

```bash
git add ScannerUiWinForms/ChartMapper.cs ScannerUiWinForms/Form1.cs
git commit -m "refactor: extract ChartMapper from Form1"
```

---

### Task 12: FileSystemActions and ScanSession

**Files:**
- Create: `ScannerUiWinForms/FileSystemActions.cs`
- Create: `ScannerUiWinForms/ScanSession.cs`
- Modify: `ScannerUiWinForms/Form1.cs`

- [ ] **Step 1: Create FileSystemActions**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace ScannerUiWinForms;

internal static class FileSystemActions
{
    public static void ShowInExplorer(string path)
    {
        Process.Start("explorer.exe", "/select,\"" + path + "\"");
    }

    public static bool TryDelete(string path, bool permanent, out string? error)
    {
        error = null;

        try
        {
            if (File.Exists(path))
            {
                if (permanent)
                    File.Delete(path);
                else
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                return true;
            }

            if (Directory.Exists(path))
            {
                if (permanent)
                    Directory.Delete(path, recursive: true);
                else
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                return true;
            }

            error = "Object is already unavailable.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
```

No package reference is required for `Microsoft.VisualBasic.FileIO` on .NET 10; if the build proves otherwise, add the package version to `Directory.Packages.props` and keep `ScannerUiWinForms.csproj` versionless.

- [ ] **Step 2: Create ScanSession**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;

namespace ScannerUiWinForms;

internal sealed class ScanSession
{
    public string? LastTarget { get; private set; }
    public bool IsDriveScan { get; private set; }
    public DriveScanner Scanner { get; private set; } = new DriveScanner();
    public FsItem? Root { get; private set; }

    public async Task<FsItem> RunAsync(
        string target,
        bool isDrive,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress)
    {
        LastTarget = target;
        IsDriveScan = isDrive;
        Scanner = new DriveScanner();

        Root = await Task.Run(
            () => isDrive
                ? Scanner.ScanDrive(target, cancellationToken, progress)
                : Scanner.ScanDirectory(target, cancellationToken, progress),
            cancellationToken);

        return Root;
    }
}
```

- [ ] **Step 3: Wire Form1 to ScanSession and FileSystemActions**

Replace direct `_scanner` field usage; update inaccessible label:

```csharp
inaccessibleTotalSizeLabel.Text = Humanize.Size(
    DriveScanMetadata.GetInaccessibleEntry(_scanRoot).Size);
```

- [ ] **Step 4: Commit**

```bash
git add ScannerUiWinForms/
git commit -m "refactor: add ScanSession and FileSystemActions"
```

---

# Phase 4: UX Improvements

### Task 13: Toolbar — Browse and Rescan

**Files:**
- Modify: `ScannerUiWinForms/Form1.Designer.cs`
- Modify: `ScannerUiWinForms/Form1.cs`

- [ ] **Step 1: Add controls in Designer**

Add `browseFolderButtonHost`, `rescanButtonHost`, and a separator as the first static toolbar items, before `scanProgressBar`. Keep drive buttons after these static controls by changing `Form1_Load` to insert drives at index `3`.

Designer pattern matches existing `cancelScanButtonHost`. `rescanButton` starts disabled.

- [ ] **Step 2: Implement handlers**

```csharp
private void browseFolderButton_Click(object sender, EventArgs e)
{
    using var dialog = new FolderBrowserDialog
    {
        Description = "Select a folder to scan",
        UseDescriptionForTitle = true
    };
    if (dialog.ShowDialog() != DialogResult.OK) return;
    StartScan(dialog.SelectedPath, isDrive: false);
}

private void rescanButton_Click(object sender, EventArgs e)
{
    if (_session.LastTarget == null) return;
    StartScan(_session.LastTarget, _session.IsDriveScan);
}
```

Extract shared `StartScan` from `LoadDrive` logic.

Update `Form1_Load` drive insertion from the current static-count calculation to this concrete placement:

```csharp
private const int DriveButtonInsertIndex = 3;

private void Form1_Load(object sender, EventArgs e)
{
    var driveInsertIndex = DriveButtonInsertIndex;
    foreach (var driveInfo in DriveInfo.GetDrives().Where(d => d.IsReady))
    {
        var driveButton = new Button { Text = driveInfo.Name };
        driveButton.Click += LoadDrive;
        var driveHost = CreateToolbarButtonHost(driveButton, autoSize: true);
        mainToolStrip.Items.Insert(driveInsertIndex++, driveHost);
    }

    if (driveInsertIndex > DriveButtonInsertIndex)
    {
        mainToolStrip.Items.Insert(driveInsertIndex,
            new ToolStripSeparator { Margin = new Padding(8, 2, 8, 2) });
    }

    freeSpaceComboBox.SelectedIndex = 1;
    filterThresholdComboBox.SelectedIndex = 4;
    mainSplitContainer.SplitterDistance = mainSplitContainer.Width - LogicalToDeviceUnits(mainSplitContainer.Width - mainSplitContainer.SplitterDistance);
}
```

- [ ] **Step 3: Enable Rescan after successful scan**

Set `rescanButton.Enabled = true` when scan completes.

- [ ] **Step 4: Add keyboard shortcuts in Form1**

```csharp
protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
{
    if (keyData == Keys.F5) { rescanButton.PerformClick(); return true; }
    if (keyData == Keys.Escape && IsScanning) { cancelScanButton.PerformClick(); return true; }
    if (keyData == (Keys.Control | Keys.O)) { browseFolderButton.PerformClick(); return true; }
    return base.ProcessCmdKey(ref msg, keyData);
}
```

- [ ] **Step 5: Manual test browse + rescan + shortcuts**

- [ ] **Step 6: Commit**

```bash
git add ScannerUiWinForms/
git commit -m "feat: add folder browse, rescan, and keyboard shortcuts"
```

---

### Task 14: User settings persistence

**Files:**
- Create: `ScannerUiWinForms/UserSettings.cs`
- Modify: `ScannerUiWinForms/Form1.cs`

- [ ] **Step 1: Create UserSettings**

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace ScannerUiWinForms;

internal sealed class UserSettings
{
    public int FilterIndex { get; set; } = 4;
    public int FreeSpaceIndex { get; set; } = 1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int SplitterDistance { get; set; }
    public bool InaccessiblePaneCollapsed { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "SizeScanner", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
        }
        catch { return new UserSettings(); }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 2: Load in Form1_Load, save in FormClosing and DisplayOptionsChanged**

Apply window size only when `WindowWidth > 0`.

- [ ] **Step 3: Manual test — change filter, restart app, confirm restored**

- [ ] **Step 4: Commit**

```bash
git add ScannerUiWinForms/UserSettings.cs ScannerUiWinForms/Form1.cs
git commit -m "feat: persist filter and layout settings to AppData"
```

---

### Task 15: Recycle Bin delete

**Files:**
- Modify: `ScannerUiWinForms/FileSystemActions.cs`
- Modify: `ScannerUiWinForms/Form1.Designer.cs` — add "Delete permanently" menu item
- Modify: `ScannerUiWinForms/Form1.cs`

- [ ] **Step 1: Default delete sends to Recycle Bin**

```csharp
Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
    path,
    UIOption.OnlyErrorDialogs,
    RecycleOption.SendToRecycleBin);
```

For directories, `DeleteDirectory` with same recycle option.

- [ ] **Step 2: Add permanent delete menu item**

`deletePermanentlyMenuItem` — calls `TryDelete(path, permanent: true, ...)`.

- [ ] **Step 3: Update confirmation text**

"Move to Recycle Bin" vs "Permanently delete" with appropriate warnings.

- [ ] **Step 4: Manual test both delete paths**

- [ ] **Step 5: Commit**

```bash
git add ScannerUiWinForms/
git commit -m "feat: send default delete to Recycle Bin with permanent override"
```

---

### Task 16: Remove obsolete progress timer

**Files:**
- Modify: `ScannerUiWinForms/Form1.Designer.cs`
- Modify: `ScannerUiWinForms/Form1.cs`

- [ ] **Step 1: Remove scanProgressTimer field, initialization, and Tick handler**

Progress now driven entirely by `IProgress<ScanProgress>`.

- [ ] **Step 2: Build and smoke test**

- [ ] **Step 3: Commit**

```bash
git add ScannerUiWinForms/
git commit -m "refactor: remove polling progress timer"
```

---

# Phase 5: Release Engineering

### Task 17: License and versioning

**Files:**
- Create: `LICENSE`
- Modify: `Directory.Packages.props`
- Modify: `ScannerUiWinForms/ScannerUiWinForms.csproj`

- [ ] **Step 1: Add MIT LICENSE**

Standard MIT template with copyright holder name from git log or "SizeScanner contributors".

- [ ] **Step 2: Add MinVer**

`Directory.Packages.props`:

```xml
<PackageVersion Include="MinVer" Version="6.0.0" />
```

`ScannerUiWinForms.csproj`:

```xml
<PackageReference Include="MinVer">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

- [ ] **Step 3: Commit**

```bash
git add LICENSE Directory.Packages.props ScannerUiWinForms/ScannerUiWinForms.csproj
git commit -m "chore: add MIT license and MinVer versioning"
```

---

### Task 18: Documentation update

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Add sections to README**

- Running tests: `dotnet test ScannerCore.Tests`
- CI requirements: Windows runner
- Settings file location
- New toolbar features (Browse, Rescan, shortcuts)
- License

- [ ] **Step 2: Update AGENTS.md**

- Document `ScannerCore.Tests`
- Document `DriveScanMetadata`, `ScanProgress`
- Document `ChartMapper`, `UserSettings`
- CI and publish notes

- [ ] **Step 3: Commit**

```bash
git add README.md AGENTS.md
git commit -m "docs: document tests, CI, settings, and UX features"
```

---

## Manual validation checklist (final)

- [ ] `dotnet test ScannerCore.Tests -c Release` — all pass
- [ ] `dotnet build SizeScanner.sln -c Release` — 0 errors
- [ ] Drive scan — progress bar, chart, inaccessible pane
- [ ] Browse folder — directory scan without free-space slice
- [ ] F5 rescan — same target
- [ ] Settings survive restart
- [ ] Delete → Recycle Bin; permanent delete menu works
- [ ] Cancel (Esc) mid-scan — clean state
- [ ] Relaunch as administrator — still visible when needed
- [ ] `dotnet publish` produces runnable folder

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-11-modernization.md`.

**Two execution options:**

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks
2. **Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
