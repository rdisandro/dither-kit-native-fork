namespace DitherKit.IR;

/// <summary>Stable display-list opcodes shared with the Rust, GLSL and Metal ports.</summary>
public static class Op
{
    public const uint Line = 1, Dot = 2, AreaBody = 3, AreaStroke = 4, Bar = 5, Pie = 6,
        Poly = 7, Gradient = 8, Background = 9, Button = 10, Meter = 11, Avatar = 12,
        Swatch = 13, Bloom = 14;
}

/// <summary>Flat f32 display list: [op-float-count, opcode, op-length, args..., tables...].
/// Table offsets are relative to the table base, immediately after the op list.</summary>
public sealed class DitherProgram
{
    public const float None = -1e9f;
    /// <summary>First bloom operation or operation-list end; null for malformed lists.</summary>
    public static int? FindBloomIndex(ReadOnlySpan<float> data)
    {
        if (data.IsEmpty || !float.IsFinite(data[0]) || data[0] < 0 ||
            data[0] != MathF.Truncate(data[0]) || (double)data[0] >= data.Length) return null;
        int end = 1 + (int)data[0], index = 1, visited = 0, split = end;
        while (index < end)
        {
            if (index + 1 >= end) return null;
            float length = data[index + 1];
            if (!float.IsFinite(length) || length < 2 || length != MathF.Truncate(length) ||
                (double)length > end - index) return null;
            if (split == end && visited < 65_536 && data[index] >= 14 && data[index] < 15) split = index;
            index += (int)length;
            visited++;
        }
        return split;
    }
    private readonly List<float> ops = [];
    private readonly List<float> tables = [];
    public bool IsEmpty => ops.Count == 0;
    public void Push(uint opcode, params float[] args)
    {
        ops.Add(opcode); ops.Add(args.Length + 2); ops.AddRange(args);
    }
    public float Table(IEnumerable<float> values)
    {
        var at = tables.Count; tables.AddRange(values); return at;
    }
    public float[] ToArray() => [(float)ops.Count, .. ops, .. tables];
    public void Line((int X, int Y) a, (int X, int Y) b, Rgba color, uint dash) =>
        Push(Op.Line, a.X, a.Y, b.X, b.Y, dash, color.R, color.G, color.B, color.A);
    public void Dot(int x, int y, Rgba ring, Rgba center) =>
        Push(Op.Dot, x, y, ring.R, ring.G, ring.B, ring.A, center.R, center.G, center.B, center.A);
    public void Bloom(int radius, float strength) => Push(Op.Bloom, radius, strength);
    /// <summary>Optional third BLOOM argument selecting the Future-Retro blue-ink glow.</summary>
    public const float GlowRamp = 1;
    /// <summary>A Future-Retro bloom: opaque blue-ink glow, thinned by ordered dither.</summary>
    public void BloomRamp(int radius, float strength) => Push(Op.Bloom, radius, strength, GlowRamp);
}

internal static class Numeric
{
    internal static float Clamp(float value) => Math.Clamp(value, 0f, 1f);
    internal static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
    internal static float Mod(float value, float divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
