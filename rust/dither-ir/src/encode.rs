//! Component encoders: the same option types as `dither-core`, but each
//! `encode` produces a display-list [`Program`] instead of a pixel grid.
//!
//! Geometry (scales, curve interpolation, slice angles, polygon points) is
//! computed here on the CPU; everything per-pixel happens in the shaders.
//! The Swift and C# ports mirror this file function for function.

use crate::color::{Rgba, Tone, palette};
use crate::program::{NONE, Program, op, rgba, tone};

/// A threshold matrix.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Matrix {
    Bayer2,
    Bayer4,
    #[default]
    Bayer8,
    Halftone,
    Noise,
}

impl Matrix {
    pub const ALL: [Matrix; 5] = [Matrix::Bayer2, Matrix::Bayer4, Matrix::Bayer8, Matrix::Halftone, Matrix::Noise];

    pub fn id(self) -> f32 {
        match self {
            Matrix::Bayer2 => 0.0,
            Matrix::Bayer4 => 1.0,
            Matrix::Bayer8 => 2.0,
            Matrix::Halftone => 3.0,
            Matrix::Noise => 4.0,
        }
    }
}

/// How a shape's interior is textured.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Fill {
    #[default]
    Gradient,
    Dotted,
    Hatched,
    Solid,
    /// Future-Retro dot matrix (the app's GlassBarGraph): one dot per
    /// two cells, lit from the axis up and brightening toward the tip.
    /// Bars and meters keep their unlit dots as a faint grid.
    Matrix,
}

impl Fill {
    pub const ALL: [Fill; 5] = [Fill::Gradient, Fill::Dotted, Fill::Hatched, Fill::Solid, Fill::Matrix];

    pub fn id(self) -> f32 {
        match self {
            Fill::Gradient => 0.0,
            Fill::Dotted => 1.0,
            Fill::Hatched => 2.0,
            Fill::Solid => 3.0,
            Fill::Matrix => 4.0,
        }
    }
}

/// Glow strength around lit pixels.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Bloom {
    #[default]
    Off,
    Low,
    High,
    Aura,
}

impl Bloom {
    pub const ALL: [Bloom; 4] = [Bloom::Off, Bloom::Low, Bloom::High, Bloom::Aura];

    /// `(radius, strength)`.
    pub fn params(self) -> (i32, f32) {
        match self {
            Bloom::Off => (0, 0.0),
            Bloom::Low => (1, 0.45),
            Bloom::High => (2, 0.6),
            Bloom::Aura => (4, 0.7),
        }
    }

    /// Emits the bloom op; Future-Retro (`ramp`) glow is dithered blue-ink.
    fn emit(self, p: &mut Program, ramp: bool) {
        if self != Bloom::Off {
            let (r, s) = self.params();
            if ramp {
                p.bloom_ramp(r, s);
            } else {
                p.bloom(r, s);
            }
        }
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Direction {
    #[default]
    Up,
    Down,
    Left,
    Right,
    Radial,
}

impl Direction {
    pub fn id(self) -> f32 {
        match self {
            Direction::Up => 0.0,
            Direction::Down => 1.0,
            Direction::Left => 2.0,
            Direction::Right => 3.0,
            Direction::Radial => 4.0,
        }
    }
}

/// A background field. The first five come from dither-core; the rest are
/// Future-Retro's dither fields, evolved in absolute grid units (160 points
/// per noise unit at a 3-point cell) so their grain does not stretch with
/// the surface.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Field {
    Plasma,
    Waves,
    Ripple,
    Noise,
    Aurora,
    /// The house field: drifting fbm clouds.
    #[default]
    Clouds,
    /// Veined marble.
    Marble,
    /// Rings rippling out from the center.
    Rings,
    /// A warped web of light, like the floor of a pool.
    Caustics,
}

impl Field {
    pub const ALL: [Field; 9] = [
        Field::Plasma,
        Field::Waves,
        Field::Ripple,
        Field::Noise,
        Field::Aurora,
        Field::Clouds,
        Field::Marble,
        Field::Rings,
        Field::Caustics,
    ];

    /// Future-Retro's fields, house field first.
    pub const LIGHT: [Field; 4] = [Field::Clouds, Field::Marble, Field::Rings, Field::Caustics];

    pub fn id(self) -> f32 {
        match self {
            Field::Plasma => 0.0,
            Field::Waves => 1.0,
            Field::Ripple => 2.0,
            Field::Noise => 3.0,
            Field::Aurora => 4.0,
            Field::Clouds => 5.0,
            Field::Marble => 6.0,
            Field::Rings => 7.0,
            Field::Caustics => 8.0,
        }
    }

