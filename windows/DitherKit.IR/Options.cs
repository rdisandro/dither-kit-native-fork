namespace DitherKit.IR;
public enum Matrix { Bayer2, Bayer4, Bayer8, Halftone, Noise }
/// <summary>Matrix is the Future-Retro dot matrix: one dot per two cells, lit from the axis.</summary>
public enum Fill { Gradient, Dotted, Hatched, Solid, Matrix }
public enum Bloom { Off, Low, High, Aura }
public enum Direction { Up, Down, Left, Right, Radial }
/// <summary>dither-core's five fields, then Future-Retro's (Clouds is the house field).</summary>
public enum Field { Plasma, Waves, Ripple, Noise, Aurora, Clouds, Marble, Rings, Caustics }
public enum ButtonStyle { Solid, Outline, Ghost }
public enum CartesianKind { Area, Line, Bar }

public static class OptionExtensions
{
    public static (int Radius, float Strength) Parameters(this Bloom bloom) => bloom switch
    { Bloom.Low => (1, .45f), Bloom.High => (2, .6f), Bloom.Aura => (4, .7f), _ => (0, 0) };
    internal static void Emit(this Bloom bloom, DitherProgram program, bool ramp)
    {
        if (bloom == Bloom.Off) return;
        var (radius, strength) = bloom.Parameters();
        if (ramp) program.BloomRamp(radius, strength); else program.Bloom(radius, strength);
    }
    public static bool IsLight(this Field field) => field >= Field.Clouds;
    public static float DefaultDensity(this Field field) => field.IsLight() ? 1f : .6f;
    public static float Density(this ButtonStyle style, bool hovered, bool pressed)
    {
        var (rest, hover, press) = style switch
        { ButtonStyle.Solid => (.32f, .55f, .85f), ButtonStyle.Outline => (0f, .3f, .6f), _ => (0f, .25f, .5f) };
        return pressed ? press : hovered ? hover : rest;
    }
    public static Rgba? Surface(this ButtonStyle style, Tone tone) =>
        style == ButtonStyle.Solid ? (tone.IsRamp ? tone.Base : tone.Shade(.22f)) : null;
    /// <summary>Native chrome for a Future-Retro button: flat fills, no texture.</summary>
    public static ButtonChrome Chrome(this ButtonStyle style, Tone tone, bool hovered, bool pressed)
    {
        var hot = hovered || pressed; var baseColor = tone.Base;
        return style switch
        {
            ButtonStyle.Solid => new(hot ? baseColor.Mix(Rgba.White, .12f) : baseColor, null, Palette.Void, baseColor.Fade(hot ? .8f : .65f), null),
            ButtonStyle.Outline => new(hot ? Palette.Marble : null, Palette.Marble, hot ? Palette.Void : Palette.Marble, null, null),
            _ => new(null, null, hot ? baseColor : Palette.Marble, null, baseColor),
        };
    }
}
/// <summary>Colors for a Future-Retro button drawn with native controls.</summary>
public readonly record struct ButtonChrome(Rgba? Fill, Rgba? Border, Rgba Label, Rgba? Glow, Rgba? Underline)
{
    /// <summary>Glow (y offset, blur radius) in points, after the app's tab bar.</summary>
    public static (float Y, float Radius) GlowGeometry(bool hovered) => hovered ? (18, 36) : (14, 30);
}
/// <summary>Future-Retro field energy: phase advances energy x 4 per second, easing between idle and hover over ~200 ms.</summary>
public readonly record struct Energy(float Idle, float Hover)
{
    public static Energy Field => new(.18f, .32f);
    public static Energy Card => new(.12f, .28f);
    public (float Phase, float Energy) Step(float phase, float energy, bool hovered, float dt)
    {
        var target = hovered ? Hover : Idle;
        energy += (target - energy) * MathF.Min(dt / .2f, 1f);
        return (phase + dt * energy * 4f, energy);
    }
}
/// <summary>Future-Retro motion: film-rate canvases, a still frame, quick and reveal durations.</summary>
public static class Motion
{
    public const double FilmFps = 24;
    public const float StillPhase = 3;
    public static readonly TimeSpan Reveal = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(160);
    public static float EaseOutCubic(float t) { t = Numeric.Clamp(t); var k = 1 - t; return 1 - k * k * k; }
}
public sealed record Gradient(Tone From)
{
    public Tone? To { get; set; }
    public Direction Direction { get; set; } = Direction.Up;
    public Matrix Matrix { get; set; } = Matrix.Bayer4;
    public float Density { get; set; } = 1;
    public DitherProgram Encode(int w, int h)
    {
        var p = new DitherProgram();
        p.Push(Op.Gradient, w, h, (float)Direction, (float)Matrix, Numeric.Clamp(Density), From.Hue, From.Chroma,
            To.HasValue ? 1 : 0, To?.Hue ?? 0, To?.Chroma ?? 0);
        return p;
    }
}
public sealed record Background(Field Field, Tone Tone)
{
    public Tone? Accent { get; set; }
    public Matrix Matrix { get; set; } = Matrix.Bayer4;
    public float Density { get; set; } = OptionExtensions.DefaultDensity(Field);
    public DitherProgram Encode(int w, int h, float time)
    {
        var p = new DitherProgram();
        p.Push(Op.Background, w, h, (float)Field, (float)Matrix, Numeric.Clamp(Density), time, Tone.Hue, Tone.Chroma,
            Accent.HasValue ? 1 : 0, Accent?.Hue ?? 0, Accent?.Chroma ?? 0);
        return p;
    }
}
public sealed record Series(string Label, Tone Tone, IReadOnlyList<double> Values)
{
    public Fill Fill { get; set; } = Fill.Gradient;
    public bool Hidden { get; set; }
}
public sealed record Slice(string Label, double Value, Tone Tone)
{
    public Fill Fill { get; set; } = Fill.Gradient;
}
public sealed record Scale(double Max, IReadOnlyList<double> Ticks)
{
    public static Scale Nice(double max, int count)
    {
        max = double.IsFinite(max) && max > 0 ? max : 1;
        var raw = max / Math.Max(count, 1);
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1d, 2d, 2.5, 5d, 10d }.Select(m => m * mag).FirstOrDefault(s => s >= raw, 10d * mag);
        var top = Math.Ceiling(max / step) * step;
        return new(top, Enumerable.Range(0, (int)Math.Round(top / step, MidpointRounding.AwayFromZero) + 1).Select(i => i * step).ToArray());
    }
    public float Y(double value, int h)
    {
        var span = (float)Math.Max(h - 1, 0);
        return span - (float)Math.Clamp(value / Max, 0d, 1d) * span;
    }
}
