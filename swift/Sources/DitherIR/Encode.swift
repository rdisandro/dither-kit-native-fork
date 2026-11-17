import Foundation

public enum Matrix: String, CaseIterable, Sendable {
    case bayer2, bayer4, bayer8, halftone, noise
    public static let all = allCases
    public func id() -> Float { Float(Self.allCases.firstIndex(of: self)!) }
}
public enum Fill: String, CaseIterable, Sendable {
    /// `matrix` is the Future-Retro dot matrix: one dot per two cells, lit
    /// from the axis, unlit dots a faint grid in bars and meters.
    case gradient, dotted, hatched, solid, matrix
    public static let all = allCases
    public func id() -> Float { Float(Self.allCases.firstIndex(of: self)!) }
}
public enum Bloom: String, CaseIterable, Sendable {
    case off, low, high, aura
    public static let all = allCases
    public func params() -> (Int, Float) { switch self { case .off: (0, 0); case .low: (1, 0.45); case .high: (2, 0.6); case .aura: (4, 0.7) } }
    func emit(_ p: inout Program, ramp: Bool) {
        if self != .off { let (r, s) = params(); if ramp { p.bloomRamp(r, s) } else { p.bloom(r, s) } }
    }
}
public enum Direction: String, CaseIterable, Sendable {
    case up, down, left, right, radial
    public func id() -> Float { Float(Self.allCases.firstIndex(of: self)!) }
}
/// Background fields: dither-core's five, then Future-Retro's (clouds is
/// the house field), evolved in absolute grid units.
public enum Field: String, CaseIterable, Sendable {
    case plasma, waves, ripple, noise, aurora, clouds, marble, rings, caustics
    public static let all = allCases
    public static let light: [Field] = [.clouds, .marble, .rings, .caustics]
    public func id() -> Float { Float(Self.allCases.firstIndex(of: self)!) }
    public var isLight: Bool { id() >= 5 }
    public func defaultDensity() -> Float { isLight ? 1 : 0.6 }
}
/// Future-Retro field energy: phase advances `energy * 4` per second and
/// eases between idle and hover over about 200 ms.
public struct Energy: Equatable, Sendable {
    public var idle: Float, hover: Float
    public init(idle: Float, hover: Float) { self.idle = idle; self.hover = hover }
    public static let field = Energy(idle: 0.18, hover: 0.32)
    public static let card = Energy(idle: 0.12, hover: 0.28)
    public func step(phase: Float, energy: Float, hovered: Bool, dt: Float) -> (phase: Float, energy: Float) {
        let target = hovered ? hover : idle
        let energy = energy + (target - energy) * min(dt / 0.2, 1)
        return (phase + dt * energy * 4, energy)
    }
}
/// Future-Retro motion: film-rate canvases, a still frame, quick and reveal durations.
public enum Motion {
    public static let filmFPS: Double = 24
    public static let stillPhase: Float = 3
    public static let reveal: Double = 0.6
    public static let quick: Double = 0.16
}
public func easeOutCubic(_ t: Float) -> Float { let t = clamp(t, 0, 1); let k = 1 - t; return 1 - k * k * k }
public struct Gradient: Equatable, Sendable {
    public var from: Tone, to: Tone?
    public var direction: Direction = .up
    public var matrix: Matrix = .bayer4
    public var density: Float = 1
    public init(from: Tone) { self.from = from }
    public func encode(w: Int, h: Int) -> Program {
        var p = Program()
        p.push(Op.gradient, [Float(w), Float(h), direction.id(), matrix.id(), clamp(density, 0, 1)] + tone(from) + [to == nil ? 0 : 1] + (to.map(tone) ?? [0, 0]))
        return p
    }
}
public struct Background: Equatable, Sendable {
    public var field: Field, tone: Tone, accent: Tone?
    public var matrix: Matrix = .bayer4
    public var density: Float
    public init(field: Field, tone: Tone) { self.field = field; self.tone = tone; self.density = field.defaultDensity() }
    public func encode(w: Int, h: Int, time: Float) -> Program {
        var p = Program()
        p.push(Op.background, [Float(w), Float(h), field.id(), matrix.id(), clamp(density, 0, 1), time, tone.hue(), tone.chroma(), accent == nil ? 0 : 1, accent?.hue() ?? 0, accent?.chroma() ?? 0])
        return p
    }
}
public enum ButtonStyle: String, CaseIterable, Sendable {
    case solid, outline, ghost
    public func id() -> Float { Float(Self.allCases.firstIndex(of: self)!) }
    public func density(hovered: Bool, pressed: Bool) -> Float {
        let values: (Float, Float, Float) = switch self { case .solid: (0.32, 0.55, 0.85); case .outline: (0, 0.3, 0.6); case .ghost: (0, 0.25, 0.5) }
        return pressed ? values.2 : hovered ? values.1 : values.0
    }
    public func surface(tone: Tone) -> Rgba? { self == .solid ? (tone.isRamp ? tone.base() : tone.shade(0.22)) : nil }
    /// Native chrome for a Future-Retro button: flat fills, no texture.
    public func chrome(tone: Tone, hovered: Bool, pressed: Bool) -> ButtonChrome {
        let hot = hovered || pressed, base = tone.base()
        switch self {
        case .solid: return ButtonChrome(fill: hot ? base.mix(.white, 0.12) : base, border: nil, label: Palette.void, glow: base.fade(hot ? 0.8 : 0.65), underline: nil)
        case .outline: return ButtonChrome(fill: hot ? Palette.marble : nil, border: Palette.marble, label: hot ? Palette.void : Palette.marble, glow: nil, underline: nil)
        case .ghost: return ButtonChrome(fill: nil, border: nil, label: hot ? base : Palette.marble, glow: nil, underline: base)
        }
    }
}
public struct ButtonChrome: Equatable, Sendable {
    public var fill: Rgba?
    public var border: Rgba?
    public var label: Rgba
    public var glow: Rgba?
    public var underline: Rgba?
    public init(fill: Rgba?, border: Rgba?, label: Rgba, glow: Rgba?, underline: Rgba?) {
        self.fill = fill; self.border = border; self.label = label; self.glow = glow; self.underline = underline
    }
    /// Glow (y offset, blur radius) in points, after the app's tab bar.
    public static func glowGeometry(hovered: Bool) -> (y: Float, radius: Float) { hovered ? (18, 36) : (14, 30) }
}
public func button(w: Int, h: Int, tone t: Tone, style: ButtonStyle, density: Float, time: Float) -> Program {
    var p = Program(); p.push(Op.button, [Float(w), Float(h), style.id(), density, time] + tone(t)); return p
}
public func meter(w: Int, h: Int, value: Float, tone t: Tone, fill: Fill) -> Program {
    var p = Program(); p.push(Op.meter, [Float(w), Float(h), value, fill.id()] + tone(t)); return p
}
public func swatch(tone t: Tone) -> Program { var p = Program(); p.push(Op.swatch, tone(t)); return p }
public func fnv1a(_ bytes: [UInt8]) -> UInt64 {
    var h: UInt64 = 0xcbf29ce484222325
    for b in bytes { h ^= UInt64(b); h = h &* 0x00000100000001b3 }
    return h
}
/// An identicon in one tone; a Future-Retro ramp lights it in its pigment,
/// accents it in its bright tint, and leaves the rest slate.
public func avatar(name: String, tone: Tone) -> Program {
    guard let id = tone.ramp else { return avatar(name: name, hue: tone.hue()) }
    return avatarProgram(name: name, hueSlot: -Float(id))
}
public func avatar(name: String, hue: Float? = nil) -> Program {
    let hash = fnv1a(Array(name.utf8))
    return avatarProgram(name: name, hueSlot: Tone.hue(hue ?? Float((hash >> 33) % 180) * 2).hue())
}
private func avatarProgram(name: String, hueSlot: Float) -> Program {
    let hash = fnv1a(Array(name.utf8)), bits = UInt32(truncatingIfNeeded: hash)
    let mirror = (hash >> 32) & 1 == 0
    let acc = UInt32(truncatingIfNeeded: hash >> 41) & 0x7fffff
    var p = Program()
    p.push(Op.avatar, [Float(bits & 0xffff), Float(bits >> 16), mirror ? 1 : 0, hueSlot, Float(acc & 0xffff), Float(acc >> 16)])
    return p
}
public struct Series: Equatable, Sendable {
    public var label: String, tone: Tone, fill: Fill, values: [Double], hidden: Bool
    public init(label: String, tone: Tone, values: [Double], fill: Fill = .gradient, hidden: Bool = false) {
        self.label = label; self.tone = tone; self.values = values; self.fill = fill; self.hidden = hidden
    }
}
public struct Scale: Equatable, Sendable {
    public var max: Double, ticks: [Double]
    public init(max: Double, ticks: [Double]) { self.max = max; self.ticks = ticks }
    public static func nice(max: Double, count: Int) -> Scale {
        let maximum = max.isFinite && max > 0 ? max : 1
        let raw = maximum / Double(Swift.max(count, 1))
        let mag = pow(10, floor(log10(raw)))
        let step = [1.0, 2, 2.5, 5, 10].map { $0 * mag }.first { $0 >= raw } ?? 10 * mag
        let top = ceil(maximum / step) * step
        let intervals = (top / step).rounded()
        let count = intervals.isNaN || intervals <= 0 ? 0 : Int(intervals)
        return Scale(max: top, ticks: (0...count).map { Double($0) * step })
    }
    public func y(_ value: Double, h: Int) -> Float { let span = Float(Swift.max(h - 1, 0)); return span - Float(clamp(value / max, 0, 1)) * span }
}
public func formatValue(_ v: Double) -> String {
    let a = abs(v)
    let (n, suffix): (Double, String) = a >= 1e9 ? (v / 1e9, "B") : a >= 1e6 ? (v / 1e6, "M") : a >= 1e3 ? (v / 1e3, "k") : (v, "")
    if abs(n - n.rounded()) < 0.05 {
        let rounded = n.rounded()
        let integer: Int64 = rounded >= Double(Int64.max) ? .max : rounded <= Double(Int64.min) ? .min : Int64(rounded)
        return String(integer) + suffix
    }
    if n.isNaN { return "NaN" + suffix }
    return String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), n) + suffix
}
public func interpolate(values: [Double], f: Float) -> Double {
    let n = values.count
    if n == 0 { return 0 }; if n == 1 { return values[0] }
    let f = clamp(f, 0, Float(n - 1)), i = f.isNaN ? 0 : min(Int(floor(f)), n - 2), t = Double(f - Float(i))
    func at(_ k: Int) -> Double { values[clamp(k, 0, n - 1)] }
    let p0 = at(i - 1), p1 = at(i), p2 = at(i + 1), p3 = at(i + 2)
    let v = 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)
    return clamp(v, min(p1, p2), max(p1, p2))
}
public func guide() -> Rgba { Tone.grey.shade(0.55).fade(0.45) }
/// Future-Retro rules: faint ink hairlines.
public func rule() -> Rgba { Palette.rule }
public enum CartesianKind: String, CaseIterable, Sendable { case area, line, bar }
public struct Cartesian: Sendable {
    public var kind: CartesianKind, series: [Series]
    public var stacked = false
    public var progress: Float = 1
    public var hover: Int?, selected: Int?
    public var bloom: Bloom = .off
    public var yMax: Double?
    public var gridLines = true
    public var tickCount = 4
    public var baseline = true
    public init(kind: CartesianKind, series: [Series]) { self.kind = kind; self.series = series }
    public var count: Int { series.map(\.values.count).max() ?? 0 }
    public var isEmpty: Bool { count == 0 }
    public func len() -> Int { count }
    public func scale() -> Scale {
        let visible = series.filter { !$0.hidden }
        let maximum: Double
        if let yMax { maximum = yMax }
        else if stacked { maximum = (0..<count).map { i in visible.reduce(0) { $0 + ($1.values.indices.contains(i) ? $1.values[i] : 0) } }.reduce(0, max) }
        else { maximum = visible.flatMap(\.values).reduce(0, max) }
        return .nice(max: maximum, count: tickCount)
    }
    public func columnX(_ i: Int, w: Int) -> Float {
        let n = max(count, 1)
        if kind == .bar { return (Float(i) + 0.5) * Float(w) / Float(n) }
        if n == 1 { return Float(w) / 2 }
        return Float(i) * Float(max(w - 1, 0)) / Float(n - 1)
    }
    public func indexAt(_ x: Float, w: Int) -> Int? {
        let n = count
        guard n > 0 && w > 0 else { return nil }
        let i = kind == .bar ? floor(x / Float(w) * Float(n)) : (x / Float(max(w, 2) - 1) * Float(n - 1)).rounded()
        if i.isNaN || i <= 0 { return 0 }
        if i >= Float(n - 1) { return n - 1 }
        return Int(i)
    }
    /// Future-Retro chrome when any series uses a ramp tone.
    public var isRamp: Bool { series.contains { $0.tone.isRamp } }
    private func alphaFor(_ index: Int) -> Float { selected != nil && selected != index ? 0.3 : 1 }
    private func stackBelow(_ si: Int, _ i: Int) -> Double { series[..<si].filter { !$0.hidden }.reduce(0) { $0 + ($1.values.indices.contains(i) ? $1.values[i] : 0) } }
    public func encode(w: Int, h: Int) -> Program {
        var p = Program()
        guard w >= 2 && h >= 2 else { return p }
        let scale = scale(), ramp = isRamp
        let (grid, gridDash, baseDash): (Rgba, UInt32, UInt32) = ramp ? (rule(), 2, 1) : (guide(), 3, 2)
        if gridLines { for t in scale.ticks.dropFirst() { let y = pixel(scale.y(t, h: h).rounded()); p.line((0, y), (w - 1, y), color: grid, dash: gridDash) } }
        if baseline { p.line((0, h - 1), (w - 1, h - 1), color: grid, dash: baseDash) }
        if kind == .bar { encodeBars(&p, scale, w, h) } else { encodeCurves(&p, scale, w, h) }
        if kind != .bar, let hover, hover >= 0 && hover < count {
            if bloom == .off { p.bloom(0, 0) } else { bloom.emit(&p, ramp: ramp) }
            encodeHover(&p, scale, w, h)
        } else { bloom.emit(&p, ramp: ramp) }
        return p
    }
    private func encodeCurves(_ p: inout Program, _ scale: Scale, _ w: Int, _ h: Int) {
        let n = count, reveal = clamp(progress, 0, 1) * Float(w)
        let grow = 0.6 + 0.4 * Double(clamp(progress, 0, 1))
        var base = [Double](repeating: 0, count: w)
        var cols = [[Float]](repeating: [Float](repeating: NONE, count: w * 2), count: series.count)
        for x in 0..<w {
            if Float(x) > reveal { break }
            let f = n > 1 ? Float(x) / Float(w - 1) * Float(n - 1) : 0
            for (si, s) in series.enumerated() where !s.hidden {
                let v = interpolate(values: s.values, f: f) * grow, b = stacked ? base[x] : 0
                cols[si][x * 2] = scale.y(b + v, h: h); cols[si][x * 2 + 1] = scale.y(b, h: h)
                if stacked { base[x] += v }
            }
        }
        let strength: Float = kind == .area ? 1 : 0.28
        let offsets: [Float?] = series.enumerated().map { si, s in s.hidden ? nil : p.table(cols[si]) }
        for (si, s) in series.enumerated() {
            guard let off = offsets[si] else { continue }
            p.push(Op.areaBody, [off, Float(w), Float(h), s.fill.id()] + tone(s.tone) + [strength, alphaFor(si)])
        }
        for (si, s) in series.enumerated() {
            guard let off = offsets[si] else { continue }
            p.push(Op.areaStroke, [off, Float(w)] + rgba(s.tone.shade(0.78).fade(alphaFor(si))))
        }
        if kind == .line && w / max(n, 1) >= 6 {
            for (si, s) in series.enumerated() where !s.hidden {
                for (i, v) in s.values.enumerated() {
                    let x = columnX(i, w: w)
                    if x > reveal { break }
                    let b = stacked ? stackBelow(si, i) : 0
                    let y = pixel(scale.y((b + v) * grow, h: h).rounded()), c = s.tone.shade(0.78).fade(alphaFor(si))
                    p.dot(pixel(x.rounded()), y, ring: c, center: c)
                }
            }
        }
    }
    private func encodeHover(_ p: inout Program, _ scale: Scale, _ w: Int, _ h: Int) {
        guard let i = hover, i >= 0 && i < count else { return }
        let x = pixel(columnX(i, w: w).rounded()), ramp = isRamp
        p.line((x, 0), (x, h - 1), color: ramp ? Palette.nearBlue : Tone.grey.shade(0.75).fade(0.7), dash: 2)
        for (si, s) in series.enumerated() where !s.hidden && s.values.indices.contains(i) {
            let b = stacked ? stackBelow(si, i) : 0
            let y = pixel(scale.y(b + s.values[i], h: h).rounded())
            if ramp { p.dot(x, y, ring: s.tone.base(), center: Palette.marble) }
            else { p.dot(x, y, ring: s.tone.shade(0.7), center: s.tone.shade(0.98)) }
        }
    }
    private func encodeBars(_ p: inout Program, _ scale: Scale, _ w: Int, _ h: Int) {
        let n = count
        guard n > 0 else { return }
        let visible = series.indices.filter { !series[$0].hidden }, k = stacked ? 1 : max(visible.count, 1)
        let band = Float(w) / Float(n), group = max(band * 0.72, 1)
        let gap: Float = group / Float(k) > 3 ? 1 : 0
        let barW = max((group - gap * (Float(k) - 1)) / Float(k), 1)
        let prog = Double(clamp(progress, 0, 1))
        for i in 0..<n {
            let x0 = Float(i) * band + (band - group) / 2
            let bandAlpha: Float = hover != nil && hover != i ? 0.45 : 1
            var base = 0.0
            for (slot, si) in visible.enumerated() {
                let s = series[si], v = (s.values.indices.contains(i) ? s.values[i] : 0) * prog
                let left = stacked ? x0 : x0 + Float(slot) * (barW + gap)
                let bx0 = pixel(left.rounded()), bx1 = pixel((left + barW).rounded()) - 1
                p.push(Op.bar, [Float(bx0), Float(bx1), scale.y(base + v, h: h), Float(pixel(scale.y(base, h: h))), Float(h), s.fill.id()] + tone(s.tone) + [alphaFor(si) * bandAlpha])
                if stacked { base += v }
            }
        }
    }
}
public func sparkline(values: [Double], tone: Tone, w: Int, h: Int, hovered: Bool, progress: Float) -> Program {
    var c = Cartesian(kind: .area, series: [Series(label: "", tone: tone, values: values)])
    c.gridLines = false; c.baseline = false; c.progress = progress; c.bloom = hovered ? .low : .off
    let lo = values.reduce(Double.infinity, min), hi = values.reduce(0, max)
    c.yMax = max(hi * 1.1, lo + 1e-9)
    return c.encode(w: w, h: h)
}
public struct Slice: Equatable, Sendable {
    public var label: String, value: Double, tone: Tone, fill: Fill
    public init(label: String, value: Double, tone: Tone, fill: Fill = .gradient) { self.label = label; self.value = value; self.tone = tone; self.fill = fill }
}
public struct PieGeometry: Sendable {
    public var center: (Float, Float), radius: Float, inner: Float, angles: [(Float, Float)]
    public func indexAt(_ x: Float, y: Float) -> Int? {
        let dx = x - center.0, dy = y - center.1, r = sqrt(dx * dx + dy * dy)
        if r < inner || r > radius + 2 { return nil }
        let a = remEuclid(atan2(dx, -dy), irTau)
        return angles.firstIndex { a >= $0.0 && a < $0.1 }
    }
}
public struct Pie: Sendable {
    public var slices: [Slice]
    public var innerRadius: Float = 0
    public var progress: Float = 1
    public var hover: Int?
    public var bloom: Bloom = .off
    public init(slices: [Slice]) { self.slices = slices }
    public func geometry(w: Int, h: Int) -> PieGeometry {
        func positive(_ value: Double) -> Double { value.isNaN ? 0 : max(value, 0) }
        let total = slices.reduce(0) { $0 + positive($1.value) }
        let sweep = irTau * clamp(progress, 0, 1)
        var acc: Float = 0
        let angles: [(Float, Float)] = slices.map { s in
            let a = total > 0 ? Float(positive(s.value) / total) * sweep : 0
            let result = (acc, acc + a); acc += a; return result
        }
        let radius = max(Float(min(w, h)) / 2 - 2.5, 1)
        return PieGeometry(center: (Float(w) / 2, Float(h) / 2), radius: radius, inner: radius * clamp(innerRadius, 0, 0.95), angles: angles)
    }
    public func encode(w: Int, h: Int) -> Program {
        let geo = geometry(w: w, h: h)
        var p = Program(), table: [Float] = []
        for (s, angles) in zip(slices, geo.angles) { table += [angles.0, angles.1] + tone(s.tone) + [s.fill.id()] }
        let offset = p.table(table)
        p.push(Op.pie, [geo.center.0, geo.center.1, geo.radius, geo.inner, slices.count > 1 ? 1 : 0, hover.map(Float.init) ?? -1, Float(slices.count), offset])
        bloom.emit(&p, ramp: slices.contains { $0.tone.isRamp })
        return p
    }
}
public struct RadarGeometry: Sendable {
    public var center: (Float, Float), radius: Float, axisTips: [(Float, Float)]
}
public struct Radar: Sendable {
    public var series: [Series]
    public var max: Double?
    public var progress: Float = 1
    public var selected: Int?
    public var rings = 4
    public var bloom: Bloom = .off
    public init(series: [Series]) { self.series = series }
    public var axes: Int { series.map(\.values.count).max() ?? 0 }
    public func geometry(w: Int, h: Int) -> RadarGeometry {
        let n = Swift.max(axes, 1), center: (Float, Float) = (Float(w) / 2, Float(h) / 2)
        let radius = Swift.max(Float(Swift.min(w, h)) / 2 - 2, 1)
        let tips: [(Float, Float)] = (0..<n).map { i in
            let a = -irHalfPi + irTau * Float(i) / Float(n)
            return (center.0 + cos(a) * radius, center.1 + sin(a) * radius)
        }
        return RadarGeometry(center: center, radius: radius, axisTips: tips)
    }
    public func encode(w: Int, h: Int) -> Program {
        let geo = geometry(w: w, h: h), n = axes
        var p = Program()
        guard n >= 3 else { return p }
        let c = (pixel(geo.center.0.rounded()), pixel(geo.center.1.rounded()))
        func at(_ i: Int, _ f: Float) -> (Int, Int) {
            let (tx, ty) = geo.axisTips[i % n]
            return (pixel((geo.center.0 + (tx - geo.center.0) * f).rounded()), pixel((geo.center.1 + (ty - geo.center.1) * f).rounded()))
        }
        let ramp = series.contains { $0.tone.isRamp }
        let (ringColor, ringDash, spokeDash): (Rgba, UInt32, UInt32) = ramp ? (rule(), 2, 2) : (guide(), 2, 3)
        for ring in 1...Swift.max(rings, 1) {
            let f = Float(ring) / Float(Swift.max(rings, 1))
            for i in 0..<n { p.line(at(i, f), at(i + 1, f), color: ringColor, dash: ringDash) }
        }
        for i in 0..<n { p.line(c, at(i, 1), color: ringColor, dash: spokeDash) }
        let maximum = max ?? Scale.nice(max: series.flatMap(\.values).reduce(0, Swift.max), count: rings).max
        let prog = clamp(progress, 0, 1)
        for (si, s) in series.enumerated() where !s.hidden {
            let alpha: Float = selected != nil && selected != si ? 0.3 : 1
            let pts: [(Float, Float)] = (0..<n).map { i in
                let f = Float(clamp((s.values.indices.contains(i) ? s.values[i] : 0) / maximum, 0, 1)) * prog
                let (tx, ty) = geo.axisTips[i]
                return (geo.center.0 + (tx - geo.center.0) * f, geo.center.1 + (ty - geo.center.1) * f)
            }
            let offset = p.table(pts.flatMap { [$0.0, $0.1] })
            p.push(Op.poly, [geo.center.0, geo.center.1, geo.radius, s.fill.id()] + tone(s.tone) + [alpha, Float(n), offset])
            let stroke = s.tone.shade(0.8).fade(alpha)
            for i in 0..<n {
                let a = (pixel(pts[i].0.rounded()), pixel(pts[i].1.rounded()))
                let b = (pixel(pts[(i + 1) % n].0.rounded()), pixel(pts[(i + 1) % n].1.rounded()))
                p.line(a, b, color: stroke, dash: 1)
            }
            for (x, y) in pts { p.dot(pixel(x.rounded()), pixel(y.rounded()), ring: stroke, center: s.tone.shade(0.97).fade(alpha)) }
        }
        bloom.emit(&p, ramp: ramp)
        return p
    }
}
