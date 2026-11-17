using System.Globalization;
namespace DitherKit.IR;

/// <summary>An 8-bit sRGB color with straight alpha.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Transparent => new(0, 0, 0, 0);
    public static Rgba Black => new(0, 0, 0);
    public static Rgba White => new(255, 255, 255);
    public static Rgba Hex(uint hex) => new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
    public Rgba WithAlpha(byte alpha) => this with { A = alpha };
    public Rgba Fade(float factor) => WithAlpha((byte)Numeric.Round(A * Numeric.Clamp(factor)));
    public Rgba Mix(Rgba other, float t)
    {
        t = Numeric.Clamp(t);
        byte L(byte a, byte b) => (byte)Numeric.Round(a + (b - (float)a) * t);
        return new(L(R, other.R), L(G, other.G), L(B, other.B), L(A, other.A));
    }
    public Rgba Over(Rgba background) => background.WithAlpha(255).Mix(WithAlpha(255), A / 255f);
    public float Luminance()
    {
        static float Lin(byte channel)
        {
            var c = channel / 255f;
            return c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
        }
        return .2126f * Lin(R) + .7152f * Lin(G) + .0722f * Lin(B);
    }
    public static Rgba Oklch(float l, float c, float h)
    {
        // Rust max returns the non-NaN operand when only one operand is NaN.
        c = float.IsNaN(c) ? 0 : MathF.Max(c, 0);
        while (true)
        {
            if (OklchToSrgb(l, c, h) is { } rgb) return rgb;
            c -= .005f;
            if (c <= 0) return OklchToSrgb(l, 0, h) ?? Black;
        }
    }
    private static Rgba? OklchToSrgb(float l, float c, float h)
    {
        var radians = h * (MathF.PI / 180f);
        var a = c * MathF.Cos(radians); var b = c * MathF.Sin(radians);
        var ll = l + .39633778f * a + .21580376f * b;
        var mm = l - .105561346f * a - .06385417f * b;
        var ss = l - .08948418f * a - 1.2914855f * b;
        var l3 = ll * ll * ll; var m3 = mm * mm * mm; var s3 = ss * ss * ss;
        var r = 4.0767417f * l3 - 3.3077116f * m3 + .23096994f * s3;
        var g = -1.268438f * l3 + 2.6097574f * m3 - .34131938f * s3;
        var bb = -.0041960863f * l3 - .7034186f * m3 + 1.7076147f * s3;
        if (r < -1e-4f || r > 1.0001f || g < -1e-4f || g > 1.0001f || bb < -1e-4f || bb > 1.0001f) return null;
        static byte Enc(float v)
        {
            v = Numeric.Clamp(v);
            var s = v <= .0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - .055f;
            return (byte)Numeric.Round(s * 255f);
        }
        return new(Enc(r), Enc(g), Enc(bb));
    }
}

/// <summary>Future-Retro palette tokens (nearai/future-retro brand/tokens/tokens.css).</summary>
public static class Palette
{
    public static Rgba Void => Rgba.Hex(0x070708);
    public static Rgba Obsidian => Rgba.Hex(0x0F0F11);
    public static Rgba Basalt => Rgba.Hex(0x18181B);
    public static Rgba Slate => Rgba.Hex(0x26262B);
    public static Rgba Pewter => Rgba.Hex(0x808088);
    public static Rgba Silver => Rgba.Hex(0xC1C1C1);
    public static Rgba Marble => Rgba.Hex(0xFAFAF7);
    public static Rgba NearBlue => Rgba.Hex(0x0091FD);
    public static Rgba NearSky => Rgba.Hex(0x83DCFF);
    public static Rgba NearDeepBlue => Rgba.Hex(0x2385C7);
    public static Rgba NearBlueInk => Rgba.Hex(0x035CA3);
    /// <summary>The primary button's hover fill: Near AI blue with 12% white.</summary>
    public static Rgba NearBlueHover => Rgba.Hex(0x1F9EFD);
    /// <summary>Unlit dot-matrix dots: body ink at 9%.</summary>
    public static Rgba Faint => new(0xC1, 0xC1, 0xC1, 23);
    /// <summary>Chart rules and axes: body ink at 14%, below bloom's cutoff.</summary>
    public static Rgba Rule => new(0xC1, 0xC1, 0xC1, 36);
    public static IReadOnlyList<Rgba> NearRamp { get; } = [NearBlueInk, NearBlue, NearSky, Marble];
    public static IReadOnlyList<Rgba> InkRamp { get; } = [Slate, Pewter, Silver, Marble];
    public const int RampNear = 1, RampInk = 2;
    /// <summary>Lit step 1..4 of ramp <paramref name="id"/>; any other step is the unlit slate.</summary>
    public static Rgba RampColor(int id, int step) =>
        step is < 1 or > 4 ? Slate : (id == RampInk ? InkRamp : NearRamp)[step - 1];
    /// <summary>Continuous ramp position 1..4 for a shade level.</summary>
    public static float RampPosition(float level) => Math.Clamp(1f + (level - .3f) * 4f, 1f, 4f);
}

