//! Colors, named tones, and perceptual (OKLCH) shade ramps.

/// An 8-bit sRGB color with straight (non-premultiplied) alpha.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub struct Rgba {
    pub r: u8,
    pub g: u8,
    pub b: u8,
    pub a: u8,
}

impl Rgba {
    pub const TRANSPARENT: Rgba = Rgba::new(0, 0, 0, 0);
    pub const BLACK: Rgba = Rgba::rgb(0, 0, 0);
    pub const WHITE: Rgba = Rgba::rgb(255, 255, 255);

    pub const fn new(r: u8, g: u8, b: u8, a: u8) -> Self {
        Self { r, g, b, a }
    }

    pub const fn rgb(r: u8, g: u8, b: u8) -> Self {
        Self { r, g, b, a: 255 }
    }

    /// Parses `0xRRGGBB`.
    pub const fn hex(hex: u32) -> Self {
        Self::rgb((hex >> 16) as u8, (hex >> 8) as u8, hex as u8)
    }

    pub const fn with_alpha(self, a: u8) -> Self {
        Self { a, ..self }
    }

    /// Multiplies the alpha channel by `factor` (0..=1).
    pub fn fade(self, factor: f32) -> Self {
        self.with_alpha((self.a as f32 * factor.clamp(0.0, 1.0)).round() as u8)
    }

    /// Linear interpolation in sRGB space (`t = 0` is `self`).
    pub fn mix(self, other: Rgba, t: f32) -> Rgba {
        let t = t.clamp(0.0, 1.0);
        let l = |a: u8, b: u8| (a as f32 + (b as f32 - a as f32) * t).round() as u8;
        Rgba::new(
            l(self.r, other.r),
            l(self.g, other.g),
            l(self.b, other.b),
            l(self.a, other.a),
        )
    }

    /// Composites `self` over an opaque `background`, returning an opaque color.
    pub fn over(self, background: Rgba) -> Rgba {
        background
            .with_alpha(255)
            .mix(self.with_alpha(255), self.a as f32 / 255.0)
    }

    /// Relative luminance (WCAG), used to choose legible label colors.
    pub fn luminance(self) -> f32 {
        let lin = |c: u8| {
            let c = c as f32 / 255.0;
            if c <= 0.04045 {
                c / 12.92
            } else {
                ((c + 0.055) / 1.055).powf(2.4)
            }
        };
        0.2126 * lin(self.r) + 0.7152 * lin(self.g) + 0.0722 * lin(self.b)
    }

    /// Converts an OKLCH color (`l` 0..1, `c` ~0..0.37, `h` degrees) to sRGB,
    /// reducing chroma until the color fits inside the sRGB gamut.
    pub fn oklch(l: f32, c: f32, h: f32) -> Rgba {
        let mut c = c.max(0.0);
        loop {
            if let Some(rgb) = oklch_to_srgb(l, c, h) {
                return rgb;
            }
            c -= 0.005;
            if c <= 0.0 {
                return oklch_to_srgb(l, 0.0, h).unwrap_or(Rgba::BLACK);
            }
        }
    }
}

fn oklch_to_srgb(l: f32, c: f32, h: f32) -> Option<Rgba> {
    let (a, b) = (c * h.to_radians().cos(), c * h.to_radians().sin());
    let l_ = l + 0.396_337_78 * a + 0.215_803_76 * b;
    let m_ = l - 0.105_561_346 * a - 0.063_854_17 * b;
    let s_ = l - 0.089_484_18 * a - 1.291_485_5 * b;
    let (l3, m3, s3) = (l_ * l_ * l_, m_ * m_ * m_, s_ * s_ * s_);
    let r = 4.076_741_7 * l3 - 3.307_711_6 * m3 + 0.230_969_94 * s3;
    let g = -1.268_438 * l3 + 2.609_757_4 * m3 - 0.341_319_38 * s3;
    let bb = -0.004_196_086_3 * l3 - 0.703_418_6 * m3 + 1.707_614_7 * s3;
    let eps = 1e-4;
    if [r, g, bb].iter().any(|v| *v < -eps || *v > 1.0 + eps) {
        return None;
    }
    let enc = |v: f32| {
        let v = v.clamp(0.0, 1.0);
        let s = if v <= 0.003_130_8 {
            12.92 * v
        } else {
            1.055 * v.powf(1.0 / 2.4) - 0.055
        };
        (s * 255.0).round() as u8
    };
    Some(Rgba::rgb(enc(r), enc(g), enc(bb)))
}

