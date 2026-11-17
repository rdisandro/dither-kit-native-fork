using System.Globalization;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace DitherKit.WinUI;

public record DitherSeries(string Label, IReadOnlyList<double> Values, string Tone = "near", string Fill = "gradient");
public record DitherSlice(string Label, double Value, string Tone = "near", string Fill = "gradient");

/// <summary>Future-Retro chrome: void ground, obsidian cards with a slate hairline,
/// the app's corner radii. Apps keep Segoe UI Variable (NDS-4); labels use a mono face.</summary>
public static class FutureRetro
{
    public static Windows.UI.Color Color(DitherKit.IR.Rgba c) => Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);
    public static SolidColorBrush Brush(DitherKit.IR.Rgba c) => new(Color(c));
    public static SolidColorBrush Void => Brush(DitherKit.IR.Palette.Void);
    public static SolidColorBrush Obsidian => Brush(DitherKit.IR.Palette.Obsidian);
    public static SolidColorBrush Slate => Brush(DitherKit.IR.Palette.Slate);
    public static SolidColorBrush Pewter => Brush(DitherKit.IR.Palette.Pewter);
    public static SolidColorBrush Silver => Brush(DitherKit.IR.Palette.Silver);
    public static SolidColorBrush Marble => Brush(DitherKit.IR.Palette.Marble);
    public static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");
    public static CornerRadius ControlRadius => new(8);
    public static CornerRadius CardRadius => new(14);
    /// <summary>A mono uppercase label.</summary>
    public static TextBlock Label(string text) => new()
    { Text = text.ToUpperInvariant(), FontFamily = Mono, FontSize = 11, CharacterSpacing = 100, Foreground = Pewter };
    /// <summary>An obsidian card with a slate hairline.</summary>
    public static Border Card(UIElement child) => new()
    { Child = child, Padding = new Thickness(16), CornerRadius = CardRadius, BorderThickness = new Thickness(1), BorderBrush = Slate, Background = Obsidian };
}

public class DitherView : UserControl
{
    public DitherSurface Surface { get; } = new();
    public double Cell { get => Surface.Cell; set { Surface.Cell = value; RefreshChrome(); } }
    public Dictionary<string, object?> Properties { get => Surface.Properties; set { Surface.Properties = value; RefreshChrome(); } }
    public string AccessibleLabel { get => AutomationProperties.GetName(this); set => AutomationProperties.SetName(this, value); }
    protected Grid Root { get; } = new();
    protected DitherView(string component, string name)
    {
        Surface.Component = component;
        Root.Children.Add(Surface);
        Content = Root;
        AccessibleLabel = name;
        MinWidth = 24; MinHeight = 24;
    }
    public void SetOption(string name, object? value) { Surface.SetOption(name, value); RefreshChrome(); }
    protected virtual void RefreshChrome() { }
}

public sealed class DitherGradient : DitherView { public DitherGradient() : base("gradient", "Dither gradient") { } }
/// <summary>A live field at film rate. Hover quickens it; with animations off it
/// holds the Future-Retro still frame. Clouds in Near AI blue by default.</summary>
public sealed class DitherBackground : DitherView
{
    public double Speed { get => Surface.Speed; set => Surface.Speed = value; }
    public DitherKit.IR.Energy Energy { get => Surface.Energy; set => Surface.Energy = value; }
    public DitherBackground() : base("background", "Animated dither background")
    {
        Surface.Animate = true;
        Background = new SolidColorBrush(Colors.Transparent);
        PointerEntered += (_, _) => Surface.Hovered = true;
        PointerExited += (_, _) => Surface.Hovered = false;
    }
}
/// <summary>A one-pigment identicon (set "hue" for an upstream multi-hue one).</summary>
public sealed class DitherAvatar : DitherView
{
    public string Text { get => Properties.GetValueOrDefault("text")?.ToString() ?? "Dither"; set { SetOption("text", value); AccessibleLabel = $"Avatar for {value}"; } }
    public DitherAvatar() : base("avatar", "Dither avatar") { Width = Height = 64; Text = "Dither"; SetOption("tone", "near"); }
}
public sealed class DitherSwatch : DitherView { public DitherSwatch() : base("swatch", "Color swatch") { Width = Height = 15; } }