    /// Future-Retro fields carry their own contrast and gain.
    pub fn is_light(self) -> bool {
        self.id() >= 5.0
    }

    /// Density the field is tuned for: Future-Retro fields at full, the
    /// upstream fields at dither-core's 0.6.
    pub fn default_density(self) -> f32 {
        if self.is_light() { 1.0 } else { 0.6 }
    }
}

/// Future-Retro field energy (`--cc-energy-*`): how fast a field's phase
/// advances, at rest and under the pointer. Phase advances `energy * 4`
/// per second and eases between the two over about 200 ms.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Energy {
    pub idle: f32,
    pub hover: f32,
}

impl Energy {
    /// The closing CTA's clouds.
    pub const FIELD: Energy = Energy { idle: 0.18, hover: 0.32 };
    /// The card clouds: calmer, behind text.
    pub const CARD: Energy = Energy { idle: 0.12, hover: 0.28 };

    /// Advances `phase` by `dt` seconds, easing `energy` toward its target.
    /// Returns the new `(phase, energy)`.
    pub fn step(self, phase: f32, energy: f32, hovered: bool, dt: f32) -> (f32, f32) {
        let target = if hovered { self.hover } else { self.idle };
        let energy = energy + (target - energy) * (dt / 0.2).min(1.0);
        (phase + dt * energy * 4.0, energy)
    }
}

impl Default for Energy {
    fn default() -> Self {
        Energy::FIELD
    }
}

/// Future-Retro canvases run at film rate.
pub const FILM_FPS: f32 = 24.0;
/// Future-Retro's still frame: the phase shown under reduced motion.
pub const STILL_PHASE: f32 = 3.0;
/// Reveal duration (`--cc-dur-reveal`), seconds.
pub const REVEAL_SECONDS: f32 = 0.6;
/// Hover/press response (`--cc-dur-quick`), seconds.
pub const QUICK_SECONDS: f32 = 0.16;

/// Cubic ease-out, the dot chart's column rise.
pub fn ease_out_cubic(t: f32) -> f32 {
    let t = t.clamp(0.0, 1.0);
    1.0 - (1.0 - t).powi(3)
}

// ---------------------------------------------------------------------------
// Surfaces

/// A dithered gradient.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Gradient {
    pub from: Tone,
    pub to: Option<Tone>,
    pub direction: Direction,
    pub matrix: Matrix,
    pub density: f32,
}

impl Gradient {
    pub fn new(from: Tone) -> Self {
        Self { from, to: None, direction: Direction::Up, matrix: Matrix::Bayer4, density: 1.0 }
    }

    pub fn encode(&self, w: usize, h: usize) -> Program {
        let mut p = Program::new();
        let [fh, fc] = tone(self.from);
        let [th, tc] = self.to.map(tone).unwrap_or([0.0, 0.0]);
        p.push(
            op::GRADIENT,
            &[
                w as f32,
                h as f32,
                self.direction.id(),
                self.matrix.id(),
                self.density.clamp(0.0, 1.0),
                fh,
                fc,
                self.to.is_some() as u8 as f32,
                th,
                tc,
            ],
        );
        p
    }
}

/// An animated dithered background.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Background {
    pub field: Field,
    pub tone: Tone,
    pub accent: Option<Tone>,
    pub matrix: Matrix,
    pub density: f32,
}

impl Background {
    pub fn new(field: Field, tone: Tone) -> Self {
        Self { field, tone, accent: None, matrix: Matrix::Bayer4, density: field.default_density() }
    }

