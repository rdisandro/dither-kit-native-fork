//! Reference interpreter for display-list programs.
//!
//! This is the executable form of `spec/SPEC.md`. It is written as plain
//! per-pixel scalar functions so that the GLSL, Metal and HLSL shaders can
//! mirror it line by line, and it is tested pixel-for-pixel against
//! `dither-core`.

use crate::color::{Rgba, palette, ramp_color, ramp_position, shade_hc};
use crate::program::{GLOW_RAMP, NONE, op};

const BAYER2: [u32; 4] = [0, 2, 3, 1];
const BAYER4: [u32; 16] = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
const BAYER8: [u32; 64] = [
    0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20,
    62, 30, 54, 22, 3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39, 13, 45, 5, 37,
    63, 31, 55, 23, 61, 29, 53, 21,
];
const HALFTONE4: [u32; 16] = [12, 5, 6, 13, 4, 0, 1, 7, 11, 3, 2, 8, 15, 10, 9, 14];

const TAU: f32 = std::f32::consts::TAU;
const PI: f32 = std::f32::consts::PI;

/// Future-Retro field units per grid cell: 160 points per noise unit at
/// the default 3-point cell (cc.js `data-pixel / data-scale`).
pub const LIGHT_SCALE: f32 = 3.0 / 160.0;

/// Matrix ids, as encoded in programs.
pub const M_BAYER2: i32 = 0;
pub const M_BAYER4: i32 = 1;
pub const M_BAYER8: i32 = 2;
pub const M_HALFTONE: i32 = 3;
pub const M_NOISE: i32 = 4;

pub fn hash2(x: i32, y: i32) -> u32 {
    let mut h = (x as u32).wrapping_mul(0x9E37_79B1) ^ (y as u32).wrapping_mul(0x85EB_CA77);
    h ^= h >> 15;
    h = h.wrapping_mul(0x2C1B_3C6D);
    h ^= h >> 12;
    h = h.wrapping_mul(0x297A_2D39);
    h ^ (h >> 15)
}

pub fn hash01(x: i32, y: i32) -> f32 {
    hash2(x, y) as f32 / u32::MAX as f32
}

pub fn threshold(m: i32, x: i32, y: i32) -> f32 {
    let (xu, yu) = (x.rem_euclid(8) as usize, y.rem_euclid(8) as usize);
    match m {
        M_BAYER2 => (BAYER2[(yu % 2) * 2 + xu % 2] as f32 + 0.5) / 4.0,
        M_BAYER4 => (BAYER4[(yu % 4) * 4 + xu % 4] as f32 + 0.5) / 16.0,
        M_HALFTONE => (HALFTONE4[(yu % 4) * 4 + xu % 4] as f32 + 0.5) / 16.0,
        M_NOISE => (hash2(x, y) as f32 + 0.5) / (u32::MAX as f32 + 1.0),
        _ => (BAYER8[yu * 8 + xu] as f32 + 0.5) / 64.0,
    }
}

pub fn on(m: i32, x: i32, y: i32, i: f32) -> bool {
    i > threshold(m, x, y)
}

/// Fill coverage at `(x, y)` for intensity `i`. Fill ids: 0 gradient,
/// 1 dotted, 2 hatched, 3 solid, 4 dot matrix.
///
/// Returns `-1` when the pixel is not covered, else its rank `u` in
/// `[0, 1)` among the covered pixels (its threshold over the intensity).
/// Ranks of an ordered matrix nest, so a ramp that steps up where `u` is
/// small spreads the brighter step evenly through the covered pixels.
pub fn cover(fill: i32, x: i32, y: i32, i: f32) -> f32 {
    match fill {
        1 => {
            if x.rem_euclid(2) == 0 && (y + (x / 2).rem_euclid(2)).rem_euclid(2) == 0 {
                let (t, k) = (threshold(M_BAYER4, x / 2, y / 2), i * 1.4);
                if k > t {
                    return t / k;
                }
            }
            -1.0
        }
        2 => {
            let (t, k) = (threshold(M_BAYER4, x, y), 0.25 + i);
            if (x + y).rem_euclid(3) == 0 && k > t { t / k } else { -1.0 }
        }
        3 => {
            if i > 0.02 {
                threshold(M_BAYER4, x, y)
            } else {
                -1.0
            }
        }
        4 => {
            if x.rem_euclid(2) == 0 && y.rem_euclid(2) == 0 && i > 0.02 {
                threshold(M_BAYER4, x / 2, y / 2)
            } else {
                -1.0
            }
        }
        _ => {
            let t = threshold(M_BAYER8, x, y);
            if i > t { t / i } else { -1.0 }
        }
    }
}