/// <summary>Future-Retro tones draw the flat Near AI button (flat pigment and void
/// label, a marble outline, or text with an underline), styled through WinUI's
/// lightweight button resources; upstream tones keep dither-core's texture.</summary>
public sealed class DitherButton : Button
{
    public DitherSurface Surface { get; } = new() { Component = "button", Animate = true };
    private readonly TextBlock label = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private bool hovered;
    private string ditherStyle = "solid";
    public string Text { get => label.Text; set { label.Text = value; AutomationProperties.SetName(this, value); } }
    public string DitherStyle { get => ditherStyle; set { ditherStyle = value; Surface.SetOption("style", value); UpdateDensity(); } }
    public string Tone { get => Surface.Properties.GetValueOrDefault("tone")?.ToString() ?? "near"; set { Surface.SetOption("tone", value); UpdateDensity(); } }
    public DitherButton()
    {
        var grid = new Grid(); grid.Children.Add(Surface); grid.Children.Add(label); Content = grid;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        Padding = new Thickness(0); MinWidth = 120; MinHeight = 42; Text = "Continue";
        PointerEntered += (_, _) => { hovered = true; UpdateDensity(); };
        PointerExited += (_, _) => { hovered = false; UpdateDensity(); };
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => UpdateDensity());
        UpdateDensity();
    }
    private void UpdateDensity()
    {
        var style = Enum.Parse<DitherKit.IR.ButtonStyle>(DitherStyle, true);
        var tone = DitherKit.IR.Tone.Parse(Tone);
        if (tone.IsRamp) { UpdateFlat(style, tone); return; }
        Surface.Visibility = Visibility.Visible;
        RenderTransform = null;
        double density = DitherKit.IR.OptionExtensions.Density(style, hovered, IsPressed);
        Surface.SetOption("density", density);
        Surface.Animate = density > .4;
        var tint = DitherKit.IR.Tone.Parse(Tone).Shade(.22f);
        Background = new SolidColorBrush(DitherStyle == "solid" ? Windows.UI.Color.FromArgb(tint.A, tint.R, tint.G, tint.B) : Colors.Transparent);
    }
    private void UpdateFlat(DitherKit.IR.ButtonStyle style, DitherKit.IR.Tone tone)
    {
        Surface.Visibility = Visibility.Collapsed;
        Surface.Animate = false;
        CornerRadius = FutureRetro.ControlRadius;
        Padding = new Thickness(20, 13, 20, 12);
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        FontSize = 15;
        var rest = DitherKit.IR.OptionExtensions.Chrome(style, tone, hovered: false, pressed: false);
        var hot = DitherKit.IR.OptionExtensions.Chrome(style, tone, hovered: true, pressed: false);
        SolidColorBrush Brush(DitherKit.IR.Rgba? c) => c is { } color ? FutureRetro.Brush(color) : new SolidColorBrush(Colors.Transparent);
        // Lightweight styling: the template's visual states read these keys.
        Resources["ButtonBackground"] = Brush(rest.Fill);
        Resources["ButtonBackgroundPointerOver"] = Brush(hot.Fill);
        Resources["ButtonBackgroundPressed"] = Brush(hot.Fill);
        Resources["ButtonForeground"] = Brush(rest.Label);
        Resources["ButtonForegroundPointerOver"] = Brush(hot.Label);
        Resources["ButtonForegroundPressed"] = Brush(hot.Label);
        Resources["ButtonBorderBrush"] = Brush(rest.Border);
        Resources["ButtonBorderBrushPointerOver"] = Brush(hot.Border);
        Resources["ButtonBorderBrushPressed"] = Brush(hot.Border);
        Background = Brush(rest.Fill);
        Foreground = Brush(hovered || IsPressed ? hot.Label : rest.Label);
        BorderBrush = Brush(rest.Border);
        BorderThickness = new Thickness(rest.Border is null ? 0 : 1);
        label.TextDecorations = rest.Underline is null ? Windows.UI.Text.TextDecorations.None : Windows.UI.Text.TextDecorations.Underline;
        // Press nudges 1px down; no scale, no layout shift.
        RenderTransform = new TranslateTransform { Y = IsPressed && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ? 1 : 0 };
    }
}