    pub fn encode(&self, w: usize, h: usize, time: f32) -> Program {
        let mut p = Program::new();
        let [h0, c0] = tone(self.tone);
        let [ah, ac] = self.accent.map(tone).unwrap_or([0.0, 0.0]);
        p.push(
            op::BACKGROUND,
            &[
                w as f32,
                h as f32,
                self.field.id(),
                self.matrix.id(),
                self.density.clamp(0.0, 1.0),
                time,
                h0,
                c0,
                self.accent.is_some() as u8 as f32,
                ah,
                ac,
            ],
        );
        p
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ButtonStyle {
    #[default]
    Solid,
    Outline,
    Ghost,
}

impl ButtonStyle {
    pub fn id(self) -> f32 {
        match self {
            ButtonStyle::Solid => 0.0,
            ButtonStyle::Outline => 1.0,
            ButtonStyle::Ghost => 2.0,
        }
    }

    /// Texture density at rest, hovered, and pressed.
    pub fn density(self, hovered: bool, pressed: bool) -> f32 {
        let (rest, hover, press) = match self {
            ButtonStyle::Solid => (0.32, 0.55, 0.85),
            ButtonStyle::Outline => (0.0, 0.3, 0.6),
            ButtonStyle::Ghost => (0.0, 0.25, 0.5),
        };
        if pressed {
            press
        } else if hovered {
            hover
        } else {
            rest
        }
    }

    /// Background tint behind the label, if any.
    pub fn surface(self, tone: Tone) -> Option<Rgba> {
        match self {
            ButtonStyle::Solid if tone.is_ramp() => Some(tone.base()),
            ButtonStyle::Solid => Some(tone.shade(0.22)),
            _ => None,
        }
    }

    /// Native chrome for a Future-Retro button (Near AI OS Design System,
    /// marketing buttons): flat fills, no texture, no sparkles.
    pub fn chrome(self, tone: Tone, hovered: bool, pressed: bool) -> ButtonChrome {
        let hot = hovered || pressed;
        let base = if tone.is_ramp() { tone.base() } else { tone.shade(0.55) };
        match self {
            // Flat pigment with void ink and the tab-bar glow below; hover
            // mixes 12% white into the fill and lifts the glow.
            ButtonStyle::Solid => ButtonChrome {
                fill: Some(if hot { base.mix(Rgba::WHITE, 0.12) } else { base }),
                border: None,
                label: palette::VOID,
                glow: Some(base.fade(if hot { 0.8 } else { 0.65 })),
                underline: None,
            },
            // A 1px marble outline; hover fills it marble with void ink.
            ButtonStyle::Outline => ButtonChrome {
                fill: hot.then_some(palette::MARBLE),
                border: Some(palette::MARBLE),
                label: if hot { palette::VOID } else { palette::MARBLE },
                glow: None,
                underline: None,
            },
            // Text only, with a 1px pigment underline; hover turns it blue.
            ButtonStyle::Ghost => ButtonChrome {
                fill: None,
                border: None,
                label: if hot { base } else { palette::MARBLE },
                glow: None,
                underline: Some(base),
            },
        }
    }
}

/// Colors for a Future-Retro button drawn with native controls.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ButtonChrome {
    pub fill: Option<Rgba>,
    pub border: Option<Rgba>,
    pub label: Rgba,
    /// Glow below the button: `0 14 30 -12` (hover `0 18 36 -12`) at this color.
    pub glow: Option<Rgba>,
    pub underline: Option<Rgba>,
}

impl ButtonChrome {
    /// Glow `(y offset, blur radius)` in points, after the app's tab bar.
    pub fn glow_geometry(hovered: bool) -> (f32, f32) {
        if hovered { (18.0, 36.0) } else { (14.0, 30.0) }
    }
}

pub fn button(w: usize, h: usize, tone_: Tone, style: ButtonStyle, density: f32, time: f32) -> Program {
    let mut p = Program::new();
    let [th, tc] = tone(tone_);
    p.push(op::BUTTON, &[w as f32, h as f32, style.id(), density, time, th, tc]);
    p
}

pub fn meter(w: usize, h: usize, value: f32, tone_: Tone, fill: Fill) -> Program {
    let mut p = Program::new();
    let [th, tc] = tone(tone_);
    p.push(op::METER, &[w as f32, h as f32, value, fill.id(), th, tc]);
    p
}

pub fn swatch(tone_: Tone) -> Program {
    let mut p = Program::new();
    p.push(op::SWATCH, &tone(tone_));
    p
}

/// 64-bit FNV-1a.
pub fn fnv1a(bytes: &[u8]) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for b in bytes {
        h ^= *b as u64;
        h = h.wrapping_mul(0x0000_0100_0000_01b3);
    }
    h
}

/// A deterministic 8x8 identicon in one tone. A Future-Retro ramp lights
/// its pattern in the ramp's pigment, accents in its bright tint, and
/// leaves the rest slate; any other tone uses that tone's hue.
pub fn avatar_tone(name: &str, tone_: Tone) -> Program {
    match tone_.ramp() {
        Some(id) => avatar_hue(name, -(id as f32)),
        None => avatar(name, Some(tone_.hue())),
    }
}

fn avatar_hue(name: &str, hue: f32) -> Program {
    let h = fnv1a(name.as_bytes());
    let bits = h as u32;
    let mirror_x = (h >> 32) & 1 == 0;
    let acc = ((h >> 41) as u32) & 0x7F_FFFF;
    let mut p = Program::new();
    p.push(
        op::AVATAR,
        &[
            (bits & 0xFFFF) as f32,
            (bits >> 16) as f32,
            mirror_x as u8 as f32,
            hue,
            (acc & 0xFFFF) as f32,
            (acc >> 16) as f32,
        ],
    );
    p
}

