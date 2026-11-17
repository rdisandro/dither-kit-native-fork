//! Golden fixture generator.
//!
//! For every scene in `scenes()`:
//! 1. render it with upstream `dither-core` (the source of truth),
//! 2. encode it with `dither-ir` and run the reference interpreter,
//! 3. compare the two pixel grids,
//! 4. write `fixtures/scenes.json` (scene parameters + encoded program) and
//!    `fixtures/png/<name>.png` (the dither-core pixels).
//!
//! Future-Retro scenes (`"source": "vm"`, names starting `fr_`) use the
//! Near AI ramps, dot-matrix fill and light fields, which dither-core does
//! not have. Their PNGs pin the reference interpreter's own output, so the
//! shader ports are held to it and `--check` catches any drift in it.
//!
//! The Swift and C# ports read `scenes.json` to check their encoders (program
//! floats) and their shaders (pixels against the PNGs).
//!
//! Usage: `cargo run -p dither-golden -- <fixtures-dir> [--check]`

use std::fs;
use std::path::{Path, PathBuf};

use dither_core as core;
use dither_ir as ir;
use serde_json::{Value, json};

fn tone_json(t: &Value) -> ir::Tone {
    match t {
        Value::Number(n) => ir::Tone::Hue(n.as_f64().unwrap() as f32),
        Value::String(s) => ir::Tone::parse(s).expect("tone"),
        _ => panic!("tone"),
    }
}

fn ctone(t: ir::Tone) -> core::Tone {
    match t {
        ir::Tone::Green => core::Tone::Green,
        ir::Tone::Blue => core::Tone::Blue,
        ir::Tone::Purple => core::Tone::Purple,
        ir::Tone::Pink => core::Tone::Pink,
        ir::Tone::Orange => core::Tone::Orange,
        ir::Tone::Red => core::Tone::Red,
        ir::Tone::Grey => core::Tone::Grey,
        ir::Tone::Hue(h) => core::Tone::Hue(h),
        ir::Tone::Near | ir::Tone::Ink => panic!("dither-core has no Future-Retro ramp"),
    }
}

fn s<'a>(v: &'a Value, k: &str) -> &'a str {
    v[k].as_str().unwrap_or_else(|| panic!("missing {k}"))
}
fn f(v: &Value, k: &str) -> f64 {
    v[k].as_f64().unwrap_or_else(|| panic!("missing {k}"))
}
fn opt_usize(v: &Value, k: &str) -> Option<usize> {
    v[k].as_u64().map(|x| x as usize)
}

fn matrix(name: &str) -> (ir::Matrix, core::Matrix) {
    match name {
        "bayer2" => (ir::Matrix::Bayer2, core::Matrix::Bayer2),
        "bayer4" => (ir::Matrix::Bayer4, core::Matrix::Bayer4),
        "bayer8" => (ir::Matrix::Bayer8, core::Matrix::Bayer8),
        "halftone" => (ir::Matrix::Halftone, core::Matrix::Halftone),
        "noise" => (ir::Matrix::Noise, core::Matrix::Noise),
        _ => panic!("matrix {name}"),
    }
}

fn fill(name: &str) -> (ir::Fill, core::Fill) {
    match name {
        "gradient" => (ir::Fill::Gradient, core::Fill::Gradient),
        "dotted" => (ir::Fill::Dotted, core::Fill::Dotted),
        "hatched" => (ir::Fill::Hatched, core::Fill::Hatched),
        "solid" => (ir::Fill::Solid, core::Fill::Solid),
        // Future-Retro only; the upstream half is never rendered.
        "matrix" => (ir::Fill::Matrix, core::Fill::Solid),
        _ => panic!("fill {name}"),
    }
}

fn bloom(name: &str) -> (ir::Bloom, core::Bloom) {
    match name {
        "off" => (ir::Bloom::Off, core::Bloom::Off),
        "low" => (ir::Bloom::Low, core::Bloom::Low),
        "high" => (ir::Bloom::High, core::Bloom::High),
        "aura" => (ir::Bloom::Aura, core::Bloom::Aura),
        _ => panic!("bloom {name}"),
    }
}

