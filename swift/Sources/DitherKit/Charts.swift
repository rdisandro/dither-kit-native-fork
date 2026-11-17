import SwiftUI
import DitherIR

/// A native legend: buttons toggle visibility, hover selects a series.
public struct DitherLegend: View {
    @Binding private var series: [Series]
    @Binding private var selected: Int?
    public init(series: Binding<[Series]>, selected: Binding<Int?> = .constant(nil)) { _series = series; _selected = selected }
    public var body: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 16) { entries }
            VStack(alignment: .leading, spacing: 8) { entries }
        }
    }
    private var entries: some View {
        ForEach(Array(series.enumerated()), id: \.offset) { index, item in
            Button { series[index].hidden.toggle() } label: {
                HStack(spacing: 6) { DitherSwatch(item.tone); Text(item.label) }.opacity(item.hidden ? 0.4 : 1)
            }
            .buttonStyle(.plain)
            .font(.caption)
            .foregroundStyle(FutureRetro.silver)
            .accessibilityLabel(item.label)
            .accessibilityValue(item.hidden ? "Hidden" : "Visible")
            .accessibilityHint("Toggle series visibility")
            .onHover { selected = $0 ? index : nil }
        }
    }
}

public struct DitherChart: View {
    public var chart: Cartesian
    public var labels: [String]
    public var cell: CGFloat
    @Environment(\.accessibilityReduceMotion) private var systemReduceMotion
    @Environment(\.ditherAnimationsEnabled) private var animationsEnabled
    private var reduceMotion: Bool { systemReduceMotion || !animationsEnabled }
    @State private var visibility = SeriesVisibility()
    @State private var selected: Int?
    @State private var hover: Int?
    @State private var pointer = CGPoint.zero
    @State private var appeared = false
    public init(_ chart: Cartesian, labels: [String] = [], cell: CGFloat = 3) { self.chart = chart; self.labels = labels; self.cell = max(cell, 0.25) }
    public init(kind: CartesianKind = .area, series: [Series], labels: [String] = [], cell: CGFloat = 3) { self.init(Cartesian(kind: kind, series: series), labels: labels, cell: cell) }
    private var options: Cartesian {
        var result = chart
        result.series = visibility.applying(to: chart.series)
        result.hover = hover ?? chart.hover
        result.selected = selected ?? chart.selected
        return result
    }
    private var legendSeries: Binding<[Series]> {
        Binding(get: { options.series }, set: { items in visibility.update(from: items) })
    }
    public var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                yAxis.frame(width: 42)
                plot
            }
            if !labels.isEmpty { xAxis.padding(.leading, 50) }
            DitherLegend(series: legendSeries, selected: $selected).padding(.leading, 50)
        }
        .onAppear { withAnimation(reduceMotion ? nil : FutureRetro.reveal) { appeared = true } }
    }
    private var yAxis: some View {
        GeometryReader { geometry in
            let scale = options.scale()
            ForEach(scale.ticks, id: \.self) { tick in
                Text(formatValue(tick)).font(.system(size: 10, design: .monospaced)).foregroundStyle(FutureRetro.pewter)
                    .position(x: 21, y: CGFloat(1 - tick / scale.max) * max(0, geometry.size.height - cell))
            }
        }
        .accessibilityHidden(true)
    }
    private var xAxis: some View {
        GeometryReader { geometry in
            let width = max(0, Int(geometry.size.width / cell))
            ForEach(Array(labels.enumerated()), id: \.offset) { index, label in
                Text(label).font(.system(size: 10, design: .monospaced)).textCase(.uppercase).foregroundStyle(FutureRetro.pewter)
                    .position(x: CGFloat(options.columnX(index, w: width)) * cell + cell / 2, y: 8)
            }
        }
        .frame(height: 16)
        .accessibilityHidden(true)
    }
    private var plot: some View {
        GeometryReader { geometry in
            let width = max(0, Int(geometry.size.width / cell))
            AnimatedDitherSurface(value: (reduceMotion || appeared) ? Double(chart.progress) : 0, cell: cell) { w, h, progress in
                var current = options; current.progress = Float(progress)
                return current.encode(w: w, h: h)
            }
            .contentShape(Rectangle())
            .onContinuousHover { phase in
                switch phase {
                case .active(let point): pointer = point; hover = options.indexAt(Float(point.x / cell), w: width)
                case .ended: hover = nil
                }
            }
            .overlay(alignment: .topLeading) {
                if let hover {
                    ChartTooltip(series: options.series, index: hover, label: labels.indices.contains(hover) ? labels[hover] : "\(hover + 1)")
                        .frame(maxWidth: min(180, geometry.size.width))
                        .offset(x: min(max(0, pointer.x + 12), max(0, geometry.size.width - 180)), y: 8)
                        .allowsHitTesting(false)
                }
            }
            .accessibilityLabel("\(chart.kind.rawValue.capitalized) chart")
            .accessibilityValue(options.series.filter { !$0.hidden }.map { "\($0.label): \($0.values.map(formatValue).joined(separator: ", "))" }.joined(separator: "; "))
        }
    }
}