/// A deterministic 8x8 identicon. Always render it into an 8x8 grid.
pub fn avatar(name: &str, hue: Option<f32>) -> Program {
    let h = fnv1a(name.as_bytes());
    let bits = h as u32;
    let mirror_x = (h >> 32) & 1 == 0;
    let hue = hue.unwrap_or(((h >> 33) % 180) as f32 * 2.0);
    let acc = ((h >> 41) as u32) & 0x7F_FFFF;
    let mut p = Program::new();
    p.push(
        op::AVATAR,
        &[
            (bits & 0xFFFF) as f32,
            (bits >> 16) as f32,
            mirror_x as u8 as f32,
            Tone::Hue(hue).hue(),
            (acc & 0xFFFF) as f32,
            (acc >> 16) as f32,
        ],
    );
    p
}

// ---------------------------------------------------------------------------
// Charts

#[derive(Clone, Debug, PartialEq)]
pub struct Series {
    pub label: String,
    pub tone: Tone,
    pub fill: Fill,
    pub values: Vec<f64>,
    pub hidden: bool,
}

impl Series {
    pub fn new(label: impl Into<String>, tone: Tone, values: impl Into<Vec<f64>>) -> Self {
        Self { label: label.into(), tone, fill: Fill::Gradient, values: values.into(), hidden: false }
    }

    pub fn fill(mut self, fill: Fill) -> Self {
        self.fill = fill;
        self
    }

    pub fn hidden(mut self, hidden: bool) -> Self {
        self.hidden = hidden;
        self
    }
}

/// A "nice" linear value scale starting at zero.
#[derive(Clone, Debug, PartialEq)]
pub struct Scale {
    pub max: f64,
    pub ticks: Vec<f64>,
}

impl Scale {
    pub fn nice(max: f64, count: usize) -> Self {
        let max = if max.is_finite() && max > 0.0 { max } else { 1.0 };
        let raw = max / count.max(1) as f64;
        let mag = 10f64.powf(raw.log10().floor());
        let step = [1.0, 2.0, 2.5, 5.0, 10.0].iter().map(|m| m * mag).find(|s| *s >= raw).unwrap_or(10.0 * mag);
        let top = (max / step).ceil() * step;
        let ticks = (0..=((top / step).round() as usize)).map(|i| i as f64 * step).collect();
        Self { max: top, ticks }
    }

    /// Pixel row (0 = top) for `value` in a plot `h` pixels tall.
    pub fn y(&self, value: f64, h: usize) -> f32 {
        let span = h.saturating_sub(1) as f32;
        span - (value / self.max).clamp(0.0, 1.0) as f32 * span
    }
}

/// Compact number formatting: `950`, `1.2k`, `3.4M`.
pub fn format_value(v: f64) -> String {
    let a = v.abs();
    let (n, suffix) = if a >= 1e9 {
        (v / 1e9, "B")
    } else if a >= 1e6 {
        (v / 1e6, "M")
    } else if a >= 1e3 {
        (v / 1e3, "k")
    } else {
        (v, "")
    };
    if (n - n.round()).abs() < 0.05 { format!("{}{suffix}", n.round() as i64) } else { format!("{n:.1}{suffix}") }
}

/// Smooth, overshoot-free interpolation at fractional index `f`.
pub fn interpolate(values: &[f64], f: f32) -> f64 {
    match values.len() {
        0 => 0.0,
        1 => values[0],
        n => {
            let f = f.clamp(0.0, (n - 1) as f32);
            let i = (f.floor() as usize).min(n - 2);
            let t = (f - i as f32) as f64;
            let at = |k: isize| values[k.clamp(0, n as isize - 1) as usize];
            let (p0, p1, p2, p3) = (at(i as isize - 1), at(i as isize), at(i as isize + 1), at(i as isize + 2));
            let v = 0.5
                * (2.0 * p1
                    + (-p0 + p2) * t
                    + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t * t
                    + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t * t * t);
            v.clamp(p1.min(p2), p1.max(p2))
        }
    }
}

/// Guide color for grid lines and crosshairs.
pub fn guide() -> Rgba {
    Tone::Grey.shade(0.55).fade(0.45)
}