fn series(v: &Value) -> (Vec<ir::Series>, Vec<core::Series>) {
    let mut a = vec![];
    let mut b = vec![];
    for sv in v["series"].as_array().unwrap() {
        let t = tone_json(&sv["tone"]);
        let values: Vec<f64> = sv["values"].as_array().unwrap().iter().map(|x| x.as_f64().unwrap()).collect();
        let (fi, fc) = fill(s(sv, "fill"));
        let hidden = sv["hidden"].as_bool().unwrap_or(false);
        a.push(ir::Series::new(s(sv, "label"), t, values.clone()).fill(fi).hidden(hidden));
        if !t.is_ramp() {
            b.push(core::Series::new(s(sv, "label"), ctone(t), values).fill(fc).hidden(hidden));
        }
    }
    (a, b)
}

type Px = Option<(u8, u8, u8, u8)>;

/// A scene's source of truth: upstream dither-core, or (for Future-Retro
/// scenes, which dither-core cannot draw) the reference interpreter itself.
fn from_vm(scene: &Value) -> bool {
    scene["source"].as_str() == Some("vm")
}

/// Encodes a scene with dither-ir.
fn encode(scene: &Value) -> ir::Program {
    let (w, h) = (f(scene, "w") as usize, f(scene, "h") as usize);
    match s(scene, "kind") {
        "gradient" => {
            let mut g = ir::Gradient::new(tone_json(&scene["from"]));
            g.to = (!scene["to"].is_null()).then(|| tone_json(&scene["to"]));
            g.direction = direction(s(scene, "direction")).0;
            g.matrix = matrix(s(scene, "matrix")).0;
            g.density = f(scene, "density") as f32;
            g.encode(w, h)
        }
        "background" => {
            let mut b = ir::Background::new(field(s(scene, "field")), tone_json(&scene["tone"]));
            b.accent = (!scene["accent"].is_null()).then(|| tone_json(&scene["accent"]));
            b.matrix = matrix(s(scene, "matrix")).0;
            b.density = f(scene, "density") as f32;
            b.encode(w, h, f(scene, "time") as f32)
        }
        "button" => ir::button(
            w,
            h,
            tone_json(&scene["tone"]),
            button_style(s(scene, "style")).0,
            f(scene, "density") as f32,
            f(scene, "time") as f32,
        ),
        "meter" => ir::meter(w, h, f(scene, "value") as f32, tone_json(&scene["tone"]), fill(s(scene, "fill")).0),
        "avatar" if !scene["tone"].is_null() => ir::avatar_tone(s(scene, "text"), tone_json(&scene["tone"])),
        "avatar" => ir::avatar(s(scene, "text"), scene["hue"].as_f64().map(|x| x as f32)),
        "swatch" => ir::swatch(tone_json(&scene["tone"])),
        "sparkline" => ir::sparkline(
            &values(scene),
            tone_json(&scene["tone"]),
            w,
            h,
            scene["hovered"].as_bool().unwrap(),
            f(scene, "progress") as f32,
        ),
        "cartesian" => {
            let si = series(scene).0;
            let mut c = ir::Cartesian::new(chart_kind(s(scene, "chart")).0, &si);
            c.stacked = scene["stacked"].as_bool().unwrap();
            c.progress = f(scene, "progress") as f32;
            c.hover = opt_usize(scene, "hover");
            c.selected = opt_usize(scene, "selected");
            c.bloom = bloom(s(scene, "bloom")).0;
            c.encode(w, h)
        }
        "pie" => {
            let a = slices(scene).0;
            let mut p = ir::Pie::new(&a);
            p.inner_radius = f(scene, "inner") as f32;
            p.progress = f(scene, "progress") as f32;
            p.hover = opt_usize(scene, "hover");
            p.bloom = bloom(s(scene, "bloom")).0;
            p.encode(w, h)
        }
        "radar" => {
            let si = series(scene).0;
            let mut r = ir::Radar::new(&si);
            r.max = scene["max"].as_f64();
            r.progress = f(scene, "progress") as f32;
            r.selected = opt_usize(scene, "selected");
            r.rings = f(scene, "rings") as usize;
            r.bloom = bloom(s(scene, "bloom")).0;
            r.encode(w, h)
        }
        k => panic!("kind {k}"),
    }
}