private struct ChartTooltip: View {
    let series: [Series]
    let index: Int
    let label: String
    var body: some View {
        TerminalPanel {
            VStack(alignment: .leading, spacing: 4) {
                Text(label).textCase(.uppercase).foregroundStyle(FutureRetro.terminalDim)
                ForEach(Array(series.enumerated()), id: \.offset) { _, item in
                    if !item.hidden && item.values.indices.contains(index) {
                        HStack { DitherSwatch(item.tone); Text(item.label); Spacer(); Text(formatValue(item.values[index])).monospacedDigit() }
                    }
                }
            }
        }
    }
}

public struct DitherPie: View {
    public var pie: Pie
    public var cell: CGFloat
    public var showsTotal: Bool
    @Environment(\.accessibilityReduceMotion) private var systemReduceMotion
    @Environment(\.ditherAnimationsEnabled) private var animationsEnabled
    private var reduceMotion: Bool { systemReduceMotion || !animationsEnabled }
    @State private var hover: Int?
    @State private var appeared = false
    public init(_ pie: Pie, cell: CGFloat = 3, showsTotal: Bool = true) { self.pie = pie; self.cell = max(cell, 0.25); self.showsTotal = showsTotal }
    public init(slices: [DitherIR.Slice], innerRadius: Float = 0, cell: CGFloat = 3) { var pie = Pie(slices: slices); pie.innerRadius = innerRadius; self.init(pie, cell: cell) }
    public var body: some View {
        VStack(spacing: 10) {
            GeometryReader { geometry in
                let width = max(0, Int(geometry.size.width / cell)), height = max(0, Int(geometry.size.height / cell))
                AnimatedDitherSurface(value: (reduceMotion || appeared) ? Double(pie.progress) : 0, cell: cell) { w, h, progress in
                    var current = pie; current.progress = Float(progress); current.hover = hover ?? pie.hover
                    return current.encode(w: w, h: h)
                }
                .contentShape(Rectangle())
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let point): hover = pie.geometry(w: width, h: height).indexAt(Float(point.x / cell), y: Float(point.y / cell))
                    case .ended: hover = nil
                    }
                }
                .overlay {
                    if showsTotal && pie.innerRadius > 0.25 {
                        VStack(spacing: 2) { Text(hover.flatMap { pie.slices.indices.contains($0) ? pie.slices[$0].label : nil } ?? "Total").ditherLabel(); Text(formatValue(hover.flatMap { pie.slices.indices.contains($0) ? pie.slices[$0].value : nil } ?? pie.slices.reduce(0) { $0 + max($1.value, 0) })).font(.system(size: 17, weight: .semibold, design: .monospaced)).foregroundStyle(FutureRetro.marble) }
                            .allowsHitTesting(false)
                    }
                }
                .accessibilityLabel("Pie chart")
                .accessibilityValue(pie.slices.map { "\($0.label): \(formatValue($0.value))" }.joined(separator: "; "))
            }
            HStack(spacing: 12) {
                ForEach(Array(pie.slices.enumerated()), id: \.offset) { index, slice in
                    Button { hover = hover == index ? nil : index } label: { HStack(spacing: 4) { DitherSwatch(slice.tone); Text(slice.label) } }
                        .buttonStyle(.plain).font(.caption).foregroundStyle(FutureRetro.silver)
                        .accessibilityValue(formatValue(slice.value))
                        .onHover { hover = $0 ? index : nil }
                }
            }
        }
        .onAppear { withAnimation(reduceMotion ? nil : FutureRetro.reveal) { appeared = true } }
    }
}