pub fn covers(fill: i32, x: i32, y: i32, i: f32) -> bool {
    cover(fill, x, y, i) >= 0.0
}

/// Rank for pixels lit without a coverage threshold (lines, rims, edges).
fn step_u(x: i32, y: i32) -> f32 {
    threshold(M_BAYER4, x, y)
}

/// Shade of a lit pixel with rank `u`. Upstream tones ignore `u`. A
/// Future-Retro ramp (negative chroma) takes the step at the level's floor
/// and steps up where `u` is under the level's fraction: ordered dither
/// between the ramp's fixed steps, never an in-between color.
pub fn shade_at(h: f32, c: f32, level: f32, u: f32) -> Rgba {
    if c >= 0.0 {
        return shade_hc(h, c, level);
    }
    let p = ramp_position(level);
    let s = p.floor();
    let step = s as i32 + (u < p - s) as i32;
    ramp_color((-c) as i32, step.min(4))
}

/// Rust's `f32::round`: half away from zero.
pub fn rnd(v: f32) -> f32 {
    v.signum() * (v.abs() + 0.5).floor()
}

/// Mixes 12% white into a color: the Future-Retro hover fill.
fn lift(c: Rgba) -> Rgba {
    c.mix(Rgba::WHITE, 0.12)
}

fn fade(c: Rgba, f: f32) -> Rgba {
    c.fade(f)
}

fn shade(h: f32, c: f32, level: f32) -> Rgba {
    shade_hc(h, c, level)
}

fn color_at(p: &[f32], i: usize) -> Rgba {
    Rgba::new(p[i] as u8, p[i + 1] as u8, p[i + 2] as u8, p[i + 3] as u8)
}

/// Step index of `(px, py)` on the Bresenham line `a -> b`, if it lies on it.
pub fn line_step(x0: i32, y0: i32, x1: i32, y1: i32, px: i32, py: i32) -> Option<i32> {
    let (adx, ady) = ((x1 - x0).abs(), (y1 - y0).abs());
    let (sx, sy) = (if x0 < x1 { 1 } else { -1 }, if y0 < y1 { 1 } else { -1 });
    if adx >= ady {
        let k = (px - x0) * sx;
        if k < 0 || k > adx {
            return None;
        }
        let m = if adx == 0 { 0 } else { (2 * k * ady + adx) / (2 * adx) };
        (py == y0 + sy * m).then_some(k)
    } else {
        let k = (py - y0) * sy;
        if k < 0 || k > ady {
            return None;
        }
        let m = (2 * k * adx + ady) / (2 * ady);
        (px == x0 + sx * m).then_some(k)
    }
}

fn smoothstep(a: f32, b: f32, x: f32) -> f32 {
    let t = ((x - a) / (b - a)).clamp(0.0, 1.0);
    t * t * (3.0 - 2.0 * t)
}

fn value_noise(x: f32, y: f32) -> f32 {
    let (xi, yi) = (x.floor() as i32, y.floor() as i32);
    let (fx, fy) = (x - x.floor(), y - y.floor());
    let sx = fx * fx * (3.0 - 2.0 * fx);
    let sy = fy * fy * (3.0 - 2.0 * fy);
    let n00 = hash01(xi, yi);
    let n10 = hash01(xi + 1, yi);
    let n01 = hash01(xi, yi + 1);
    let n11 = hash01(xi + 1, yi + 1);
    let top = n00 + (n10 - n00) * sx;
    let bot = n01 + (n11 - n01) * sx;
    top + (bot - top) * sy
}

fn fbm(x: f32, y: f32, octaves: i32) -> f32 {
    let (mut s, mut amp, mut f, mut n) = (0.0, 0.5, 1.0, 0.0);
    for _ in 0..octaves {
        s += amp * value_noise(x * f, y * f);
        n += amp;
        amp *= 0.5;
        f *= 2.03;
    }
    s / n
}

