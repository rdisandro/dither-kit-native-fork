# Future-Retro styles and effects

DitherKit's default look follows **Future-Retro**, Near AI's visual-content layer: light drawn by a small machine and shown on a visible pixel grid, in one blue on void. dither-core's multi-hue tones still exist, unchanged and pixel-identical to upstream, but every component now defaults to the Future-Retro ramps.

Source: [nearai/future-retro](https://github.com/nearai/future-retro) @ `90b083d444bbb9322046fb5330820015750d18aa` (v0.3: `brand/BRAND.md`, `brand/specs/{color,texture-and-process,motion,chrome-and-components}.md`, `brand/tokens/tokens.css`, `library/core/{cc.js,scenes.js}`). Brand and interaction rules come from the [Near AI OS Design System](https://github.com/nearai/near-ai-os-design-system) @ `fe19acd737dfbb26ba3ac263cb3cf3e718a17b81` (NDS-4, NDS-45, NDS-46; `docs/patterns/13-web.md` § *Buttons*, *Art direction*). Values below are copied from those files; no Future-Retro library code or reference images are vendored.

## What changed, and where it comes from

| Future-Retro rule | DitherKit |
| --- | --- |
| **One pigment.** Near AI blue on void; the canvas ramp is blue-ink `#035CA3` → blue `#0091FD` → sky `#83DCFF` → marble `#FAFAF7` | `Tone::Near` (the default tone). `Tone::Ink` is the neutral ramp from the same tokens (slate `#26262B` → pewter `#808088` → silver `#C1C1C1` → marble) for a second series without a second hue |
| **Quantise into the ramp with ordered dither**, never an in-between color | A lit pixel's shade level picks a ramp position; the pixel takes the step below or above it by its ordered rank (below). Constant colors (strokes, dots) take the nearest step |
| **4×4 Bayer** for fields | `Gradient` and `Background` default to `Matrix::Bayer4` |
| **Dither fields**: clouds (the house field), marble, rings, caustics, at 160 px per noise unit with contrast 0.1 and density 1.25 | `Field::Clouds` (default), `Marble`, `Rings`, `Caustics`, evolved in absolute grid units so the grain doesn't stretch with the surface |
| **Energy**: idle 0.18 / hover 0.32 (cards 0.12 / 0.28); hover adds energy, not movement | `Energy::FIELD` / `Energy::CARD`; backgrounds integrate phase at `energy × 4` per second and ease between idle and hover over ~200 ms |
| **Film rate**: 24 fps, paused offscreen, one still frame under reduced motion | `FILM_FPS`, `STILL_PHASE` (3 s). SwiftUI uses `TimelineView(.animation(minimumInterval: 1/24))`, GTK throttles its tick callback, WinUI's timer runs at 1/24 s. Reduce Motion (or GTK animations off, or WinUI `AnimationsEnabled = false`) holds the still frame |
| **Dot-matrix chart** (the app's GlassBarGraph): dots lit from the axis, brightening toward the tip; unlit dots a faint grid | `Fill::Matrix`. Bars align dots to their left edge and the axis on a two-cell pitch; meters fill left to right. Unlit dots are body ink at 9% |
| **Rules**: faint hairlines; dotted rules on a two-cell pitch | Ramp charts draw grid lines as body ink at 14% (the cc-dots axis), dotted; the baseline solid. Rules sit below bloom's alpha cutoff so they never glow |
| **Glows are blue** | Ramp charts emit `BLOOM` with a third argument: glow pixels become opaque blue-ink, thinned by the ordered dither, instead of faded copies of their neighbor |
| **The current column is a blue signal** | Ramp charts draw the hover crosshair dotted Near AI blue, with marble-centered hover dots |
| **Buttons**: flat blue with void ink and the tab bar's glow (`0 14 30 -12` at 65%, hover `0 18 36 -12` at 80%), hover mixes in 12% white; secondary is a 1 px marble outline that fills marble on hover; ghost is text with a 1 px underline; press nudges 1 px down. No halftone fills, checker hover, gradients or arrows | `ButtonStyle::chrome()` returns these colors. Ramp-tone `DitherButton`s are native flat chrome in all three toolkits; the `BUTTON` op draws the same flat fills for ramp tones (no texture, checker or sparkles) |
| **Motion tokens**: reveals 600 ms ease-out cubic; hover and press 160 ms; nothing bounces | Chart, pie, radar and sparkline entrances take 600 ms (`REVEAL_SECONDS`, `ease_out_cubic`). Meters ease to new values instead of springing |
| **Chrome**: void field, obsidian cards with a slate hairline, continuous corners, mono uppercase labels, Terminal tooltips | `swift/Sources/DitherKit/Theme.swift` (`FutureRetro`, `.ditherCard()`, `.ditherLabel()`), `dither_gtk::future_retro_css()` / `install_future_retro_css()`, and `DitherKit.WinUI.FutureRetro`. Native apps keep the platform's system face (NDS-4) and use its monospace for labels. Corner radii are the app's continuous values: control 8, card 14 |
| **Avatars**: one pigment | `avatar_tone()` lights the identicon in the ramp's pigment, accents it in its bright tint, and leaves the rest slate |

## Display-list additions

All additions are opt-in through new values, so every upstream program renders exactly as before. The 39 upstream scenes still match dither-core 0.1.0 with zero mismatched pixels.

- **Ramp tones.** A tone's chroma slot holds `-id`: `-1` Near, `-2` Ink. Shaders keep the OKLCH path for non-negative chroma.
- **Ordered rank.** Coverage now returns a rank `u` in `[0, 1)` for each covered pixel, `threshold / intensity`, or the 4×4 Bayer threshold where a pixel has no coverage test (lines, rims, edges, dots). A ramp shade at level `ℓ` takes position `p = clamp(1 + 4(ℓ − 0.3), 1, 4)` and step `floor(p) + (u < fract(p))`. Ranks of an ordered matrix nest, so the brighter step spreads evenly through the covered pixels.
- **`Fill::Matrix` = 4**: see above. In `BAR` it draws the unlit grid above the value; in `METER` it draws the unlit track.
- **Fields 5–8**: clouds, marble, rings, caustics (`LIGHT_SCALE = 3/160` units per cell; fbm with octave gain 0.5 and lacunarity 2.03; then `((v − 0.5) × 1.3 + 0.5) × 1.25`, clamped). Their shade level peaks at `0.3 + 0.42 × light`, so saturated light stays a dither of blue and sky.
- **`BLOOM` third argument** `1`: blue-ink glow.
- **`AVATAR` hue slot** `< 0`: a ramp identicon.
- **`SWATCH` / `METER` / `BUTTON`** with a ramp tone: dithered chip, faint dot track, flat fills.

## Fixtures

`fixtures/scenes.json` keeps the 39 upstream scenes byte-for-byte and appends 35 Future-Retro scenes (`fr_*`, `"source": "vm"`). dither-core cannot draw them, so their PNGs pin the reference interpreter's own output. `dither-golden --check` still compares every upstream scene against dither-core and rejects any drift in the Future-Retro PNGs. Every port's encoder and shader tests run all 74 scenes.

## Not adopted

Future-Retro's scenes (the gateway hall, globe, code rain, network, commons, tool cluster), image treatments (Floyd–Steinberg, halftone, engraving, glyphs) and the glyph field are illustrations and image processes, not component styles; they stay in Future-Retro. Upstream tones, fills, fields and textured buttons remain available for compatibility, but they are not Future-Retro and the galleries don't use them.

## Governance note

Future-Retro's implementing guide says app surfaces take no Future-Retro art unless the OS design system names a place for it, and NDS-46 says any app theme keeps the Near AI colors and flat squircle radii. This port keeps both the colors and the flat radii, and it is a native UI toolkit rather than a marketing page. Whether native app surfaces should carry the dithered art is a call for Near AI design.
