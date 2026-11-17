# dither-kit-native: plan and status

The original roadmap is preserved below. For current implementation and validation status, see [README.md](README.md) and [execution evidence](docs/EXECUTION.md). The Future-Retro styles and effects (ramp tones, the dot-matrix fill, light fields 5–8, blue-ink bloom) are specified in [docs/FUTURE-RETRO.md](docs/FUTURE-RETRO.md) and [spec/SPEC.md](spec/SPEC.md).

This repo holds native ports of [dither-kit-rs](https://github.com/DeepanshuMishraa/dither-kit-rs) for three toolkits:

- **GTK4**, written in Rust with gtk4-rs and drawn with GLSL shaders
- **SwiftUI**, drawn with Metal shaders
- **WinUI 3**, written in C# and drawn with Direct2D (HLSL) shaders through ComputeSharp.D2D1

Each port does its dithering in a shader written for its own platform. To keep all three producing the same pixels, every component is first turned into the same small **display list** (a flat `f32` array). Each shader is a line-by-line port of one reference interpreter, and every port is tested against the same golden images, which are generated from upstream `dither-core`.

License: MIT OR Apache-2.0, the same as upstream (both license files are copied in).

---

## Architecture

```
component options ──(CPU encoder, per language)──► display-list program (f32[])
                                                         │
                         ┌───────────────────────────────┼──────────────────────────┐
                    GLSL (GTK)                      Metal (SwiftUI)             HLSL/D2D (WinUI)
                         └──────────── pixels (one grid of dither pixels) ──────────┘
                                   ▲ checked against fixtures/png/*.png
```

### Program format (see `rust/dither-ir/src/program.rs`)

```
[P, op, len, args..., op, len, args..., <tables>]
```
- `P` is the number of floats taken up by ops. Ops start at index 1.
- Each op is `[opcode, total_len_including_header, args...]`.
- Tables (curve columns, pie slices, polygon points) come after the ops. Ops refer to a table by its offset from the table base, which is `1 + P`.
- Ops are painted in order, and later ops overwrite earlier ones.
- A `BLOOM` op (14) splits the program in two:
  - The **base** list is every op before `BLOOM`. Bloom is applied to its result.
  - The **overlay** list is every op after `BLOOM`. It is painted on top after bloom. Chart hover crosshairs and dots go here.
  - When there is no `BLOOM` op, the whole program is the base list.
- Constant colors are 4 floats holding RGBA in 0..255. Varying colors are a tone, stored as `(hue, chroma)`. Shaders compute the OKLCH shade per pixel. A negative chroma names a Future-Retro ramp (`-1` Near AI blue, `-2` ink), dithered between its four fixed steps.
- In column tables, `NONE = -1e9` means "no geometry in this column".

| op | id | args |
|---|---|---|
| LINE | 1 | x0 y0 x1 y1 dash rgba |
| DOT | 2 | x y ring_rgba center_rgba |
| AREA_BODY | 3 | table w h fill hue chroma strength alpha |
| AREA_STROKE | 4 | table w rgba |
| BAR | 5 | bx0 bx1 top bottom h fill hue chroma alpha |
| PIE | 6 | cx cy radius inner multi hover n table (5 floats per slice: a0 a1 hue chroma fill) |
| POLY | 7 | cx cy radius fill hue chroma alpha n table (x,y pairs) |
| GRADIENT | 8 | w h dir matrix density from_h from_c has_to to_h to_c |
| BACKGROUND | 9 | w h field matrix density time h c has_accent acc_h acc_c |
| BUTTON | 10 | w h style density time h c (sparkles included) |
| METER | 11 | w h value fill h c |
| AVATAR | 12 | bits_lo16 bits_hi16 mirror_x hue acc_lo16 acc_hi (always rendered as an 8×8 grid; hue < 0 names a ramp) |
| SWATCH | 13 | h c |
| BLOOM | 14 | radius strength [glow] (radius 0 just marks where the overlay starts; glow 1 = Future-Retro blue-ink) |

Enum ids:
- matrix: bayer2 = 0, bayer4 = 1, bayer8 = 2, halftone = 3, noise = 4
- fill: gradient = 0, dotted = 1, hatched = 2, solid = 3, matrix = 4 (Future-Retro dot matrix)
- direction: up = 0, down = 1, left = 2, right = 3, radial = 4
- field: plasma = 0, waves = 1, ripple = 2, noise = 3, aurora = 4, clouds = 5, marble = 6, rings = 7, caustics = 8 (5–8 are Future-Retro)
- button style: solid = 0, outline = 1, ghost = 2
- bloom (radius, strength): low = (1, .45), high = (2, .6), aura = (4, .7)

### Parity rules for shader ports
Check these in every shader language:
- **Bresenham lines** are tested per pixel with a closed form. On the major axis, `k = |p - p0|`. The minor offset is `(2·k·a_minor + a_major) / (2·a_major)`, using integer division. It is exhaustively tested against real Bresenham in `vm.rs`.
- **`rnd`** rounds half away from zero, like Rust's `round`: `sign(v)*floor(|v|+0.5)`. Don't rely on the shader language's `round()`, because its tie behavior varies.
- **`emod`** is Euclidean modulo. GLSL, MSL and HLSL `%` on negative ints differ from it.
- **Hash** values are `uint` with a bit-preserving int→uint cast:
  - GLSL: `uint(x)`
  - Metal: `as_type<uint>(x)`
  - HLSL: `asuint(x)`
  - `hash01 = float(h) / 4294967296.0`
- **`powi(12)`** is computed by repeated squaring, not `pow`.
- **The OKLCH gamut loop** must subtract `0.005` from chroma one step at a time, exactly as the reference does. Don't replace it with a multiply.
- **Pie angle** is `atan2(dx, -dy)` wrapped into [0, τ).
- **Bloom search order** is d = 1..r, then oy = -d..d, then ox = -d..d, visiting only the ring. The first neighbor with alpha > 160 wins.
- **Outputs**: the base pass is straight alpha. The final output is premultiplied for compositing, except in tests.

---

## Status

### ✅ Done
1. **`rust/dither-ir`** has no dependencies. It contains:
   - `color.rs`: tones and OKLCH, copied from dither-core, plus `shade_hc`
   - `program.rs`: the format and its builders
   - `encode.rs`: every component as an encoder (gradient, background, button, meter, avatar, swatch, area/line/bar charts, sparkline, pie, radar), with the same option types as dither-core, plus geometry helpers (`index_at`, `PieGeometry::index_at`, `RadarGeometry`, `format_value`)
   - `vm.rs`: the **reference interpreter**, plus unit tests for the Bayer tables and closed-form Bresenham
2. **`rust/dither-golden`** renders 39 scenes with `dither-core = "=0.1.0"` and with dither-ir's VM, then compares them. **All 39 match pixel for pixel with 0 mismatches**, including bloom, sparkles, hover overlays, stacked charts, pies and radar. It writes:
   - `fixtures/scenes.json`: each scene's parameters plus its encoded `program`
   - `fixtures/png/<name>.png`: grid-resolution RGBA from dither-core
   - Run it with `cd rust && cargo run -p dither-golden -- ../fixtures`. Add `--check` to compare without writing.
3. **`rust/dither-gtk/shaders/dither.glsl`**: the full GLSL port of `vm.rs`, covering both passes and the `uStraight` test switch. **It has never been compiled or run.**

### 🔜 Next: GTK4 port (`rust/dither-gtk`)
Dependencies: `gtk4 = "0.11"` (the system has GTK 4.14), `glow = "0.18"`, `epoxy = "0.1"` + `libloading` for GL symbol loading inside GtkGLArea, and dev-dependencies `khronos-egl = { version = "6", features = ["dynamic"] }` and `png`.

1. **`renderer.rs`**: a glow-based renderer that works with any GL context.
   - Compile the shader by prepending `#version 330 core`. On GLES, prepend `#version 300 es` instead (the shader has an `#ifdef GL_ES` precision block).
   - Upload the program into an `R32F` texture, 1024 texels wide (`DATA_W`), using `texelFetch`.
   - Pass 0 renders into FBO A (w×h RGBA8). Pass 1 samples A as `uBase` and renders into FBO B.
   - Blit B to the target framebuffer with `glBlitFramebuffer(..., GL_NEAREST)`, scaling to `cell × scale_factor` device pixels and anchoring at the top-left. GL's y axis is flipped, so the destination y0 is `fb_h - h*cellpx`.
   - Uniforms: `uPass`, `uGrid`, `uStraight`, `uData` (unit 0), `uBase` (unit 1).
2. **`tests/golden.rs`**: a headless test.
   - Create an EGL surfaceless context with Mesa (`EGL_PLATFORM_SURFACELESS_MESA`, then an OpenGL core 3.3 or GLES 3 context).
   - For each scene, render the stored `program` at its w×h with `uStraight = 1`, read the pixels, and compare them to the PNG.
   - Tolerance: at most ±2 per channel, and at most 1% of pixels may differ (GPU `sin`/`atan`/`pow` drift).
   - Packages needed here: `libegl-mesa0 libgl1-mesa-dri libegl-dev`. All are installed in this workspace.
3. **`surface.rs`**: a `DitherSurface` widget wrapping `gtk::GLArea`.
   - It holds a `cell` size and an `encode: Box<dyn Fn(w, h) -> Program>`.
   - On `resize` or `render`, it computes `grid = floor(alloc / cell)`, encodes, renders, and blits.
   - Call `set_has_stencil_buffer(false)` and `set_auto_render(true)`, and use `queue_render` when state changes.
   - Load GL through epoxy.
4. **Components** follow dither-gpui's API names:
   - `DitherChart` (area, line, bar): a `gtk::Overlay` containing the surface, y-axis tick labels from `Scale.ticks` with `format_value`, x labels, a tooltip label that follows the hover index, and a legend of `DitherSwatch` + label toggle buttons that set hidden/selected.
   - `DitherPie`: hover through `PieGeometry::index_at`, with the center total.
   - `DitherRadar`: axis labels at `RadarGeometry.axis_tips`.
   - `DitherSparkline`: hover adds bloom.
   - `DitherButton`: a `gtk::Button` with the surface behind its label. Density comes from `ButtonStyle::density(hovered, pressed)`, read from `EventControllerMotion` and `GestureClick`. When density is above 0.4, a tick callback drives the sparkle time. The Solid style adds a surface tint.
   - `DitherGradient`, `DitherBackground` (tick callback × speed), `DitherAvatar` (8×8 grid, so cell = size/8), `DitherMeter` (springs to new values, with a label).
   - Motion: run a 900 ms ease-out entrance on a tick callback. Skip it when `gtk::Settings::is_gtk_enable_animations()` is false.
   - Accessibility: set an accessible label on the surfaces (`set_accessible_role` / `update_property(Label)`).
5. **`examples/gallery.rs`** shows every component. Check it under `xvfb-run` with a screenshot (llvmpipe).

### 🔜 SwiftUI port (`swift/`, Swift package `DitherKit`, macOS 14 / iOS 17)
1. **`DitherIR`** (pure Swift, no SwiftUI) mirrors `encode.rs` function for function: Tone/OKLCH, Program, all encoders, Scale, interpolate, geometry, fnv1a. Use `Double` and `Float` exactly where Rust uses f64 and f32. Tests load `../fixtures/scenes.json`, encode each scene, and compare the floats to `program` with a tolerance of 1e-4.
2. **`Shaders/Dither.metal`** ports `dither.glsl`. It needs two `[[ stitchable ]]` functions:
   - `ditherBase(float2 position, half4 color, device const float *data, int count, float cell)` for `.colorEffect`. Compute the grid coordinate as `floor(position / cell)`.
   - `ditherPost(float2 position, SwiftUI::Layer layer, device const float *data, int count, float cell)` for `.layerEffect(..., maxSampleOffset: 4·cell)`. It does bloom by sampling `layer.sample((gx+ox+0.5)*cell, ...)`. The layer is premultiplied, so un-premultiply before the `a > 160` test. Then it paints the overlay ops.
   - Pass data with `.floatArray(program)`. Use `as_type<uint>` for the hash.
3. **Views**:
   - `DitherSurface(cell:encode:)` is `Rectangle().colorEffect(...).layerEffect(...)` inside a `GeometryReader`.
   - Components: `DitherChart`, `DitherPie`, `DitherRadar`, `DitherSparkline`, `DitherButton` (a `ButtonStyle`), `DitherGradient`, `DitherBackground` (`TimelineView(.animation)`), `DitherAvatar`, `DitherMeter`, and `DitherLegend`.
   - Chrome uses SwiftUI `Text`, `.onContinuousHover`, and `@Environment(\.accessibilityReduceMotion)`.
4. **Golden test**: render each scene with `ImageRenderer` at scale 1 and cell 1, then compare to the PNGs with the same tolerance. This needs macOS, so run it in CI on `macos-15`.
5. **`Examples/Gallery`**: a SwiftUI app target.

### 🔜 WinUI 3 port (`windows/`)
1. **`DitherKit.IR`** (net8.0, cross-platform) is the C# mirror of `encode.rs`. Its xUnit fixture tests can run on Linux with `dotnet test`.
2. **`DitherKit.WinUI`** (net8.0-windows10.0.19041, Windows App SDK + `Microsoft.Graphics.Win2D` + `ComputeSharp.D2D1.WinUI`):
   - **Shader**: an `[D2DInputCount(0)] [D2DRequiresScenePosition]` `ID2D1PixelShader` that ports `dither.glsl`. Program data comes through `D2D1ResourceTexture1D<float>` (`[D2DResourceTextureIndex(0)]`), and the grid size is a captured field.
   - **Bloom**: to avoid D2D complex-input mapping, evaluate the base list at neighboring pixels inside the same shader. That is cheap because we render at grid resolution.
   - **Drawing**: render into a `CanvasRenderTarget` sized to the grid (96 DPI) through `PixelShaderEffect<T>`, then `DrawImage` it scaled by cell × DPI with `CanvasImageInterpolation.NearestNeighbor` in a `CanvasControl`.
   - **Before writing it**, check the current ComputeSharp 3.x API for resource-texture managers.
   - **Controls**: `DitherChart`, `DitherPie`, `DitherRadar`, `DitherSparkline`, `DitherButton`, `DitherGradient`, `DitherBackground`, `DitherAvatar`, `DitherMeter`, `DitherLegend`. Chrome uses XAML TextBlocks and AutomationProperties. Respect `UISettings.AnimationsEnabled`.
3. **Golden test**: a Windows-only test that renders through Win2D on a `CanvasDevice` with no window, then compares to the PNGs. Run it in CI on `windows-latest`.
4. **`DitherKit.Gallery`**: a WinUI 3 app.

### 🔜 CI, README, publishing
Add `.github/workflows/ci.yml` with three jobs:
- **ubuntu**: cargo test, golden `--check`, the GTK headless shader test under Mesa, and `dotnet test` for DitherKit.IR.
- **macos-15**: `swift test`, covering the encoder fixtures and the Metal golden test.
- **windows-latest**: build WinUI and run the D2D golden test.

Then write the README (architecture diagram, per-toolkit quick starts), add the license and a credit to upstream, and push to the GitHub repo.

---

## Layout
```
LICENSE-MIT, LICENSE-APACHE, PLAN.md
fixtures/scenes.json, fixtures/png/*.png     # generated; shared by all ports
rust/Cargo.toml                              # workspace: dither-ir, dither-golden, dither-gtk
rust/dither-ir/      encoder + reference VM (source of truth for shaders)
rust/dither-golden/  fixture generator / parity check vs dither-core
rust/dither-gtk/     shaders/dither.glsl (written); src/lib.rs is a stub
swift/               (todo)
windows/             (todo)
```

## Notes
- Upstream `dither-core` is published on crates.io as 0.1.0. The golden crate pins `=0.1.0`. If upstream changes, rerun the generator and review the diffs.
- Upstream GPUI defaults to 3 logical pixels per dither pixel. Use that as the default `cell` everywhere.
- Sparklines leave out the baseline op rather than erasing it afterward, so they match except in one edge case: while hovered, bloom can light pixels on the bottom row where upstream would have had a guide pixel. The fixtures still match exactly.