/// Renders an upstream scene with dither-core.
fn upstream(scene: &Value) -> core::PixelGrid {
    let (w, h) = (f(scene, "w") as usize, f(scene, "h") as usize);
    match s(scene, "kind") {
        "gradient" => {
            let mut g = core::Gradient::new(ctone(tone_json(&scene["from"])))
                .direction(direction(s(scene, "direction")).1)
                .matrix(matrix(s(scene, "matrix")).1)
                .density(f(scene, "density") as f32);
            if !scene["to"].is_null() {
                g = g.to(ctone(tone_json(&scene["to"])));
            }
            g.render(w, h)
        }
        "background" => {
            let mut b = core::Background::new(field_core(s(scene, "field")), ctone(tone_json(&scene["tone"])))
                .matrix(matrix(s(scene, "matrix")).1)
                .density(f(scene, "density") as f32);
            if !scene["accent"].is_null() {
                b = b.accent(ctone(tone_json(&scene["accent"])));
            }
            b.render(w, h, f(scene, "time") as f32)
        }
        "button" => core::button_texture(
            w,
            h,
            ctone(tone_json(&scene["tone"])),
            button_style(s(scene, "style")).1,
            f(scene, "density") as f32,
            f(scene, "time") as f32,
        ),
        "meter" => core::meter(w, h, f(scene, "value") as f32, ctone(tone_json(&scene["tone"])), fill(s(scene, "fill")).1),
        "avatar" => core::avatar(s(scene, "text"), scene["hue"].as_f64().map(|x| x as f32)),
        "swatch" => {
            let t = ctone(tone_json(&scene["tone"]));
            let mut g = core::PixelGrid::new(w, h);
            for y in 0..h as i32 {
                for x in 0..w as i32 {
                    g.set(x, y, t.shade(if (x + y) % 2 == 0 { 0.72 } else { 0.48 }));
                }
            }
            g
        }
        "sparkline" => core::sparkline(
            &values(scene),
            ctone(tone_json(&scene["tone"])),
            w,
            h,
            scene["hovered"].as_bool().unwrap(),
            f(scene, "progress") as f32,
        ),
        "cartesian" => {
            let sc = series(scene).1;
            let mut c = core::Cartesian::new(chart_kind(s(scene, "chart")).1, &sc);
            c.stacked = scene["stacked"].as_bool().unwrap();
            c.progress = f(scene, "progress") as f32;
            c.hover = opt_usize(scene, "hover");
            c.selected = opt_usize(scene, "selected");
            c.bloom = bloom(s(scene, "bloom")).1;
            c.render(w, h)
        }
        "pie" => {
            let b = slices(scene).1;
            let mut p = core::Pie::new(&b);
            p.inner_radius = f(scene, "inner") as f32;
            p.progress = f(scene, "progress") as f32;
            p.hover = opt_usize(scene, "hover");
            p.bloom = bloom(s(scene, "bloom")).1;
            p.render(w, h)
        }
        "radar" => {
            let sc = series(scene).1;
            let mut r = core::Radar::new(&sc);
            r.max = scene["max"].as_f64();
            r.progress = f(scene, "progress") as f32;
            r.selected = opt_usize(scene, "selected");
            r.rings = f(scene, "rings") as usize;
            r.bloom = bloom(s(scene, "bloom")).1;
            r.render(w, h)
        }
        k => panic!("kind {k}"),
    }
}

