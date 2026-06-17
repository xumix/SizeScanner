# Avalonia UI Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a new, modern Avalonia UI front-end (`SizeScanner.Avalonia`) for SizeScanner that reaches feature parity with the existing WinForms UI, rendering the nested disk-usage chart with an Avalonia 12 custom sunburst control — without deleting or altering `ScannerUiWinForms`.

**Architecture:** A new Avalonia desktop app (MVVM, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection) references the unchanged `ScannerCore`. All platform/IO behaviour sits behind interfaces (SOLID DIP) so view-models and the chart builder are unit-testable. The nested disk-usage chart is a true sunburst: `SunburstChartBuilder` produces immutable segment/layout data (`SunburstSegment` with start/sweep angles and ring indexes), and an Avalonia 12 custom control renders annular sectors and performs polar hit-testing.

**Tech Stack:** .NET 10 (`net10.0-windows`, win-x64), Avalonia 12.0.0 (Fluent theme, Inter font), CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, xUnit v3. Central Package Management via `Directory.Packages.props`.

---

## Locked Design Decisions

These were chosen to keep the migration moving; change them here before execution if you disagree.

1. **Keep WinForms project untouched.** `ScannerUiWinForms` stays in the solution and remains the release artifact for now. The new project is additive.
2. **Windows-only.** `ScannerCore` uses Win32 P/Invoke and the UI needs Win32 features (recycle bin, `runas`, registry, `explorer /select`). New project targets `net10.0-windows`, `win-x64`, like the others. Avalonia runs fine on this TFM.
3. **Full feature parity, modernized layout.** Replicate every WinForms capability (drive buttons, browse, rescan, cancel, progress, free-space toggle, filter threshold, nested chart, hover status path, click-to-scope, go-up/go-to-root, right-click context menu with Open/Delete/Delete-permanently, inaccessible pane with total + relaunch-as-admin, status bar, keyboard shortcuts, settings persistence). Use a clean Fluent-themed layout rather than a pixel clone. The WinForms floating path tooltip is intentionally deferred; do not port the custom tooltip in this plan.
4. **Separate settings file.** Persist to `%AppData%\SizeScanner\settings.avalonia.json` so the two UIs do not clobber each other's window geometry.
5. **MVVM + DI + interfaces.** Each external concern (scan, settings, filesystem actions, elevation, drive enumeration, folder picker, dialogs) is an interface with a Windows implementation, composed via `Microsoft.Extensions.DependencyInjection`.
6. **New test project.** `SizeScanner.Avalonia.Tests` (xUnit v3) covers the pure logic: color palette, sunburst layout/hit-testing, settings store, scan service, and the navigation/status-path logic in the view-models. Views are verified by build + manual run.
7. **Chart orientation parity.** WinForms draws level-0 (top-level children) on the OUTER ring. The sunburst layout preserves that by defining `RingIndex == Level`, where ring index `0` is the outermost ring; deeper children increment the ring index and move inward toward the center.

---

## File Structure

New project `SizeScanner.Avalonia/` (root namespace `SizeScanner.Avalonia`):

| File | Responsibility |
|------|----------------|
| `SizeScanner.Avalonia.csproj` | SDK project, packages, manifest, icon |
| `Program.cs` | Entry point, `BuildAvaloniaApp` |
| `App.axaml` / `App.axaml.cs` | Theme/fonts, DI bootstrap, main window wiring |
| `app.manifest` | UAC `asInvoker` (copied from WinForms) |
| `ViewLocator.cs` | VM → View resolution |
| `Abstractions/I*.cs` | Service interfaces (DIP seams) |
| `Services/*.cs` | Windows/Avalonia implementations |
| `Models/UserSettings.cs`, `DriveItem.cs`, `FilterOption.cs` | DTOs / option models |
| `Charting/SliceColorPalette.cs` | HSB color logic (ported) |
| `Charting/SunburstChartBuilder.cs` | Builds true sunburst segment layout from `FsItem` trees |
| `Charting/SunburstChart.cs`, `SunburstSegment.cs`, `SunburstHitTest.cs` | Builder result types + polar hit-testing |
| `ViewModels/ViewModelBase.cs` | `ObservableObject` base |
| `ViewModels/ChartViewModel.cs` | Chart layout, scope nav, hover status path, slice actions |
| `ViewModels/MainWindowViewModel.cs` | Scan orchestration, options, inaccessible pane, settings |
| `Views/MainWindow.axaml` / `.cs` | Window shell: toolbar, split, status bar, pane |
| `Views/ChartView.axaml` / `.cs`, `Views/SunburstChartControl.cs` | Sunburst control + nav bar + pointer handling |
| `Converters/*.cs` | Small XAML converters as needed |
| `Assets/main.ico` | App icon (reuse WinForms icon) |

New test project `SizeScanner.Avalonia.Tests/` (root namespace `SizeScanner.Avalonia.Tests`):

| File | Responsibility |
|------|----------------|
| `SizeScanner.Avalonia.Tests.csproj` | xUnit v3 test project |
| `TestTree.cs` | Helper to build `FsItem` trees in-memory |
| `TempDir.cs` | Temp directory helper (mirrors `ScannerCore.Tests/TemporaryDirectory.cs`) |
| `SliceColorPaletteTests.cs`, `SunburstChartBuilderTests.cs` | Charting tests |
| `JsonSettingsStoreTests.cs`, `ScanServiceTests.cs` | Service tests |
| `ChartViewModelTests.cs`, `MainWindowViewModelTests.cs` | View-model tests |

---

## Task 1: Scaffold the Avalonia project and add packages

**Files:**
- Create: `SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`
- Create: `SizeScanner.Avalonia/app.manifest`
- Create: `SizeScanner.Avalonia/Program.cs`
- Create: `SizeScanner.Avalonia/App.axaml`, `SizeScanner.Avalonia/App.axaml.cs`
- Create: `SizeScanner.Avalonia/ViewLocator.cs`
- Create: `SizeScanner.Avalonia/ViewModels/ViewModelBase.cs`
- Create: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` (temporary stub, replaced in Task 10)
- Create: `SizeScanner.Avalonia/Views/MainWindow.axaml`, `.axaml.cs` (temporary stub, replaced in Task 12)
- Create: `SizeScanner.Avalonia/Assets/main.ico` (copy of `ScannerUiWinForms/icons/main.ico`)
- Modify: `Directory.Packages.props`
- Modify: `SizeScanner.slnx`

- [ ] **Step 1: Copy the app icon**

```powershell
New-Item -ItemType Directory -Force -Path SizeScanner.Avalonia\Assets | Out-Null
Copy-Item ScannerUiWinForms\icons\main.ico SizeScanner.Avalonia\Assets\main.ico
```
(If `ScannerUiWinForms/icons/main.ico` is missing, skip; the csproj `ApplicationIcon` line can be removed.)

- [ ] **Step 2: Create the project file**

`SizeScanner.Avalonia/SizeScanner.Avalonia.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <SupportedOSPlatform>windows</SupportedOSPlatform>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <RootNamespace>SizeScanner.Avalonia</RootNamespace>
    <AssemblyName>SizeScanner.Avalonia</AssemblyName>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <ApplicationIcon>Assets\main.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaResource Include="Assets\**" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ScannerCore\ScannerCore.csproj" />
    <PackageReference Include="Avalonia" />
    <PackageReference Include="Avalonia.Desktop" />
    <PackageReference Include="Avalonia.Themes.Fluent" />
    <PackageReference Include="Avalonia.Fonts.Inter" />
    <PackageReference Include="Avalonia.Diagnostics" Condition="'$(Configuration)' == 'Debug'" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add package versions to Central Package Management**

Run these so NuGet writes the current versions into `Directory.Packages.props` (CPM is enabled, so `dotnet add package` records the `<PackageVersion>` there):

```powershell
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package Avalonia
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package Avalonia.Desktop
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package Avalonia.Themes.Fluent
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package Avalonia.Fonts.Inter
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package Avalonia.Diagnostics
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package CommunityToolkit.Mvvm
dotnet add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj package Microsoft.Extensions.DependencyInjection
```

Expected result in `Directory.Packages.props` (approximate; use what NuGet resolves):
```xml
<PackageVersion Include="Avalonia" Version="12.0.0" />
<PackageVersion Include="Avalonia.Desktop" Version="12.0.0" />
<PackageVersion Include="Avalonia.Themes.Fluent" Version="12.0.0" />
<PackageVersion Include="Avalonia.Fonts.Inter" Version="12.0.0" />
<PackageVersion Include="Avalonia.Diagnostics" Version="12.0.0" />
<PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.1" />
<PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
```

> **Known caveat:** CommunityToolkit.Mvvm 8.4.0's source generator fails to build under the .NET 10 default `LangVersion` (C# 14). Use the newest published version. If the build emits MVVM source-generator errors, add `<LangVersion>13.0</LangVersion>` to `Directory.Build.props` (it currently sets none) as a fallback, or upgrade the package.

- [ ] **Step 4: Create `app.manifest`**

Copy `ScannerUiWinForms/app.manifest` verbatim to `SizeScanner.Avalonia/app.manifest` (UAC `asInvoker`, Windows 10/11 compat, common-controls dependency).

- [ ] **Step 5: Create `Program.cs`**

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;

namespace SizeScanner.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

- [ ] **Step 6: Create `ViewModels/ViewModelBase.cs`**

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace SizeScanner.Avalonia.ViewModels;

public abstract class ViewModelBase : ObservableObject;
```

- [ ] **Step 7: Create a temporary `MainWindowViewModel` stub**

`SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public string Greeting => "SizeScanner (Avalonia)";
}
```

- [ ] **Step 8: Create `ViewLocator.cs`**

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SizeScanner.Avalonia.ViewModels;

namespace SizeScanner.Avalonia;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;
        var name = param.GetType().FullName!.Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
```

- [ ] **Step 9: Create `App.axaml`**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="SizeScanner.Avalonia.App"
             xmlns:local="using:SizeScanner.Avalonia"
             RequestedThemeVariant="Default">
    <Application.DataTemplates>
        <local:ViewLocator />
    </Application.DataTemplates>
    <Application.Styles>
        <FluentTheme />
    </Application.Styles>
</Application>
```

- [ ] **Step 10: Create `App.axaml.cs` (DI bootstrap; services registered in Task 13)**

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SizeScanner.Avalonia.ViewModels;
using SizeScanner.Avalonia.Views;

namespace SizeScanner.Avalonia;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();
        foreach (var plugin in toRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }
}
```

Add `using System.Linq;` at the top.

- [ ] **Step 11: Create a temporary `MainWindow`**

`SizeScanner.Avalonia/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:SizeScanner.Avalonia.ViewModels"
        x:Class="SizeScanner.Avalonia.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Icon="/Assets/main.ico"
        Width="1100" Height="720"
        Title="SizeScanner">
    <TextBlock Text="{Binding Greeting}" HorizontalAlignment="Center" VerticalAlignment="Center" />
</Window>
```

`SizeScanner.Avalonia/Views/MainWindow.axaml.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;

namespace SizeScanner.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
```

