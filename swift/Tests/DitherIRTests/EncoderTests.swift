import Foundation
import XCTest
@testable import DitherIR

final class EncoderTests: XCTestCase {
    func testEveryGoldenSceneIsIndependentlyEncoded() throws {
        let root = URL(fileURLWithPath: #filePath).resolvingSymlinksInPath().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let document = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: root.appendingPathComponent("fixtures/scenes.json"))) as? [String: Any])
        let scenes = try XCTUnwrap(document["scenes"] as? [[String: Any]])
        XCTAssertEqual(scenes.count, 74)
        for scene in scenes {
            let expected = (scene["program"] as! [NSNumber]).map(\.floatValue)
            // Expected floats are used only for comparison; the encoder receives options.
            var options = scene
            options.removeValue(forKey: "program")
            let actual = try encodeScene(options).data
            let name = scene["name"] as! String
            XCTAssertEqual(actual.count, expected.count, name)
            for (i, pair) in zip(actual, expected).enumerated() {
                XCTAssertEqual(pair.0, pair.1, accuracy: 0.0001, "\(name) float \(i)")
            }
        }
    }

    func testProgramKeepsTablesAfterOperationsAndBloomSplit() {
        var p = Program()
        XCTAssertEqual(p.table([7, 8]), 0)
        p.push(Op.swatch, [250, 0.19])
        XCTAssertEqual(p.table([9]), 2)
        p.bloom(2, 0.6)
        XCTAssertEqual(p.data, [8, 13, 4, 250, 0.19, 14, 4, 2, 0.6, 7, 8, 9])
        XCTAssertEqual(Program().data, [0])
    }

    func testHiddenSeriesAndStackedScaleAndReveal() {
        let hidden = Series(label: "hidden", tone: .red, values: [1000, 1000], hidden: true)
        let a = Series(label: "a", tone: .blue, values: [1, 2])
        let b = Series(label: "b", tone: .green, values: [3])
        var c = Cartesian(kind: .area, series: [hidden, a, b])
        c.stacked = true
        XCTAssertEqual(c.scale().max, 4)
        c.gridLines = false; c.baseline = false; c.progress = 0
        c.hover = 1; c.selected = 1
        let data = c.encode(w: 4, h: 5).data
        let ops = operations(data)
        XCTAssertEqual(ops.map { Int($0[0]) }, [3, 3, 4, 4, 14, 1, 2])
        XCTAssertEqual(ops[0][9], 1)
        XCTAssertEqual(ops[1][9], 0.3)
        XCTAssertEqual(ops[4], [14, 4, 0, 0])
        let tableBase = 1 + Int(data[0])
        XCTAssertEqual(Array(data[(tableBase + 2)..<(tableBase + 8)]), Array(repeating: NONE, count: 6))
        XCTAssertEqual(c.indexAt(-100, w: 4), 0)
        XCTAssertEqual(c.indexAt(100, w: 4), 1)
        c.kind = .bar
        XCTAssertEqual(c.indexAt(-100, w: 4), 0)
        XCTAssertEqual(c.indexAt(100, w: 4), 1)
        XCTAssertFalse(operations(c.encode(w: 4, h: 5).data).contains { $0[0] == Float(Op.bloom) })
    }

    func testSurfaceClampingAndPieBoundaries() {
        var gradient = Gradient(from: .blue)
        gradient.density = 3
        XCTAssertEqual(gradient.encode(w: 3, h: 2).data[7], 1)
        var background = Background(field: .noise, tone: .pink)
        background.density = -2
        XCTAssertEqual(background.encode(w: 3, h: 2, time: 0).data[7], 0)
        XCTAssertEqual(ButtonStyle.solid.density(hovered: true, pressed: true), 0.85)
        XCTAssertNil(ButtonStyle.outline.surface(tone: .blue))
        XCTAssertEqual(Bloom.aura.params().0, 4)
        var pie = Pie(slices: [Slice(label: "negative", value: -5, tone: .red), Slice(label: "positive", value: 2, tone: .blue)])
        pie.innerRadius = 2; pie.progress = 2
        let geometry = pie.geometry(w: 20, h: 20)
        XCTAssertEqual(geometry.inner, geometry.radius * 0.95)
        XCTAssertEqual(geometry.angles[0].1, 0)
        XCTAssertEqual(geometry.angles[1].1, Float(bitPattern: 0x40c90fdb))
        XCTAssertNil(geometry.indexAt(10, y: 10))
        XCTAssertNil(geometry.indexAt(40, y: 10))
        XCTAssertEqual(avatar(name: "Ada", hue: -30).data[6], 330)
    }

    func testFormatValueUsesSaturatingIntegerConversion() {
        XCTAssertEqual(formatValue(-0.01), "0")
        XCTAssertEqual(formatValue(1e30), "9223372036854775807B")
        XCTAssertEqual(formatValue(-1e30), "-9223372036854775808B")
    }

    func testScaleWithExtremeFiniteMaximumMatchesRustCast() {
        let scale = Scale.nice(max: .greatestFiniteMagnitude, count: 1)
        XCTAssertTrue(scale.max.isNaN)
        XCTAssertEqual(scale.ticks.count, 1)
        XCTAssertTrue(scale.ticks[0].isNaN)
    }

    func testPieTreatsNaNSliceAsZeroLikeRustMaximum() {
        let pie = Pie(slices: [Slice(label: "nan", value: .nan, tone: .red), Slice(label: "one", value: 1, tone: .blue)])
        let angles = pie.geometry(w: 20, h: 20).angles
        XCTAssertEqual(angles[0].0, 0)
        XCTAssertEqual(angles[0].1, 0)
        XCTAssertEqual(angles[1].0, 0)
        XCTAssertEqual(angles[1].1, Float(bitPattern: 0x40c90fdb))
    }

    func testNonFiniteHitCoordinatesMatchSaturatingRustCasts() {
        let c = Cartesian(kind: .line, series: [Series(label: "", tone: .blue, values: [1, 2, 3])])
        XCTAssertEqual(c.indexAt(.nan, w: 100), 0)
        XCTAssertEqual(c.indexAt(.infinity, w: 100), 2)
        XCTAssertEqual(c.indexAt(-.infinity, w: 100), 0)
    }

    func testColorAndGeometryHelpers() {
        XCTAssertEqual(Tone.parse(" Violet "), .purple)
        XCTAssertEqual(Tone.parse("-30"), .hue(330))
        XCTAssertNil(Tone.parse("unknown"))
        XCTAssertEqual(Rgba.hex(0x102030), Rgba(r: 16, g: 32, b: 48))
        XCTAssertEqual(Rgba.white.fade(0.5).a, 128)
        XCTAssertEqual(Rgba.black.mix(.white, 0.5), Rgba(r: 128, g: 128, b: 128))
        XCTAssertEqual(Rgba(r: 255, g: 0, b: 0, a: 128).over(.black), Rgba(r: 128, g: 0, b: 0))
        XCTAssertEqual(Rgba.white.luminance(), 1, accuracy: 0.00001)
        XCTAssertEqual(Scale.nice(max: 305, count: 4).ticks, [0, 100, 200, 300, 400])
        XCTAssertEqual(Scale.nice(max: .nan, count: 0).max, 1)
        XCTAssertEqual(interpolate(values: [], f: 0), 0)
        XCTAssertEqual(interpolate(values: [3], f: 5), 3)
        XCTAssertEqual(interpolate(values: [2, 8], f: 0.5), 5)
        XCTAssertEqual(formatValue(1200), "1.2k")
        XCTAssertEqual(formatValue(3_000_000), "3M")
        XCTAssertEqual(fnv1a(Array("hello".utf8)), 0xa430d84680aabd0b)
        let chart = Cartesian(kind: .line, series: [Series(label: "a", tone: .blue, values: [2, 8, 4])])
        XCTAssertEqual(chart.columnX(1, w: 101), 50)
        XCTAssertEqual(chart.indexAt(51, w: 101), 1)
        XCTAssertNil(Cartesian(kind: .bar, series: []).indexAt(1, w: 100))
        XCTAssertTrue(chart.encode(w: 1, h: 10).isEmpty)
        let pie = Pie(slices: [Slice(label: "a", value: 1, tone: .red), Slice(label: "b", value: 1, tone: .green)])
        XCTAssertEqual(pie.geometry(w: 48, h: 48).indexAt(35, y: 24), 0)
        XCTAssertEqual(pie.geometry(w: 48, h: 48).indexAt(13, y: 24), 1)
        XCTAssertTrue(Radar(series: [Series(label: "", tone: .red, values: [1, 2])]).encode(w: 10, h: 10).isEmpty)
    }
}

private func encodeScene(_ s: [String: Any]) throws -> Program {
    func str(_ k: String) -> String { s[k] as! String }
    func f(_ k: String) -> Float { (s[k] as! NSNumber).floatValue }
    func opt(_ k: String) -> Int? { (s[k] as? NSNumber)?.intValue }
    func tone(_ v: Any?) -> Tone { if let n = v as? NSNumber { return .hue(n.floatValue) }; return Tone.parse(v as! String)! }
    func tones(_ k: String) -> Tone? { guard let v = s[k], !(v is NSNull) else { return nil }; return tone(v) }
    func series() -> [Series] { (s["series"] as! [[String: Any]]).map { v in Series(label: v["label"] as! String, tone: tone(v["tone"]), values: (v["values"] as! [NSNumber]).map(\.doubleValue), fill: Fill(rawValue: v["fill"] as! String)!, hidden: v["hidden"] as? Bool ?? false) } }
    let w = Int(f("w")), h = Int(f("h"))
    switch str("kind") {
    case "gradient":
        var g = Gradient(from: tone(s["from"]))
        g.to = tones("to"); g.direction = Direction(rawValue: str("direction"))!; g.matrix = Matrix(rawValue: str("matrix"))!; g.density = f("density")
        return g.encode(w: w, h: h)
    case "background":
        var b = Background(field: Field(rawValue: str("field"))!, tone: tone(s["tone"]))
        b.accent = tones("accent"); b.matrix = Matrix(rawValue: str("matrix"))!; b.density = f("density")
        return b.encode(w: w, h: h, time: f("time"))
    case "button": return button(w: w, h: h, tone: tone(s["tone"]), style: ButtonStyle(rawValue: str("style"))!, density: f("density"), time: f("time"))
    case "meter": return meter(w: w, h: h, value: f("value"), tone: tone(s["tone"]), fill: Fill(rawValue: str("fill"))!)
    case "avatar":
        if let t = tones("tone") { return avatar(name: str("text"), tone: t) }
        return avatar(name: str("text"), hue: (s["hue"] as? NSNumber)?.floatValue)
    case "swatch": return swatch(tone: tone(s["tone"]))
    case "sparkline": return sparkline(values: (s["values"] as! [NSNumber]).map(\.doubleValue), tone: tone(s["tone"]), w: w, h: h, hovered: s["hovered"] as! Bool, progress: f("progress"))
    case "cartesian":
        var c = Cartesian(kind: CartesianKind(rawValue: str("chart"))!, series: series())
        c.stacked = s["stacked"] as! Bool; c.progress = f("progress"); c.hover = opt("hover"); c.selected = opt("selected"); c.bloom = Bloom(rawValue: str("bloom"))!
        return c.encode(w: w, h: h)
    case "pie":
        var p = Pie(slices: (s["slices"] as! [[String: Any]]).map { v in Slice(label: v["label"] as! String, value: (v["value"] as! NSNumber).doubleValue, tone: tone(v["tone"]), fill: Fill(rawValue: v["fill"] as! String)!) })
        p.innerRadius = f("inner"); p.progress = f("progress"); p.hover = opt("hover"); p.bloom = Bloom(rawValue: str("bloom"))!
        return p.encode(w: w, h: h)
    case "radar":
        var r = Radar(series: series())
        r.max = (s["max"] as? NSNumber)?.doubleValue; r.progress = f("progress"); r.selected = opt("selected"); r.rings = Int(f("rings")); r.bloom = Bloom(rawValue: str("bloom"))!
        return r.encode(w: w, h: h)
    default: throw NSError(domain: "unknown scene", code: 1)
    }
}

private func operations(_ data: [Float]) -> [[Float]] {
    var at = 1, result: [[Float]] = []
    while at < 1 + Int(data[0]) {
        let end = at + Int(data[at + 1])
        result.append(Array(data[at..<end]))
        at = end
    }
    return result
}