public sealed class DitherMeter : DitherView
{
    private readonly TextBlock label = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch tween = new();
    private double value, displayed, from;
    public double Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 1); label.Text = this.value.ToString("P0", CultureInfo.CurrentCulture); AccessibleLabel = $"Meter {label.Text}";
            if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) { from = displayed; tween.Restart(); timer.Start(); } else { displayed = this.value; Surface.SetOption("value", displayed); } }
    }
    /// <summary>A dot-matrix meter in Near AI blue. It eases to new values (cubic, 600 ms); nothing bounces.</summary>
    public DitherMeter() : base("meter", "Meter")
    {
        MinHeight = 24; Surface.Margin = new Thickness(0, 0, 52, 0); Root.Children.Add(label);
        label.FontFamily = FutureRetro.Mono; label.Foreground = FutureRetro.Silver;
        Surface.SetOption("tone", "near"); Surface.SetOption("fill", "matrix");
        timer.Tick += (_, _) =>
        {
            double t = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ? Math.Clamp(tween.Elapsed / DitherKit.IR.Motion.Reveal, 0, 1) : 1;
            displayed = from + (value - from) * DitherKit.IR.Motion.EaseOutCubic((float)t);
            Surface.SetOption("value", displayed);
            if (t >= 1) { displayed = value; timer.Stop(); }
        };
        Unloaded += (_, _) => timer.Stop();
    }
}

public sealed class DitherSparkline : DitherView
{
    public DitherSparkline() : base("sparkline", "Sparkline")
    {
        Surface.AnimateEntrance = true;
        Background = new SolidColorBrush(Colors.Transparent);
        PointerEntered += (_, _) => SetOption("hovered", true);
        PointerExited += (_, _) => SetOption("hovered", false);
        GotFocus += (_, _) => SetOption("hovered", true);
        LostFocus += (_, _) => SetOption("hovered", false);
        IsTabStop = true;
    }
}

public sealed class DitherLegend : StackPanel
{
    public event EventHandler<int>? Toggled;
    public event EventHandler<int?>? Selected;
    private (string Label, string Tone, bool Visible)[] current = [];
    public DitherLegend() { Orientation = Orientation.Horizontal; Spacing = 12; }
    public void SetItems(IEnumerable<(string Label, string Tone, bool Visible)> items)
    {
        var next = items.ToArray();
        if (next.Length == current.Length && next.Select((x, i) => x.Label == current[i].Label && x.Tone == current[i].Tone).All(x => x))
        {
            for (int i = 0; i < next.Length; i++) { var t = (ToggleButton)Children[i]; t.IsChecked = next[i].Visible; t.Opacity = next[i].Visible ? 1 : .4; }
            current = next; return;
        }
        current = next;
        Children.Clear(); int index = 0;
        foreach (var item in next)
        {
            int current = index++;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var swatch = new DitherSwatch(); swatch.SetOption("tone", item.Tone); content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = item.Label, Foreground = FutureRetro.Silver });
            var toggle = new ToggleButton { Content = content, IsChecked = item.Visible, Opacity = item.Visible ? 1 : .4 };
            AutomationProperties.SetName(toggle, $"Show {item.Label}");
            toggle.Click += (_, _) => Toggled?.Invoke(this, current);
            toggle.PointerEntered += (_, _) => Selected?.Invoke(this, current);
            toggle.PointerExited += (_, _) => Selected?.Invoke(this, null);
            toggle.GotFocus += (_, _) => Selected?.Invoke(this, current);
            toggle.LostFocus += (_, _) => Selected?.Invoke(this, null);
            Children.Add(toggle);
        }
    }
}

internal static class ChromeData
{
    private static readonly JsonSerializerOptions options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, options);
    public static bool IsHidden(JsonElement row) => row.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True;
    public static Dictionary<string, object?>[] ToggleHidden(Dictionary<string, object?> props, int index) => Rows(props, "series").Select((r, i) => {
        var row = r.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        if (i == index) row["hidden"] = !IsHidden(r);
        return row;
    }).ToArray();
    public static bool Bool(Dictionary<string, object?> props, string key) => props.TryGetValue(key, out var v) && Json(v).ValueKind == JsonValueKind.True;
    public static double? Number(Dictionary<string, object?> props, string key) => props.TryGetValue(key, out var v) && Json(v).ValueKind == JsonValueKind.Number ? Json(v).GetDouble() : null;
    public static string Text(JsonElement element, string key, string fallback = "") => element.TryGetProperty(key, out var p) ? p.ToString() : fallback;
    public static List<JsonElement> Rows(Dictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var value)) return [];
        var element = Json(value);
        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(x => x.Clone()).ToList() : [];
    }
}