/// Future-Retro rules: faint ink hairlines (dotted on a two-cell pitch for
/// grid lines, solid for the axis).
pub fn rule() -> Rgba {
    palette::RULE
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum CartesianKind {
    #[default]
    Area,
    Line,
    Bar,
}

/// Options for area, line, and bar charts.
#[derive(Clone, Debug)]
pub struct Cartesian<'a> {
    pub kind: CartesianKind,
    pub series: &'a [Series],
    pub stacked: bool,
    pub progress: f32,
    pub hover: Option<usize>,
    pub selected: Option<usize>,
    pub bloom: Bloom,
    pub y_max: Option<f64>,
    pub grid_lines: bool,
    pub tick_count: usize,
    /// Draw the dashed baseline (sparklines turn it off).
    pub baseline: bool,
}

impl<'a> Cartesian<'a> {
    pub fn new(kind: CartesianKind, series: &'a [Series]) -> Self {
        Self {
            kind,
            series,
            stacked: false,
            progress: 1.0,
            hover: None,
            selected: None,
            bloom: Bloom::Off,
            y_max: None,
            grid_lines: true,
            tick_count: 4,
            baseline: true,
        }
    }

    pub fn len(&self) -> usize {
        self.series.iter().map(|s| s.values.len()).max().unwrap_or(0)
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }

    pub fn scale(&self) -> Scale {
        let visible = self.series.iter().filter(|s| !s.hidden);
        let max = self.y_max.unwrap_or_else(|| {
            if self.stacked {
                (0..self.len())
                    .map(|i| visible.clone().map(|s| s.values.get(i).copied().unwrap_or(0.0)).sum::<f64>())
                    .fold(0.0, f64::max)
            } else {
                visible.flat_map(|s| s.values.iter().copied()).fold(0.0, f64::max)
            }
        });
        Scale::nice(max, self.tick_count)
    }

    pub fn column_x(&self, i: usize, w: usize) -> f32 {
        let n = self.len().max(1);
        match self.kind {
            CartesianKind::Bar => (i as f32 + 0.5) * w as f32 / n as f32,
            _ if n == 1 => w as f32 / 2.0,
            _ => i as f32 * (w.saturating_sub(1)) as f32 / (n - 1) as f32,
        }
    }

    pub fn index_at(&self, x: f32, w: usize) -> Option<usize> {
        let n = self.len();
        if n == 0 || w == 0 {
            return None;
        }
        let i = match self.kind {
            CartesianKind::Bar => (x / w as f32 * n as f32).floor(),
            _ => (x / (w.max(2) - 1) as f32 * (n - 1) as f32).round(),
        };
        Some((i.max(0.0) as usize).min(n - 1))
    }

    /// Future-Retro chrome (slate rules, a blue crosshair, blue-ink glow)
    /// when any series uses a ramp tone.
    pub fn is_ramp(&self) -> bool {
        self.series.iter().any(|s| s.tone.is_ramp())
    }

    fn alpha_for(&self, series: usize) -> f32 {
        match self.selected {
            Some(s) if s != series => 0.3,
            _ => 1.0,
        }
    }

    fn stack_below(&self, series: usize, i: usize) -> f64 {
        self.series[..series].iter().filter(|s| !s.hidden).map(|s| s.values.get(i).copied().unwrap_or(0.0)).sum()
    }

    pub fn encode(&self, w: usize, h: usize) -> Program {
        let mut p = Program::new();
        if w < 2 || h < 2 {
            return p;
        }
        let scale = self.scale();
        let ramp = self.is_ramp();
        let (grid, grid_dash, base_dash) = if ramp { (rule(), 2, 1) } else { (guide(), 3, 2) };
        if self.grid_lines {
            for &t in scale.ticks.iter().skip(1) {
                let y = scale.y(t, h).round() as i32;
                p.line((0, y), (w as i32 - 1, y), grid, grid_dash);
            }
        }
        if self.baseline {
            p.line((0, h as i32 - 1), (w as i32 - 1, h as i32 - 1), grid, base_dash);
        }
        match self.kind {
            CartesianKind::Area | CartesianKind::Line => self.encode_curves(&mut p, &scale, w, h),
            CartesianKind::Bar => self.encode_bars(&mut p, &scale, w, h),
        }
        if self.kind != CartesianKind::Bar && self.hover.is_some_and(|i| i < self.len()) {
            // Hover overlay is painted after bloom; make sure the split exists.
            if self.bloom == Bloom::Off {
                p.bloom(0, 0.0);
            } else {
                self.bloom.emit(&mut p, ramp);
            }
            self.encode_hover(&mut p, &scale, w, h);
        } else {
            self.bloom.emit(&mut p, ramp);
        }
        p
    }