- [ ] **Step 12: Register the project in the solution**

```powershell
dotnet sln SizeScanner.slnx add SizeScanner.Avalonia\SizeScanner.Avalonia.csproj
```

- [ ] **Step 13: Build and run to confirm the blank window**

Run: `dotnet build SizeScanner.Avalonia\SizeScanner.Avalonia.csproj -c Debug`
Expected: build succeeds.
Run: `dotnet run --project SizeScanner.Avalonia\SizeScanner.Avalonia.csproj`
Expected: a Fluent-themed window titled "SizeScanner" showing the greeting text. Close it.

- [ ] **Step 14: Commit**

```powershell
git add SizeScanner.Avalonia Directory.Packages.props SizeScanner.slnx
git commit -m "feat: scaffold SizeScanner.Avalonia project"
```

---

## Task 2: Create the Avalonia test project

**Files:**
- Create: `SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`
- Create: `SizeScanner.Avalonia.Tests/TestTree.cs`
- Create: `SizeScanner.Avalonia.Tests/TempDir.cs`
- Create: `SizeScanner.Avalonia.Tests/SmokeTests.cs`
- Modify: `SizeScanner.slnx`

- [ ] **Step 1: Create the test project file**

`SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <RootNamespace>SizeScanner.Avalonia.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\SizeScanner.Avalonia\SizeScanner.Avalonia.csproj" />
    <ProjectReference Include="..\ScannerCore\ScannerCore.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
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

- [ ] **Step 2: Create the `FsItem` tree helper**

`SizeScanner.Avalonia.Tests/TestTree.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using ScannerCore;

namespace SizeScanner.Avalonia.Tests;

internal static class TestTree
{
    public static FsItem File(string name, long size) => new(name, size, isDir: false);

    public static FsItem Dir(string name, params FsItem[] children)
    {
        long size = 0;
        foreach (var c in children) size += c.Size;
        var dir = new FsItem(name, size, isDir: true);
        AttachChildren(dir, children);
        return dir;
    }

    private static void AttachChildren(FsItem parent, IReadOnlyList<FsItem> children)
    {
        var list = new List<FsItem>(children);
        parent.Items = list;
        foreach (var child in list)
            SetParent(child, parent);
    }

