using System.Text.Json;
using DitherKit.IR;
using Xunit;
namespace DitherKit.IR.Tests;
public sealed class FixtureTests
{
    public static IEnumerable<object[]> Scenes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/scenes.json")));
        foreach (var scene in doc.RootElement.GetProperty("scenes").EnumerateArray())
            yield return [scene.GetProperty("name").GetString()!, scene.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void EncoderMatchesRustFixture(string name, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var scene = doc.RootElement;
        // Expected bytecode is kept out of the encoder input: it must derive every float.
        var props = scene.EnumerateObject().Where(p => p.Name != "program")
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        var actual = Encoder.Encode(scene.GetProperty("kind").GetString()!, props).ToArray();
        var expected = scene.GetProperty("program").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Assert.True(expected.Length == actual.Length, $"{name}: expected {expected.Length} floats, got {actual.Length}");
        for (var i = 0; i < expected.Length; i++)
            Assert.True(float.IsFinite(actual[i]) && Math.Abs(expected[i] - actual[i]) <= 1e-4f,
                $"{name}[{i}]: expected {expected[i]:R}, got {actual[i]:R}");
    }

    [Fact]
    public void FixtureCoversAllScenes() => Assert.Equal(74, Scenes().Count());
}