    fn encode_curves(&self, p: &mut Program, scale: &Scale, w: usize, h: usize) {
        let n = self.len();
        let reveal = self.progress.clamp(0.0, 1.0) * w as f32;
        let grow = 0.6 + 0.4 * self.progress.clamp(0.0, 1.0) as f64;
        let mut base = vec![0.0f64; w];
        let mut cols: Vec<Vec<f32>> = vec![vec![NONE; w * 2]; self.series.len()];
        for x in 0..w {
            if x as f32 > reveal {
                break;
            }
            let f = if n > 1 { x as f32 / (w - 1) as f32 * (n - 1) as f32 } else { 0.0 };
            for (si, s) in self.series.iter().enumerate().filter(|(_, s)| !s.hidden) {
                let v = interpolate(&s.values, f) * grow;
                let b = if self.stacked { base[x] } else { 0.0 };
                cols[si][x * 2] = scale.y(b + v, h);
                cols[si][x * 2 + 1] = scale.y(b, h);
                if self.stacked {
                    base[x] += v;
                }
            }
        }
        let strength = if self.kind == CartesianKind::Area { 1.0 } else { 0.28 };
        let offsets: Vec<Option<f32>> = self
            .series
            .iter()
            .enumerate()
            .map(|(si, s)| (!s.hidden).then(|| p.table(&cols[si])))
            .collect();
        for (si, s) in self.series.iter().enumerate() {
            let Some(off) = offsets[si] else { continue };
            let [th, tc] = tone(s.tone);
            p.push(
                op::AREA_BODY,
                &[off, w as f32, h as f32, s.fill.id(), th, tc, strength, self.alpha_for(si)],
            );
        }
        for (si, s) in self.series.iter().enumerate() {
            let Some(off) = offsets[si] else { continue };
            let mut a = vec![off, w as f32];
            a.extend(rgba(s.tone.shade(0.78).fade(self.alpha_for(si))));
            p.push(op::AREA_STROKE, &a);
        }
        if self.kind == CartesianKind::Line && w / n.max(1) >= 6 {
            for (si, s) in self.series.iter().enumerate().filter(|(_, s)| !s.hidden) {
                for (i, v) in s.values.iter().enumerate() {
                    let x = self.column_x(i, w);
                    if x > reveal {
                        break;
                    }
                    let b = if self.stacked { self.stack_below(si, i) } else { 0.0 };
                    let y = scale.y((b + v) * grow, h).round() as i32;
                    let c = s.tone.shade(0.78).fade(self.alpha_for(si));
                    p.dot(x.round() as i32, y, c, c);
                }
            }
        }
    }

    fn encode_hover(&self, p: &mut Program, scale: &Scale, w: usize, h: usize) {
        let Some(i) = self.hover.filter(|i| *i < self.len()) else { return };
        let x = self.column_x(i, w).round() as i32;
        let ramp = self.is_ramp();
        // Future-Retro: the current column is a dotted Near AI blue signal.
        let cross = if ramp { palette::NEAR_BLUE } else { Tone::Grey.shade(0.75).fade(0.7) };
        p.line((x, 0), (x, h as i32 - 1), cross, 2);
        for (si, s) in self.series.iter().enumerate().filter(|(_, s)| !s.hidden) {
            let Some(v) = s.values.get(i) else { continue };
            let b = if self.stacked { self.stack_below(si, i) } else { 0.0 };
            let y = scale.y(b + v, h).round() as i32;
            if ramp {
                p.dot(x, y, s.tone.base(), palette::MARBLE);
            } else {
                p.dot(x, y, s.tone.shade(0.7), s.tone.shade(0.98));
            }
        }
    }

    fn encode_bars(&self, p: &mut Program, scale: &Scale, w: usize, h: usize) {
        let n = self.len();
        if n == 0 {
            return;
        }
        let visible: Vec<usize> = (0..self.series.len()).filter(|i| !self.series[*i].hidden).collect();
        let k = if self.stacked { 1 } else { visible.len().max(1) };
        let band = w as f32 / n as f32;
        let group = (band * 0.72).max(1.0);
        let gap = if group / k as f32 > 3.0 { 1.0 } else { 0.0 };
        let bar_w = ((group - gap * (k as f32 - 1.0)) / k as f32).max(1.0);
        let prog = self.progress.clamp(0.0, 1.0) as f64;
        for i in 0..n {
            let x0 = i as f32 * band + (band - group) / 2.0;
            let band_alpha = match self.hover {
                Some(hv) if hv != i => 0.45,
                _ => 1.0,
            };
            let mut base = 0.0;
            for (slot, &si) in visible.iter().enumerate() {
                let s = &self.series[si];
                let v = s.values.get(i).copied().unwrap_or(0.0) * prog;
                let left = if self.stacked { x0 } else { x0 + slot as f32 * (bar_w + gap) };
                let (bx0, bx1) = (left.round() as i32, (left + bar_w).round() as i32 - 1);
                let top = scale.y(base + v, h);
                let bottom = scale.y(base, h) as i32;
                let [th, tc] = tone(s.tone);
                p.push(
                    op::BAR,
                    &[
                        bx0 as f32,
                        bx1 as f32,
                        top,
                        bottom as f32,
                        h as f32,
                        s.fill.id(),
                        th,
                        tc,
                        self.alpha_for(si) * band_alpha,
                    ],
                );
                if self.stacked {
                    base += v;
                }
            }
        }
    }
}

