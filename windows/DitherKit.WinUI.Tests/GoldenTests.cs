using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DitherKit.WinUI;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace DitherKit.WinUI.Tests;

public sealed class GoldenTests(ITestOutputHelper output)
{
    [Fact]
    public async Task EveryFixtureRendersThroughDirect2DWithinGoldenTolerance()
    {
        // No XAML window or dispatcher is involved. Force software WARP so the
        // same test also runs on hosted CI machines without a physical GPU.
        using var device = new CanvasDevice(true);
        string scenesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "scenes.json"));
        using var scenes = JsonDocument.Parse(File.ReadAllText(scenesPath));
        int count = 0;
        foreach (var scene in scenes.RootElement.GetProperty("scenes").EnumerateArray())
        {
            string name = scene.GetProperty("name").GetString()!;
            int w = scene.GetProperty("w").GetInt32(), h = scene.GetProperty("h").GetInt32();
            float[] program = scene.GetProperty("program").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            using var rendered = DitherRenderer.Render(device, program, w, h, straightAlpha: true);
            var pixels = rendered.GetPixelBytes(); // DXGI R8G8B8A8, straight alpha
            string pngPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "png", $"{name}.png"));
            output.WriteLine($"Loading golden PNG: {pngPath}; exists={File.Exists(pngPath)}");
            Assert.True(File.Exists(pngPath), $"Golden image is missing: {pngPath}");
            using var file = File.OpenRead(pngPath);
            using var stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Assert.Equal((uint)w, decoder.PixelWidth);
            Assert.Equal((uint)h, decoder.PixelHeight);
            var expected = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            int different = 0, maximum = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int delta = Math.Max(Math.Max(Math.Abs(pixels[i] - expected[i]), Math.Abs(pixels[i + 1] - expected[i + 1])),
                    Math.Max(Math.Abs(pixels[i + 2] - expected[i + 2]), Math.Abs(pixels[i + 3] - expected[i + 3])));
                if (delta != 0) different++;
                maximum = Math.Max(maximum, delta);
            }
            Assert.True(maximum <= 2, $"{name}: maximum channel error {maximum} > 2");
            Assert.True(different <= w * h * .01, $"{name}: {different}/{w * h} differing pixels > 1%");
            count++;
        }
        Assert.Equal(74, count);
        if (Environment.GetEnvironmentVariable("DITHER_PROFILE") == "1")
            Profile(device, scenes.RootElement.GetProperty("scenes"));
    }

    // Opt-in diagnostics only: correctness above always checks every scene.
    // Both modes use this process, this WARP device and the current shader;
    // only CPU-cached program metadata versus the legacy GPU scan differs.
    private void Profile(CanvasDevice device, JsonElement scenes)
    {
        string[] names = ["gradient_2", "background_plasma", "area_2", "line_4", "pie_2", "radar_1"];
        const int warmupRendersPerMode = 10;
        const int rendersPerSample = 25;
        const int sampleCount = 7;
        foreach (string name in names)
        {
            var scene = scenes.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
            int w = scene.GetProperty("w").GetInt32(), h = scene.GetProperty("h").GetInt32();
            var program = scene.GetProperty("program").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            byte[] RenderAndSynchronize(bool legacyGpuScan)
            {
                using var target = legacyGpuScan
                    ? LegacyProfileRenderer.Render(device, program, w, h)
                    : DitherRenderer.Render(device, program, w, h, straightAlpha: true);
                return target.GetPixelBytes(); // Both paths finish GPU work and dispose the target before returning.
            }
            double Measure(bool legacyGpuScan)
            {
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < rendersPerSample; i++) _ = RenderAndSynchronize(legacyGpuScan);
                clock.Stop();
                return clock.Elapsed.TotalMicroseconds / rendersPerSample;
            }

            for (int i = 0; i < warmupRendersPerMode; i++)
            {
                _ = RenderAndSynchronize(true);
                _ = RenderAndSynchronize(false);
            }
            byte[] legacyPixels = RenderAndSynchronize(true);
            byte[] cachedPixels = RenderAndSynchronize(false);
            Assert.True(legacyPixels.AsSpan().SequenceEqual(cachedPixels),
                $"{name}: legacy GPU scan and CPU-cached program split must produce byte-identical RGBA before profiling");

            double[] legacySamples = new double[sampleCount];
            double[] cachedSamples = new double[sampleCount];
            for (int sample = 0; sample < sampleCount; sample++)
            {
                // Reverse the order in successive pairs to distribute temporal drift.
                if (sample % 2 == 0)
                {
                    legacySamples[sample] = Measure(true);
                    cachedSamples[sample] = Measure(false);
                }
                else
                {
                    cachedSamples[sample] = Measure(false);
                    legacySamples[sample] = Measure(true);
                }
            }
            double legacyMedian = legacySamples.Order().ElementAt(sampleCount / 2);
            double cachedMedian = cachedSamples.Order().ElementAt(sampleCount / 2);
            output.WriteLine("DITHER_PROFILE " + JsonSerializer.Serialize(new
            {
                scene = name, width = w, height = h,
                comparison = "Current shader: legacy GPU metadata scan versus CPU-cached program split; hue trig hoist is shared. Not a full historical shader comparison.",
                renderer = "WARP; both modes share one CanvasDevice in this process",
                warmup_renders_per_mode = warmupRendersPerMode, renders_per_sample = rendersPerSample,
                sample_count = sampleCount, exact_rgba_match = true,
                clock = "Synchronized wall time per render: allocation, upload, drawing, readback and disposal; not isolated GPU time",
                baseline_legacy_gpu_scan = new { median_us = legacyMedian, samples_us = legacySamples },
                cpu_cached_program_split = new { median_us = cachedMedian, samples_us = cachedSamples },
                median_ratio_legacy_over_cached = legacyMedian / cachedMedian,
                paired_samples = Enumerable.Range(0, sampleCount).Select(i => new
                {
                    sample = i,
                    first_mode = i % 2 == 0 ? "legacy_gpu_scan" : "cpu_cached_program_split",
                    legacy_gpu_scan_us = legacySamples[i], cpu_cached_program_split_us = cachedSamples[i]
                }).ToArray(),
                revision = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
                processor_identifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unavailable",
                processor_count = Environment.ProcessorCount,
                runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                os_architecture = RuntimeInformation.OSArchitecture.ToString(),
                process_architecture = RuntimeInformation.ProcessArchitecture.ToString()
            }));
        }
    }
}