/// Renders a scene. Returns (program, interpreter pixels, expected pixels).
fn render(scene: &Value) -> (Vec<f32>, Vec<Px>, Vec<Px>) {
    let (w, h) = (f(scene, "w") as usize, f(scene, "h") as usize);
    let program = encode(scene).to_vec();
    let ours: Vec<Px> = ir::vm::render(&program, w, h).into_iter().map(|c| c.map(|c| (c.r, c.g, c.b, c.a))).collect();
    let theirs = if from_vm(scene) {
        ours.clone()
    } else {
        let grid = upstream(scene);
        (0..h)
            .flat_map(|y| (0..w).map(move |x| (x, y)))
            .map(|(x, y)| grid.get(x, y).map(|c| (c.r, c.g, c.b, c.a)))
            .collect()
    };
    (program, ours, theirs)
}

fn values(scene: &Value) -> Vec<f64> {
    scene["values"].as_array().unwrap().iter().map(|x| x.as_f64().unwrap()).collect()
}

fn direction(name: &str) -> (ir::Direction, core::Direction) {
    match name {
        "up" => (ir::Direction::Up, core::Direction::Up),
        "down" => (ir::Direction::Down, core::Direction::Down),
        "left" => (ir::Direction::Left, core::Direction::Left),
        "right" => (ir::Direction::Right, core::Direction::Right),
        "radial" => (ir::Direction::Radial, core::Direction::Radial),
        d => panic!("direction {d}"),
    }
}

fn field(name: &str) -> ir::Field {
    *ir::Field::ALL.iter().find(|f| field_name(**f) == name).unwrap_or_else(|| panic!("field {name}"))
}

fn field_name(f: ir::Field) -> &'static str {
    match f {
        ir::Field::Plasma => "plasma",
        ir::Field::Waves => "waves",
        ir::Field::Ripple => "ripple",
        ir::Field::Noise => "noise",
        ir::Field::Aurora => "aurora",
        ir::Field::Clouds => "clouds",
        ir::Field::Marble => "marble",
        ir::Field::Rings => "rings",
        ir::Field::Caustics => "caustics",
    }
}

fn field_core(name: &str) -> core::Field {
    match name {
        "plasma" => core::Field::Plasma,
        "waves" => core::Field::Waves,
        "ripple" => core::Field::Ripple,
        "noise" => core::Field::Noise,
        "aurora" => core::Field::Aurora,
        x => panic!("dither-core has no field {x}"),
    }
}

fn button_style(name: &str) -> (ir::ButtonStyle, core::ButtonStyle) {
    match name {
        "solid" => (ir::ButtonStyle::Solid, core::ButtonStyle::Solid),
        "outline" => (ir::ButtonStyle::Outline, core::ButtonStyle::Outline),
        "ghost" => (ir::ButtonStyle::Ghost, core::ButtonStyle::Ghost),
        x => panic!("style {x}"),
    }
}

fn chart_kind(name: &str) -> (ir::CartesianKind, core::CartesianKind) {
    match name {
        "area" => (ir::CartesianKind::Area, core::CartesianKind::Area),
        "line" => (ir::CartesianKind::Line, core::CartesianKind::Line),
        "bar" => (ir::CartesianKind::Bar, core::CartesianKind::Bar),
        x => panic!("chart {x}"),
    }
}

fn slices(scene: &Value) -> (Vec<ir::Slice>, Vec<core::Slice>) {
    let mut a = vec![];
    let mut b = vec![];
    for sv in scene["slices"].as_array().unwrap() {
        let t = tone_json(&sv["tone"]);
        let (fi, fc) = fill(s(sv, "fill"));
        a.push(ir::Slice::new(s(sv, "label"), f(sv, "value"), t).fill(fi));
        if !t.is_ramp() && fi != ir::Fill::Matrix {
            b.push(core::Slice::new(s(sv, "label"), f(sv, "value"), ctone(t)).fill(fc));
        }
    }
    (a, b)
}

