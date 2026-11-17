# Native port execution and validation

Implemented from the supplied archive at baseline `abaabe4`. All planned ports, component wrappers, galleries, shared fixture tests, CI jobs, licenses and documentation are present.

Source published: https://github.com/iqlusioninc/dither-kit-native

## Verified source

All three native CI jobs passed at **`e53d2db`**:
https://github.com/iqlusioninc/dither-kit-native/actions/runs/43759867023

| Platform | Validation |
| --- | --- |
| Ubuntu 24.04 | Rust workspace tests; all 39 GLSL fixtures through desktop GL and GLES under surfaceless Mesa; premultiplied-alpha/top-left nearest-blit regression; committed fixture integrity; 74 C# encoder/helper tests on .NET 8; GTK gallery capture under Xvfb/llvmpipe |
| macOS 15 | Full Swift package build; nine IR tests, including independent encoding of all 39 scenes; strict Metal/ImageRenderer comparison of all 39 scenes; production premultiplied comparison of all 39 scenes; legend visibility regression |
| Windows | Visual Studio MSBuild gallery and test builds; native FXC shader compilation; windowless Win2D/WARP comparison of all 39 golden images, with no skips |

The logical shader gate permits a maximum channel difference of 2 and at most 1% nonidentical pixels. Encoder arrays are checked independently against the fixture programs within `1e-4`. The Rust reference VM still matches pinned upstream `dither-core = "=0.1.0"` exactly in all 39 scenes. `golden --check` additionally rejects changed or missing committed JSON/PNG artifacts without rewriting them.

Additional local validation passed: native Apple OpenGL goldens, strict GTK Clippy, sandboxed SwiftPM builds, and an iOS 17 simulator-destination cross-build with correctly targeted Metal. The local Apple GL and Metal comparisons observed maximum channel drift 1.

## Future-Retro update (local, macOS 27 / Apple M5 Pro)

The [Future-Retro styles](FUTURE-RETRO.md) were validated locally before CI:

| Check | Result |
| --- | --- |
| `cargo test -p dither-ir -p dither-golden`; `dither-golden -- ../fixtures --check` | 8 VM tests pass. All 39 upstream scenes still match dither-core 0.1.0 with 0 mismatched pixels; the 35 Future-Retro scenes match their committed PNGs. `scenes.json` keeps the upstream scenes byte-identical |
| `swift test --package-path swift` | 12 IR tests (all 74 scenes independently encoded) and 3 DitherKit tests, including Metal/ImageRenderer and premultiplied comparisons of all 74 scenes. Every Future-Retro scene is pixel-exact |
| `cargo test -p dither-gtk` (CGL headless, Apple OpenGL 4.1) | All 74 GLSL goldens pass; every Future-Retro scene is pixel-exact. `glslangValidator` also accepts the shader as `#version 330 core` and `#version 300 es` |
| `dotnet test windows/DitherKit.IR.Tests` (.NET 8.0.428) | 113 tests pass, including all 74 encoder fixtures |
| `DitherKit.WinUI` compile on macOS (`-t:Compile`, runtime-shader mode) | No errors or warnings; ComputeSharp's D2D generator emits HLSL for the new helpers |
| Galleries | SwiftUI `--snapshot` export inspected (`artifacts/swift-gallery.png`); GTK `--smoke` capture on macOS passes its readiness checks; GTK flat-button CSS inspected at rest and hover. `cargo clippy -p dither-gtk -p dither-ir -p dither-golden --all-targets` is clean |

Still for CI: GLSL goldens under Linux Mesa (desktop GL and GLES), FXC compilation and the Win2D/WARP goldens on Windows, the WinUI gallery build (its PRI/XAML tasks are Windows-only), and Windows button chrome on a running app. WinUI ramp buttons have no glow; WinUI has no colored drop shadow without composition code.

## Gallery evidence

`docs/gallery-gtk.png` is the verified Linux llvmpipe gallery capture from before the Future-Retro update; CI uploads a fresh capture as the `gtk-gallery` artifact. `artifacts/swift-gallery.png` is the inspected native SwiftUI gallery export. The GTK smoke check disables animations before constructing components, waits for completed frames and stable allocation, checks seven separated axis-label groups, and verifies populated GPU chart geometry before saving the image.

Native component review fixed visibility overrides, axes positioned against actual scale geometry, GTK callback ownership cycles, WinUI hover handling for unequal-length series, and button density/ticking behavior. Encoder regressions cover formatting/saturating casts and NaN reductions.

## Build decisions

- GTK requires Rust 1.92+ and GTK 4.10+. The pure Rust IR retains its lower MSRV. System libepoxy is loaded with `libloading`; the obsolete Rust `epoxy` crate depends on yanked packages and cannot resolve a fresh build.
- ComputeSharp 3.2 requires SDK 22621 at compile time; Windows runtime minimum remains build 19041. Visual Studio MSBuild supplies the required PRI/XAML tasks. `global.json` selects .NET SDK 8 rather than an unrelated newer installed SDK.
- FXC signed division/modulo performance warnings are intentional for reference parity. They are not promoted to errors for this shader. Native shader compilation uses `SkipOptimization`, IEEE strictness and row-major packing, with normal validation retained and optional linking disabled. O0 and O3 exceeded the bounded compile limit; the accepted native gallery rebuild completed in 157 seconds. Optimization and physical-GPU performance tuning need separate measurements and the same golden gates.
- Windows CI separately checks managed-only packaging, a clean native shader rebuild and test artifacts. The managed-only stage is diagnostic and cannot satisfy the native shader or pixel gates. Time limits and compiler CPU/memory logs prevent silent indefinite builds; only text diagnostics are uploaded.

## Swift pixel transport

SwiftUI normally transports premultiplied layer colors. Converting low-alpha RGBA8 samples back to straight RGB amplifies quantization. The logical Metal golden test therefore uses straight RGBA transport in both passes, matching the plan’s base/test-output exception. It runs the identical interpreter, actual layer sampling, bloom and overlays; it does not recompute the base in the post pass or modify fixtures. Production remains premultiplied and is separately checked against premultiplied golden bytes.

## Validation limits

The Windows renderer was exercised on WARP, not a physical Windows GPU. WinUI gallery compilation passed; manual Windows control interaction and accessibility-device checks were not performed. The iOS destination cross-build passed, but iOS runtime rendering was not exercised. No package-registry release or signed application distribution is claimed.