/// Future-Retro palette tokens (`brand/tokens/tokens.css` in nearai/future-retro).
///
/// One field (void) and one pigment (Near AI blue). Lit pixels in a
/// Future-Retro surface are always one of the four steps of a ramp, chosen
/// per pixel with an ordered Bayer dither; there are no in-between colors.
pub mod palette {
    use super::Rgba;

    /// The field: page, sections, canvases.
    pub const VOID: Rgba = Rgba::hex(0x070708);
    /// Raised surface: cards, plates.
    pub const OBSIDIAN: Rgba = Rgba::hex(0x0F0F11);
    /// Panels.
    pub const BASALT: Rgba = Rgba::hex(0x18181B);
    /// Hairline rules and borders.
    pub const SLATE: Rgba = Rgba::hex(0x26262B);
    /// Muted text (5.14:1 on void).
    pub const PEWTER: Rgba = Rgba::hex(0x808088);
    /// Body ink (11.19:1 on void).
    pub const SILVER: Rgba = Rgba::hex(0xC1C1C1);
    /// Strong ink and the brightest step of every ramp (19.26:1 on void).
    pub const MARBLE: Rgba = Rgba::hex(0xFAFAF7);
    /// The pigment: accent, primary buttons, signals (6.20:1 on void).
    pub const NEAR_BLUE: Rgba = Rgba::hex(0x0091FD);
    /// The bright tint: glows and highlights.
    pub const NEAR_SKY: Rgba = Rgba::hex(0x83DCFF);
    /// A mid step: far signals.
    pub const NEAR_DEEP_BLUE: Rgba = Rgba::hex(0x2385C7);
    /// The deepest step of the canvas ramp. Never text (2.94:1).
    pub const NEAR_BLUE_INK: Rgba = Rgba::hex(0x035CA3);
    /// The primary button's hover fill: Near AI blue with 12% white mixed in.
    pub const NEAR_BLUE_HOVER: Rgba = Rgba::hex(0x1F9EFD);
    /// Unlit dots of a dot matrix: body ink at 9% (cc-dots), a faint grid.
    pub const FAINT: Rgba = Rgba::new(0xC1, 0xC1, 0xC1, 23);
    /// Chart rules and axes: body ink at 14% (the cc-dots axis). Below
    /// bloom's alpha cutoff, so rules never glow.
    pub const RULE: Rgba = Rgba::new(0xC1, 0xC1, 0xC1, 36);

    /// The art ramp: transparent, then blue-ink, blue, sky, marble.
    pub const NEAR_RAMP: [Rgba; 4] = [NEAR_BLUE_INK, NEAR_BLUE, NEAR_SKY, MARBLE];
    /// The neutral ink ramp: slate, pewter, silver, marble.
    pub const INK_RAMP: [Rgba; 4] = [SLATE, PEWTER, SILVER, MARBLE];
}

/// Ramp ids, stored in a program as a negative chroma (`-id`).
pub const RAMP_NEAR: i32 = 1;
pub const RAMP_INK: i32 = 2;

/// Lit step `1..=4` of ramp `id`; any other step is the unlit slate.
pub fn ramp_color(id: i32, step: i32) -> Rgba {
    let ramp = if id == RAMP_INK { &palette::INK_RAMP } else { &palette::NEAR_RAMP };
    match step {
        1..=4 => ramp[step as usize - 1],
        _ => palette::SLATE,
    }
}

/// Continuous ramp position `1.0..=4.0` for a shade level: `0.3` is the
/// deepest step, `0.55` the pigment, `0.8` the bright tint, `1.0` marble.
pub fn ramp_position(level: f32) -> f32 {
    (1.0 + (level - 0.3) * 4.0).clamp(1.0, 4.0)
}

/// A named dither color, or any hue in degrees.
///
/// Each tone expands into a shade ramp via [`Tone::shade`], so every
/// component gets matching dark, mid, and highlight colors. [`Tone::Near`]
/// and [`Tone::Ink`] are the Future-Retro ramps: their shades are the four
/// fixed steps of a ramp, ordered-dithered per pixel by the shaders.
#[derive(Clone, Copy, Debug, PartialEq, Default)]
pub enum Tone {
    /// Near AI blue: blue-ink, blue, sky, marble. The one pigment.
    #[default]
    Near,
    /// Neutral ink: slate, pewter, silver, marble.
    Ink,
    Green,
    Blue,
    Purple,
    Pink,
    Orange,
    Red,
    Grey,
    /// Any hue, 0..360 degrees.
    Hue(f32),
}