fn scenes() -> Vec<Value> {
    let visitors = json!([
        {"label": "Desktop", "tone": "blue", "fill": "gradient", "values": [186, 305, 237, 73, 209, 214, 250, 190]},
        {"label": "Mobile", "tone": "pink", "fill": "hatched", "values": [80, 200, 120, 190, 130, 140, 160, 110]},
        {"label": "Tablet", "tone": "green", "fill": "dotted", "values": [40, 60, 90, 70, 50, 80, 75, 60]}
    ]);
    let skills = json!([
        {"label": "Alpha", "tone": "purple", "fill": "gradient", "values": [5, 3, 4, 2, 5, 4]},
        {"label": "Beta", "tone": "orange", "fill": "dotted", "values": [3, 5, 2, 4, 3, 5]}
    ]);
    let mut v = vec![];
    for (i, (from, to, dir, m)) in [
        ("green", Value::Null, "up", "bayer8"),
        ("blue", json!("pink"), "right", "bayer4"),
        ("purple", Value::Null, "radial", "halftone"),
        ("orange", json!("red"), "down", "noise"),
        ("200", json!("grey"), "left", "bayer2"),
    ]
    .iter()
    .enumerate()
    {
        let from = if from.parse::<f64>().is_ok() { json!(200) } else { json!(from) };
        v.push(json!({"name": format!("gradient_{i}"), "kind": "gradient", "w": 64, "h": 24, "from": from,
            "to": to, "direction": dir, "matrix": m, "density": if i == 3 { 0.7 } else { 1.0 }}));
    }
    for (i, field) in ["plasma", "waves", "ripple", "noise", "aurora"].iter().enumerate() {
        v.push(json!({"name": format!("background_{field}"), "kind": "background", "w": 80, "h": 32,
            "field": field, "tone": (["green", "blue", "purple", "orange", "pink"][i]),
            "accent": if i % 2 == 0 { json!("pink") } else { Value::Null },
            "matrix": if i == 3 { "noise" } else { "bayer8" }, "density": 0.6 + 0.08 * i as f64, "time": 1.25 * i as f64}));
    }
    for (i, (style, d)) in [("solid", 0.32), ("solid", 0.85), ("outline", 0.3), ("ghost", 0.5), ("outline", 0.6)].iter().enumerate() {
        v.push(json!({"name": format!("button_{i}_{style}"), "kind": "button", "w": 40, "h": 12,
            "tone": (["green", "blue", "pink", "orange", "purple"][i]), "style": style, "density": d, "time": 0.7 + i as f64}));
    }
    for (i, fill) in ["gradient", "dotted", "hatched", "solid"].iter().enumerate() {
        v.push(json!({"name": format!("meter_{fill}"), "kind": "meter", "w": 60, "h": 6,
            "value": ([0.62, 0.35, 0.8, 0.5][i]), "tone": ([json!("green"), json!("blue"), json!("red"), json!(75)][i].clone()), "fill": fill}));
    }
    for name in ["Ada Lovelace", "Grace Hopper", "Zaki", "satoshi"] {
        v.push(json!({"name": format!("avatar_{}", name.replace(' ', "_").to_lowercase()), "kind": "avatar",
            "w": 8, "h": 8, "text": name, "hue": Value::Null}));
    }
    v.push(json!({"name": "swatch_blue", "kind": "swatch", "w": 5, "h": 5, "tone": "blue"}));
    for (i, hov) in [false, true].iter().enumerate() {
        v.push(json!({"name": format!("sparkline_{i}"), "kind": "sparkline", "w": 48, "h": 14,
            "values": [3, 7, 4, 9, 6, 11, 8, 12, 10], "tone": (["green", "pink"][i]), "hovered": hov, "progress": 1.0}));
    }
    let chart_cases = [
        ("area", false, None, None, "off", 1.0),
        ("area", true, Some(3), None, "low", 1.0),
        ("area", false, None, Some(1), "high", 0.6),
        ("line", false, Some(5), None, "off", 1.0),
        ("line", true, None, None, "aura", 1.0),
        ("bar", false, Some(2), None, "off", 1.0),
        ("bar", true, None, Some(0), "low", 0.7),
    ];
    for (i, (chart, stacked, hover, selected, bl, prog)) in chart_cases.iter().enumerate() {
        v.push(json!({"name": format!("{chart}_{i}"), "kind": "cartesian", "w": 120, "h": 44, "chart": chart,
            "stacked": stacked, "hover": hover, "selected": selected, "bloom": bl, "progress": prog,
            "series": visitors}));
    }
    let slices = json!([
        {"label": "Chrome", "value": 275, "tone": "blue", "fill": "gradient"},
        {"label": "Safari", "value": 200, "tone": "green", "fill": "dotted"},
        {"label": "Firefox", "value": 187, "tone": "orange", "fill": "hatched"},
        {"label": "Edge", "value": 90, "tone": "purple", "fill": "solid"}
    ]);
    for (i, (inner, hover, bl, prog)) in [(0.0, None, "off", 1.0), (0.55, Some(1), "low", 1.0), (0.4, None, "aura", 0.7)]
        .iter()
        .enumerate()
    {
        v.push(json!({"name": format!("pie_{i}"), "kind": "pie", "w": 48, "h": 48, "slices": slices,
            "inner": inner, "hover": hover, "bloom": bl, "progress": prog}));
    }
    for (i, (sel, bl, prog)) in [(None, "off", 1.0), (Some(0), "high", 1.0), (None, "low", 0.5)].iter().enumerate() {
        v.push(json!({"name": format!("radar_{i}"), "kind": "radar", "w": 56, "h": 56, "series": skills,
            "max": Value::Null, "selected": sel, "bloom": bl, "progress": prog, "rings": 4}));
    }
    v.extend(future_retro_scenes());
    v
}