public struct DitherRadar: View {
    public var radar: Radar
    public var labels: [String]
    public var cell: CGFloat
    @Environment(\.accessibilityReduceMotion) private var systemReduceMotion
    @Environment(\.ditherAnimationsEnabled) private var animationsEnabled
    private var reduceMotion: Bool { systemReduceMotion || !animationsEnabled }
    @State private var appeared = false
    @State private var selected: Int?
    @State private var visibility = SeriesVisibility()
    public init(_ radar: Radar, labels: [String] = [], cell: CGFloat = 3) { self.radar = radar; self.labels = labels; self.cell = max(cell, 0.25) }
    public init(series: [Series], labels: [String] = [], cell: CGFloat = 3) { self.init(Radar(series: series), labels: labels, cell: cell) }
    private var options: Radar {
        var current = radar; current.selected = selected ?? radar.selected
        current.series = visibility.applying(to: radar.series)
        return current
    }
    public var body: some View {
        VStack(spacing: 8) {
            GeometryReader { geometry in
                let width = max(0, Int(max(0, geometry.size.width - 60) / cell)), height = max(0, Int(max(0, geometry.size.height - 60) / cell))
                let geo = options.geometry(w: width, h: height)
                ZStack(alignment: .topLeading) {
                    AnimatedDitherSurface(value: (reduceMotion || appeared) ? Double(radar.progress) : 0, cell: cell) { w, h, progress in
                        var current = options; current.progress = Float(progress)
                        return current.encode(w: w, h: h)
                    }.padding(30)
                    ForEach(Array(labels.enumerated()), id: \.offset) { index, label in
                        if geo.axisTips.indices.contains(index) {
                            let tip = geo.axisTips[index]
                            let dx = tip.0 - geo.center.0, dy = tip.1 - geo.center.1
                            Text(label).font(.system(size: 10, design: .monospaced)).textCase(.uppercase).foregroundStyle(FutureRetro.pewter).position(x: 30 + CGFloat(tip.0) * cell + CGFloat(dx) / CGFloat(max(geo.radius, 1)) * 15, y: 30 + CGFloat(tip.1) * cell + CGFloat(dy) / CGFloat(max(geo.radius, 1)) * 15)
                        }
                    }
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Radar chart")
                .accessibilityValue(options.series.filter { !$0.hidden }.map { "\($0.label): \($0.values.map(formatValue).joined(separator: ", "))" }.joined(separator: "; "))
            }
            DitherLegend(series: Binding(get: { options.series }, set: { values in visibility.update(from: values) }), selected: $selected)
        }
        .onAppear { withAnimation(reduceMotion ? nil : FutureRetro.reveal) { appeared = true } }
    }
}

/// Preserve supplied visibility until the user overrides it, including revealing
/// a series supplied as hidden. Kept outside views so state transitions are testable.
struct SeriesVisibility {
    private var overrides: [Int: Bool] = [:]
    func applying(to source: [Series]) -> [Series] {
        source.enumerated().map { index, value in
            var item = value
            item.hidden = overrides[index] ?? value.hidden
            return item
        }
    }
    mutating func update(from values: [Series]) {
        overrides = Dictionary(uniqueKeysWithValues: values.enumerated().map { ($0.offset, $0.element.hidden) })
    }
}
