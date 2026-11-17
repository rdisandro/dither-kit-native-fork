# Dither Kit for WinUI 3

Requires Windows 10 build 19041+, .NET 8, the Windows SDK 22621, and Visual Studio's Windows application development build tools. Run the commands below in a Visual Studio Developer PowerShell so `MSBuild.exe` is on PATH. The [Microsoft WinUI CI workflow](https://learn.microsoft.com/windows/apps/package-and-deploy/ci-for-winui3) uses Visual Studio MSBuild because its PRI/XAML tasks are not supplied by the standalone dotnet build host. The projects target SDK 22621 because ComputeSharp 3.2.0 requires its reference surface. Runtime minimum remains Windows 10 19041.

```powershell
msbuild DitherKit.Gallery/DitherKit.Gallery.csproj -restore -p:Platform=x64
./DitherKit.Gallery/bin/x64/Debug/net8.0-windows10.0.22621.0/win-x64/DitherKit.Gallery.exe
```

The unpackaged gallery includes the Windows App SDK runtime. Framework-dependent applications and the windowless test host need the [Windows App Runtime 1.7](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) installed. Tests explicitly enable the Windows App SDK bootstrap initializer, then create a software Win2D `CanvasDevice` (WARP):

```powershell
dotnet test DitherKit.IR.Tests/DitherKit.IR.Tests.csproj
msbuild DitherKit.WinUI.Tests/DitherKit.WinUI.Tests.csproj -restore -p:Platform=x64
dotnet test DitherKit.WinUI.Tests/DitherKit.WinUI.Tests.csproj -p:Platform=x64 --no-build --no-restore
```

`DitherGradient`, `DitherBackground`, `DitherChart`, `DitherPie`, `DitherRadar`, `DitherSparkline`, `DitherAvatar`, `DitherSwatch`, and `DitherMeter` expose `Properties` (the shared fixture/encoder parameter names) and `SetOption(name, value)`. The chart, pie and radar also provide typed `Series`/`Slices` setters. `DitherButton` is a real WinUI `Button`; `DitherLegend` uses keyboard-accessible native toggle buttons. Set `AccessibleLabel` to a useful description of your data.

```csharp
var chart = new DitherChart {
    Width = 480, Height = 220, Chart = "area",
    Labels = ["Jan", "Feb", "Mar"],
    Series = [new DitherSeries("Desktop", [186, 305, 237], "blue")]
};
chart.SetOption("bloom", "low");
```

`DitherSurface` hosts a Win2D `CanvasControl`. `DitherRenderer` uploads the flat display list as a Float32 one-channel 1D resource texture using the [ComputeSharp 3.2.0 resource manager API](https://github.com/Sergio0694/ComputeSharp/blob/v3.2.0/src/ComputeSharp.D2D1/Shaders/Interop/D2D1ResourceTextureManager.cs), draws the full interpreter at grid resolution, then scales with nearest-neighbor interpolation. Win2D applies drawing-session DPI; the surface does not multiply DPI twice. Bloom reevaluates only the base display list and preserves the reference neighbor visitation order. Motion follows `UISettings.AnimationsEnabled`; idle buttons do not schedule continuous draws.

On non-Windows hosts, the shader uses ComputeSharp's runtime compilation descriptor so its C# source can be checked without `D3DCOMPILER_47.dll`. Windows builds precompile the shader with FXC optimization skipped, IEEE-strict arithmetic and row-major packing. Optional D2D shader linking is disabled to compile the large interpreter once. Reconsider optimization/linking only after measuring compile time, render performance and golden parity together. Signed integer division/modulo remains intentional, so FXC performance warnings are not promoted to errors for this shader. A macOS cross-compilation does not validate HLSL, Windows packaging, native control behavior, or rendered pixels; run the Windows job for those checks. Golden tests compare all 74 fixture images, permitting at most 2 byte values of error per channel and at most 1% differing pixels.