/// Future-Retro scenes: Near AI blue and ink ramps on void, rendered by the
/// reference interpreter.
fn future_retro_scenes() -> Vec<Value> {
    let mut v = vec![];
    let mut push = |mut scene: Value| {
        scene["source"] = json!("vm");
        v.push(scene);
    };
    for (i, (from, to, dir, m, density)) in [
        ("near", Value::Null, "up", "bayer4", 1.0),
        ("near", json!("ink"), "right", "bayer4", 1.0),
        ("ink", Value::Null, "radial", "bayer8", 0.8),
    ]
    .iter()
    .enumerate()
    {
        push(json!({"name": format!("fr_gradient_{i}"), "kind": "gradient", "w": 64, "h": 24, "from": from,
            "to": to, "direction": dir, "matrix": m, "density": density}));
    }
    for (i, field) in ["clouds", "marble", "rings", "caustics"].iter().enumerate() {
        push(json!({"name": format!("fr_background_{field}"), "kind": "background", "w": 96, "h": 40,
            "field": field, "tone": "near", "accent": if i == 1 { json!("ink") } else { Value::Null },
            "matrix": if i == 3 { "bayer8" } else { "bayer4" }, "density": 1.0, "time": 3.0 + 1.5 * i as f64}));
    }
    push(json!({"name": "fr_background_clouds_ink", "kind": "background", "w": 96, "h": 40, "field": "clouds",
        "tone": "ink", "accent": "near", "matrix": "bayer4", "density": 0.8, "time": 12.0}));
    for (i, (style, tone, d)) in [
        ("solid", "near", 0.32),
        ("solid", "near", 0.85),
        ("outline", "ink", 0.0),
        ("outline", "near", 0.3),
        ("ghost", "near", 0.5),
    ]
    .iter()
    .enumerate()
    {
        push(json!({"name": format!("fr_button_{i}_{style}"), "kind": "button", "w": 40, "h": 12,
            "tone": tone, "style": style, "density": d, "time": 0.7 + i as f64}));
    }
    for (fill, tone, value) in [("matrix", "near", 0.62), ("gradient", "ink", 0.4), ("hatched", "near", 0.8)] {
        push(json!({"name": format!("fr_meter_{fill}"), "kind": "meter", "w": 60, "h": 6,
            "value": value, "tone": tone, "fill": fill}));
    }
    for (name, tone) in [("Ada Lovelace", "near"), ("Grace Hopper", "ink"), ("satoshi", "near")] {
        push(json!({"name": format!("fr_avatar_{}", name.replace(' ', "_").to_lowercase()), "kind": "avatar",
            "w": 8, "h": 8, "text": name, "hue": Value::Null, "tone": tone}));
    }
    for tone in ["near", "ink"] {
        push(json!({"name": format!("fr_swatch_{tone}"), "kind": "swatch", "w": 5, "h": 5, "tone": tone}));
    }
    for (i, hov) in [false, true].iter().enumerate() {
        push(json!({"name": format!("fr_sparkline_{i}"), "kind": "sparkline", "w": 48, "h": 14,
            "values": [3, 7, 4, 9, 6, 11, 8, 12, 10], "tone": "near", "hovered": hov, "progress": 1.0}));
    }
    let traffic = json!([
        {"label": "Inference", "tone": "near", "fill": "gradient", "values": [186, 305, 237, 73, 209, 214, 250, 190]},
        {"label": "Training", "tone": "ink", "fill": "hatched", "values": [80, 200, 120, 190, 130, 140, 160, 110]}
    ]);
    let matrix = json!([
        {"label": "Inference", "tone": "near", "fill": "matrix", "values": [186, 305, 237, 73, 209, 214, 250, 190]},
        {"label": "Training", "tone": "ink", "fill": "matrix", "values": [80, 200, 120, 190, 130, 140, 160, 110]}
    ]);
    let single = json!([
        {"label": "Traces", "tone": "near", "fill": "matrix", "values": [12, 18, 13, 29, 24, 36, 30, 44, 41, 0]}
    ]);
    let chart_cases = [
        ("area", &traffic, false, None, None, "off", 1.0),
        ("area", &traffic, true, Some(3), None, "low", 1.0),
        ("line", &traffic, false, Some(5), None, "off", 1.0),
        ("line", &traffic, false, None, Some(0), "aura", 1.0),
        ("bar", &single, false, Some(7), None, "off", 1.0),
        ("bar", &matrix, false, None, None, "low", 0.7),
        ("bar", &matrix, true, None, Some(1), "off", 1.0),
        ("bar", &traffic, false, Some(2), None, "high", 1.0),
    ];
    for (i, (chart, series, stacked, hover, selected, bl, prog)) in chart_cases.iter().enumerate() {
        push(json!({"name": format!("fr_{chart}_{i}"), "kind": "cartesian", "w": 120, "h": 44, "chart": chart,
            "stacked": stacked, "hover": hover, "selected": selected, "bloom": bl, "progress": prog,
            "series": series}));
    }
    let slices = json!([
        {"label": "Compute", "value": 275, "tone": "near", "fill": "gradient"},
        {"label": "Storage", "value": 200, "tone": "ink", "fill": "dotted"},
        {"label": "Network", "value": 120, "tone": "near", "fill": "matrix"},
        {"label": "Other", "value": 90, "tone": "ink", "fill": "solid"}
    ]);
    for (i, (inner, hover, bl, prog)) in [(0.55, Some(1), "low", 1.0), (0.0, None, "aura", 0.7)].iter().enumerate() {
        push(json!({"name": format!("fr_pie_{i}"), "kind": "pie", "w": 48, "h": 48, "slices": slices,
            "inner": inner, "hover": hover, "bloom": bl, "progress": prog}));
    }
    let skills = json!([
        {"label": "Current", "tone": "near", "fill": "gradient", "values": [8, 6, 9, 5, 7]},
        {"label": "Target", "tone": "ink", "fill": "dotted", "values": [9, 8, 7, 9, 8]}
    ]);
    for (i, (sel, bl)) in [(None, "off"), (Some(0), "high")].iter().enumerate() {
        push(json!({"name": format!("fr_radar_{i}"), "kind": "radar", "w": 56, "h": 56, "series": skills,
            "max": Value::Null, "selected": sel, "bloom": bl, "progress": 1.0, "rings": 4}));
    }
    v
}