/// A sparkline: an area chart with no axes, grid or baseline.
pub fn sparkline(values: &[f64], tone_: Tone, w: usize, h: usize, hovered: bool, progress: f32) -> Program {
    let series = [Series::new("", tone_, values.to_vec())];
    let mut c = Cartesian::new(CartesianKind::Area, &series);
    c.grid_lines = false;
    c.baseline = false;
    c.progress = progress;
    c.bloom = if hovered { Bloom::Low } else { Bloom::Off };
    let lo = values.iter().copied().fold(f64::INFINITY, f64::min);
    let hi = values.iter().copied().fold(0.0, f64::max);
    c.y_max = Some((hi * 1.1).max(lo + 1e-9));
    c.encode(w, h)
}

#[derive(Clone, Debug, PartialEq)]
pub struct Slice {
    pub label: String,
    pub value: f64,
    pub tone: Tone,
    pub fill: Fill,
}

impl Slice {
    pub fn new(label: impl Into<String>, value: f64, tone: Tone) -> Self {
        Self { label: label.into(), value, tone, fill: Fill::Gradient }
    }

    pub fn fill(mut self, fill: Fill) -> Self {
        self.fill = fill;
        self
    }
}

#[derive(Clone, Debug)]
pub struct Pie<'a> {
    pub slices: &'a [Slice],
    pub inner_radius: f32,
    pub progress: f32,
    pub hover: Option<usize>,
    pub bloom: Bloom,
}

#[derive(Clone, Debug)]
pub struct PieGeometry {
    pub center: (f32, f32),
    pub radius: f32,
    pub inner: f32,
    /// Start/end angle per slice, radians clockwise from 12 o'clock.
    pub angles: Vec<(f32, f32)>,
}

impl PieGeometry {
    pub fn index_at(&self, x: f32, y: f32) -> Option<usize> {
        let (dx, dy) = (x - self.center.0, y - self.center.1);
        let r = (dx * dx + dy * dy).sqrt();
        if r < self.inner || r > self.radius + 2.0 {
            return None;
        }
        let a = dx.atan2(-dy).rem_euclid(std::f32::consts::TAU);
        self.angles.iter().position(|(a0, a1)| a >= *a0 && a < *a1)
    }
}

impl<'a> Pie<'a> {
    pub fn new(slices: &'a [Slice]) -> Self {
        Self { slices, inner_radius: 0.0, progress: 1.0, hover: None, bloom: Bloom::Off }
    }

    pub fn geometry(&self, w: usize, h: usize) -> PieGeometry {
        let total: f64 = self.slices.iter().map(|s| s.value.max(0.0)).sum();
        let sweep = std::f32::consts::TAU * self.progress.clamp(0.0, 1.0);
        let mut acc = 0.0f32;
        let angles = self
            .slices
            .iter()
            .map(|s| {
                let a = if total > 0.0 { (s.value.max(0.0) / total) as f32 * sweep } else { 0.0 };
                let r = (acc, acc + a);
                acc += a;
                r
            })
            .collect();
        let radius = (w.min(h) as f32 / 2.0 - 2.5).max(1.0);
        PieGeometry {
            center: (w as f32 / 2.0, h as f32 / 2.0),
            radius,
            inner: radius * self.inner_radius.clamp(0.0, 0.95),
            angles,
        }
    }

    pub fn encode(&self, w: usize, h: usize) -> Program {
        let geo = self.geometry(w, h);
        let mut p = Program::new();
        let mut table = Vec::with_capacity(self.slices.len() * 5);
        for (s, (a0, a1)) in self.slices.iter().zip(&geo.angles) {
            let [th, tc] = tone(s.tone);
            table.extend([*a0, *a1, th, tc, s.fill.id()]);
        }
        let off = p.table(&table);
        p.push(
            op::PIE,
            &[
                geo.center.0,
                geo.center.1,
                geo.radius,
                geo.inner,
                (self.slices.len() > 1) as u8 as f32,
                self.hover.map(|h| h as f32).unwrap_or(-1.0),
                self.slices.len() as f32,
                off,
            ],
        );
        self.bloom.emit(&mut p, self.slices.iter().any(|s| s.tone.is_ramp()));
        p
    }
}

