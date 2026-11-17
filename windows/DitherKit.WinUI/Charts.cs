using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DitherKit.WinUI;

public sealed class DitherChart : DitherView
{
    private readonly Grid plot = new();
    private readonly Canvas yAxis = new();
    private readonly Canvas xAxis = new() { Height = 28 };
    // A Terminal-window tooltip: macOS Terminal greys, mono text.
    private readonly TextBlock tooltip = new() { Padding = new Thickness(9, 6, 9, 6), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        FontFamily = FutureRetro.Mono, FontSize = 11.5, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE4, 0xE4, 0xE6)) };
    public DitherLegend Legend { get; } = new();
    private int hover = -1;
    public IReadOnlyList<string> Labels { get; set; } = [];
    public string Chart { get => Properties.GetValueOrDefault("chart")?.ToString() ?? "area"; set => SetOption("chart", value); }
    public IReadOnlyList<DitherSeries> Series
    {
        set => SetOption("series", value.Select(s => new { label = s.Label, values = s.Values, tone = s.Tone, fill = s.Fill }).ToArray());
    }

    public DitherChart() : base("cartesian", "Interactive dither chart")
    {
        Root.Children.Clear(); Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Root.RowDefinitions.Add(new() { Height = GridLength.Auto }); Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); Root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Root.Children.Add(yAxis); Grid.SetColumn(plot, 1); Root.Children.Add(plot); plot.Children.Add(Surface); plot.Children.Add(tooltip);
        Grid.SetRow(xAxis, 1); Grid.SetColumn(xAxis, 1); Root.Children.Add(xAxis);
        Grid.SetRow(Legend, 2); Grid.SetColumn(Legend, 1); Root.Children.Add(Legend);
        Surface.AnimateEntrance = true; IsTabStop = true;
        plot.Background = new SolidColorBrush(Colors.Transparent);
        plot.PointerMoved += (_, e) =>
        {
            var model = Model();
            if (model.Length > 0) SelectHover(model.IndexAt((float)(e.GetCurrentPoint(plot).Position.X / Cell), Math.Max(1, Surface.GridWidth)) ?? -1);
        };
        plot.PointerExited += (_, _) => SelectHover(-1);
        LostFocus += (_, _) => SelectHover(-1);
        KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Left or VirtualKey.Right) { SelectHover(hover + (e.Key == VirtualKey.Left ? -1 : 1)); e.Handled = true; }
            if (e.Key == VirtualKey.Escape) { SelectHover(-1); e.Handled = true; }
        };
        Legend.Toggled += (_, index) => { Surface.SetOption("series", ChromeData.ToggleHidden(Properties, index)); RefreshChrome(); };
        Legend.Selected += (_, index) => Surface.SetOption("selected", index);
        SizeChanged += (_, _) => RefreshChrome();
        Surface.SizeChanged += (_, _) => RefreshChrome();
    }

    private DitherKit.IR.Cartesian Model() => new(Enum.Parse<DitherKit.IR.CartesianKind>(Chart, true), DitherKit.IR.Encoder.DecodeSeries(Properties))
    {
        Stacked = ChromeData.Bool(Properties, "stacked"),
        YMax = ChromeData.Number(Properties, "y_max"),
        TickCount = (int)(ChromeData.Number(Properties, "tick_count") ?? 4)
    };

    private void SelectHover(int index)
    {
        var model = Model();
        int n = model.Length;
        int next = index < 0 || n == 0 ? -1 : Math.Clamp(index, 0, n - 1);
        if (next == hover) return;
        hover = next;
        Surface.SetOption("hover", hover < 0 ? null : hover);
        tooltip.Visibility = hover < 0 ? Visibility.Collapsed : Visibility.Visible;
        if (hover < 0) return;
        string title = hover < Labels.Count ? Labels[hover] : $"Point {hover + 1}";
        tooltip.Text = title + "\n" + string.Join("\n", model.Series.Where(s => !s.Hidden && hover < s.Values.Count).Select(s => $"{s.Label}: {DitherKit.IR.Encoder.FormatValue(s.Values[hover])}"));
        tooltip.Measure(new Windows.Foundation.Size(240, double.PositiveInfinity));
        double x = model.ColumnX(hover, Math.Max(1, Surface.GridWidth)) * Cell;
        tooltip.HorizontalAlignment = HorizontalAlignment.Left;
        tooltip.VerticalAlignment = VerticalAlignment.Top;
        tooltip.Margin = new Thickness(Math.Clamp(x + 12, 0, Math.Max(0, plot.ActualWidth - tooltip.DesiredSize.Width)), 8, 0, 0);
        AutomationProperties.SetHelpText(this, tooltip.Text);
    }

    protected override void RefreshChrome()
    {
        var rows = ChromeData.Rows(Properties, "series");
        Legend.SetItems(rows.Select((r, i) => (ChromeData.Text(r, "label"), ChromeData.Text(r, "tone", "near"), !ChromeData.IsHidden(r))));
        yAxis.Children.Clear();
        var model = Model(); var scale = model.Scale();
        int gridHeight = Math.Max(1, (int)(Surface.ActualHeight / Cell));
        int gridWidth = Math.Max(1, (int)(Surface.ActualWidth / Cell));
        foreach (double tick in scale.Ticks)
        {
            var label = new TextBlock { Text = DitherKit.IR.Encoder.FormatValue(tick), FontSize = 10, FontFamily = FutureRetro.Mono, Foreground = FutureRetro.Pewter };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(label, scale.Y(tick, gridHeight) * Cell - label.DesiredSize.Height / 2);
            Canvas.SetLeft(label, Math.Max(0, 40 - label.DesiredSize.Width));
            yAxis.Children.Add(label);
        }
        xAxis.Children.Clear();
        int count = model.Length;
        for (int i = 0; i < count; i++)
        {
            var label = new TextBlock { Text = (i < Labels.Count ? Labels[i] : (i + 1).ToString()).ToUpperInvariant(), FontSize = 10, FontFamily = FutureRetro.Mono, Foreground = FutureRetro.Pewter };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, model.ColumnX(i, gridWidth) * Cell - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, 4); xAxis.Children.Add(label);
        }
        AccessibleLabel = $"{Chart} chart. " + string.Join(". ", rows.Select(r => ChromeData.Text(r, "label"))) + ". Use left and right arrows to inspect values.";
    }
}

