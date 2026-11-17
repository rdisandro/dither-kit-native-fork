namespace DitherKit.IR;

/// <summary>CPU geometry and display-list encoding for area, line and bar charts.</summary>
public sealed class Cartesian(CartesianKind kind, IReadOnlyList<Series> series)
{
    public CartesianKind Kind { get; set; } = kind;
    public IReadOnlyList<Series> Series { get; set; } = series;
    public bool Stacked { get; set; }
    public float Progress { get; set; } = 1;
    public int? Hover { get; set; }
    public int? Selected { get; set; }
    public Bloom Bloom { get; set; }
    public double? YMax { get; set; }
    public bool GridLines { get; set; } = true;
    public int TickCount { get; set; } = 4;
    public bool Baseline { get; set; } = true;
    public int Length => Series.Select(s => s.Values.Count).DefaultIfEmpty().Max();
    public bool IsEmpty => Length == 0;
    public Scale Scale()
    {
        var visible = Series.Where(s => !s.Hidden).ToArray();
        var max = YMax ?? (Stacked
            ? Enumerable.Range(0, Length).Select(i => visible.Sum(s => ValueAt(s, i))).DefaultIfEmpty().Max()
            : visible.SelectMany(s => s.Values).Prepend(0).Max());
        return IR.Scale.Nice(max, TickCount);
    }
    internal static double ValueAt(Series s, int i) => i >= 0 && i < s.Values.Count ? s.Values[i] : 0;
    public float ColumnX(int i, int w)
    {
        var n = Math.Max(Length, 1);
        return Kind == CartesianKind.Bar ? (i + .5f) * w / n : n == 1 ? w / 2f : (float)i * Math.Max(w - 1, 0) / (n - 1);
    }
    public int? IndexAt(float x, int w)
    {
        var n = Length;
        if (n == 0 || w == 0) return null;
        var i = Kind == CartesianKind.Bar ? (int)MathF.Floor(x / w * n) : Numeric.Round(x / (Math.Max(w, 2) - 1) * (n - 1));
        return Math.Clamp(i, 0, n - 1);
    }
    /// <summary>Future-Retro chrome when any series uses a ramp tone.</summary>
    public bool IsRamp => Series.Any(s => s.Tone.IsRamp);
    private float AlphaFor(int si) => Selected is { } selected && selected != si ? .3f : 1f;
    private double StackBelow(int si, int i) => Series.Take(si).Where(s => !s.Hidden).Sum(s => ValueAt(s, i));
    public DitherProgram Encode(int w, int h)
    {
        var p = new DitherProgram();
        if (w < 2 || h < 2) return p;
        var scale = Scale(); var ramp = IsRamp;
        var (grid, gridDash, baseDash) = ramp ? (Encoder.Rule(), 2u, 1u) : (Encoder.Guide(), 3u, 2u);
        if (GridLines)
            foreach (var tick in scale.Ticks.Skip(1))
            {
                var y = Numeric.Round(scale.Y(tick, h)); p.Line((0, y), (w - 1, y), grid, gridDash);
            }
        if (Baseline) p.Line((0, h - 1), (w - 1, h - 1), grid, baseDash);
        if (Kind == CartesianKind.Bar) EncodeBars(p, scale, w, h); else EncodeCurves(p, scale, w, h);
        if (Kind != CartesianKind.Bar && Hover is { } hover && hover >= 0 && hover < Length)
        {
            if (Bloom == Bloom.Off) p.Bloom(0, 0); else Bloom.Emit(p, ramp);
            EncodeHover(p, scale, w, h);
        }
        else Bloom.Emit(p, ramp);
        return p;
    }
    private void EncodeCurves(DitherProgram p, Scale scale, int w, int h)
    {
        var n = Length;
        var reveal = Numeric.Clamp(Progress) * w;
        var grow = .6d + .4d * Numeric.Clamp(Progress);
        var bases = new double[w];
        var cols = Series.Select(_ => Enumerable.Repeat(DitherProgram.None, w * 2).ToArray()).ToArray();
        for (var x = 0; x < w; x++)
        {
            if (x > reveal) break;
            var f = n > 1 ? (float)x / (w - 1) * (n - 1) : 0;
            for (var si = 0; si < Series.Count; si++)
            {
                var s = Series[si]; if (s.Hidden) continue;
                var v = Encoder.Interpolate(s.Values, f) * grow;
                var b = Stacked ? bases[x] : 0;
                cols[si][x * 2] = scale.Y(b + v, h);
                cols[si][x * 2 + 1] = scale.Y(b, h);
                if (Stacked) bases[x] += v;
            }
        }
        var strength = Kind == CartesianKind.Area ? 1f : .28f;
        var offsets = Series.Select((s, si) => s.Hidden ? (float?)null : p.Table(cols[si])).ToArray();
        for (var si = 0; si < Series.Count; si++)
        {
            if (offsets[si] is not { } off) continue;
            var s = Series[si];
            p.Push(Op.AreaBody, off, w, h, (float)s.Fill, s.Tone.Hue, s.Tone.Chroma, strength, AlphaFor(si));
        }
        for (var si = 0; si < Series.Count; si++)
        {
            if (offsets[si] is not { } off) continue;
            var color = Series[si].Tone.Shade(.78f).Fade(AlphaFor(si));
            p.Push(Op.AreaStroke, off, w, color.R, color.G, color.B, color.A);
        }
        if (Kind != CartesianKind.Line || w / Math.Max(n, 1) < 6) return;
        for (var si = 0; si < Series.Count; si++)
        {
            var s = Series[si]; if (s.Hidden) continue;
            for (var i = 0; i < s.Values.Count; i++)
            {
                var x = ColumnX(i, w); if (x > reveal) break;
                var b = Stacked ? StackBelow(si, i) : 0;
                var y = Numeric.Round(scale.Y((b + s.Values[i]) * grow, h));
                var c = s.Tone.Shade(.78f).Fade(AlphaFor(si));
                p.Dot(Numeric.Round(x), y, c, c);
            }
        }
    }
    private void EncodeHover(DitherProgram p, Scale scale, int w, int h)
    {
        if (Hover is not { } i || i < 0 || i >= Length) return;
        var x = Numeric.Round(ColumnX(i, w)); var ramp = IsRamp;
        p.Line((x, 0), (x, h - 1), ramp ? Palette.NearBlue : Tone.Grey.Shade(.75f).Fade(.7f), 2);
        for (var si = 0; si < Series.Count; si++)
        {
            var s = Series[si]; if (s.Hidden || i >= s.Values.Count) continue;
            var b = Stacked ? StackBelow(si, i) : 0;
            var y = Numeric.Round(scale.Y(b + s.Values[i], h));
            if (ramp) p.Dot(x, y, s.Tone.Base, Palette.Marble);
            else p.Dot(x, y, s.Tone.Shade(.7f), s.Tone.Shade(.98f));
        }
    }
    private void EncodeBars(DitherProgram p, Scale scale, int w, int h)
    {
        var n = Length; if (n == 0) return;
        var visible = Enumerable.Range(0, Series.Count).Where(i => !Series[i].Hidden).ToArray();
        var k = Stacked ? 1 : Math.Max(visible.Length, 1);
        var band = (float)w / n;
        var group = MathF.Max(band * .72f, 1);
        var gap = group / k > 3 ? 1f : 0;
        var barW = MathF.Max((group - gap * (k - 1f)) / k, 1);
        var prog = (double)Numeric.Clamp(Progress);
        for (var i = 0; i < n; i++)
        {
            var x0 = i * band + (band - group) / 2f;
            var bandAlpha = Hover is { } hv && hv != i ? .45f : 1f;
            var b = 0d;
            for (var slot = 0; slot < visible.Length; slot++)
            {
                var si = visible[slot]; var s = Series[si];
                var v = ValueAt(s, i) * prog;
                var left = Stacked ? x0 : x0 + slot * (barW + gap);
                var bx0 = Numeric.Round(left); var bx1 = Numeric.Round(left + barW) - 1;
                p.Push(Op.Bar, bx0, bx1, scale.Y(b + v, h), (int)scale.Y(b, h), h, (float)s.Fill,
                    s.Tone.Hue, s.Tone.Chroma, AlphaFor(si) * bandAlpha);
                if (Stacked) b += v;
            }
        }
    }
}