/// Future-Retro fields (ids 5 clouds, 6 marble, 7 rings, 8 caustics) at
/// grid cell `(x, y)` of a `w x h` surface, after cc.js `FIELDS`, with the
/// house contrast (0.1) and density gain (1.25) applied.
pub fn light_field(f: i32, x: i32, y: i32, w: i32, h: i32, t: f32) -> f32 {
    let (fx, fy) = (x as f32 * LIGHT_SCALE, y as f32 * LIGHT_SCALE);
    let v = match f {
        6 => {
            let n = fbm(fx * 0.8 + t * 0.06, fy * 0.8 - t * 0.03, 4);
            0.5 + 0.42 * ((fx * 0.8 + fy * 0.35 + n * 4.2 + t * 0.35) * PI).sin()
        }
        7 => {
            let dx = (x as f32 - w as f32 * 0.5) * LIGHT_SCALE;
            let dy = (y as f32 - h as f32 * 0.5) * LIGHT_SCALE;
            let d = (dx * dx + dy * dy).sqrt();
            0.5 + 0.45 * (d * 7.0 - t * 2.2 + fbm(fx, fy, 2) * 3.2).sin()
        }
        8 => {
            let n = fbm(fx * 0.9 + t * 0.12, fy * 0.9 - t * 0.08, 3);
            let a = ((fx * 1.3 + n * 3.1 + t * 0.4) * PI).sin().abs();
            let b = ((fy * 1.1 - n * 2.7 - t * 0.3) * PI).sin().abs();
            let k = 1.0 - a * b;
            let k2 = k * k;
            0.12 + k2 * k2 * k2 * k * 0.95
        }
        _ => {
            let v = fbm(fx + t * 0.31, fy - t * 0.17, 3) * 0.7
                + value_noise(fx * 2.1 - t * 0.23, fy * 2.1 + t * 0.19) * 0.3;
            0.5 + (v - 0.5) * 1.9
        }
    };
    ((0.5 + (v - 0.5) * 1.3) * 1.25).clamp(0.0, 1.0)
}

pub fn field(f: i32, u: f32, v: f32, t: f32) -> f32 {
    let s = match f {
        1 => {
            let crest = 0.5 + 0.18 * (u * 6.0 + t * 1.1).sin() + 0.08 * (u * 15.0 - t * 1.7).sin();
            (1.0 - (v - crest).abs() * 2.6).clamp(0.0, 1.0) * 0.9 + v * 0.15
        }
        2 => {
            let d = ((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)).sqrt();
            0.5 + 0.5 * (d * 30.0 - t * 3.0).sin() * (1.0 - d * 1.2).max(0.0)
        }
        3 => value_noise(u * 6.0 + t * 0.35, v * 4.0 - t * 0.2) * 0.7 + value_noise(u * 13.0 - t * 0.5, v * 9.0) * 0.3,
        4 => {
            let curtain = value_noise(u * 5.0 + t * 0.25, t * 0.15) * 0.6 + 0.4 * (u * 9.0 + t * 0.6).sin().abs();
            let fall = (1.0 - (v - 0.35 * curtain).max(0.0) * 1.6).clamp(0.0, 1.0);
            curtain * fall
        }
        _ => {
            let a = (u * 7.0 + t * 0.9).sin();
            let b = (v * 6.0 - t * 0.7).sin();
            let c = ((u + v) * 5.0 + t * 0.5).sin();
            let d = (((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)).sqrt() * 12.0 - t * 1.2).sin();
            (a + b + c + d) / 8.0 + 0.5
        }
    };
    s.clamp(0.0, 1.0)
}

fn direction(d: i32, x: i32, y: i32, w: i32, h: i32) -> f32 {
    let fx = if w > 1 { x as f32 / (w - 1) as f32 } else { 0.5 };
    let fy = if h > 1 { y as f32 / (h - 1) as f32 } else { 0.5 };
    match d {
        1 => fy,
        2 => 1.0 - fx,
        3 => fx,
        4 => {
            let (dx, dy) = (fx - 0.5, fy - 0.5);
            ((dx * dx + dy * dy).sqrt() / std::f32::consts::FRAC_1_SQRT_2).min(1.0)
        }
        _ => 1.0 - fy,
    }
}