    // FsItem.Parent has an internal setter; tests assign via reflection-free helper
    // by re-using AttachChildren semantics. Parent is needed for path/scope tests.
    private static void SetParent(FsItem child, FsItem parent)
    {
        typeof(FsItem).GetProperty(nameof(FsItem.Parent))!.SetValue(child, parent);
    }
}
```

> **Note:** `FsItem.AttachChildren` is `internal` to `ScannerCore`; tests cannot call it. `Parent` has an `internal set`. The reflection helper above keeps tests independent of `ScannerCore` internals. If you prefer, add `[assembly: InternalsVisibleTo("SizeScanner.Avalonia.Tests")]` to `ScannerCore` instead and call `AttachChildren` — but reflection avoids modifying `ScannerCore`.

- [ ] **Step 3: Create the temp-directory helper**

`SizeScanner.Avalonia.Tests/TempDir.cs` — copy the body of `ScannerCore.Tests/TemporaryDirectory.cs`, renaming the class to `TempDir` and the namespace to `SizeScanner.Avalonia.Tests`.

- [ ] **Step 4: Add a smoke test**

`SizeScanner.Avalonia.Tests/SmokeTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Tree_helper_sums_child_sizes()
    {
        var root = TestTree.Dir("root",
            TestTree.File("a", 100),
            TestTree.File("b", 50));

        Assert.Equal(150, root.Size);
        Assert.Equal(2, root.Items!.Count);
        Assert.Same(root, root.Items![0].Parent);
    }
}
```

- [ ] **Step 5: Add the test project to the solution and run**

```powershell
dotnet sln SizeScanner.slnx add SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj
dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj -c Debug
```
Expected: 1 passing test.

- [ ] **Step 6: Commit**

```powershell
git add SizeScanner.Avalonia.Tests SizeScanner.slnx
git commit -m "test: add SizeScanner.Avalonia.Tests project"
```

---

## Task 3: Settings model and store (TDD)

**Files:**
- Create: `SizeScanner.Avalonia/Models/UserSettings.cs`
- Create: `SizeScanner.Avalonia/Abstractions/ISettingsStore.cs`
- Create: `SizeScanner.Avalonia/Services/JsonSettingsStore.cs`
- Test: `SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`

- [ ] **Step 1: Write the failing test**

`SizeScanner.Avalonia.Tests/JsonSettingsStoreTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using SizeScanner.Avalonia.Models;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        using var dir = new TempDir();
        var store = new JsonSettingsStore(Path.Combine(dir.Path, "missing.json"));

        var settings = store.Load();

        Assert.Equal(4, settings.FilterIndex);
        Assert.Equal(1, settings.FreeSpaceIndex);
    }

    [Fact]
    public void Save_then_load_roundtrips_values()
    {
        using var dir = new TempDir();
        var store = new JsonSettingsStore(Path.Combine(dir.Path, "settings.json"));

        store.Save(new UserSettings
        {
            FilterIndex = 2,
            FreeSpaceIndex = 0,
            WindowWidth = 1234,
            WindowHeight = 567,
            SplitterDistance = 800,
            InaccessiblePaneCollapsed = true
        });

        var loaded = store.Load();

        Assert.Equal(2, loaded.FilterIndex);
        Assert.Equal(0, loaded.FreeSpaceIndex);
        Assert.Equal(1234, loaded.WindowWidth);
        Assert.Equal(567, loaded.WindowHeight);
        Assert.Equal(800, loaded.SplitterDistance);
        Assert.True(loaded.InaccessiblePaneCollapsed);
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ not valid json");
        var store = new JsonSettingsStore(path);

        var settings = store.Load();

        Assert.Equal(4, settings.FilterIndex);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — `UserSettings` / `JsonSettingsStore` do not exist (compile error).

- [ ] **Step 3: Create the settings model**

`SizeScanner.Avalonia/Models/UserSettings.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Models;

public sealed class UserSettings
{
    public int FilterIndex { get; set; } = 4;
    public int FreeSpaceIndex { get; set; } = 1;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int SplitterDistance { get; set; }
    public bool InaccessiblePaneCollapsed { get; set; }
}
```

- [ ] **Step 4: Create the store interface**

`SizeScanner.Avalonia/Abstractions/ISettingsStore.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Abstractions;

public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}
```

- [ ] **Step 5: Implement the JSON store**

`SizeScanner.Avalonia/Services/JsonSettingsStore.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;

    public JsonSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SizeScanner", "settings.avalonia.json"))
    {
    }

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_filePath)) ?? new UserSettings();
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add SizeScanner.Avalonia\Models SizeScanner.Avalonia\Abstractions SizeScanner.Avalonia\Services SizeScanner.Avalonia.Tests\JsonSettingsStoreTests.cs
git commit -m "feat: add UserSettings model and JSON settings store"
```

---

## Task 4: Scan service (TDD)

**Files:**
- Create: `SizeScanner.Avalonia/Abstractions/IScanService.cs`
- Create: `SizeScanner.Avalonia/Services/ScanService.cs`
- Test: `SizeScanner.Avalonia.Tests/ScanServiceTests.cs`

- [ ] **Step 1: Write the failing test**

`SizeScanner.Avalonia.Tests/ScanServiceTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ScanServiceTests
{
    [Fact]
    public async Task RunAsync_directory_scan_builds_tree()
    {
        using var dir = new TempDir();
        dir.CreateFile("a.bin", 2048);
        dir.CreateFile("sub\\b.bin", 1024);

        var service = new ScanService();
        var progress = new Progress<ScanProgress>(_ => { });

        var root = await service.RunAsync(dir.Path, isDrive: false, CancellationToken.None, progress);

        Assert.False(service.IsDriveScan);
        Assert.Equal(dir.Path, service.LastTarget);
        Assert.NotNull(root.Items);
        Assert.True(root.Size >= 3072);
    }

    [Fact]
    public async Task RunAsync_honors_cancellation()
    {
        using var dir = new TempDir();
        dir.CreateFile("a.bin", 16);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = new ScanService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(dir.Path, isDrive: false, cts.Token, new Progress<ScanProgress>(_ => { })));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — `ScanService` does not exist.

- [ ] **Step 3: Create the interface**

`SizeScanner.Avalonia/Abstractions/IScanService.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;

namespace SizeScanner.Avalonia.Abstractions;

public interface IScanService
{
    string LastTarget { get; }
    bool IsDriveScan { get; }
    DriveScanner Scanner { get; }

    Task<FsItem> RunAsync(string target, bool isDrive, CancellationToken cancellationToken, IProgress<ScanProgress> progress);
}
```

- [ ] **Step 4: Implement the service (port of `ScanSession`)**

`SizeScanner.Avalonia/Services/ScanService.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class ScanService : IScanService
{
    public string LastTarget { get; private set; } = string.Empty;
    public bool IsDriveScan { get; private set; }
    public DriveScanner Scanner { get; private set; } = new();

    public async Task<FsItem> RunAsync(
        string target,
        bool isDrive,
        CancellationToken cancellationToken,
        IProgress<ScanProgress> progress)
    {
        LastTarget = target;
        IsDriveScan = isDrive;
        Scanner = new DriveScanner();

        return await Task.Run(
            () => isDrive
                ? Scanner.ScanDrive(target, cancellationToken, progress)
                : Scanner.ScanDirectory(target, cancellationToken, progress),
            cancellationToken);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add SizeScanner.Avalonia\Abstractions\IScanService.cs SizeScanner.Avalonia\Services\ScanService.cs SizeScanner.Avalonia.Tests\ScanServiceTests.cs
git commit -m "feat: add scan service wrapping DriveScanner"
```

---

## Task 5: Platform services — filesystem actions, elevation, drives

**Files:**
- Create: `SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`
- Create: `SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`
- Create: `SizeScanner.Avalonia/Abstractions/IElevationService.cs`
- Create: `SizeScanner.Avalonia/Services/WindowsElevationService.cs`
- Create: `SizeScanner.Avalonia/Abstractions/IDriveProvider.cs`
- Create: `SizeScanner.Avalonia/Models/DriveItem.cs`
- Create: `SizeScanner.Avalonia/Services/DriveProvider.cs`
- Test: `SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`

- [ ] **Step 1: Write the failing test (permanent delete only — safe on temp files)**

`SizeScanner.Avalonia.Tests/WindowsFileSystemActionsTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using SizeScanner.Avalonia.Services;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class WindowsFileSystemActionsTests
{
    [Fact]
    public void TryDelete_permanent_removes_file_and_reports_success()
    {
        using var dir = new TempDir();
        var file = dir.CreateFile("doomed.bin", 16);
        var actions = new WindowsFileSystemActions();

        var ok = actions.TryDelete(file, permanent: true, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void TryDelete_missing_path_reports_failure()
    {
        using var dir = new TempDir();
        var actions = new WindowsFileSystemActions();

        var ok = actions.TryDelete(Path.Combine(dir.Path, "nope.bin"), permanent: true, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — `WindowsFileSystemActions` does not exist.

- [ ] **Step 3: Create `IFileSystemActions` and the Windows implementation (port of `FileSystemActions`)**

`SizeScanner.Avalonia/Abstractions/IFileSystemActions.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Abstractions;

public interface IFileSystemActions
{
    void ShowInExplorer(string path);
    bool TryDelete(string path, bool permanent, out string? error);
}
```

`SizeScanner.Avalonia/Services/WindowsFileSystemActions.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.VisualBasic.FileIO;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemActions : IFileSystemActions
{
    public void ShowInExplorer(string path)
    {
        Process.Start("explorer.exe", "/select,\"" + path + "\"");
    }

    public bool TryDelete(string path, bool permanent, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(path))
            {
                if (permanent) File.Delete(path);
                else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return true;
            }

            if (Directory.Exists(path))
            {
                if (permanent) Directory.Delete(path, recursive: true);
                else FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
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

> `Microsoft.VisualBasic.FileIO` is part of the Windows desktop framework and available on `net10.0-windows` without an extra package.

- [ ] **Step 4: Create `IElevationService` and the Windows implementation (port of relaunch-as-admin)**

`SizeScanner.Avalonia/Abstractions/IElevationService.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Abstractions;

public interface IElevationService
{
    bool IsRunningAsAdministrator();

    /// <summary>Relaunches elevated. Returns false if the user cancelled the UAC prompt.</summary>
    bool TryRelaunchAsAdministrator(out string? error);
}
```

`SizeScanner.Avalonia/Services/WindowsElevationService.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsElevationService : IElevationService
{
    public bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool TryRelaunchAsAdministrator(out string? error)
    {
        error = null;
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            error = "Could not determine the application path.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // user cancelled UAC
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
```

- [ ] **Step 5: Create the drive provider**

`SizeScanner.Avalonia/Models/DriveItem.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Models;

/// <param name="Root">Two-character drive root, e.g. "C:".</param>
/// <param name="DisplayName">Label shown on the toolbar button, e.g. "C:\".</param>
public sealed record DriveItem(string Root, string DisplayName);
```

`SizeScanner.Avalonia/Abstractions/IDriveProvider.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Abstractions;

public interface IDriveProvider
{
    IReadOnlyList<DriveItem> GetReadyDrives();
}
```

`SizeScanner.Avalonia/Services/DriveProvider.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.IO;
using System.Linq;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.Services;

public sealed class DriveProvider : IDriveProvider
{
    public IReadOnlyList<DriveItem> GetReadyDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new DriveItem(d.Name[..2], d.Name))
            .ToList();
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add SizeScanner.Avalonia\Abstractions SizeScanner.Avalonia\Services SizeScanner.Avalonia\Models SizeScanner.Avalonia.Tests\WindowsFileSystemActionsTests.cs
git commit -m "feat: add filesystem, elevation, and drive services"
```

---

## Task 6: Avalonia UI services — folder picker and dialogs

These wrap Avalonia's `StorageProvider` and a modal message dialog. They are UI-bound (no unit tests); verified later by manual run.

**Files:**
- Create: `SizeScanner.Avalonia/Abstractions/IFolderPicker.cs`
- Create: `SizeScanner.Avalonia/Services/AvaloniaFolderPicker.cs`
- Create: `SizeScanner.Avalonia/Abstractions/IDialogService.cs`
- Create: `SizeScanner.Avalonia/Services/AvaloniaDialogService.cs`
- Create: `SizeScanner.Avalonia/Abstractions/ITopLevelProvider.cs`
- Create: `SizeScanner.Avalonia/Services/TopLevelProvider.cs`

- [ ] **Step 1: Create a top-level provider (so services can reach the active window)**

`SizeScanner.Avalonia/Abstractions/ITopLevelProvider.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;

namespace SizeScanner.Avalonia.Abstractions;

public interface ITopLevelProvider
{
    TopLevel? TopLevel { get; }
    void Register(TopLevel topLevel);
}
```

`SizeScanner.Avalonia/Services/TopLevelProvider.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class TopLevelProvider : ITopLevelProvider
{
    public TopLevel? TopLevel { get; private set; }
    public void Register(TopLevel topLevel) => TopLevel = topLevel;
}
```

- [ ] **Step 2: Create the folder picker**

`SizeScanner.Avalonia/Abstractions/IFolderPicker.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;

namespace SizeScanner.Avalonia.Abstractions;

public interface IFolderPicker
{
    /// <summary>Returns the selected folder path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
```

`SizeScanner.Avalonia/Services/AvaloniaFolderPicker.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class AvaloniaFolderPicker : IFolderPicker
{
    private readonly ITopLevelProvider _topLevel;

    public AvaloniaFolderPicker(ITopLevelProvider topLevel) => _topLevel = topLevel;

    public async Task<string?> PickFolderAsync(string title)
    {
        var top = _topLevel.TopLevel;
        if (top is null) return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        return folder?.TryGetLocalPath();
    }
}
```

- [ ] **Step 3: Create the dialog service (confirm + info)**

`SizeScanner.Avalonia/Abstractions/IDialogService.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;

namespace SizeScanner.Avalonia.Abstractions;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
}
```

`SizeScanner.Avalonia/Services/AvaloniaDialogService.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using SizeScanner.Avalonia.Abstractions;

namespace SizeScanner.Avalonia.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private readonly ITopLevelProvider _topLevel;

    public AvaloniaDialogService(ITopLevelProvider topLevel) => _topLevel = topLevel;

    public Task<bool> ConfirmAsync(string title, string message) => ShowAsync(title, message, confirm: true);

    public async Task ShowInfoAsync(string title, string message) => await ShowAsync(title, message, confirm: false);

    private async Task<bool> ShowAsync(string title, string message, bool confirm)
    {
        if (_topLevel.TopLevel is not Window owner) return false;

        var result = false;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MinWidth = 360
        };

        var ok = new Button { Content = confirm ? "Yes" : "OK", IsDefault = true, MinWidth = 88 };
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        buttons.Children.Add(ok);

        if (confirm)
        {
            var cancel = new Button { Content = "No", IsCancel = true, MinWidth = 88 };
            cancel.Click += (_, _) => { result = false; dialog.Close(); };
            buttons.Children.Add(cancel);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 },
                buttons
            }
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build SizeScanner.Avalonia\SizeScanner.Avalonia.csproj`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add SizeScanner.Avalonia\Abstractions SizeScanner.Avalonia\Services
git commit -m "feat: add Avalonia folder picker and dialog services"
```

---

## Task 7: Slice color palette (TDD)

Port the HSB logic from `ChartMapper` to a pure, testable class returning `Avalonia.Media.Color`.

**Files:**
- Create: `SizeScanner.Avalonia/Charting/SliceColorPalette.cs`
- Test: `SizeScanner.Avalonia.Tests/SliceColorPaletteTests.cs`

- [ ] **Step 1: Write the failing test**

`SizeScanner.Avalonia.Tests/SliceColorPaletteTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SliceColorPaletteTests
{
    [Fact]
    public void LevelBaseColor_single_child_uses_hue_zero()
    {
        var hsb = SliceColorPalette.LevelBaseColor(0, 1);
        Assert.Equal(0f, hsb.Hue);
        Assert.Equal(0.95f, hsb.Saturation, 3);
        Assert.Equal(0.68f, hsb.Brightness, 3);
    }

    [Fact]
    public void LevelBaseColor_distributes_hue_across_siblings()
    {
        var second = SliceColorPalette.LevelBaseColor(1, 4);
        Assert.Equal(90f, second.Hue, 3);
    }

    [Fact]
    public void ChildShade_keeps_parent_hue_and_varies_brightness()
    {
        var parent = SliceColorPalette.LevelBaseColor(1, 4); // hue 90
        var shade = SliceColorPalette.ChildShade(parent, 0, 3);
        Assert.Equal(parent.Hue, shade.Hue, 3);
        Assert.Equal(0.38f, shade.Brightness, 3); // minBrightness for first of many
    }

    [Fact]
    public void ToColor_grayscale_when_saturation_zero()
    {
        var hsb = new SliceHsb(0f, 0f, 0.5f);
        var color = hsb.ToColor();
        Assert.Equal(color.R, color.G);
        Assert.Equal(color.G, color.B);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — `SliceColorPalette` / `SliceHsb` do not exist.

- [ ] **Step 3: Implement the palette (ported `ColorFromHsb`, `GetLevelBaseColor`, `GetChildShade`)**

`SizeScanner.Avalonia/Charting/SliceColorPalette.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia.Media;

namespace SizeScanner.Avalonia.Charting;

public readonly record struct SliceHsb(float Hue, float Saturation, float Brightness)
{
    public Color ToColor()
    {
        if (Saturation <= 0)
        {
            var gray = (byte)Math.Round(Brightness * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var hue = (Hue % 360 + 360) % 360;
        var h = hue / 60f;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var p = Brightness * (1 - Saturation);
        var q = Brightness * (1 - Saturation * f);
        var t = Brightness * (1 - Saturation * (1 - f));

        float r, g, b;
        switch (i % 6)
        {
            case 0: r = Brightness; g = t; b = p; break;
            case 1: r = q; g = Brightness; b = p; break;
            case 2: r = p; g = Brightness; b = t; break;
            case 3: r = p; g = q; b = Brightness; break;
            case 4: r = t; g = p; b = Brightness; break;
            default: r = Brightness; g = p; b = q; break;
        }

        return Color.FromRgb(
            (byte)Math.Round(r * 255),
            (byte)Math.Round(g * 255),
            (byte)Math.Round(b * 255));
    }
}

public static class SliceColorPalette
{
    public static SliceHsb LevelBaseColor(int siblingIndex, int siblingCount)
    {
        var hue = siblingCount <= 1 ? 0f : 360f * siblingIndex / siblingCount;
        return new SliceHsb(hue, 0.95f, 0.68f);
    }

    public static SliceHsb ChildShade(SliceHsb parent, int siblingIndex, int siblingCount)
    {
        if (siblingCount <= 1)
            return new SliceHsb(parent.Hue, parent.Saturation, parent.Brightness);

        const float minBrightness = 0.38f;
        const float maxBrightness = 0.92f;
        var brightness = minBrightness + (maxBrightness - minBrightness) * siblingIndex / (siblingCount - 1);
        return new SliceHsb(parent.Hue, parent.Saturation, brightness);
    }
}
```

> **Note:** The WinForms code derived child shades from `Color.GetHue()/GetSaturation()` of the parent's already-converted RGB. Carrying the original `SliceHsb` (above) is equivalent and avoids RGB round-trip drift. The test asserts hue/brightness directly.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add SizeScanner.Avalonia\Charting\SliceColorPalette.cs SizeScanner.Avalonia.Tests\SliceColorPaletteTests.cs
git commit -m "feat: add slice color palette (HSB)"
```

---

## Task 8: Sunburst chart builder (TDD)

Build a true sunburst layout from `FsItem` trees. The builder does not create chart-control objects; it creates immutable segment data with ring index, angular span, size, color, and optional source node. The custom Avalonia control in Task 11 draws those segments and uses the same segment data for polar hit-testing.

**Files:**
- Create: `SizeScanner.Avalonia/Charting/SunburstSegment.cs`
- Create: `SizeScanner.Avalonia/Charting/SunburstChart.cs`
- Create: `SizeScanner.Avalonia/Charting/SunburstHitTest.cs`
- Create: `SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`
- Test: `SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`

- [ ] **Step 1: Write the failing tests (layout, threshold collapse, hit-testing)**

`SizeScanner.Avalonia.Tests/SunburstChartBuilderTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Avalonia;
using SizeScanner.Avalonia.Charting;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class SunburstChartBuilderTests
{
    [Fact]
    public void Empty_root_produces_empty_layout()
    {
        var root = TestTree.Dir("root");
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Empty(chart.Segments);
        Assert.Equal(0, chart.RingCount);
        Assert.Equal(0, chart.TotalSize);
    }

    [Fact]
    public void Level_zero_segments_are_on_outer_ring_and_split_full_circle()
    {
        var root = TestTree.Dir("root",
            TestTree.Dir("a",
                TestTree.File("a1", 60),
                TestTree.File("a2", 40)),     // a = 100
            TestTree.File("b", 100));         // total = 200

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        Assert.Equal(2, chart.RingCount);
        var outer = chart.Segments.Where(s => s.RingIndex == 0).ToArray();
        Assert.Equal(2, outer.Length);
        Assert.All(outer, s => Assert.Equal(0, s.Level));
        Assert.Equal(360d, outer.Sum(s => s.SweepAngle), 3);
        Assert.Contains(chart.Segments, s => s.Node?.Name == "a1" && s.RingIndex == 1);
    }

    [Fact]
    public void Items_at_or_below_threshold_become_non_actionable_placeholder_segments()
    {
        var root = TestTree.Dir("root",
            TestTree.File("big", 1000),
            TestTree.File("tiny", 5));

        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 10);

        Assert.Contains(chart.Segments, s => s.Node?.Name == "big" && !s.IsPlaceholder);
        var placeholder = Assert.Single(chart.Segments.Where(s => s.IsPlaceholder));
        Assert.Null(placeholder.Node);
        Assert.Equal(5, placeholder.Size);
    }

    [Fact]
    public void HitTest_returns_segment_matching_ring_and_angle()
    {
        var root = TestTree.Dir("root",
            TestTree.File("first", 100),
            TestTree.File("second", 100));
        var chart = new SunburstChartBuilder().Build(root, filterThreshold: 0);

        // With 0 degrees at 12 o'clock and clockwise angles, the first half includes the right side.
        var hit = SunburstHitTest.HitTest(chart, new Point(80, 50), new Size(100, 100));

        Assert.NotNull(hit);
        Assert.Equal("first", hit.Node?.Name);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — sunburst layout types do not exist.

- [ ] **Step 3: Create the result types**

`SizeScanner.Avalonia/Charting/SunburstSegment.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Media;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

public sealed record SunburstSegment(
    FsItem? Node,
    int Level,
    int RingIndex,
    long Size,
    double StartAngle,
    double SweepAngle,
    Color Color,
    bool IsPlaceholder)
{
    public double EndAngle => StartAngle + SweepAngle;
    public bool IsActionable => Node is not null && !IsPlaceholder;
}
```

`SizeScanner.Avalonia/Charting/SunburstChart.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace SizeScanner.Avalonia.Charting;

public sealed record SunburstChart(
    IReadOnlyList<SunburstSegment> Segments,
    int RingCount,
    long TotalSize);
```

`SizeScanner.Avalonia/Charting/SunburstHitTest.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using Avalonia;

namespace SizeScanner.Avalonia.Charting;

public static class SunburstHitTest
{
    public static SunburstSegment? HitTest(
        SunburstChart chart,
        Point point,
        Size bounds,
        double innerHoleRatio = 0.18)
    {
        if (chart.RingCount <= 0 || chart.Segments.Count == 0)
            return null;

        var outerRadius = Math.Min(bounds.Width, bounds.Height) / 2d;
        if (outerRadius <= 0)
            return null;

        var center = new Point(bounds.Width / 2d, bounds.Height / 2d);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var innerRadius = outerRadius * innerHoleRatio;
        if (distance < innerRadius || distance > outerRadius)
            return null;

        var ringWidth = (outerRadius - innerRadius) / chart.RingCount;
        var ringIndex = (int)Math.Floor((outerRadius - distance) / ringWidth);
        if (ringIndex == chart.RingCount)
            ringIndex--;

        var angle = (Math.Atan2(dy, dx) * 180d / Math.PI + 90d + 360d) % 360d;
        return chart.Segments.FirstOrDefault(s =>
            s.IsActionable &&
            s.RingIndex == ringIndex &&
            ContainsAngle(s, angle));
    }

    private static bool ContainsAngle(SunburstSegment segment, double angle)
    {
        var start = Normalize(segment.StartAngle);
        var end = Normalize(segment.EndAngle);
        return segment.SweepAngle >= 360d ||
               (start <= end
                   ? angle >= start && angle < end
                   : angle >= start || angle < end);
    }

    private static double Normalize(double angle) => (angle % 360d + 360d) % 360d;
}
```

- [ ] **Step 4: Implement the builder**

`SizeScanner.Avalonia/Charting/SunburstChartBuilder.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using ScannerCore;

namespace SizeScanner.Avalonia.Charting;

/// <summary>
/// Builds a true sunburst layout: each segment has a ring, angular span, color, and optional FsItem.
/// Thresholded entries remain as transparent placeholders so sibling angles still add up correctly,
/// but they are not actionable and are ignored by hit-testing.
/// </summary>
public sealed class SunburstChartBuilder
{
    private long _filterThreshold;
    private readonly List<SunburstSegment> _segments = new();
    private int _ringCount;

    public SunburstChart Build(FsItem root, long filterThreshold)
    {
        _filterThreshold = filterThreshold;
        _segments.Clear();
        _ringCount = root.Items is { Count: > 0 } ? CountRings(root, level: 0) : 0;

        if (_ringCount == 0)
            return new SunburstChart([], 0, 0);

        var total = PositiveSum(root.Items!);
        if (total <= 0)
            return new SunburstChart([], 0, 0);

        AddChildren(root.Items!, level: 0, startAngle: 0d, sweepAngle: 360d, parentColor: null);
        return new SunburstChart(_segments, _ringCount, total);
    }

    private int CountRings(FsItem node, int level)
    {
        if (node.Items is not { Count: > 0 })
            return level;

        var max = level + 1;
        foreach (var child in node.Items)
            if (child.Size > _filterThreshold && child.Items is { Count: > 0 })
                max = System.Math.Max(max, CountRings(child, level + 1));
        return max;
    }

    private void AddChildren(
        IReadOnlyList<FsItem> children,
        int level,
        double startAngle,
        double sweepAngle,
        SliceHsb? parentColor)
    {
        var total = PositiveSum(children);
        if (total <= 0)
            return;

        var cursor = startAngle;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childSweep = sweepAngle * System.Math.Max(0, child.Size) / total;
            var hsb = parentColor is null
                ? SliceColorPalette.LevelBaseColor(i, children.Count)
                : SliceColorPalette.ChildShade(parentColor.Value, i, children.Count);
            var isPlaceholder = child.Size <= _filterThreshold;
            var node = isPlaceholder ? null : child;
            var color = isPlaceholder ? Colors.Transparent : hsb.ToColor();

            _segments.Add(new SunburstSegment(
                node,
                level,
                level,
                child.Size,
                cursor,
                childSweep,
                color,
                isPlaceholder));

            if (!isPlaceholder && child.Items is { Count: > 0 })
                AddChildren(child.Items, level + 1, cursor, childSweep, hsb);

            cursor += childSweep;
        }
    }

    private static long PositiveSum(IEnumerable<FsItem> items) =>
        items.Sum(item => System.Math.Max(0, item.Size));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS (all four builder tests).

- [ ] **Step 6: Commit**

```powershell
git add SizeScanner.Avalonia\Charting SizeScanner.Avalonia.Tests\SunburstChartBuilderTests.cs
git commit -m "feat: add sunburst chart layout"
```

---

## Task 9: ChartViewModel (TDD)

Owns the sunburst layout, scope navigation, hover status text, click-to-scope, and slice actions (open/delete). Pure logic is tested with in-memory `FsItem` trees. The floating custom tooltip is deferred and is not part of this task.

**Files:**
- Create: `SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`
- Test: `SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

`SizeScanner.Avalonia.Tests/ChartViewModelTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.ViewModels;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class ChartViewModelTests
{
    private sealed class NoopFs : IFileSystemActions
    {
        public void ShowInExplorer(string path) { }
        public bool TryDelete(string path, bool permanent, out string? error) { error = null; return true; }
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }

    private static ChartViewModel CreateVm() => new(new NoopFs(), new NoopDialogs());

    private static FsItem SampleDriveRoot() =>
        TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.Dir("Windows",
                TestTree.File("kernel.sys", 300)),
            TestTree.File("page.sys", 200));

    [Fact]
    public void SetScan_then_Refresh_populates_chart_layout()
    {
        var vm = CreateVm();
        vm.SetScan(SampleDriveRoot(), isDrive: true, targetPath: "C:\\");
        vm.Refresh(filterThreshold: 0, includeFreeSpace: false);

        Assert.NotEmpty(vm.Layout.Segments);
        Assert.False(vm.IsScoped);
    }

    [Fact]
    public void Scoping_into_directory_updates_scope_state()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0, includeFreeSpace: false);

        var windows = root.Items![2];
        Assert.True(vm.TryScopeAt(windows));
        Assert.True(vm.IsScoped);
        Assert.Contains("Windows", vm.ScopeLabel);

        vm.GoToRootCommand.Execute(null);
        Assert.False(vm.IsScoped);
    }

    [Fact]
    public void CannotScope_into_free_space_or_files()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0, includeFreeSpace: true);

        var freeSpace = root.Items![0];
        var file = root.Items![3];
        Assert.False(vm.TryScopeAt(freeSpace));
        Assert.False(vm.TryScopeAt(file));
    }

    [Fact]
    public void Hover_builds_status_path_only()
    {
        var vm = CreateVm();
        var root = SampleDriveRoot();
        vm.SetScan(root, isDrive: true, targetPath: "C:\\");
        vm.Refresh(0, includeFreeSpace: false);

        var kernel = root.Items![2].Items![0]; // Windows/kernel.sys
        vm.Hover(kernel);

        Assert.Contains("kernel.sys", vm.HoverPath);

        vm.ClearHover();
        Assert.Equal(string.Empty, vm.HoverPath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — `ChartViewModel` does not exist.

- [ ] **Step 3: Implement the view-model**

`SizeScanner.Avalonia/ViewModels/ChartViewModel.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Charting;

namespace SizeScanner.Avalonia.ViewModels;

public sealed partial class ChartViewModel : ViewModelBase
{
    private readonly SunburstChartBuilder _builder = new();
    private readonly IFileSystemActions _fileSystem;
    private readonly IDialogService _dialogs;

    private FsItem? _scanRoot;
    private FsItem? _chartRootWithoutSynthetic;
    private bool _isDriveScan;
    private bool _includeFreeSpace;
    private long _filterThreshold;
    private string _targetPath = string.Empty;
    private FsItem? _scopedRoot;
    private string _displayRootPath = string.Empty;

    public ChartViewModel(IFileSystemActions fileSystem, IDialogService dialogs)
    {
        _fileSystem = fileSystem;
        _dialogs = dialogs;
    }

    [ObservableProperty] private SunburstChart _layout = new([], 0, 0);
    [ObservableProperty] private bool _isScoped;
    [ObservableProperty] private string _scopeLabel = string.Empty;
    [ObservableProperty] private string _hoverPath = string.Empty;

    public FsItem? ContextTarget { get; private set; }
    public string ContextTargetPath { get; private set; } = string.Empty;

    public void SetScan(FsItem scanRoot, bool isDrive, string targetPath)
    {
        _scanRoot = scanRoot;
        _isDriveScan = isDrive;
        _targetPath = targetPath;
        _chartRootWithoutSynthetic = BuildChartRootWithoutSyntheticEntries(scanRoot, isDrive);
        _scopedRoot = null;
        UpdateScopeState();
    }

    public void Refresh(long filterThreshold, bool includeFreeSpace)
    {
        _filterThreshold = filterThreshold;
        _includeFreeSpace = includeFreeSpace;
        if (_scanRoot is null)
        {
            Layout = new SunburstChart([], 0, 0);
            return;
        }

        Layout = _builder.Build(GetDisplayRoot(), _filterThreshold);
    }

    public FsItem? ResolveNode(SunburstSegment? segment) => segment?.Node;

    public void Hover(FsItem? node)
    {
        if (node is null) { ClearHover(); return; }

        var chain = AncestorChain(node);
        HoverPath = BuildFullPath(chain);
    }

    public void ClearHover()
    {
        HoverPath = string.Empty;
    }

    public bool CanScopeTo(FsItem? item)
    {
        if (item is null || !item.IsDir) return false;
        if (item.Items is null || item.Items.Count == 0) return false;
        if (item.Name == DriveScanMetadata.FreeSpaceName || item.Name == DriveScanMetadata.InaccessibleName) return false;
        return true;
    }

    public bool TryScopeAt(FsItem node)
    {
        if (!CanScopeTo(node)) return false;
        _scopedRoot = node;
        UpdateScopeState();
        Refresh(_filterThreshold, _includeFreeSpace);
        return true;
    }

    [RelayCommand]
    private void GoUp()
    {
        if (_scopedRoot is null || _scanRoot is null) return;

        var parent = _scopedRoot.Parent;
        _scopedRoot = parent is null || ReferenceEquals(parent, _scanRoot) ? null : parent;
        UpdateScopeState();
        Refresh(_filterThreshold, _includeFreeSpace);
    }

    [RelayCommand]
    private void GoToRoot()
    {
        if (_scopedRoot is null) return;
        _scopedRoot = null;
        UpdateScopeState();
        Refresh(_filterThreshold, _includeFreeSpace);
    }

    public void SetContextTarget(FsItem? node)
    {
        ContextTarget = node;
        ContextTargetPath = node is null ? string.Empty : BuildFullPath(AncestorChain(node));
    }

    public bool IsFreeSpace(FsItem? node) => node?.Name == DriveScanMetadata.FreeSpaceName;

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (ContextTarget is not null)
            _fileSystem.ShowInExplorer(ContextTargetPath);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeleteAsync()
    {
        if (ContextTarget is null) return;
        if (!await _dialogs.ConfirmAsync("Move to Recycle Bin", "Move to Recycle Bin?\n\n" + ContextTargetPath))
            return;
        if (!_fileSystem.TryDelete(ContextTargetPath, permanent: false, out var error))
            await _dialogs.ShowInfoAsync("Delete failed", error ?? "Delete failed.");
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeletePermanentlyAsync()
    {
        if (ContextTarget is null) return;
        var prompt = "Are you sure you want to permanently delete " + ContextTargetPath + "? This cannot be undone.";
        if (!await _dialogs.ConfirmAsync("Permanently delete", prompt))
            return;
        if (!_fileSystem.TryDelete(ContextTargetPath, permanent: true, out var error))
            await _dialogs.ShowInfoAsync("Delete failed", error ?? "Delete failed.");
    }

    private FsItem GetDisplayRoot() => _scopedRoot ?? GetBaseChartRoot();

    private FsItem GetBaseChartRoot() =>
        _includeFreeSpace && _isDriveScan
            ? _scanRoot!
            : _chartRootWithoutSynthetic ?? _scanRoot!;

    private void UpdateScopeState()
    {
        IsScoped = _scopedRoot is not null;
        if (_scopedRoot is null || _scanRoot is null)
        {
            _displayRootPath = _targetPath;
            ScopeLabel = string.Empty;
            return;
        }

        _displayRootPath = _scopedRoot.TryGetPathFrom(_scanRoot, out var path) ? path : _targetPath;
        ScopeLabel = $"{_displayRootPath}  |  {Humanize.FsItem(_scopedRoot)}";
    }

    private IReadOnlyList<FsItem> AncestorChain(FsItem node)
    {
        // From the scope/base root down to the hovered node (root-first).
        var stop = _scopedRoot ?? GetBaseChartRoot();
        var chain = new List<FsItem>();
        for (var current = node; current is not null && !ReferenceEquals(current, stop); current = current.Parent)
            chain.Add(current);
        chain.Reverse();
        return chain;
    }

    private string BuildFullPath(IReadOnlyList<FsItem> chain)
    {
        var path = _displayRootPath;
        foreach (var item in chain)
            path = Path.Combine(path, item.Name);
        return path;
    }

    private static FsItem? BuildChartRootWithoutSyntheticEntries(FsItem scanRoot, bool isDrive)
    {
        if (!isDrive || scanRoot.Items is null) return null;

        var stripped = new FsItem(scanRoot.Name, scanRoot.Size, scanRoot.IsDir)
        {
            Items = scanRoot.Items.Skip(DriveScanMetadata.SyntheticEntryCount).ToList()
        };
        // Preserve Parent links for path/scope resolution.
        foreach (var child in stripped.Items)
            child.Parent = scanRoot; // children already parented to scanRoot
        return stripped;
    }
}
```

> **Note on `child.Parent = scanRoot`:** `FsItem.Parent` has an `internal` setter, so this assignment will NOT compile from the Avalonia assembly. Two options — pick one during implementation:
> - **(A, recommended)** Add `[assembly: InternalsVisibleTo("SizeScanner.Avalonia")]` to `ScannerCore` (new file `ScannerCore/AssemblyInfo.cs`). Then the line compiles.
> - **(B)** Drop the loop entirely: the stripped children are the *same* `FsItem` instances already parented to `scanRoot`, so their `Parent` is already correct — the reassignment is redundant. Remove the `foreach` block. This requires no `ScannerCore` change and is the simplest fix.
>
> Default to **(B)**: delete the `foreach` loop and its comment. The `BuildChartRootWithoutSyntheticEntries` then mirrors the WinForms version exactly.

- [ ] **Step 4: Apply the chosen Parent fix**

Default (Option B): remove the `foreach (var child in stripped.Items) child.Parent = scanRoot;` block so the method ends:

```csharp
        var stripped = new FsItem(scanRoot.Name, scanRoot.Size, scanRoot.IsDir)
        {
            Items = scanRoot.Items.Skip(DriveScanMetadata.SyntheticEntryCount).ToList()
        };
        return stripped;
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS (all ChartViewModel tests).

- [ ] **Step 6: Commit**

```powershell
git add SizeScanner.Avalonia\ViewModels\ChartViewModel.cs SizeScanner.Avalonia.Tests\ChartViewModelTests.cs
git commit -m "feat: add ChartViewModel (scope nav, hover, slice actions)"
```

---

## Task 10: MainWindowViewModel (TDD)

Replaces the Task 1 stub. Orchestrates scanning, toolbar options, the inaccessible pane, status/progress, settings, and owns the `ChartViewModel`.

**Files:**
- Modify: `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs`
- Create: `SizeScanner.Avalonia/Models/FilterOption.cs`
- Test: `SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing tests (with a fake scan service)**

`SizeScanner.Avalonia.Tests/MainWindowViewModelTests.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;
using SizeScanner.Avalonia.ViewModels;
using Xunit;

namespace SizeScanner.Avalonia.Tests;

public sealed class MainWindowViewModelTests
{
    private sealed class FakeScan : IScanService
    {
        private readonly FsItem _root;
        public FakeScan(FsItem root) => _root = root;
        public string LastTarget { get; private set; } = string.Empty;
        public bool IsDriveScan { get; private set; }
        public DriveScanner Scanner { get; } = new();
        public Task<FsItem> RunAsync(string target, bool isDrive, CancellationToken ct, IProgress<ScanProgress> p)
        {
            LastTarget = target;
            IsDriveScan = isDrive;
            return Task.FromResult(_root);
        }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public UserSettings Saved { get; private set; } = new();
        public UserSettings Load() => new();
        public void Save(UserSettings settings) => Saved = settings;
    }

    private sealed class FakeDrives : IDriveProvider
    {
        public System.Collections.Generic.IReadOnlyList<DriveItem> GetReadyDrives() =>
            new[] { new DriveItem("C:", "C:\\") };
    }

    private sealed class FakeElevation : IElevationService
    {
        public bool IsRunningAsAdministrator() => true;
        public bool TryRelaunchAsAdministrator(out string? error) { error = null; return true; }
    }

    private sealed class FakeFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class NoopFs : IFileSystemActions
    {
        public void ShowInExplorer(string path) { }
        public bool TryDelete(string path, bool permanent, out string? error) { error = null; return true; }
    }

    private sealed class NoopDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }

    private static MainWindowViewModel CreateVm(FsItem root, FakeSettings? settings = null) =>
        new(new FakeScan(root), settings ?? new FakeSettings(), new FakeDrives(),
            new FakeElevation(), new FakeFolderPicker(),
            new ChartViewModel(new NoopFs(), new NoopDialogs()));

    private static FsItem DriveRoot() =>
        TestTree.Dir("C:\\",
            TestTree.File(DriveScanMetadata.FreeSpaceName, 500),
            TestTree.File(DriveScanMetadata.InaccessibleName, 0),
            TestTree.File("page.sys", 200));

    [Fact]
    public void Initialize_populates_drive_buttons_and_default_options()
    {
        var vm = CreateVm(DriveRoot());
        vm.Initialize();

        Assert.Single(vm.Drives);
        Assert.Equal(4, vm.FilterIndex);
        Assert.Equal(1, vm.FreeSpaceIndex);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanDirectory_sets_chart_and_ready_state()
    {
        var vm = CreateVm(DriveRoot());
        vm.Initialize();

        await vm.ScanTargetAsync("D:\\data", isDrive: false);

        Assert.False(vm.IsScanning);
        Assert.True(vm.CanRescan);
        Assert.NotEmpty(vm.Chart.Layout.Segments);
        Assert.Equal("Ready", vm.StatusText);
    }

    [Fact]
    public async Task ChangingOptions_after_scan_saves_settings()
    {
        var settings = new FakeSettings();
        var vm = CreateVm(DriveRoot(), settings);
        vm.Initialize();
        await vm.ScanTargetAsync("D:\\data", isDrive: false);

        vm.FilterIndex = 2;

        Assert.Equal(2, settings.Saved.FilterIndex);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: FAIL — `MainWindowViewModel` has the wrong shape.

- [ ] **Step 3: Create the filter option model**

`SizeScanner.Avalonia/Models/FilterOption.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SizeScanner.Avalonia.Models;

public sealed record FilterOption(string Label);
```

- [ ] **Step 4: Implement the view-model**

Replace `SizeScanner.Avalonia/ViewModels/MainWindowViewModel.cs` with:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScannerCore;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Models;

namespace SizeScanner.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IScanService _scan;
    private readonly ISettingsStore _settingsStore;
    private readonly IDriveProvider _driveProvider;
    private readonly IElevationService _elevation;
    private readonly IFolderPicker _folderPicker;

    private FsItem? _scanRoot;
    private CancellationTokenSource? _scanCts;
    private bool _suppressOptionChanges;
    private bool _initialized;

    public MainWindowViewModel(
        IScanService scan,
        ISettingsStore settingsStore,
        IDriveProvider driveProvider,
        IElevationService elevation,
        IFolderPicker folderPicker,
        ChartViewModel chart)
    {
        _scan = scan;
        _settingsStore = settingsStore;
        _driveProvider = driveProvider;
        _elevation = elevation;
        _folderPicker = folderPicker;
        Chart = chart;

        for (var i = 0; i <= 8; i++)
            FilterOptions.Add(new FilterOption(FilterLabels[i]));
    }

    public ChartViewModel Chart { get; }

    public ObservableCollection<DriveItem> Drives { get; } = [];
    public ObservableCollection<string> FreeSpaceOptions { get; } = ["Show free space", "Hide free space"];
    public ObservableCollection<FilterOption> FilterOptions { get; } = [];
    public ObservableCollection<string> InaccessiblePaths { get; } = [];

    [ObservableProperty] private int _filterIndex = 4;
    [ObservableProperty] private int _freeSpaceIndex = 1;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _canRescan;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _statusDetails = string.Empty;
    [ObservableProperty] private string _inaccessibleTotalSize = Humanize.Size(0);
    [ObservableProperty] private bool _relaunchAsAdminVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InaccessiblePaneVisible))]
    private bool _inaccessiblePaneCollapsed;

    public bool InaccessiblePaneVisible => !InaccessiblePaneCollapsed;

    private static readonly string[] FilterLabels =
    [
        "No threshold (SLOW!)", "0.25% (Slower)", "0.5%", "0.75%", "1%",
        "1.25%", "1.5%", "1.75% (Rougher)", "2% (ROUGH!)"
    ];

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var drive in _driveProvider.GetReadyDrives())
            Drives.Add(drive);

        var settings = _settingsStore.Load();
        _suppressOptionChanges = true;
        FreeSpaceIndex = settings.FreeSpaceIndex;
        FilterIndex = settings.FilterIndex;
        InaccessiblePaneCollapsed = settings.InaccessiblePaneCollapsed;
        _suppressOptionChanges = false;
    }

    [RelayCommand]
    private async Task ScanDriveAsync(DriveItem? drive)
    {
        if (drive is not null)
            await ScanTargetAsync(drive.Root, isDrive: true);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _folderPicker.PickFolderAsync("Select a folder to scan");
        if (!string.IsNullOrEmpty(path))
            await ScanTargetAsync(path, isDrive: false);
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        if (!string.IsNullOrEmpty(_scan.LastTarget))
            await ScanTargetAsync(_scan.LastTarget, _scan.IsDriveScan);
    }

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    [RelayCommand]
    private void ToggleInaccessiblePane()
    {
        InaccessiblePaneCollapsed = !InaccessiblePaneCollapsed;
        Persist();
    }

    [RelayCommand]
    private void RelaunchAsAdmin()
    {
        if (_elevation.TryRelaunchAsAdministrator(out _))
            Environment.Exit(0);
    }

    public async Task ScanTargetAsync(string target, bool isDrive)
    {
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        SetScanningState(true);
        StatusText = $"Scanning {target}...";
        StatusDetails = string.Empty;
        ProgressValue = 0;

        var progress = new Progress<ScanProgress>(OnScanProgress);
        FsItem root;
        try
        {
            root = await _scan.RunAsync(target, isDrive, token, progress);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            FinishCancelled();
            return;
        }

        if (token.IsCancellationRequested) { FinishCancelled(); return; }

        _scanRoot = root;
        PopulateInaccessible(isDrive);

        Chart.SetScan(root, isDrive, _scan.Scanner.CurrentTarget ?? target);
        RefreshChart();

        CanRescan = true;
        RescanCommand.NotifyCanExecuteChanged();
        ProgressValue = 0;
        StatusText = "Ready";
        StatusDetails = string.Empty;
        SetScanningState(false);
        _scanCts.Dispose();
        _scanCts = null;
    }

    private void PopulateInaccessible(bool isDrive)
    {
        InaccessiblePaths.Clear();
        foreach (var path in _scan.Scanner.Inaccessible)
            InaccessiblePaths.Add(path);

        InaccessibleTotalSize = isDrive && _scanRoot is not null
            ? Humanize.Size(DriveScanMetadata.GetInaccessibleEntry(_scanRoot).Size)
            : Humanize.Size(0);

        RelaunchAsAdminVisible = InaccessiblePaths.Count > 0 && !_elevation.IsRunningAsAdministrator();
    }

    private void OnScanProgress(ScanProgress p)
    {
        if (p.PercentComplete.HasValue)
            ProgressValue = Math.Min(p.PercentComplete.Value, 100);
        StatusDetails = p.CurrentPath;
    }

    private void SetScanningState(bool scanning) => IsScanning = scanning;

    private void FinishCancelled()
    {
        ProgressValue = 0;
        StatusText = "Scan cancelled";
        StatusDetails = string.Empty;
        SetScanningState(false);
        _scanCts?.Dispose();
        _scanCts = null;
    }

    private void RefreshChart()
    {
        if (_scanRoot is null) return;
        var percent = 0.0025f * FilterIndex;
        var includeFreeSpace = FreeSpaceIndex == 0;
        var threshold = _scan.Scanner.GetDisplayThreshold(percent, includeFreeSpace);
        Chart.Refresh(threshold, includeFreeSpace);
    }

    partial void OnFilterIndexChanged(int value) => OnDisplayOptionsChanged();
    partial void OnFreeSpaceIndexChanged(int value) => OnDisplayOptionsChanged();

    private void OnDisplayOptionsChanged()
    {
        if (_suppressOptionChanges || IsScanning || _scanRoot is null) return;
        RefreshChart();
        Persist();
    }

    public UserSettings CaptureSettings(int windowWidth, int windowHeight, int splitterDistance) => new()
    {
        FilterIndex = FilterIndex,
        FreeSpaceIndex = FreeSpaceIndex,
        WindowWidth = windowWidth,
        WindowHeight = windowHeight,
        SplitterDistance = splitterDistance,
        InaccessiblePaneCollapsed = InaccessiblePaneCollapsed
    };

    public void SaveOnClose(int windowWidth, int windowHeight, int splitterDistance) =>
        _settingsStore.Save(CaptureSettings(windowWidth, windowHeight, splitterDistance));

    private void Persist() =>
        _settingsStore.Save(new UserSettings
        {
            FilterIndex = FilterIndex,
            FreeSpaceIndex = FreeSpaceIndex,
            InaccessiblePaneCollapsed = InaccessiblePaneCollapsed
        });
}
```

> **Settings persistence detail:** `Persist()` (option/pane changes) saves without window geometry, so it would zero the stored width/height. To preserve geometry across option changes, the view passes current geometry on close via `SaveOnClose`, and `Persist()` should merge: load current settings, update the changed fields, save. For simplicity and to match WinForms (which always rewrote everything from live controls on every change), the final wiring in Task 12 calls a single `SaveOnClose`-style method from the View that has access to window geometry. Keep `Persist()` for option changes but have it **load-then-merge**:
>
> ```csharp
> private void Persist()
> {
>     var s = _settingsStore.Load();
>     s.FilterIndex = FilterIndex;
>     s.FreeSpaceIndex = FreeSpaceIndex;
>     s.InaccessiblePaneCollapsed = InaccessiblePaneCollapsed;
>     _settingsStore.Save(s);
> }
> ```
>
> Use this load-then-merge version. The `MainWindowViewModelTests.ChangingOptions_after_scan_saves_settings` test still passes because `FakeSettings.Load()` returns defaults and `Save` captures `FilterIndex == 2`.

- [ ] **Step 5: Apply the load-then-merge `Persist()`**

Replace the `Persist()` body with the load-then-merge version shown above.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SizeScanner.Avalonia.Tests\SizeScanner.Avalonia.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add SizeScanner.Avalonia\ViewModels\MainWindowViewModel.cs SizeScanner.Avalonia\Models\FilterOption.cs SizeScanner.Avalonia.Tests\MainWindowViewModelTests.cs
git commit -m "feat: add MainWindowViewModel orchestration"
```

---

## Task 11: ChartView (Sunburst control + nav bar + pointer handling)

Builds the visual chart control and wires pointer interaction to `ChartViewModel`. This task intentionally does not implement the WinForms custom tooltip; hover updates only the status path through `ChartViewModel.Hover`.

**Files:**
- Create: `SizeScanner.Avalonia/Views/SunburstChartControl.cs`
- Create: `SizeScanner.Avalonia/Views/ChartView.axaml`
- Create: `SizeScanner.Avalonia/Views/ChartView.axaml.cs`

- [ ] **Step 1: Create the custom sunburst control**

`SizeScanner.Avalonia/Views/SunburstChartControl.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SizeScanner.Avalonia.Charting;

namespace SizeScanner.Avalonia.Views;

public sealed class SunburstChartControl : Control
{
    public static readonly StyledProperty<SunburstChart?> ChartProperty =
        AvaloniaProperty.Register<SunburstChartControl, SunburstChart?>(nameof(Chart));

    private static readonly Pen SegmentBorder = new(Brushes.Black, 1);

    public SunburstChart? Chart
    {
        get => GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    static SunburstChartControl()
    {
        AffectsRender<SunburstChartControl>(ChartProperty);
    }

    public SunburstSegment? HitTestSegment(Point point) =>
        Chart is null ? null : SunburstHitTest.HitTest(Chart, point, Bounds.Size);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var chart = Chart;
        if (chart is null || chart.RingCount == 0)
            return;

        foreach (var segment in chart.Segments)
        {
            if (segment.IsPlaceholder)
                continue;

            using var geometry = CreateSegmentGeometry(Bounds.Size, segment, chart.RingCount);
            context.DrawGeometry(new SolidColorBrush(segment.Color), SegmentBorder, geometry);
        }
    }

    private static StreamGeometry CreateSegmentGeometry(Size bounds, SunburstSegment segment, int ringCount)
    {
        var outerRadius = Math.Min(bounds.Width, bounds.Height) / 2d;
        var center = new Point(bounds.Width / 2d, bounds.Height / 2d);
        var innerHole = outerRadius * 0.18;
        var ringWidth = (outerRadius - innerHole) / ringCount;
        var segmentOuter = outerRadius - segment.RingIndex * ringWidth;
        var segmentInner = segmentOuter - ringWidth;
        var sweep = Math.Min(segment.SweepAngle, 359.999d);
        var largeArc = sweep > 180d;

        var outerStart = PointOnCircle(center, segmentOuter, segment.StartAngle);
        var outerEnd = PointOnCircle(center, segmentOuter, segment.StartAngle + sweep);
        var innerEnd = PointOnCircle(center, segmentInner, segment.StartAngle + sweep);
        var innerStart = PointOnCircle(center, segmentInner, segment.StartAngle);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(outerStart, isFilled: true);
            ctx.ArcTo(outerEnd, new Size(segmentOuter, segmentOuter), 0, largeArc, SweepDirection.Clockwise);
            ctx.LineTo(innerEnd);
            ctx.ArcTo(innerStart, new Size(segmentInner, segmentInner), 0, largeArc, SweepDirection.CounterClockwise);
            ctx.EndFigure(isClosed: true);
        }
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = (angle - 90d) * Math.PI / 180d;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
```

- [ ] **Step 2: Create the XAML**

`SizeScanner.Avalonia/Views/ChartView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:SizeScanner.Avalonia.ViewModels"
             xmlns:views="using:SizeScanner.Avalonia.Views"
             x:Class="SizeScanner.Avalonia.Views.ChartView"
             x:DataType="vm:ChartViewModel">
    <Grid>
        <!-- Scope navigation bar -->
        <Border VerticalAlignment="Top" HorizontalAlignment="Stretch"
                Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}"
                IsVisible="{Binding IsScoped}"
                Padding="8,6" ZIndex="2">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Content="Go up" Command="{Binding GoUpCommand}" />
                <Button Content="Go to root" Command="{Binding GoToRootCommand}" />
                <TextBlock Text="{Binding ScopeLabel}" VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis" />
            </StackPanel>
        </Border>

        <views:SunburstChartControl x:Name="PART_Chart"
                                    Chart="{Binding Layout}"
                                    Focusable="True" />
    </Grid>
</UserControl>
```

- [ ] **Step 3: Create the code-behind (pointer → VM)**

`SizeScanner.Avalonia/Views/ChartView.axaml.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ScannerCore;
using SizeScanner.Avalonia.ViewModels;

namespace SizeScanner.Avalonia.Views;

public partial class ChartView : UserControl
{
    private SunburstChartControl _chart = null!;

    public ChartView()
    {
        InitializeComponent();
        _chart = this.FindControl<SunburstChartControl>("PART_Chart")!;

        _chart.PointerMoved += OnPointerMoved;
        _chart.PointerExited += OnPointerExited;
        _chart.PointerPressed += OnPointerPressed;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private ChartViewModel? Vm => DataContext as ChartViewModel;

    private FsItem? HitTest(Point position)
    {
        if (Vm is null) return null;
        return Vm.ResolveNode(_chart.HitTestSegment(position));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Vm is null) return;
        var position = e.GetPosition(_chart);
        var node = HitTest(position);
        Vm.Hover(node);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e) => Vm?.ClearHover();

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is null) return;
        var props = e.GetCurrentPoint(_chart).Properties;
        var node = HitTest(e.GetPosition(_chart));

        if (props.IsLeftButtonPressed)
        {
            if (node is not null) Vm.TryScopeAt(node);
            return;
        }

        if (props.IsRightButtonPressed)
        {
            // Free space is not actionable; suppress the menu for it.
            if (node is null || Vm.IsFreeSpace(node))
            {
                Vm.SetContextTarget(null);
                e.Handled = true;
                return;
            }

            Vm.SetContextTarget(node);
        }
    }
}
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build SizeScanner.Avalonia\SizeScanner.Avalonia.csproj`
Expected: build succeeds. (Visual correctness is checked in Task 14.)

- [ ] **Step 5: Commit**

```powershell
git add SizeScanner.Avalonia\Views\SunburstChartControl.cs SizeScanner.Avalonia\Views\ChartView.axaml SizeScanner.Avalonia\Views\ChartView.axaml.cs
git commit -m "feat: add Avalonia sunburst chart control"
```

---

## Task 12: MainWindow layout, context menu, keyboard shortcuts, settings wiring

Replaces the Task 1 stub with the full window: toolbar (drive buttons, browse, rescan, cancel, progress, free-space, filter, toggle pane), a two-pane layout (chart | inaccessible), status bar, the slice context menu, keyboard shortcuts, and load/save of window geometry.

**Files:**
- Modify: `SizeScanner.Avalonia/Views/MainWindow.axaml`
- Modify: `SizeScanner.Avalonia/Views/MainWindow.axaml.cs`
- Create: `SizeScanner.Avalonia/Converters/BoolToGridLengthConverter.cs` (for collapsible pane)

- [ ] **Step 1: Create the pane converter**

`SizeScanner.Avalonia/Converters/BoolToGridLengthConverter.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace SizeScanner.Avalonia.Converters;

/// <summary>True (pane visible) => star width; False => zero width.</summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Create the window XAML**

`SizeScanner.Avalonia/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:SizeScanner.Avalonia.ViewModels"
        xmlns:views="using:SizeScanner.Avalonia.Views"
        xmlns:conv="using:SizeScanner.Avalonia.Converters"
        xmlns:models="using:SizeScanner.Avalonia.Models"
        x:Class="SizeScanner.Avalonia.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Icon="/Assets/main.ico"
        Width="1100" Height="720" MinWidth="720" MinHeight="480"
        Title="SizeScanner">

    <Window.Resources>
        <conv:BoolToGridLengthConverter x:Key="PaneWidth" />
    </Window.Resources>

    <Window.KeyBindings>
        <KeyBinding Gesture="F5" Command="{Binding RescanCommand}" />
        <KeyBinding Gesture="Ctrl+O" Command="{Binding BrowseCommand}" />
        <KeyBinding Gesture="Escape" Command="{Binding CancelScanCommand}" />
    </Window.KeyBindings>

    <DockPanel>
        <!-- Toolbar -->
        <Border DockPanel.Dock="Top" Padding="8,6"
                Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}">
            <StackPanel Orientation="Horizontal" Spacing="6">
                <Button Content="Browse..." Command="{Binding BrowseCommand}"
                        IsEnabled="{Binding !IsScanning}" />
                <Button Content="Rescan" Command="{Binding RescanCommand}"
                        IsEnabled="{Binding !IsScanning}" />
                <Separator />
                <ItemsControl ItemsSource="{Binding Drives}" IsEnabled="{Binding !IsScanning}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" Spacing="4" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="models:DriveItem">
                            <Button Content="{Binding DisplayName}"
                                    Command="{Binding $parent[Window].((vm:MainWindowViewModel)DataContext).ScanDriveCommand}"
                                    CommandParameter="{Binding}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <Separator />
                <ProgressBar Width="280" Minimum="0" Maximum="100"
                             Value="{Binding ProgressValue}"
                             IsVisible="{Binding IsScanning}" VerticalAlignment="Center" />
                <Button Content="Cancel" Command="{Binding CancelScanCommand}"
                        IsVisible="{Binding IsScanning}" />
                <Separator />
                <ComboBox ItemsSource="{Binding FreeSpaceOptions}"
                          SelectedIndex="{Binding FreeSpaceIndex}"
                          IsEnabled="{Binding !IsScanning}" />
                <TextBlock Text="Filter:" VerticalAlignment="Center" Margin="8,0,0,0" />
                <ComboBox ItemsSource="{Binding FilterOptions}"
                          SelectedIndex="{Binding FilterIndex}"
                          IsEnabled="{Binding !IsScanning}">
                    <ComboBox.ItemTemplate>
                        <DataTemplate x:DataType="models:FilterOption">
                            <TextBlock Text="{Binding Label}" />
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
                <Separator />
                <Button Content="Toggle Inaccessible" Command="{Binding ToggleInaccessiblePaneCommand}"
                        IsEnabled="{Binding !IsScanning}" />
            </StackPanel>
        </Border>

        <!-- Status bar -->
        <Border DockPanel.Dock="Bottom" Padding="8,4"
                Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}">
            <StackPanel Orientation="Horizontal" Spacing="16">
                <TextBlock Text="{Binding StatusText}" />
                <TextBlock Text="{Binding StatusDetails}" TextTrimming="CharacterEllipsis" />
            </StackPanel>
        </Border>

        <!-- Body: chart | inaccessible pane -->
        <Grid ColumnDefinitions="*,Auto,Auto">
            <views:ChartView Grid.Column="0" DataContext="{Binding Chart}">
                <views:ChartView.ContextMenu>
                    <ContextMenu>
                        <MenuItem Header="Open in Explorer" Command="{Binding OpenInExplorerCommand}" />
                        <MenuItem Header="Delete" Command="{Binding DeleteCommand}" />
                        <MenuItem Header="Delete permanently" Command="{Binding DeletePermanentlyCommand}" />
                    </ContextMenu>
                </views:ChartView.ContextMenu>
            </views:ChartView>

            <GridSplitter Grid.Column="1" Width="4"
                          IsVisible="{Binding InaccessiblePaneVisible}"
                          ResizeDirection="Columns" />

            <Grid Grid.Column="2" Width="360"
                  IsVisible="{Binding InaccessiblePaneVisible}"
                  RowDefinitions="Auto,*" Margin="6">
                <WrapPanel Grid.Row="0" Margin="0,0,0,6">
                    <TextBlock Text="Inaccessible objects ( total " VerticalAlignment="Center" />
                    <TextBlock Text="{Binding InaccessibleTotalSize}" VerticalAlignment="Center" />
                    <TextBlock Text=" ):" VerticalAlignment="Center" />
                    <Button Content="Relaunch as administrator" Margin="12,0,0,0"
                            Command="{Binding RelaunchAsAdminCommand}"
                            IsVisible="{Binding RelaunchAsAdminVisible}" />
                </WrapPanel>
                <ListBox Grid.Row="1" ItemsSource="{Binding InaccessiblePaths}" />
            </Grid>
        </Grid>
    </DockPanel>
</Window>
```

> **Context menu data context:** The `ContextMenu` inherits the `ChartView`'s `DataContext` (the `ChartViewModel`), so `OpenInExplorerCommand`/`DeleteCommand`/`DeletePermanentlyCommand` resolve directly. The right-click handler in `ChartView` (`SetContextTarget`) runs on `PointerPressed` before the menu opens, so the target is set. If the menu shows for free space, `SetContextTarget(null)` makes the commands no-ops; optionally hide items when `ContextTarget` is null in a later refinement.

- [ ] **Step 3: Create the window code-behind (DI'd VM, settings geometry, init)**

`SizeScanner.Avalonia/Views/MainWindow.axaml.cs`:

```csharp
// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Avalonia.Controls;
using Avalonia.Interactivity;
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.ViewModels;

namespace SizeScanner.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly ITopLevelProvider? _topLevelProvider;

    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel, ISettingsStore settingsStore, ITopLevelProvider topLevelProvider)
        : this()
    {
        _topLevelProvider = topLevelProvider;
        DataContext = viewModel;

        var settings = settingsStore.Load();
        if (settings.WindowWidth > 0) Width = settings.WindowWidth;
        if (settings.WindowHeight > 0) Height = settings.WindowHeight;

        Opened += (_, _) =>
        {
            topLevelProvider.Register(this);
            viewModel.Initialize();
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SaveOnClose((int)Width, (int)Height, splitterDistance: 0);
        base.OnClosing(e);
    }
}
```

> `splitterDistance` is unused by the Avalonia layout (the pane is a fixed-width column); pass 0. `SaveOnClose` persists width/height + options + pane state. Window geometry is restored in the constructor.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build SizeScanner.Avalonia\SizeScanner.Avalonia.csproj`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add SizeScanner.Avalonia\Views\MainWindow.axaml SizeScanner.Avalonia\Views\MainWindow.axaml.cs SizeScanner.Avalonia\Converters\BoolToGridLengthConverter.cs
git commit -m "feat: build MainWindow layout, context menu, shortcuts"
```

---

## Task 13: Compose dependency injection

Register every service, the `ChartViewModel`, the `MainWindowViewModel`, and resolve `MainWindow` from the container.

**Files:**
- Modify: `SizeScanner.Avalonia/App.axaml.cs`

- [ ] **Step 1: Replace `ConfigureServices` and main window resolution**

In `SizeScanner.Avalonia/App.axaml.cs`, update the body of `OnFrameworkInitializationCompleted` and `ConfigureServices`:

```csharp
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            desktop.MainWindow = provider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ITopLevelProvider, TopLevelProvider>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<IFileSystemActions, WindowsFileSystemActions>();
        services.AddSingleton<IElevationService, WindowsElevationService>();
        services.AddSingleton<IDriveProvider, DriveProvider>();
        services.AddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        services.AddSingleton<ChartViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
```

Add the needed usings at the top:

```csharp
using SizeScanner.Avalonia.Abstractions;
using SizeScanner.Avalonia.Services;
```

(`SizeScanner.Avalonia.ViewModels` and `SizeScanner.Avalonia.Views` are already imported.)

- [ ] **Step 2: Build and run**

Run: `dotnet build SizeScanner.Avalonia\SizeScanner.Avalonia.csproj`
Expected: build succeeds.
Run: `dotnet run --project SizeScanner.Avalonia\SizeScanner.Avalonia.csproj`
Expected: the full window opens with toolbar, drive buttons, empty chart area, and an inaccessible pane. Close it.

- [ ] **Step 3: Commit**

```powershell
git add SizeScanner.Avalonia\App.axaml.cs
git commit -m "feat: wire dependency injection for Avalonia app"
```

---

## Task 14: Manual verification

No new tests; this validates runtime behavior against real folders and drives.

- [ ] **Step 1: Run the app**

Run: `dotnet run --project SizeScanner.Avalonia\SizeScanner.Avalonia.csproj -c Debug`

- [ ] **Step 2: Scan a folder (Ctrl+O or Browse)**

Pick a folder with nested subfolders. Confirm:
- Progress bar + "Scanning..." status appear during the scan, then "Ready".
- Nested doughnut renders with multiple rings and per-slice colors.

- [ ] **Step 3: Verify ring orientation matches WinForms**

WinForms draws **level-0 (top-level children) on the OUTER ring**. Run `ScannerUiWinForms` side-by-side (`dotnet run --project ScannerUiWinForms\ScannerUiWinForms.csproj`) on the same folder and compare.
- Expected: top-level children are on the outer ring, and nested children appear one ring inward per directory level.
- If orientation is wrong, fix the `RingIndex`/radius mapping in `SunburstChartControl` rather than changing the `SunburstChartBuilder` data contract (`RingIndex == Level`, where `0` is the outer ring). Rebuild and re-run `dotnet test SizeScanner.Avalonia.Tests`.

- [ ] **Step 4: Verify interactions**

- Hover a slice → status bar shows the full path. No floating custom tooltip is expected in this plan.
- Left-click a directory slice → chart scopes in; nav bar appears with "Go up" / "Go to root" and the scope label.
- "Go up" / "Go to root" navigate correctly.
- Right-click a file/folder slice → context menu: Open in Explorer (selects in Explorer), Delete (recycle bin, with confirm), Delete permanently (with confirm).
- Right-click the free-space slice → no actionable menu.

- [ ] **Step 5: Verify options + pane + drive scan**

- Toggle "Show/Hide free space" and change the Filter threshold → chart updates; settings file `%AppData%\SizeScanner\settings.avalonia.json` updates.
- "Toggle Inaccessible" hides/shows the right pane.
- Scan a drive (e.g., C:) → free-space and inaccessible synthetic slices behave per the free-space option; inaccessible list + total populate; "Relaunch as administrator" appears only when there are inaccessible items and not already elevated.

- [ ] **Step 6: Verify shortcuts + persistence**

- F5 rescans; Esc cancels a running scan; Ctrl+O opens the folder picker.
- Resize the window, change options, close, reopen → geometry and options restored from `settings.avalonia.json`.

- [ ] **Step 7: Commit any tuning**

```powershell
git add -A
git commit -m "fix: tune Avalonia chart orientation to match WinForms"
```

(Skip if no changes were needed.)

---

## Task 15: Solution build + GitHub CI integration

Ensure both UIs build together and GitHub CI compiles the new projects. Keep WinForms as the release artifact. Only the GitHub workflow is in scope.

**Files:**
- Modify: `.github/workflows/dotnet-desktop.yml`

- [ ] **Step 1: Verify the whole solution builds and all tests pass**

Run: `dotnet restore SizeScanner.slnx`
Run: `dotnet build SizeScanner.slnx -c Release`
Run: `dotnet test ScannerCore.Tests/ScannerCore.Tests.csproj -c Release`
Run: `dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj -c Release`
Expected: all succeed.

- [ ] **Step 2: Add the Avalonia test run + build artifact to GitHub CI**

In `.github/workflows/dotnet-desktop.yml`, after the existing "Execute unit tests" step, add a second test step:

```yaml
      - name: Execute Avalonia UI tests
        run: >
          dotnet test SizeScanner.Avalonia.Tests/SizeScanner.Avalonia.Tests.csproj
          -c ${{ matrix.configuration }}
          --verbosity normal
          --logger "trx;LogFileName=avalonia-test-results.trx"
          --collect:"XPlat Code Coverage"
          --results-directory TestResults
```

Because the existing "Build the application" step builds `SizeScanner.slnx`, the new projects are already compiled by CI — no further build change is required. Optionally extend the "Upload build artifacts" `path` to include the Avalonia output:

```yaml
          path: |
            ScannerUiWinForms/bin/${{ matrix.configuration }}/net10.0-windows/win-x64/
            SizeScanner.Avalonia/bin/${{ matrix.configuration }}/net10.0-windows/win-x64/
            ScannerCore/bin/${{ matrix.configuration }}/net10.0-windows/win-x64/
```

Leave `.github/workflows/release.yml` unchanged (WinForms remains the released app for now).

- [ ] **Step 3: Commit**

```powershell
git add .github/workflows/dotnet-desktop.yml
git commit -m "ci: build and test SizeScanner.Avalonia"
```

---

## Task 16: Update documentation

Reflect the new project in the agent/contributor guide.

**Files:**
- Modify: `AGENTS.md`

- [ ] **Step 1: Add the new projects to the solution-layout table**

In `AGENTS.md`, add rows under the solution layout table:

```markdown
| `SizeScanner.Avalonia/` | Avalonia UI (MVVM + custom sunburst chart) — modern front-end; Windows-only due to ScannerCore |
| `SizeScanner.Avalonia.Tests/` | xUnit tests for Avalonia chart builder, services, and view-models |
```

Add a short note near the conventions section:

```markdown
- Avalonia UI lives in `SizeScanner.Avalonia/`; keep platform/IO behind interfaces in `Abstractions/` with Windows implementations in `Services/`. Chart building belongs in `Charting/SunburstChartBuilder.cs`; view-models in `ViewModels/`.
```

- [ ] **Step 2: Commit**

```powershell
git add AGENTS.md
git commit -m "docs: document SizeScanner.Avalonia in AGENTS.md"
```

---

## Self-Review Notes

- **Spec coverage:** Every WinForms capability is mapped — drive buttons (Task 5/10/12), browse/rescan/cancel/progress (10/12), free-space + filter options (10/12), nested chart (7/8/11), hover status path (9/11), click-to-scope + go-up/go-to-root (9/11), right-click context menu open/delete/delete-permanently with free-space suppression (9/11/12), inaccessible pane + total + relaunch-as-admin (10/12), status bar (12), keyboard shortcuts F5/Esc/Ctrl+O (12), settings persistence incl. window geometry (3/10/12). Old project untouched (additive only). Floating custom tooltip parity is intentionally deferred.
- **Sunburst model:** Nested rings are represented by `SunburstSegment` data with ring index, angular span, size, color, and optional source `FsItem`. `SunburstHitTest` performs polar hit-testing against the same data, and `SunburstChartControl` renders annular sectors without a third-party chart dependency.
- **SOLID:** All external concerns are interfaces (`IScanService`, `ISettingsStore`, `IFileSystemActions`, `IElevationService`, `IDriveProvider`, `IFolderPicker`, `IDialogService`, `ITopLevelProvider`) with single-responsibility implementations, composed via DI; view-models depend on abstractions and are unit-tested with fakes.
- **Type consistency:** `SunburstChart`/`SunburstSegment`/`SunburstHitTest`/`SliceHsb` names are stable across tasks; `ChartViewModel` method names (`SetScan`, `Refresh`, `Hover`, `ClearHover`, `TryScopeAt`, `ResolveNode`, `SetContextTarget`, `IsFreeSpace`) match their usages in Task 11/12; command names (`GoUpCommand`, `GoToRootCommand`, `ScanDriveCommand`, `BrowseCommand`, `RescanCommand`, `CancelScanCommand`, `ToggleInaccessiblePaneCommand`, `RelaunchAsAdminCommand`, `OpenInExplorerCommand`, `DeleteCommand`, `DeletePermanentlyCommand`) match the XAML bindings.
- **Known risks called out inline:** CommunityToolkit.Mvvm + C# 14 build caveat (Task 1), `FsItem.Parent` internal-setter handling (Task 9), settings load-then-merge (Task 10), custom sunburst geometry/hit-testing verification (Task 14).