#[derive(Clone, Debug)]
pub struct Radar<'a> {
    pub series: &'a [Series],
    pub max: Option<f64>,
    pub progress: f32,
    pub selected: Option<usize>,
    pub rings: usize,
    pub bloom: Bloom,
}

#[derive(Clone, Debug)]
pub struct RadarGeometry {
    pub center: (f32, f32),
    pub radius: f32,
    pub axis_tips: Vec<(f32, f32)>,
}

impl<'a> Radar<'a> {
    pub fn new(series: &'a [Series]) -> Self {
        Self { series, max: None, progress: 1.0, selected: None, rings: 4, bloom: Bloom::Off }
    }

    fn axes(&self) -> usize {
        self.series.iter().map(|s| s.values.len()).max().unwrap_or(0)
    }

    pub fn geometry(&self, w: usize, h: usize) -> RadarGeometry {
        let n = self.axes().max(1);
        let center = (w as f32 / 2.0, h as f32 / 2.0);
        let radius = (w.min(h) as f32 / 2.0 - 2.0).max(1.0);
        let axis_tips = (0..n)
            .map(|i| {
                let a = -std::f32::consts::FRAC_PI_2 + std::f32::consts::TAU * i as f32 / n as f32;
                (center.0 + a.cos() * radius, center.1 + a.sin() * radius)
            })
            .collect();
        RadarGeometry { center, radius, axis_tips }
    }

    pub fn encode(&self, w: usize, h: usize) -> Program {
        let geo = self.geometry(w, h);
        let mut p = Program::new();
        let n = self.axes();
        if n < 3 {
            return p;
        }
        let c = (geo.center.0.round() as i32, geo.center.1.round() as i32);
        let ramp = self.series.iter().any(|s| s.tone.is_ramp());
        let (ring_color, ring_dash, spoke_dash) = if ramp { (rule(), 2, 2) } else { (guide(), 2, 3) };
        let at = |i: usize, f: f32| {
            let (tx, ty) = geo.axis_tips[i % n];
            (
                (geo.center.0 + (tx - geo.center.0) * f).round() as i32,
                (geo.center.1 + (ty - geo.center.1) * f).round() as i32,
            )
        };
        for ring in 1..=self.rings.max(1) {
            let f = ring as f32 / self.rings.max(1) as f32;
            for i in 0..n {
                p.line(at(i, f), at(i + 1, f), ring_color, ring_dash);
            }
        }
        for i in 0..n {
            p.line(c, at(i, 1.0), ring_color, spoke_dash);
        }
        let max = self.max.unwrap_or_else(|| {
            Scale::nice(self.series.iter().flat_map(|s| s.values.iter().copied()).fold(0.0, f64::max), self.rings).max
        });
        let prog = self.progress.clamp(0.0, 1.0);
        for (si, s) in self.series.iter().enumerate().filter(|(_, s)| !s.hidden) {
            let alpha = match self.selected {
                Some(sel) if sel != si => 0.3,
                _ => 1.0,
            };
            let pts: Vec<(f32, f32)> = (0..n)
                .map(|i| {
                    let f = (s.values.get(i).copied().unwrap_or(0.0) / max).clamp(0.0, 1.0) as f32 * prog;
                    let (tx, ty) = geo.axis_tips[i];
                    (geo.center.0 + (tx - geo.center.0) * f, geo.center.1 + (ty - geo.center.1) * f)
                })
                .collect();
            let flat: Vec<f32> = pts.iter().flat_map(|(x, y)| [*x, *y]).collect();
            let off = p.table(&flat);
            let [th, tc] = tone(s.tone);
            p.push(
                op::POLY,
                &[geo.center.0, geo.center.1, geo.radius, s.fill.id(), th, tc, alpha, n as f32, off],
            );
            let stroke = s.tone.shade(0.8).fade(alpha);
            for i in 0..n {
                let a = (pts[i].0.round() as i32, pts[i].1.round() as i32);
                let b = (pts[(i + 1) % n].0.round() as i32, pts[(i + 1) % n].1.round() as i32);
                p.line(a, b, stroke, 1);
            }
            for &(x, y) in &pts {
                p.dot(x.round() as i32, y.round() as i32, stroke, s.tone.shade(0.97).fade(alpha));
            }
        }
        self.bloom.emit(&mut p, ramp);
        p
    }
}
