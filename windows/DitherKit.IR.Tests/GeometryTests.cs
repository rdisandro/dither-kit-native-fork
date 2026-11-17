using System.Text;
using DitherKit.IR;
using Xunit;
namespace DitherKit.IR.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void DefaultToneIsRustNear()
    {
        Assert.Equal(Tone.Near, default(Tone));
        Assert.NotEqual(Tone.Green, Tone.FromHue(150));
        Assert.Equal(-1, default(Tone).Chroma);
        Assert.Equal(Palette.NearBlue, default(Tone).Base);
    }

    [Fact]
    public void RampTonesShadeToTheirNearestStep()
    {
        Assert.Equal(Palette.NearSky, Tone.Near.Shade(.78f));
        Assert.Equal(Palette.Marble, Tone.Near.Shade(1));
        Assert.Equal(Palette.Pewter, Tone.Ink.Base);
        Assert.Equal(Tone.Ink, Tone.Parse(" Ink "));
        Assert.Equal(Palette.NearBlueHover, Palette.NearBlue.Mix(Rgba.White, .12f));
        var chrome = ButtonStyle.Solid.Chrome(Tone.Near, hovered: true, pressed: false);
        Assert.Equal(Palette.NearBlueHover, chrome.Fill);
        Assert.Equal(Palette.Void, chrome.Label);
        var energy = Energy.Field.Step(0, Energy.Field.Idle, hovered: true, dt: .1f);
        Assert.Equal(.25f, energy.Energy, 5);
    }

    [Fact]
    public void ProgramUsesRelativeTableOffsetsAndSnapshotArrays()
    {
        var p = new DitherProgram();
        Assert.True(p.IsEmpty);
        Assert.Equal(0, p.Table([4, 5]));
        Assert.Equal(2, p.Table([6]));
        p.Push(Op.AreaBody, 2);
        Assert.False(p.IsEmpty);
        var snapshot = p.ToArray();
        Assert.Equal(new float[] { 3, 3, 3, 2, 4, 5, 6 }, snapshot);
        snapshot[0] = 999;
        Assert.Equal(3, p.ToArray()[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ScaleFallsBackToPositiveUnitRange(double max)
    {
        var scale = Scale.Nice(max, 4);
        Assert.Equal(1, scale.Max);
        Assert.Equal(new double[] { 0, .25, .5, .75, 1 }, scale.Ticks);
        Assert.Equal(9, scale.Y(-1, 10));
        Assert.Equal(0, scale.Y(2, 10));
        Assert.Equal(0, scale.Y(.5, 0));
    }

    [Fact]
    public void ScaleSupportsZeroTickRequestAndNiceSteps()
    {
        Assert.Equal(10, Scale.Nice(7, 0).Max);
        Assert.Equal(new double[] { 0, 10 }, Scale.Nice(7, 0).Ticks);
        Assert.Equal(400, Scale.Nice(305, 4).Max);
    }

    [Fact]
    public void InterpolationClampsEndpointsAndCannotOvershootAdjacentSamples()
    {
        Assert.Equal(0, Encoder.Interpolate([], 4));
        Assert.Equal(3, Encoder.Interpolate([3], -1));
        Assert.Equal(0, Encoder.Interpolate([0, 10], -1));
        Assert.Equal(5, Encoder.Interpolate([0, 10], .5f));
        Assert.Equal(10, Encoder.Interpolate([0, 10], 4));
        double[] values = [1000, 0, 1, -1000];
        for (var i = 0; i <= 100; i++) Assert.InRange(Encoder.Interpolate(values, 1 + i / 100f), 0, 1);
    }

    [Fact]
    public void CartesianScaleIgnoresHiddenSeriesAndHandlesJaggedStacks()
    {
        Series[] series = [new("a", Tone.Blue, [1, 2]), new("hidden", Tone.Red, [100]) { Hidden = true }, new("b", Tone.Green, [3])];
        var c = new Cartesian(CartesianKind.Area, series) { Stacked = true };
        Assert.Equal(2, c.Length);
        Assert.Equal(4, c.Scale().Max);
        Assert.Equal(0, c.IndexAt(-20, 11));
        Assert.Equal(1, c.IndexAt(50, 11));
        Assert.Null(c.IndexAt(0, 0));
        Assert.True(c.Encode(1, 9).IsEmpty);
        var empty = new Cartesian(CartesianKind.Line, []);
        Assert.True(empty.IsEmpty);
        Assert.Null(empty.IndexAt(4, 10));
        c.Kind = CartesianKind.Bar;
        Assert.Equal(2.5f, c.ColumnX(0, 10));
        Assert.Equal(1, c.IndexAt(5, 10));
    }

    [Fact]
    public void SparklineScaleReductionsIgnoreNanSamplesLikeRust()
    {
        var program = Encoder.Sparkline([double.NaN, 3, 7, 4, 6], Tone.Blue, 10, 10).ToArray();
        var table = 1 + (int)program[0];
        float[] expected = [1.4537044f, 3.5000005f, 4.4845676f, 3.4043207f, 2.25f];
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(program[table + (i + 5) * 2], expected[i] - 1e-5f, expected[i] + 1e-5f);
    }

    [Fact]
    public void ChartMaximumReductionsIgnoreNanSamplesLikeRust()
    {
        Assert.Equal(3, new Cartesian(CartesianKind.Area, [new Series("a", Tone.Blue, [double.NaN, 3])]).Scale().Max);
        Series[] series = [new("a", Tone.Green, [1, 2, 3]), new("hidden", Tone.Red, [double.NaN]) { Hidden = true }];
        Assert.Equal(new Radar(series) { Max = 3 }.Encode(20, 20).ToArray(), new Radar(series).Encode(20, 20).ToArray());
    }

    [Fact]
    public void EmptyChartsAndPartialSparklineRemainValidPrograms()
    {
        Assert.True(new Radar([]).Encode(20, 20).IsEmpty);
        Assert.True(new Radar([new Series("a", Tone.Green, [1, 2])]).Encode(20, 20).IsEmpty);
        Assert.Empty(new Pie([]).Geometry(1, 1).Angles);
        Assert.Equal(1, new Pie([]).Geometry(1, 1).Radius);
        var sparkline = Encoder.Sparkline([3, 7], Tone.Green, 8, 4, progress: 0).ToArray();
        var tableBase = 1 + (int)sparkline[0];
        Assert.NotEqual(DitherProgram.None, sparkline[tableBase]);
        Assert.All(sparkline.Skip(tableBase + 2), v => Assert.Equal(DitherProgram.None, v));
    }

    [Fact]
    public void PieIgnoresNanSliceValuesLikeRustMax()
    {
        var geo = new Pie([new Slice("nan", double.NaN, Tone.Red), new Slice("one", 1, Tone.Green)]).Geometry(20, 20);
        Assert.Equal((0f, 0f), geo.Angles[0]);
        Assert.Equal((0f, MathF.Tau), geo.Angles[1]);
    }

    [Fact]
    public void OklchTreatsNanChromaAsZeroLikeRustMax() =>
        Assert.Equal(new Rgba(99, 99, 99), Rgba.Oklch(.5f, float.NaN, 0));

    [Fact]
    public void PieHitTestRespectsHoleSweepAndNegativeValues()
    {
        var p = new Pie([new Slice("a", 2, Tone.Green), new Slice("b", -1, Tone.Red)])
        { InnerRadius = .5f, Progress = .5f };
        var geo = p.Geometry(20, 20);
        Assert.Equal(MathF.PI, geo.Angles[0].End);
        Assert.Equal(geo.Angles[1].Start, geo.Angles[1].End);
        Assert.Null(geo.IndexAt(10, 10));
        Assert.Equal(0, geo.IndexAt(16, 10));
        Assert.Null(geo.IndexAt(4, 10));
        Assert.Null(geo.IndexAt(50, 10));
        Assert.Null(new Pie([new Slice("zero", 0, Tone.Green)]).Geometry(20, 20).IndexAt(16, 10));
    }

    [Fact]
    public void AvatarUsesFnv1aUtf8AndNormalizesExplicitHue()
    {
        Assert.Equal(0xcbf29ce484222325UL, Encoder.Fnv1a([]));
        Assert.Equal(0xa430d84680aabd0bUL, Encoder.Fnv1a(Encoding.UTF8.GetBytes("hello")));
        Assert.Equal(330, Encoder.Avatar("é", -30).ToArray()[6]);
        Assert.NotEqual(Encoder.Avatar("e").ToArray(), Encoder.Avatar("é").ToArray());
    }

    [Theory]
    [InlineData(-0.01, "0")]
    [InlineData(-0.0, "0")]
    [InlineData(1e30, "9223372036854775807B")]
    [InlineData(-1e30, "-9223372036854775808B")]
    [InlineData(1e20, "100000000000B")]
    [InlineData(9.223372036854776e27, "9223372036854775807B")]
    [InlineData(-9.223372036854776e27, "-9223372036854775808B")]
    [InlineData(double.MaxValue, "9223372036854775807B")]
    [InlineData(double.PositiveInfinity, "infB")]
    [InlineData(double.NegativeInfinity, "-infB")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(950, "950")]
    [InlineData(1200, "1.2k")]
    [InlineData(3400000, "3.4M")]
    [InlineData(-1000000000, "-1B")]
    public void ValuesUseInvariantCompactFormatting(double value, string expected) => Assert.Equal(expected, Encoder.FormatValue(value));

    [Fact]
    public void ColorRampsParsingAndStraightAlphaFollowRustRules()
    {
        Assert.Equal(Tone.Purple, Tone.Parse(" VIOLET "));
        Assert.Equal(Tone.Grey, Tone.Parse("gray"));
        Assert.Equal(330, Tone.Parse("-30").Hue);
        Assert.False(Tone.TryParse("bad", out _));
        Assert.Equal(Tone.Blue.Shade(0), Tone.Blue.Shade(-1));
        Assert.Equal(Tone.Blue.Shade(1), Tone.Blue.Shade(2));
        Assert.Equal(new Rgba(0x12, 0x34, 0x56), Rgba.Hex(0x123456));
        Assert.Equal((byte)128, Rgba.White.Fade(.5f).A);
        Assert.Equal(new Rgba(128, 128, 128), Rgba.Black.Mix(Rgba.White, .5f));
        Assert.Equal(new Rgba(128, 0, 127), new Rgba(255, 0, 0, 128).Over(new Rgba(0, 0, 255)));
        Assert.Equal(0, Rgba.Black.Luminance());
        Assert.InRange(Rgba.White.Luminance(), .99999f, 1.00001f);
    }

    [Fact]
    public void DataAdaptersExposeTheSameTypedInputUsedByEncoders()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
        {"series":[{"label":"CPU","tone":"blue","values":[2,3],"hidden":true,"fill":"dotted"}],
         "slices":[{"label":"A","tone":75,"value":4,"fill":"hatched"}]}
        """);
        var properties = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        var series = Assert.Single(Encoder.DecodeSeries(properties));
        Assert.Equal("CPU", series.Label);
        Assert.Equal(Tone.Blue, series.Tone);
        Assert.Equal(new double[] { 2, 3 }, series.Values);
        Assert.Equal(Fill.Dotted, series.Fill);
        Assert.True(series.Hidden);
        var slice = Assert.Single(Encoder.DecodeSlices(properties));
        Assert.Equal(4, slice.Value);
        Assert.Equal(75, slice.Tone.Hue);
        Assert.Equal(Fill.Hatched, slice.Fill);
    }

    [Fact]
    public void DispatcherAcceptsNativeValuesAndTypedCollections()
    {
        Series[] series = [new("a", Tone.Blue, [1, 2, 3]) { Fill = Fill.Hatched }];
        var actual = Encoder.Encode("bar", new Dictionary<string, object?>
        { ["w"] = 20, ["h"] = 10, ["series"] = series, ["progress"] = .5, ["stacked"] = true, ["bloom"] = Bloom.Low });
        var expected = new Cartesian(CartesianKind.Bar, series) { Progress = .5f, Stacked = true, Bloom = Bloom.Low }.Encode(20, 10);
        Assert.Equal(expected.ToArray(), actual.ToArray());
        Assert.Throws<ArgumentException>(() => Encoder.Encode("bogus", new Dictionary<string, object?>()));
    }
}