public sealed class DitherPie : DitherView
{
    private readonly TextBlock total = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        FontFamily = FutureRetro.Mono, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FutureRetro.Marble };
    public DitherLegend Legend { get; } = new();
    private int hover = -1;
    public IReadOnlyList<DitherSlice> Slices { set => SetOption("slices", value.Select(s => new { label = s.Label, value = s.Value, tone = s.Tone, fill = s.Fill }).ToArray()); }
    public DitherPie() : base("pie", "Interactive dither pie chart")
    {
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Root.Children.Add(total); Grid.SetRow(Legend, 1); Root.Children.Add(Legend);
        Surface.AnimateEntrance = true; IsTabStop = true; Background = new SolidColorBrush(Colors.Transparent);
        PointerMoved += (_, e) =>
        {
            var p = e.GetCurrentPoint(Surface).Position;
            var model = new DitherKit.IR.Pie(DitherKit.IR.Encoder.DecodeSlices(Properties)) { InnerRadius = (float)(ChromeData.Number(Properties, "inner") ?? 0) };
            var geometry = model.Geometry(Math.Max(1, (int)(Surface.ActualWidth / Cell)), Math.Max(1, (int)(Surface.ActualHeight / Cell)));
            Select(geometry.IndexAt((float)(p.X / Cell), (float)(p.Y / Cell)) ?? -1);
        };
        PointerExited += (_, _) => Select(-1);
        KeyDown += (_, e) => { if (e.Key is VirtualKey.Left or VirtualKey.Right) { int n = ChromeData.Rows(Properties, "slices").Count; if (n > 0) Select((hover + (e.Key == VirtualKey.Left ? n - 1 : 1)) % n); e.Handled = true; } };
        Legend.Toggled += (_, index) => Select(hover == index ? -1 : index);
        Legend.Selected += (_, index) => Select(index ?? -1);
    }
    private void Select(int index) { if (hover == index) return; hover = index; Surface.SetOption("hover", index < 0 ? null : index); RefreshChrome(); }
    protected override void RefreshChrome()
    {
        var rows = ChromeData.Rows(Properties, "slices"); double sum = rows.Sum(s => { double v = s.GetProperty("value").GetDouble(); return double.IsNaN(v) ? 0 : Math.Max(v, 0); });
        total.Text = hover >= 0 && hover < rows.Count ? $"{ChromeData.Text(rows[hover], "label")}\n{rows[hover].GetProperty("value").GetDouble():G4}" : sum.ToString("G4");
        // Center chrome is shown only in a donut's empty center.
        double inner = Properties.GetValueOrDefault("inner") is System.Text.Json.JsonElement el ? el.GetDouble() : Convert.ToDouble(Properties.GetValueOrDefault("inner") ?? 0);
        total.Visibility = inner > 0 ? Visibility.Visible : Visibility.Collapsed;
        Legend.SetItems(rows.Select((r, i) => (ChromeData.Text(r, "label"), ChromeData.Text(r, "tone", "near"), hover < 0 || hover == i)));
        AccessibleLabel = $"Pie chart, total {sum:G4}. " + string.Join(". ", rows.Select(r => $"{ChromeData.Text(r, "label")}: {r.GetProperty("value").GetDouble():G4}"));
        AutomationProperties.SetHelpText(this, total.Text);
    }
}

