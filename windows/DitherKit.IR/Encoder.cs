using System.Globalization;
using System.Text;
using System.Text.Json;
namespace DitherKit.IR;

/// <summary>Simple surface encoders, geometry utilities and fixture-compatible component dispatch.</summary>
public static class Encoder
{
    public static IReadOnlyList<Series> DecodeSeries(IReadOnlyDictionary<string, object?> properties) => new Properties(properties).Series();
    public static IReadOnlyList<Slice> DecodeSlices(IReadOnlyDictionary<string, object?> properties) => new Properties(properties).Slices();

    public static DitherProgram Button(int w, int h, Tone tone, ButtonStyle style, float density, float time)
    {
        var p = new DitherProgram(); p.Push(Op.Button, w, h, (float)style, density, time, tone.Hue, tone.Chroma); return p;
    }
    public static DitherProgram Meter(int w, int h, float value, Tone tone, Fill fill)
    {
        var p = new DitherProgram(); p.Push(Op.Meter, w, h, value, (float)fill, tone.Hue, tone.Chroma); return p;
    }
    public static DitherProgram Swatch(Tone tone)
    {
        var p = new DitherProgram(); p.Push(Op.Swatch, tone.Hue, tone.Chroma); return p;
    }
    public static ulong Fnv1a(ReadOnlySpan<byte> bytes)
    {
        var h = 0xcbf29ce484222325UL;
        foreach (var b in bytes) h = unchecked((h ^ b) * 0x100000001b3UL);
        return h;
    }
    public static DitherProgram Avatar(string name, float? hue = null)
    {
        var h = Fnv1a(Encoding.UTF8.GetBytes(name));
        return AvatarProgram(name, Tone.FromHue(hue ?? (float)((h >> 33) % 180) * 2f).Hue);
    }
    /// <summary>An identicon in one tone; a Future-Retro ramp lights it in its pigment,
    /// accents it in its bright tint, and leaves the rest slate.</summary>
    public static DitherProgram Avatar(string name, Tone tone) =>
        tone.Ramp is { } id ? AvatarProgram(name, -id) : Avatar(name, tone.Hue);
    private static DitherProgram AvatarProgram(string name, float hueSlot)
    {
        var h = Fnv1a(Encoding.UTF8.GetBytes(name)); var bits = (uint)h;
        var mirror = ((h >> 32) & 1) == 0;
        var acc = (uint)(h >> 41) & 0x7fffff;
        var p = new DitherProgram();
        p.Push(Op.Avatar, bits & 0xffff, bits >> 16, mirror ? 1 : 0, hueSlot, acc & 0xffff, acc >> 16);
        return p;
    }
    public static string FormatValue(double value)
    {
        var a = Math.Abs(value);
        var (n, suffix) = a >= 1e9 ? (value / 1e9, "B") : a >= 1e6 ? (value / 1e6, "M") : a >= 1e3 ? (value / 1e3, "k") : (value, "");
        var rounded = Math.Round(n, MidpointRounding.AwayFromZero);
        if (Math.Abs(n - rounded) < .05)
        {
            // Rust's f64 -> i64 cast saturates and converts negative zero to integer zero.
            var integer = rounded >= long.MaxValue ? long.MaxValue : rounded <= long.MinValue ? long.MinValue : (long)rounded;
            return integer.ToString(CultureInfo.InvariantCulture) + suffix;
        }
        var text = double.IsPositiveInfinity(n) ? "inf" : double.IsNegativeInfinity(n) ? "-inf"
            : n.ToString("F1", CultureInfo.InvariantCulture);
        return text + suffix;
    }
    public static double Interpolate(IReadOnlyList<double> values, float f)
    {
        var n = values.Count;
        if (n == 0) return 0;
        if (n == 1) return values[0];
        f = Math.Clamp(f, 0, n - 1);
        var i = Math.Min((int)MathF.Floor(f), n - 2);
        var t = (double)(f - i);
        double At(int k) => values[Math.Clamp(k, 0, n - 1)];
        var p0 = At(i - 1); var p1 = At(i); var p2 = At(i + 1); var p3 = At(i + 2);
        var v = .5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
            + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
        return Math.Clamp(v, Math.Min(p1, p2), Math.Max(p1, p2));
    }
    public static Rgba Guide() => Tone.Grey.Shade(.55f).Fade(.45f);
    /// <summary>Future-Retro rules: faint ink hairlines.</summary>
    public static Rgba Rule() => Palette.Rule;
    public static DitherProgram Sparkline(IReadOnlyList<double> values, Tone tone, int w, int h, bool hovered = false, float progress = 1)
    {
        // Rust f64::min ignores NaN samples; LINQ Min propagates them.
        var lo = values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.PositiveInfinity).Min();
        var hi = values.Prepend(0).Max();
        return new Cartesian(CartesianKind.Area, [new Series("", tone, values)])
        { GridLines = false, Baseline = false, Progress = progress, Bloom = hovered ? Bloom.Low : Bloom.Off,
            YMax = Math.Max(hi * 1.1, lo + 1e-9) }.Encode(w, h);
    }

    /// <summary>Encode a component from ordinary .NET values or JSON values. Width/height use w/h.
    /// Supports gradient, background, button, meter, avatar, swatch, sparkline, cartesian,
    /// area, line, bar, pie and radar. Programs are always computed from component parameters.</summary>
    public static DitherProgram Encode(string component, IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var p = new Properties(properties);
        var w = p.Int("w", 64); var h = p.Int("h", 24);
        var t = p.Tone("tone", IR.Tone.Near);
        var bloom = p.Enum("bloom", Bloom.Off);
        var progress = p.Float("progress", 1);
        switch (component.ToLowerInvariant())
        {
            case "gradient":
                return new Gradient(p.Tone("from", t)) { To = p.OptionalTone("to"), Direction = p.Enum("direction", Direction.Up),
                    Matrix = p.Enum("matrix", Matrix.Bayer4), Density = p.Float("density", 1) }.Encode(w, h);
            case "background":
                var field = p.Enum("field", Field.Clouds);
                return new Background(field, t) { Accent = p.OptionalTone("accent"),
                    Matrix = p.Enum("matrix", Matrix.Bayer4), Density = p.Float("density", field.DefaultDensity()) }.Encode(w, h, p.Float("time", 0));
            case "button":
                var style = p.Enum("style", ButtonStyle.Solid);
                return Button(w, h, t, style, p.Float("density", style.Density(p.Bool("hovered"), p.Bool("pressed"))), p.Float("time", 0));
            case "meter": return Meter(w, h, p.Float("value", 0), t, p.Enum("fill", Fill.Gradient));
            case "avatar":
                return p.OptionalTone("tone") is { } avatarTone ? Avatar(p.String("text", ""), avatarTone)
                    : Avatar(p.String("text", ""), p.OptionalFloat("hue"));
            case "swatch": return Swatch(t);
            case "sparkline": return Sparkline(p.Doubles("values"), t, w, h, p.Bool("hovered"), progress);
            case "cartesian": case "area": case "line": case "bar":
                var kind = component.Equals("cartesian", StringComparison.OrdinalIgnoreCase) ? p.Enum("chart", CartesianKind.Area)
                    : System.Enum.Parse<CartesianKind>(component, true);
                return new Cartesian(kind, p.Series()) { Stacked = p.Bool("stacked"), Progress = progress, Hover = p.OptionalInt("hover"),
                    Selected = p.OptionalInt("selected"), Bloom = bloom, YMax = p.OptionalDouble("y_max"), GridLines = p.Bool("grid_lines", true),
                    TickCount = p.Int("tick_count", 4), Baseline = p.Bool("baseline", true) }.Encode(w, h);
            case "pie": return new Pie(p.Slices()) { InnerRadius = p.Float("inner", 0), Progress = progress, Hover = p.OptionalInt("hover"), Bloom = bloom }.Encode(w, h);
            case "radar": return new Radar(p.Series()) { Max = p.OptionalDouble("max"), Progress = progress, Selected = p.OptionalInt("selected"),
                Rings = p.Int("rings", 4), Bloom = bloom }.Encode(w, h);
            default: throw new ArgumentException($"Unknown component '{component}'.", nameof(component));
        }
    }

    // JSON conversion is only an input adapter. It never reads expected bytecode or fixtures.
    private sealed class Properties(IReadOnlyDictionary<string, object?> values)
    {
        private object? Get(string name)
        {
            if (!values.TryGetValue(name, out var value)) return null;
            if (value is JsonElement j) return j.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : j;
            return value;
        }
        public string String(string name, string fallback) => Get(name) switch
        { null => fallback, JsonElement j => j.ValueKind == JsonValueKind.String ? j.GetString()! : j.ToString(), var v => Convert.ToString(v, CultureInfo.InvariantCulture)! };
        public double? OptionalDouble(string name) => Get(name) switch
        { null => null, JsonElement j => j.GetDouble(), var v => Convert.ToDouble(v, CultureInfo.InvariantCulture) };
        public float? OptionalFloat(string name) => (float?)OptionalDouble(name);
        public int? OptionalInt(string name) => (int?)OptionalDouble(name);
        public float Float(string name, float fallback) => OptionalFloat(name) ?? fallback;
        public int Int(string name, int fallback) => OptionalInt(name) ?? fallback;
        public bool Bool(string name, bool fallback = false) => Get(name) switch
        { null => fallback, JsonElement j => j.GetBoolean(), var v => Convert.ToBoolean(v, CultureInfo.InvariantCulture) };
        public T Enum<T>(string name, T fallback) where T : struct, System.Enum => Get(name) is null ? fallback : System.Enum.Parse<T>(String(name, ""), true);
        public Tone? OptionalTone(string name) => Get(name) switch
        { null => null, IR.Tone tone => tone, JsonElement j when j.ValueKind == JsonValueKind.Number => IR.Tone.FromHue(j.GetSingle()),
            string s => IR.Tone.Parse(s), JsonElement j => IR.Tone.Parse(j.GetString()!), var v => IR.Tone.FromHue(Convert.ToSingle(v, CultureInfo.InvariantCulture)) };
        public Tone Tone(string name, Tone fallback) => OptionalTone(name) ?? fallback;
        public double[] Doubles(string name) => Get(name) switch
        { null => [], JsonElement j => j.EnumerateArray().Select(v => v.GetDouble()).ToArray(), IEnumerable<double> xs => xs.ToArray(),
            System.Collections.IEnumerable xs => xs.Cast<object>().Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToArray(),
            _ => throw new ArgumentException($"'{name}' must be an array.") };
        private IEnumerable<Properties> Objects(string name)
        {
            var value = Get(name);
            if (value is null) return [];
            var json = value is JsonElement j ? j : JsonSerializer.SerializeToElement(value);
            return json.EnumerateArray().Select(v => new Properties(v.EnumerateObject().ToDictionary(k => k.Name, k => (object?)k.Value.Clone()))).ToArray();
        }
        public IReadOnlyList<Series> Series() => Get("series") is IReadOnlyList<IR.Series> typed ? typed
            : Objects("series").Select(p => new IR.Series(p.String("label", ""), p.Tone("tone", IR.Tone.Near), p.Doubles("values"))
                { Fill = p.Enum("fill", Fill.Gradient), Hidden = p.Bool("hidden") }).ToArray();
        public IReadOnlyList<Slice> Slices() => Get("slices") is IReadOnlyList<Slice> typed ? typed
            : Objects("slices").Select(p => new Slice(p.String("label", ""), p.OptionalDouble("value") ?? 0, p.Tone("tone", IR.Tone.Near))
                { Fill = p.Enum("fill", Fill.Gradient) }).ToArray();
    }
}
