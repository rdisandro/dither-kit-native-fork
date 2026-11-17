import SwiftUI
import DitherKit
#if os(macOS)
import AppKit
#endif

@main struct DitherGallery: App {
    init() {
        #if os(macOS)
        if let index = CommandLine.arguments.firstIndex(of: "--snapshot"), CommandLine.arguments.indices.contains(index + 1) {
            let renderer = ImageRenderer(content: GalleryView().environment(\.ditherAnimationsEnabled, false).frame(width: 1100).environment(\.colorScheme, .dark))
            renderer.scale = 1
            guard let image = renderer.cgImage else { fatalError("Could not render gallery") }
            let bitmap = NSBitmapImageRep(cgImage: image)
            do { try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[index + 1])) }
            catch { fatalError("Could not write gallery: \(error)") }
            exit(0)
        }
        #endif
    }
    var body: some Scene {
        WindowGroup {
            ScrollView { GalleryView().frame(maxWidth: 1200) }.background(FutureRetro.void).preferredColorScheme(.dark)
                .frame(minWidth: 760, minHeight: 700)
        }
        .defaultSize(width: 1100, height: 1000)
    }
}

struct GalleryView: View {
    @State private var meter: Float = 0.67
    private let series = [
        Series(label: "Inference", tone: .near, values: [12, 18, 13, 29, 24, 36, 30, 44, 41]),
        Series(label: "Training", tone: .ink, values: [8, 11, 10, 17, 14, 22, 18, 27, 25], fill: .hatched)
    ]
    private let days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun", "Mon", "Tue"]
    private let slices = [
        DitherIR.Slice(label: "Compute", value: 56, tone: .near),
        DitherIR.Slice(label: "Storage", value: 28, tone: .ink, fill: .dotted),
        DitherIR.Slice(label: "Network", value: 16, tone: .near, fill: .matrix)
    ]
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            hero
            HStack(spacing: 18) {
                panel("Area · hover for values") { DitherChart(kind: .area, series: series, labels: days).frame(height: 190) }
                panel("Line · toggle the legend") { DitherChart(kind: .line, series: series, labels: days).frame(height: 190) }
            }
            HStack(spacing: 18) {
                panel("Dot matrix · traces per day") { DitherChart(dotMatrix, labels: days).frame(height: 175) }
                panel("Stacked dot matrix") { DitherChart(stackedChart).frame(height: 175) }
            }
            HStack(alignment: .top, spacing: 18) {
                panel("Allocation · donut") { DitherPie(slices: slices, innerRadius: 0.55).frame(height: 225) }
                panel("Radar · axis geometry") { DitherRadar(series: [Series(label: "Current", tone: .near, values: [8, 6, 9, 5, 7]), Series(label: "Target", tone: .ink, values: [9, 8, 7, 9, 8], fill: .dotted)], labels: ["Speed", "Quality", "Cost", "Scale", "Safety"]).frame(height: 225) }
            }
            HStack(spacing: 18) {
                panel("Identity · micro charts") {
                    HStack(spacing: 18) {
                        DitherAvatar("DitherKit", size: 64)
                        DitherAvatar("Metal", tone: .ink, size: 64)
                        DitherSparkline([5, 8, 6, 13, 9, 17, 15, 23]).frame(height: 64)
                    }
                }
                panel("Native controls") {
                    DitherMeter(value: meter, label: "GPU utilization")
                    HStack(spacing: 14) {
                        Button("Refresh") { meter = meter > 0.8 ? 0.35 : 0.92 }.buttonStyle(DitherButton())
                        Button("Secondary") { meter = 0.52 }.buttonStyle(DitherButton(style: .outline))
                        Button("Reset") { meter = 0.67 }.buttonStyle(DitherButton(style: .ghost))
                    }
                }
            }
            HStack(spacing: 18) {
                panel("Gradient · blue to ink") { DitherGradient(from: .near, to: .ink, direction: .right).frame(height: 96) }
                panel("Fields · marble, rings, caustics") {
                    // Card-sized art takes the 2-point grid.
                    HStack(spacing: 10) {
                        DitherBackground(Background(field: .marble, tone: .near), energy: .card, cell: 2)
                        DitherBackground(Background(field: .rings, tone: .near), energy: .card, cell: 2)
                        DitherBackground(Background(field: .caustics, tone: .near), energy: .card, cell: 2)
                    }
                    .frame(height: 96)
                }
            }
            Text("Near AI blue on void · 3-point cells · 24 fps · Reduce Motion holds a still frame")
                .ditherLabel()
        }
        .padding(28).background(FutureRetro.void)
    }
    private var dotMatrix: Cartesian {
        Cartesian(kind: .bar, series: [Series(label: "Traces", tone: .near, values: [12, 18, 13, 29, 24, 36, 30, 44, 41], fill: .matrix)])
    }
    private var stackedChart: Cartesian {
        var chart = Cartesian(kind: .bar, series: series.map { var s = $0; s.fill = .matrix; return s })
        chart.stacked = true
        return chart
    }
    private var hero: some View {
        ZStack(alignment: .bottomLeading) {
            DitherBackground(Background(field: .clouds, tone: .near))
                .mask(RadialGradient(colors: [.black, .black.opacity(0.45), .clear], center: UnitPoint(x: 0.92, y: 0.1), startRadius: 40, endRadius: 760))
            VStack(alignment: .leading, spacing: 8) {
                Text("DitherKit").ditherLabel()
                Text("Rendered by machine,\nin one blue").font(.system(size: 40, weight: .semibold)).tracking(-1.2).foregroundStyle(FutureRetro.marble)
                Text("Shader pixels on a 3-point grid. Native controls around them.").foregroundStyle(FutureRetro.silver)
            }
            .shadow(color: .black, radius: 2).shadow(color: .black, radius: 10).shadow(color: .black.opacity(0.9), radius: 24)
            .padding(28)
        }
        .frame(height: 230)
        .frame(maxWidth: .infinity)
        .clipShape(.rect(cornerRadius: FutureRetro.paneRadius, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: FutureRetro.paneRadius, style: .continuous).strokeBorder(FutureRetro.slate, lineWidth: 1))
    }
    private func panel<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title).ditherLabel()
            content()
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .ditherCard()
    }
}