impl Tone {
    /// The upstream (dither-core) named tones, in legend order.
    pub const ALL: [Tone; 7] = [
        Tone::Green,
        Tone::Blue,
        Tone::Purple,
        Tone::Pink,
        Tone::Orange,
        Tone::Red,
        Tone::Grey,
    ];

    /// The Future-Retro ramps, in legend order.
    pub const RAMPS: [Tone; 2] = [Tone::Near, Tone::Ink];

    pub fn name(self) -> &'static str {
        match self {
            Tone::Near => "near",
            Tone::Ink => "ink",
            Tone::Green => "green",
            Tone::Blue => "blue",
            Tone::Purple => "purple",
            Tone::Pink => "pink",
            Tone::Orange => "orange",
            Tone::Red => "red",
            Tone::Grey => "grey",
            Tone::Hue(_) => "hue",
        }
    }

    /// Parses a tone name (`"near"`, `"blue"`) or a hue number (`"210"`).
    pub fn parse(s: &str) -> Option<Tone> {
        let s = s.trim().to_ascii_lowercase();
        Some(match s.as_str() {
            "near" | "near-blue" => Tone::Near,
            "ink" => Tone::Ink,
            "green" => Tone::Green,
            "blue" => Tone::Blue,
            "purple" | "violet" => Tone::Purple,
            "pink" => Tone::Pink,
            "orange" => Tone::Orange,
            "red" => Tone::Red,
            "grey" | "gray" => Tone::Grey,
            other => Tone::Hue(other.parse::<f32>().ok()?.rem_euclid(360.0)),
        })
    }

    /// The Future-Retro ramp id, if this tone is a ramp.
    pub fn ramp(self) -> Option<i32> {
        match self {
            Tone::Near => Some(RAMP_NEAR),
            Tone::Ink => Some(RAMP_INK),
            _ => None,
        }
    }

    pub fn is_ramp(self) -> bool {
        self.ramp().is_some()
    }

    pub fn hue(self) -> f32 {
        match self {
            Tone::Near | Tone::Ink => 0.0,
            Tone::Green => 150.0,
            Tone::Blue => 250.0,
            Tone::Purple => 300.0,
            Tone::Pink => 350.0,
            Tone::Orange => 55.0,
            Tone::Red => 25.0,
            Tone::Grey => 260.0,
            Tone::Hue(h) => h.rem_euclid(360.0),
        }
    }

    /// OKLCH chroma, or `-id` for a Future-Retro ramp.
    pub fn chroma(self) -> f32 {
        match self {
            Tone::Near => -(RAMP_NEAR as f32),
            Tone::Ink => -(RAMP_INK as f32),
            Tone::Grey => 0.012,
            _ => 0.19,
        }
    }

    /// A color along this tone's ramp: `0.0` is near-black, `0.5` is the
    /// signature color, `1.0` is a pale highlight. A Future-Retro ramp
    /// returns its nearest fixed step.
    pub fn shade(self, level: f32) -> Rgba {
        shade_hc(self.hue(), self.chroma(), level)
    }

    /// The tone's signature color.
    pub fn base(self) -> Rgba {
        self.shade(0.55)
    }
}

impl From<f32> for Tone {
    fn from(hue: f32) -> Self {
        Tone::Hue(hue)
    }
}

/// Shade of an arbitrary `(hue, chroma)` pair; the formula every shader
/// port implements as `shade`. A negative chroma names a Future-Retro ramp
/// and returns the step nearest the level (shaders dither between steps).
pub fn shade_hc(hue: f32, chroma: f32, level: f32) -> Rgba {
    if chroma < 0.0 {
        let p = ramp_position(level);
        return ramp_color(-chroma as i32, (p.signum() * (p.abs() + 0.5).floor()) as i32);
    }
    let level = level.clamp(0.0, 1.0);
    let l = 0.22 + level * 0.72;
    let c = chroma * (1.0 - (level - 0.55).abs() * 1.3).clamp(0.25, 1.0);
    Rgba::oklch(l, c, hue)
}