/// Evaluates one op at pixel `(x, y)`. Returns the color it paints, if any.
/// `cur` is the color painted so far (needed by the button's sparkles).
fn eval_op(p: &[f32], i: usize, tb: usize, x: i32, y: i32, cur: Option<Rgba>) -> Option<Rgba> {
    let a = |k: usize| p[i + 2 + k];
    let ai = |k: usize| p[i + 2 + k] as i32;
    match p[i] as u32 {
        op::LINE => {
            let k = line_step(ai(0), ai(1), ai(2), ai(3), x, y)?;
            let dash = ai(4);
            (dash <= 1 || k % dash == 0).then(|| color_at(p, i + 7))
        }
        op::DOT => {
            let (dx, dy) = (x - ai(0), y - ai(1));
            if dx.abs() > 1 || dy.abs() > 1 {
                return None;
            }
            Some(if dx == 0 && dy == 0 { color_at(p, i + 8) } else { color_at(p, i + 4) })
        }
        op::AREA_BODY => {
            let (off, w, h) = (ai(0) as usize, ai(1), ai(2));
            if x >= w {
                return None;
            }
            let top = p[tb + off + x as usize * 2];
            let bottom = p[tb + off + x as usize * 2 + 1];
            if top == NONE {
                return None;
            }
            if y < top.ceil() as i32 + 1 || y > bottom as i32 {
                return None;
            }
            let span = (h - 1) as f32 - top;
            let t = 1.0 - (y as f32 - top) / span.max(1.0);
            let u = cover(ai(3), x, y, (0.1 + 0.9 * t) * a(6));
            (u >= 0.0).then(|| fade(shade_at(a(4), a(5), 0.32 + 0.4 * t, u), a(7)))
        }
        op::AREA_STROKE => {
            let (off, w) = (ai(0) as usize, ai(1));
            if x >= w {
                return None;
            }
            let top = p[tb + off + x as usize * 2];
            if top == NONE {
                return None;
            }
            let ty = rnd(top) as i32;
            let mut from = ty;
            if x > 0 {
                let prev = p[tb + off + (x as usize - 1) * 2];
                if prev != NONE {
                    from = rnd(prev) as i32;
                }
            }
            (y >= from.min(ty) && y <= from.max(ty)).then(|| color_at(p, i + 4))
        }
        op::BAR => {
            let (bx0, bx1, top, bottom, h) = (ai(0), ai(1), a(2), ai(3), ai(4));
            let ty = rnd(top) as i32;
            if x < bx0 || x > bx1.max(bx0) || y > bottom {
                return None;
            }
            if ai(5) == 4 {
                // Dot matrix: dots on a two-cell pitch from the bar's left
                // edge and the axis, unlit dots a faint grid above the value.
                if (x - bx0).rem_euclid(2) != 0 || (h - 1 - y).rem_euclid(2) != 0 {
                    return None;
                }
                if y < ty {
                    return Some(palette::FAINT);
                }
                let s = (bottom - y + 1) as f32 / (bottom - ty + 1).max(1) as f32;
                let u = threshold(M_BAYER4, (x - bx0) / 2, (h - 1 - y) / 2);
                return Some(fade(shade_at(a(6), a(7), 0.3 + 0.55 * s, u), a(8)));
            }
            if y < ty {
                return None;
            }
            let t = 1.0 - (y as f32 - top) / ((h - 1) as f32 - top).max(1.0);
            let u = if y == ty { step_u(x, y) } else { cover(ai(5), x, y, 0.3 + 0.7 * t) };
            let level = if y == ty { 0.82 } else { 0.32 + 0.38 * t };
            (u >= 0.0).then(|| fade(shade_at(a(6), a(7), level, u), a(8)))
        }
        op::PIE => {
            let (cx, cy, radius, inner) = (a(0), a(1), a(2), a(3));
            let (multi, hover, n, off) = (ai(4) != 0, ai(5), ai(6), ai(7) as usize);
            let (dx, dy) = (x as f32 + 0.5 - cx, y as f32 + 0.5 - cy);
            let r = (dx * dx + dy * dy).sqrt();
            let ang = dx.atan2(-dy).rem_euclid(TAU);
            let mut idx = -1;
            for s in 0..n {
                let (a0, a1) = (p[tb + off + s as usize * 5], p[tb + off + s as usize * 5 + 1]);
                if ang >= a0 && ang < a1 {
                    idx = s;
                    break;
                }
            }
            if idx < 0 {
                return None;
            }
            let row = tb + off + idx as usize * 5;
            let lift = if hover == idx { 2.0 } else { 0.0 };
            let outer = radius - 2.0 + lift;
            if r < inner || r > outer {
                return None;
            }
            let (a0, a1) = (p[row], p[row + 1]);
            if multi && (ang - a0).min(a1 - ang) * r < 0.9 {
                return None;
            }
            let dim = if hover >= 0 && hover != idx { 0.5 } else { 1.0 };
            let t = ((r - inner) / (outer - inner).max(1.0)).clamp(0.0, 1.0);
            let rim = outer - r < 1.2;
            let u = if rim { step_u(x, y) } else { cover(p[row + 4] as i32, x, y, 0.3 + 0.7 * t) };
            if u >= 0.0 {
                let level = if rim { 0.8 } else { 0.3 + 0.4 * t };
                Some(fade(shade_at(p[row + 2], p[row + 3], level, u), dim))
            } else {
                None
            }
        }
        op::POLY => {
            let (cx, cy, radius, fill) = (a(0), a(1), a(2), ai(3));
            let (n, off) = (ai(7) as usize, ai(8) as usize);
            let (px, py) = (x as f32 + 0.5, y as f32 + 0.5);
            let mut inside = false;
            let mut j = n - 1;
            for k in 0..n {
                let (xi, yi) = (p[tb + off + k * 2], p[tb + off + k * 2 + 1]);
                let (xj, yj) = (p[tb + off + j * 2], p[tb + off + j * 2 + 1]);
                if (yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi {
                    inside = !inside;
                }
                j = k;
            }
            if !inside {
                return None;
            }
            let d = ((px - cx) * (px - cx) + (py - cy) * (py - cy)).sqrt() / radius;
            let u = cover(fill, x, y, 0.2 + 0.55 * d);
            (u >= 0.0).then(|| fade(shade_at(a(4), a(5), 0.32 + 0.35 * d, u), a(6)))
        }
        op::GRADIENT => {
            let (w, h, dir, m, density) = (ai(0), ai(1), ai(2), ai(3), a(4));
            if x >= w || y >= h {
                return None;
            }
            let pos = direction(dir, x, y, w, h);
            if ai(7) != 0 {
                let (th, tc) = if on(m, x, y, pos) { (a(8), a(9)) } else { (a(5), a(6)) };
                let (t, k) = (threshold(m, x + 3, y + 5), density * (0.55 + 0.45 * pos));
                (k > t).then(|| shade_at(th, tc, 0.35 + 0.35 * pos, t / k))
            } else {
                let (t, k) = (threshold(m, x, y), (1.0 - pos) * density);
                (k > t).then(|| shade_at(a(5), a(6), 0.3 + 0.4 * (1.0 - pos), t / k))
            }
        }
        op::BACKGROUND => {
            let (w, h, f, m, density, time) = (ai(0), ai(1), ai(2), ai(3), a(4), a(5));
            if x >= w || y >= h {
                return None;
            }
            let it = if f >= 5 {
                light_field(f, x, y, w, h, time)
            } else {
                let (u, v) = (x as f32 / w.max(1) as f32, y as f32 / h.max(1) as f32);
                smoothstep(0.18, 0.82, field(f, u, v, time))
            };
            let (t, k) = (threshold(m, x, y), it * density);
            // `k > t` (not `k <= t`): NaN stays unlit, as in every shader.
            let lit = k > t;
            if !lit {
                return None;
            }
            let accent = ai(8) != 0 && on(M_BAYER4, x + 1, y + 2, (it - 0.62) * 2.2);
            let (th, tc) = if accent { (a(9), a(10)) } else { (a(6), a(7)) };
            // Future-Retro fields peak below the bright tint, so saturated
            // light stays a dither of blue and sky instead of a flat fill.
            let level = if f >= 5 { 0.3 + 0.42 * it } else { 0.3 + 0.5 * it };
            Some(shade_at(th, tc, level, t / k))
        }
        op::BUTTON => {
            let (w, h, style, density, time, th, tc) = (ai(0), ai(1), ai(2), a(3), a(4), a(5), a(6));
            if x >= w || y >= h {
                return None;
            }
            let edge = x == 0 || y == 0 || x + 1 == w || y + 1 == h;
            if tc < 0.0 {
                // Future-Retro buttons are flat: pigment fill (lifted 12%
                // toward white when hot), a marble outline that fills on
                // hover, or nothing. No texture, checker or sparkles.
                let base = ramp_color((-tc) as i32, 2);
                return match style {
                    0 => Some(if density > 0.4 { lift(base) } else { base }),
                    1 => (edge || density > 0.0).then_some(palette::MARBLE),
                    _ => None,
                };
            }
            let mut c = None;
            if edge && style != 2 {
                if (x + y) % 2 == 0 || style == 0 {
                    c = Some(shade(th, tc, 0.6));
                }
            } else {
                let fy = if h > 1 { y as f32 / (h - 1) as f32 } else { 0.5 };
                let it = density * (1.05 - 0.45 * fy);
                if covers(0, x, y, it) {
                    c = Some(shade(th, tc, 0.45 + 0.25 * (1.0 - fy)));
                }
            }
            // Sparkles land on pixels lit so far (this op, or ones below it).
            if density > 0.4 && hash01(x, y ^ 0x5bd1) < 0.04 * density && c.or(cur).is_some() {
                let phase = hash01(y, x ^ 0x1b56) * TAU;
                let speed = 1.5 + 2.5 * hash01(x ^ 0x77, y);
                let wink = (time * speed + phase).sin().max(0.0).powi(12);
                if wink > 0.35 {
                    c = Some(fade(shade(th, tc, 0.97), wink));
                }
            }
            c
        }
        op::METER => {
            let (w, h, value, fill, th, tc) = (ai(0), ai(1), a(2), ai(3), a(4), a(5));
            if x >= w || y >= h {
                return None;
            }
            let filled = rnd(value.clamp(0.0, 1.0) * w as f32) as i32;
            if fill == 4 {
                // Dot matrix: lit dots brighten toward the value; the rest
                // of the track stays a faint grid.
                if x.rem_euclid(2) != 0 || y.rem_euclid(2) != 0 {
                    return None;
                }
                if x < filled {
                    let s = (x + 1) as f32 / filled.max(1) as f32;
                    return Some(shade_at(th, tc, 0.3 + 0.55 * s, threshold(M_BAYER4, x / 2, y / 2)));
                }
                return Some(palette::FAINT);
            }
            if x < filled {
                let it = 0.35 + 0.65 * (x as f32 + 1.0) / filled.max(1) as f32;
                let u = cover(fill, x, y, it);
                (u >= 0.0 || x + 1 == filled)
                    .then(|| shade_at(th, tc, 0.4 + 0.35 * it, if u >= 0.0 { u } else { step_u(x, y) }))
            } else if tc < 0.0 {
                (x.rem_euclid(2) == 0 && y.rem_euclid(2) == 0).then_some(palette::FAINT)
            } else if (x + y) % 4 == 0 {
                Some(fade(shade(260.0, 0.012, 0.35), 0.6))
            } else {
                None
            }
        }
        op::AVATAR => {
            if !(0..8).contains(&x) || !(0..8).contains(&y) {
                return None;
            }
            let bits = (ai(0) as u32) | ((ai(1) as u32) << 16);
            let mirror_x = ai(2) != 0;
            let hue = a(3);
            let acc = (ai(4) as u32) | ((ai(5) as u32) << 16);
            let i = if mirror_x {
                (y * 4 + if x < 4 { x } else { 7 - x }) as u32
            } else {
                ((if y < 4 { y } else { 7 - y }) * 8 + x) as u32
            };
            let lit = (bits >> i) & 1 == 1;
            let accent = lit && (acc >> (i % 23)) & 1 == 1 && (acc >> ((i + 7) % 23)) & 1 == 1;
            if hue < 0.0 {
                // A Future-Retro ramp: pigment, its bright tint, slate.
                let id = (-hue) as i32;
                return Some(if accent {
                    ramp_color(id, 3)
                } else if lit {
                    ramp_color(id, 2)
                } else {
                    palette::SLATE
                });
            }
            let level = if accent {
                0.92
            } else if lit {
                0.62
            } else {
                0.16
            };
            Some(shade(hue, 0.19, level))
        }
        op::SWATCH if a(1) < 0.0 => Some(shade_at(a(0), a(1), 0.6, step_u(x, y))),
        op::SWATCH => Some(shade(a(0), a(1), if (x + y) % 2 == 0 { 0.72 } else { 0.48 })),
        _ => None,
    }
}

/// Paints ops in `[start, end)` over `cur`.
fn eval_list(p: &[f32], start: usize, end: usize, x: i32, y: i32, mut cur: Option<Rgba>) -> Option<Rgba> {
    let tb = 1 + p[0] as usize;
    let mut i = start;
    while i < end {
        if p[i] as u32 == op::BLOOM {
            break;
        }
        if let Some(c) = eval_op(p, i, tb, x, y, cur) {
            cur = Some(c);
        }
        i += p[i + 1] as usize;
    }
    cur
}

/// Index of the BLOOM op, or the end of the op list.
pub fn bloom_index(p: &[f32]) -> usize {
    let end = 1 + p[0] as usize;
    let mut i = 1;
    while i < end {
        if p[i] as u32 == op::BLOOM {
            return i;
        }
        i += p[i + 1] as usize;
    }
    end
}

/// Base pass: every op before BLOOM.
pub fn base(p: &[f32], x: i32, y: i32) -> Option<Rgba> {
    eval_list(p, 1, bloom_index(p), x, y, None)
}

/// Post pass: bloom over `lookup` (the base pass), then the overlay ops.
pub fn post(p: &[f32], x: i32, y: i32, lookup: &dyn Fn(i32, i32) -> Option<Rgba>) -> Option<Rgba> {
    let end = 1 + p[0] as usize;
    let b = bloom_index(p);
    let mut cur = lookup(x, y);
    if b < end {
        let (radius, strength) = (p[b + 2] as i32, p[b + 3]);
        let ramp_glow = p[b + 1] >= 5.0 && p[b + 4] == GLOW_RAMP;
        if cur.is_none() && radius > 0 {
            'search: for d in 1..=radius {
                for oy in -d..=d {
                    for ox in -d..=d {
                        if ox.abs() != d && oy.abs() != d {
                            continue;
                        }
                        if let Some(c) = lookup(x + ox, y + oy)
                            && c.a > 160
                        {
                            let glow = strength * (1.0 - (d as f32 - 1.0) / radius as f32);
                            if on(M_BAYER4, x, y, glow) {
                                cur = Some(if ramp_glow { palette::NEAR_BLUE_INK } else { c.fade(0.35 + 0.25 * glow) });
                            }
                            break 'search;
                        }
                    }
                }
            }
        }
        cur = eval_list(p, b + p[b + 1] as usize, end, x, y, cur);
    }
    cur
}