public sealed class DitherRadar : DitherView
{
    private readonly Canvas axes = new() { IsHitTestVisible = false };
    public IReadOnlyList<string> Labels { get; set; } = [];
    public DitherLegend Legend { get; } = new();
    public IReadOnlyList<DitherSeries> Series { set => SetOption("series", value.Select(s => new { label = s.Label, values = s.Values, tone = s.Tone, fill = s.Fill }).ToArray()); }
    public DitherRadar() : base("radar", "Dither radar chart")
    {
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Surface.Margin = new Thickness(28); Root.Children.Add(axes); Grid.SetRow(Legend, 1); Root.Children.Add(Legend);
        Surface.AnimateEntrance = true; SizeChanged += (_, _) => RefreshChrome();
        Legend.Toggled += (_, index) => { Surface.SetOption("series", ChromeData.ToggleHidden(Properties, index)); RefreshChrome(); };
        Legend.Selected += (_, index) => Surface.SetOption("selected", index);
        Surface.SizeChanged += (_, _) => RefreshChrome();
    }
    protected override void RefreshChrome()
    {
        axes.Children.Clear(); var rows = ChromeData.Rows(Properties, "series");
        var model = new DitherKit.IR.Radar(DitherKit.IR.Encoder.DecodeSeries(Properties));
        var geometry = model.Geometry(Math.Max(1, (int)(Surface.ActualWidth / Cell)), Math.Max(1, (int)(Surface.ActualHeight / Cell)));
        for (int i = 0; i < geometry.AxisTips.Count; i++)
        {
            var tip = geometry.AxisTips[i];
            double dx = tip.X - geometry.Center.X, dy = tip.Y - geometry.Center.Y;
            double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            var label = new TextBlock { Text = (i < Labels.Count ? Labels[i] : $"Axis {i + 1}").ToUpperInvariant(), FontSize = 10, FontFamily = FutureRetro.Mono, Foreground = FutureRetro.Pewter };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Surface.Margin.Left + tip.X * Cell + dx / length * 14 - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, Surface.Margin.Top + tip.Y * Cell + dy / length * 14 - label.DesiredSize.Height / 2); axes.Children.Add(label);
        }
        Legend.SetItems(rows.Select(r => (ChromeData.Text(r, "label"), ChromeData.Text(r, "tone", "near"), !ChromeData.IsHidden(r))));
        AccessibleLabel = "Radar chart. " + string.Join(". ", rows.Select(r => ChromeData.Text(r, "label"))) + ". Axes: " + string.Join(", ", Labels);
    }
}
