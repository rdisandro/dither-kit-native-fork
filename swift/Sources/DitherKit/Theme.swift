import SwiftUI
import DitherIR

/// Future-Retro chrome for native surfaces: void ground, obsidian cards with a
/// slate hairline, continuous corners, Near AI blue as the one pigment.
/// App surfaces keep the platform's system faces (Near AI OS Design System
/// NDS-4); labels and values use its monospaced design.
public enum FutureRetro {
    public static let void = Palette.void.swiftUIColor
    public static let obsidian = Palette.obsidian.swiftUIColor
    public static let basalt = Palette.basalt.swiftUIColor
    public static let slate = Palette.slate.swiftUIColor
    public static let pewter = Palette.pewter.swiftUIColor
    public static let silver = Palette.silver.swiftUIColor
    public static let marble = Palette.marble.swiftUIColor
    public static let nearBlue = Palette.nearBlue.swiftUIColor
    public static let nearSky = Palette.nearSky.swiftUIColor
    /// macOS Terminal greys, for tooltips.
    public static let terminal = Color(red: 0x1E / 255, green: 0x1E / 255, blue: 0x1F / 255)
    public static let terminalText = Color(red: 0xE4 / 255, green: 0xE4 / 255, blue: 0xE6 / 255)
    public static let terminalDim = Color(red: 0x8E / 255, green: 0x8E / 255, blue: 0x93 / 255)

    /// The app's continuous corner radii.
    public static let tileRadius: CGFloat = 6
    public static let controlRadius: CGFloat = 8
    public static let cardRadius: CGFloat = 14
    public static let paneRadius: CGFloat = 16

    /// Reveals: 600 ms, eased out (cubic), like the dot chart's column rise.
    public static let reveal = Animation.timingCurve(0.33, 1, 0.68, 1, duration: Motion.reveal)
    /// Hover and press responses: 160 ms (`--cc-ease-out`).
    public static let quick = Animation.timingCurve(0.16, 1, 0.3, 1, duration: Motion.quick)
}

public extension View {
    /// A mono uppercase label (navigation, panel titles, data labels).
    func ditherLabel() -> some View {
        font(.system(size: 11, weight: .medium, design: .monospaced))
            .textCase(.uppercase)
            .tracking(1.1)
            .foregroundStyle(FutureRetro.pewter)
    }

    /// An obsidian card with a slate hairline and continuous corners.
    func ditherCard(padding: CGFloat = 16) -> some View {
        self.padding(padding)
            .background(FutureRetro.obsidian, in: .rect(cornerRadius: FutureRetro.cardRadius, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: FutureRetro.cardRadius, style: .continuous).strokeBorder(FutureRetro.slate, lineWidth: 1))
    }
}

/// A Terminal-window tooltip body.
struct TerminalPanel<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        content
            .font(.system(size: 11.5, design: .monospaced))
            .foregroundStyle(FutureRetro.terminalText)
            .padding(.horizontal, 10).padding(.vertical, 8)
            .background(FutureRetro.terminal, in: .rect(cornerRadius: FutureRetro.controlRadius, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: FutureRetro.controlRadius, style: .continuous).strokeBorder(.white.opacity(0.11), lineWidth: 1))
            .shadow(color: .black.opacity(0.75), radius: 12, y: 10)
    }
}

/// Integrates a field's phase at its energy, so hover quickens the field
/// without a jump. Reference type: the timeline advances it between frames.
@MainActor final class FieldClock {
    private var phase = Motion.stillPhase
    private var energy: Float?
    private var last: Date?
    func advance(to date: Date, hovered: Bool, energy rates: Energy, speed: Float) -> Float {
        let dt = last.map { Float(min(max(date.timeIntervalSince($0), 0), 0.12)) } ?? 0
        last = date
        let next = rates.step(phase: phase, energy: energy ?? rates.idle, hovered: hovered, dt: dt * speed)
        phase = next.phase
        energy = next.energy
        return phase
    }
}
