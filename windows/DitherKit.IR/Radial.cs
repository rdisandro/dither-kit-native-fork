namespace DitherKit.IR;
public sealed record PieGeometry((float X, float Y) Center, float Radius, float Inner, IReadOnlyList<(float Start, float End)> Angles)
{
    public int? IndexAt(float x, float y)
    {
        var dx = x - Center.X; var dy = y - Center.Y;
        var r = MathF.Sqrt(dx * dx + dy * dy);
        if (r < Inner || r > Radius + 2) return null;
        var a = Numeric.Mod(MathF.Atan2(dx, -dy), MathF.Tau);
        for (var i = 0; i < Angles.Count; i++)
            if (a >= Angles[i].Start && a < Angles[i].End) return i;
        return null;
    }
}
public sealed class Pie(IReadOnlyList<Slice> slices)
{
    public IReadOnlyList<Slice> Slices { get; set; } = slices;
    public float InnerRadius { get; set; }
    public float Progress { get; set; } = 1;
    public int? Hover { get; set; }
    public Bloom Bloom { get; set; }
    public PieGeometry Geometry(int w, int h)
    {
        static double Nonnegative(double value) => double.IsNaN(value) ? 0 : Math.Max(value, 0);
        var total = Slices.Sum(s => Nonnegative(s.Value));
        var sweep = MathF.Tau * Numeric.Clamp(Progress);
        var acc = 0f;
        var angles = Slices.Select(s =>
        {
            var a = total > 0 ? (float)(Nonnegative(s.Value) / total) * sweep : 0;
            var result = (acc, acc + a); acc += a; return result;
        }).ToArray();
        var radius = MathF.Max(Math.Min(w, h) / 2f - 2.5f, 1);
        return new((w / 2f, h / 2f), radius, radius * Math.Clamp(InnerRadius, 0, .95f), angles);
    }
    public DitherProgram Encode(int w, int h)
    {
        var geo = Geometry(w, h); var p = new DitherProgram();
        var table = Slices.SelectMany((s, i) => new[] { geo.Angles[i].Start, geo.Angles[i].End, s.Tone.Hue, s.Tone.Chroma, (float)s.Fill });
        var off = p.Table(table);
        p.Push(Op.Pie, geo.Center.X, geo.Center.Y, geo.Radius, geo.Inner, Slices.Count > 1 ? 1 : 0, Hover ?? -1, Slices.Count, off);
        Bloom.Emit(p, Slices.Any(s => s.Tone.IsRamp)); return p;
    }
}
public sealed record RadarGeometry((float X, float Y) Center, float Radius, IReadOnlyList<(float X, float Y)> AxisTips);
public sealed class Radar(IReadOnlyList<Series> series)
{
    public IReadOnlyList<Series> Series { get; set; } = series;
    public double? Max { get; set; }
    public float Progress { get; set; } = 1;
    public int? Selected { get; set; }
    public int Rings { get; set; } = 4;
    public Bloom Bloom { get; set; }
    public int Axes => Series.Select(s => s.Values.Count).DefaultIfEmpty().Max();
    public RadarGeometry Geometry(int w, int h)
    {
        var n = Math.Max(Axes, 1); var center = (X: w / 2f, Y: h / 2f);
        var radius = MathF.Max(Math.Min(w, h) / 2f - 2, 1);
        var tips = Enumerable.Range(0, n).Select(i =>
        {
            var a = -MathF.PI / 2f + MathF.Tau * i / n;
            return (center.X + MathF.Cos(a) * radius, center.Y + MathF.Sin(a) * radius);
        }).ToArray();
        return new(center, radius, tips);
    }
    public DitherProgram Encode(int w, int h)
    {
        var geo = Geometry(w, h); var p = new DitherProgram(); var n = Axes;
        if (n < 3) return p;
        var center = (Numeric.Round(geo.Center.X), Numeric.Round(geo.Center.Y));
        (int, int) At(int i, float f)
        {
            var (tx, ty) = geo.AxisTips[i % n];
            return (Numeric.Round(geo.Center.X + (tx - geo.Center.X) * f), Numeric.Round(geo.Center.Y + (ty - geo.Center.Y) * f));
        }
        var ramp = Series.Any(s => s.Tone.IsRamp);
        var (ringColor, ringDash, spokeDash) = ramp ? (Encoder.Rule(), 2u, 2u) : (Encoder.Guide(), 2u, 3u);
        for (var ring = 1; ring <= Math.Max(Rings, 1); ring++)
        {
            var f = (float)ring / Math.Max(Rings, 1);
            for (var i = 0; i < n; i++) p.Line(At(i, f), At(i + 1, f), ringColor, ringDash);
        }
        for (var i = 0; i < n; i++) p.Line(center, At(i, 1), ringColor, spokeDash);
        var max = Max ?? Scale.Nice(Series.SelectMany(s => s.Values).Prepend(0).Max(), Rings).Max;
        var prog = Numeric.Clamp(Progress);
        for (var si = 0; si < Series.Count; si++)
        {
            var s = Series[si]; if (s.Hidden) continue;
            var alpha = Selected is { } selected && selected != si ? .3f : 1f;
            var pts = Enumerable.Range(0, n).Select(i =>
            {
                var f = (float)Math.Clamp(Cartesian.ValueAt(s, i) / max, 0d, 1d) * prog;
                var (tx, ty) = geo.AxisTips[i];
                return (X: geo.Center.X + (tx - geo.Center.X) * f, Y: geo.Center.Y + (ty - geo.Center.Y) * f);
            }).ToArray();
            var off = p.Table(pts.SelectMany(pt => new[] { pt.X, pt.Y }));
            p.Push(Op.Poly, geo.Center.X, geo.Center.Y, geo.Radius, (float)s.Fill, s.Tone.Hue, s.Tone.Chroma, alpha, n, off);
            var stroke = s.Tone.Shade(.8f).Fade(alpha);
            for (var i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                p.Line((Numeric.Round(a.X), Numeric.Round(a.Y)), (Numeric.Round(b.X), Numeric.Round(b.Y)), stroke, 1);
            }
            foreach (var pt in pts) p.Dot(Numeric.Round(pt.X), Numeric.Round(pt.Y), stroke, s.Tone.Shade(.97f).Fade(alpha));
        }
        Bloom.Emit(p, ramp); return p;
    }
}