/// Renders a whole program into a `w x h` grid, row-major.
pub fn render(p: &[f32], w: usize, h: usize) -> Vec<Option<Rgba>> {
    let mut basebuf = vec![None; w * h];
    for y in 0..h {
        for x in 0..w {
            basebuf[y * w + x] = base(p, x as i32, y as i32);
        }
    }
    let lookup = |x: i32, y: i32| {
        if x < 0 || y < 0 || x >= w as i32 || y >= h as i32 {
            None
        } else {
            basebuf[y as usize * w + x as usize]
        }
    };
    let mut out = vec![None; w * h];
    for y in 0..h {
        for x in 0..w {
            out[y * w + x] = post(p, x as i32, y as i32, &lookup);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::color::{Tone, palette};
    use crate::encode::{Bloom, CartesianKind, Cartesian, Energy, Fill, Series, avatar_tone, meter};

    #[test]
    fn ramp_shading_dithers_between_fixed_steps() {
        let [h, c] = crate::program::tone(Tone::Near);
        // The pigment, the bright tint and marble at whole positions.
        assert_eq!(shade_at(h, c, 0.55, 0.0), palette::NEAR_BLUE);
        assert_eq!(shade_at(h, c, 0.8, 0.99), palette::NEAR_SKY);
        assert_eq!(shade_at(h, c, 1.0, 0.0), palette::MARBLE);
        assert_eq!(shade_at(h, c, 0.0, 0.0), palette::NEAR_BLUE_INK);
        // Halfway between blue and sky: the rank picks the step.
        assert_eq!(shade_at(h, c, 0.675, 0.2), palette::NEAR_SKY);
        assert_eq!(shade_at(h, c, 0.675, 0.7), palette::NEAR_BLUE);
        let [ih, ic] = crate::program::tone(Tone::Ink);
        assert_eq!(shade_at(ih, ic, 0.3, 0.5), palette::SLATE);
        // Upstream tones ignore the rank.
        let [bh, bc] = crate::program::tone(Tone::Blue);
        assert_eq!(shade_at(bh, bc, 0.6, 0.1), shade_at(bh, bc, 0.6, 0.9));
        // CPU-side shades are the nearest step.
        assert_eq!(Tone::Near.shade(0.78), palette::NEAR_SKY);
        assert_eq!(Tone::Ink.base(), palette::PEWTER);
    }

    #[test]
    fn ramp_ranks_spread_the_brighter_step_evenly() {
        // At a 50% fraction, half of the covered pixels in an 8x8 tile step up.
        let mut up = 0;
        let mut lit = 0;
        for y in 0..8 {
            for x in 0..8 {
                let u = cover(0, x, y, 0.5);
                if u >= 0.0 {
                    lit += 1;
                    up += (u < 0.5) as i32;
                }
            }
        }
        assert_eq!((lit, up), (32, 16));
    }

    #[test]
    fn dot_matrix_keeps_unlit_dots_as_a_faint_grid() {
        let series = [Series::new("", Tone::Near, vec![2.0, 4.0]).fill(Fill::Matrix)];
        let mut chart = Cartesian::new(CartesianKind::Bar, &series);
        chart.grid_lines = false;
        chart.baseline = false;
        let program = chart.encode(10, 12).to_vec();
        let px = render(&program, 10, 12);
        let at = |x: usize, y: usize| px[y * 10 + x];
        // Column 0 is half height: lit dots below, faint dots above, gaps between.
        assert_eq!(at(1, 1), Some(palette::FAINT));
        assert!(at(1, 11).is_some_and(|c| c != palette::FAINT && c.a == 255));
        assert_eq!(at(2, 11), None);
        assert_eq!(at(1, 10), None);
        let meter_px = render(&meter(8, 2, 0.5, Tone::Ink, Fill::Matrix).to_vec(), 8, 2);
        assert_eq!(meter_px[6], Some(palette::FAINT));
        assert_eq!(meter_px[1], None);
    }

    #[test]
    fn ramp_charts_take_future_retro_chrome() {
        let series = [Series::new("", Tone::Near, vec![1.0, 3.0, 2.0])];
        let mut chart = Cartesian::new(CartesianKind::Area, &series);
        chart.bloom = Bloom::Low;
        let program = chart.encode(30, 12).to_vec();
        // Rules sit below bloom's cutoff; the glow is opaque blue-ink.
        const { assert!(palette::RULE.a <= 160) };
        let b = bloom_index(&program);
        assert_eq!(&program[b..b + 5], &[op::BLOOM as f32, 5.0, 1.0, 0.45, GLOW_RAMP]);
        let px = render(&program, 30, 12);
        assert!(px.contains(&Some(palette::NEAR_BLUE_INK)));
        assert!(px.iter().flatten().all(|c| c.a < 255
            || palette::NEAR_RAMP.contains(c)
            || *c == palette::NEAR_BLUE_INK));
    }

    #[test]
    fn ramp_avatars_and_buttons_are_one_pigment() {
        let px = render(&avatar_tone("Ada", Tone::Near).to_vec(), 8, 8);
        assert!(px.iter().all(|c| matches!(c, Some(c) if *c == palette::SLATE || palette::NEAR_RAMP.contains(c))));
        let flat = render(&crate::encode::button(6, 4, Tone::Near, crate::encode::ButtonStyle::Solid, 0.32, 1.0).to_vec(), 6, 4);
        assert!(flat.iter().all(|c| *c == Some(palette::NEAR_BLUE)));
        let hot = render(&crate::encode::button(6, 4, Tone::Near, crate::encode::ButtonStyle::Solid, 0.85, 1.0).to_vec(), 6, 4);
        assert!(hot.iter().all(|c| *c == Some(palette::NEAR_BLUE_HOVER)));
    }

    #[test]
    fn energy_eases_toward_hover_and_advances_phase() {
        let e = Energy::FIELD;
        let (phase, energy) = e.step(0.0, e.idle, true, 0.1);
        assert!((energy - 0.25).abs() < 1e-6);
        assert!((phase - 0.1).abs() < 1e-6);
        let (_, settled) = e.step(phase, energy, true, 1.0);
        assert_eq!(settled, e.hover);
    }

    #[test]
    fn bayer_tables_match_recursive_construction() {
        for y in 0..4 {
            for x in 0..4 {
                let inner = BAYER2[(y % 2) * 2 + (x % 2)];
                let outer = BAYER2[(y / 2) * 2 + (x / 2)];
                assert_eq!(BAYER4[y * 4 + x], 4 * inner + outer);
            }
        }
        for y in 0..8 {
            for x in 0..8 {
                let inner = BAYER4[(y % 4) * 4 + (x % 4)];
                let outer = BAYER2[((y / 4) % 2) * 2 + ((x / 4) % 2)];
                assert_eq!(BAYER8[y * 8 + x], 4 * inner + outer);
            }
        }
    }

    fn bresenham(x0: i32, y0: i32, x1: i32, y1: i32) -> Vec<(i32, i32)> {
        let (dx, dy) = ((x1 - x0).abs(), -(y1 - y0).abs());
        let (sx, sy) = (if x0 < x1 { 1 } else { -1 }, if y0 < y1 { 1 } else { -1 });
        let (mut x, mut y, mut err) = (x0, y0, dx + dy);
        let mut v = vec![];
        loop {
            v.push((x, y));
            if x == x1 && y == y1 {
                break;
            }
            let e2 = 2 * err;
            if e2 >= dy {
                err += dy;
                x += sx;
            }
            if e2 <= dx {
                err += dx;
                y += sy;
            }
        }
        v
    }

    #[test]
    fn closed_form_line_matches_bresenham() {
        let r = 9;
        for x0 in -r..=r {
            for y0 in -r..=r {
                for (x1, y1) in [(r, 3), (-4, r), (2, -r), (-r, -r), (0, 5), (7, 0), (x0, y0)] {
                    let pts = bresenham(x0, y0, x1, y1);
                    for (k, &(px, py)) in pts.iter().enumerate() {
                        assert_eq!(line_step(x0, y0, x1, y1, px, py), Some(k as i32));
                    }
                    let n = (-r - 1..=r + 1)
                        .flat_map(|px| (-r - 1..=r + 1).map(move |py| (px, py)))
                        .filter(|&(px, py)| line_step(x0, y0, x1, y1, px, py).is_some())
                        .count();
                    assert_eq!(n, pts.len());
                }
            }
        }
    }
}
