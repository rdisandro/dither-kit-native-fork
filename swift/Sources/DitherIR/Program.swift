/// The platform independent display list. Operations precede relative-offset tables.
public struct Program: Equatable, Sendable {
    private var ops: [Float] = []
    private var tables: [Float] = []
    public init() {}
    /// First bloom operation or operation-list end; malformed lists return nil.
    public static func bloomIndex(in data: [Float]) -> Int? {
        guard let header = data.first, let count = Int(exactly: header), count >= 0, count < data.count else { return nil }
        let end = count + 1
        var index = 1, visited = 0, split = end
        while index < end {
            guard index + 1 < end, let length = Int(exactly: data[index + 1]),
                  length >= 2, length <= end - index else { return nil }
            if split == end && visited < 65_536 && data[index] >= 14 && data[index] < 15 { split = index }
            index += length
            visited += 1
        }
        return split
    }
    public var isEmpty: Bool { ops.isEmpty }
    public var data: [Float] { [Float(ops.count)] + ops + tables }
    public func toArray() -> [Float] { data }
    public func toVec() -> [Float] { data }
    public mutating func push(_ opcode: UInt32, _ args: [Float]) {
        ops += [Float(opcode), Float(args.count + 2)] + args
    }
    @discardableResult public mutating func table(_ values: [Float]) -> Float {
        let offset = Float(tables.count)
        tables += values
        return offset
    }
    public mutating func line(_ start: (Int, Int), _ end: (Int, Int), color: Rgba, dash: UInt32) {
        push(Op.line, [Float(start.0), Float(start.1), Float(end.0), Float(end.1), Float(dash)] + rgba(color))
    }
    public mutating func dot(_ x: Int, _ y: Int, ring: Rgba, center: Rgba) {
        push(Op.dot, [Float(x), Float(y)] + rgba(ring) + rgba(center))
    }
    public mutating func bloom(_ radius: Int, _ strength: Float) { push(Op.bloom, [Float(radius), strength]) }
    /// A Future-Retro bloom: opaque blue-ink glow, thinned by ordered dither.
    public mutating func bloomRamp(_ radius: Int, _ strength: Float) { push(Op.bloom, [Float(radius), strength, glowRamp]) }
}

public enum Op {
    public static let line: UInt32 = 1, dot: UInt32 = 2, areaBody: UInt32 = 3, areaStroke: UInt32 = 4
    public static let bar: UInt32 = 5, pie: UInt32 = 6, poly: UInt32 = 7, gradient: UInt32 = 8
    public static let background: UInt32 = 9, button: UInt32 = 10, meter: UInt32 = 11
    public static let avatar: UInt32 = 12, swatch: UInt32 = 13, bloom: UInt32 = 14
}
public let NONE: Float = -1e9
/// Optional third BLOOM argument selecting the Future-Retro blue-ink glow.
public let glowRamp: Float = 1
public func rgba(_ color: Rgba) -> [Float] { [Float(color.r), Float(color.g), Float(color.b), Float(color.a)] }
public func tone(_ tone: Tone) -> [Float] { [tone.hue(), tone.chroma()] }