fn write_png(path: &Path, w: usize, h: usize, px: &[Px]) {
    let file = fs::File::create(path).unwrap();
    let mut enc = png::Encoder::new(std::io::BufWriter::new(file), w as u32, h as u32);
    enc.set_color(png::ColorType::Rgba);
    enc.set_depth(png::BitDepth::Eight);
    let mut wr = enc.write_header().unwrap();
    let data: Vec<u8> = px.iter().flat_map(|c| c.map(|(r, g, b, a)| [r, g, b, a]).unwrap_or([0; 4])).collect();
    wr.write_image_data(&data).unwrap();
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let dir = PathBuf::from(args.get(1).map(String::as_str).unwrap_or("../fixtures"));
    let check_only = args.iter().any(|a| a == "--check");
    if !check_only {
        fs::create_dir_all(dir.join("png")).unwrap();
    }
    let mut out = vec![];
    let mut failed = 0;
    for mut scene in scenes() {
        let name = s(&scene, "name").to_string();
        let (program, ours, theirs) = render(&scene);
        let (w, h) = (f(&scene, "w") as usize, f(&scene, "h") as usize);
        let diff = ours.iter().zip(&theirs).filter(|(a, b)| a != b).count();
        let lit = theirs.iter().filter(|c| c.is_some()).count();
        let source = if from_vm(&scene) { "vm" } else { "dither-core" };
        println!("{name:<24} {w:>3}x{h:<3} lit {lit:>5}  mismatched {diff}  ({source})");
        if diff > 0 {
            failed += 1;
        }
        if !check_only {
            write_png(&dir.join("png").join(format!("{name}.png")), w, h, &theirs);
        } else if let Err(error) = check_png(&dir.join("png").join(format!("{name}.png")), w, h, &theirs) {
            eprintln!("{name}: {error}");
            failed += 1;
        }
        scene["program"] = json!(program);
        out.push(scene);
    }
    let expected = json!({"version": 1, "scenes": out});
    if !check_only {
        fs::write(
            dir.join("scenes.json"),
            serde_json::to_string_pretty(&expected).unwrap(),
        )
        .unwrap();
    } else {
        let stored = fs::read(dir.join("scenes.json"))
            .map_err(|error| error.to_string())
            .and_then(|bytes| serde_json::from_slice::<Value>(&bytes).map_err(|error| error.to_string()));
        match stored {
            Ok(value) if value == expected => {}
            Ok(_) => {
                eprintln!("scenes.json differs from the generated scene parameters or programs");
                failed += 1;
            }
            Err(error) => {
                eprintln!("scenes.json: {error}");
                failed += 1;
            }
        }
    }
    if failed > 0 {
        eprintln!("{failed} parity or fixture check(s) failed");
        std::process::exit(1);
    }
}

/// Compare decoded pixels rather than compression bytes so PNG encoders may vary.
fn check_png(path: &Path, w: usize, h: usize, pixels: &[Px]) -> Result<(), String> {
    let file = fs::File::open(path).map_err(|error| error.to_string())?;
    let mut reader = png::Decoder::new(std::io::BufReader::new(file))
        .read_info().map_err(|error| error.to_string())?;
    let mut bytes = vec![0; reader.output_buffer_size()];
    let info = reader.next_frame(&mut bytes).map_err(|error| error.to_string())?;
    if info.width as usize != w || info.height as usize != h
        || info.color_type != png::ColorType::Rgba || info.bit_depth != png::BitDepth::Eight {
        return Err("expected grid-size 8-bit RGBA PNG".into());
    }
    let expected: Vec<u8> = pixels.iter()
        .flat_map(|color| color.map(|(r, g, b, a)| [r, g, b, a]).unwrap_or([0; 4])).collect();
    if bytes[..info.buffer_size()] != expected {
        return Err("stored pixels differ from the scene's source".into());
    }
    Ok(())
}