/// <summary>A named perceptual shade ramp, or any hue in degrees. Near and Ink are the
/// Future-Retro ramps: four fixed steps that shaders ordered-dither between.</summary>
public readonly record struct Tone
{
    private enum Kind { Near, Ink, Green, Blue, Purple, Pink, Orange, Red, Grey, Hue }
    private readonly Kind kind;
    private readonly float customHue;
    private Tone(Kind kind, float customHue = 0) { this.kind = kind; this.customHue = customHue; }
    /// <summary>Near AI blue: blue-ink, blue, sky, marble. The one pigment.</summary>
    public static Tone Near => new(Kind.Near);
    /// <summary>Neutral ink: slate, pewter, silver, marble.</summary>
    public static Tone Ink => new(Kind.Ink);
    public static Tone Green => new(Kind.Green);
    public static Tone Blue => new(Kind.Blue);
    public static Tone Purple => new(Kind.Purple);
    public static Tone Pink => new(Kind.Pink);
    public static Tone Orange => new(Kind.Orange);
    public static Tone Red => new(Kind.Red);
    public static Tone Grey => new(Kind.Grey);
    public static IReadOnlyList<Tone> All { get; } = [Green, Blue, Purple, Pink, Orange, Red, Grey];
    public static IReadOnlyList<Tone> Ramps { get; } = [Near, Ink];
    /// <summary>The Future-Retro ramp id, if this tone is a ramp.</summary>
    public int? Ramp => kind switch { Kind.Near => Palette.RampNear, Kind.Ink => Palette.RampInk, _ => null };
    public bool IsRamp => Ramp.HasValue;
    public static Tone FromHue(float hue) => new(Kind.Hue, hue);
    public string Name => kind.ToString().ToLowerInvariant();
    public float Hue => kind switch
    {
        Kind.Near or Kind.Ink => 0,
        Kind.Green => 150, Kind.Blue => 250, Kind.Purple => 300, Kind.Pink => 350,
        Kind.Orange => 55, Kind.Red => 25, Kind.Grey => 260, _ => Numeric.Mod(customHue, 360f)
    };
    /// <summary>OKLCH chroma, or <c>-id</c> for a Future-Retro ramp.</summary>
    public float Chroma => kind switch { Kind.Near => -Palette.RampNear, Kind.Ink => -Palette.RampInk, Kind.Grey => .012f, _ => .19f };
    public static Tone Parse(string value) => TryParse(value, out var tone) ? tone : throw new FormatException($"Unknown tone '{value}'.");
    public static bool TryParse(string value, out Tone tone)
    {
        var text = value.Trim().ToLowerInvariant();
        Tone? named = text switch { "near" or "near-blue" => Near, "ink" => Ink, "green" => Green, "blue" => Blue, "purple" or "violet" => Purple,
            "pink" => Pink, "orange" => Orange, "red" => Red, "grey" or "gray" => Grey, _ => null };
        if (named is { } n) { tone = n; return true; }
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
        { tone = FromHue(Numeric.Mod(h, 360)); return true; }
        tone = default; return false;
    }
    public Rgba Shade(float level) => ShadeHc(Hue, Chroma, level);
    public Rgba Base => Shade(.55f);
    public static Rgba ShadeHc(float hue, float chroma, float level)
    {
        if (chroma < 0) return Palette.RampColor((int)-chroma, (int)MathF.Floor(Palette.RampPosition(level) + .5f));
        level = Numeric.Clamp(level);
        var l = .22f + level * .72f;
        var c = chroma * Math.Clamp(1f - MathF.Abs(level - .55f) * 1.3f, .25f, 1f);
        return Rgba.Oklch(l, c, hue);
    }
}
