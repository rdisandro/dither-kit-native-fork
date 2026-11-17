using System.Text.Json;
using DitherKit.WinUI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DitherKit.Gallery;

public partial class App : Application
{
    private Window? window;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window { Title = "DitherKit · WinUI 3" };
        var page = new StackPanel { Spacing = 18, Padding = new Thickness(32), MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(FutureRetro.Label("DitherKit · WinUI 3"));
        page.Children.Add(new TextBlock { Text = "Rendered by machine, in one blue", FontSize = 40, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = -30, Foreground = FutureRetro.Marble });
        page.Children.Add(new TextBlock { Text = "Shader pixels on a 3-pixel grid. Native controls around them.", FontSize = 16, TextWrapping = TextWrapping.Wrap, Foreground = FutureRetro.Silver });
        var hero = new DitherBackground { Height = 150 };
        page.Children.Add(Card("Clouds · hover to quicken", hero));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var meter = new DitherMeter { Width = 460, Height = 28, Value = .62 };
        foreach (var (text, style) in new[] { ("Update meter", "solid"), ("Secondary", "outline"), ("Ghost", "ghost") })
        {
            var button = new DitherButton { Text = text, DitherStyle = style, Tone = "near" };
            button.Click += (_, _) => meter.Value = meter.Value > .8 ? .35 : .92;
            buttons.Children.Add(button);
        }
        var controls = new StackPanel { Spacing = 16 };
        controls.Children.Add(buttons);
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = .62, Width = 460, Header = "Meter value", StepFrequency = .01 };
        slider.ValueChanged += (_, e) => meter.Value = e.NewValue;
        controls.Children.Add(meter); controls.Children.Add(slider);
        page.Children.Add(Card("Native controls", controls));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenes.json")));
        var selected = new HashSet<string> { "fr_gradient_1", "fr_background_rings", "fr_avatar_ada_lovelace", "fr_swatch_near", "fr_area_0", "fr_line_2", "fr_bar_4", "fr_sparkline_1", "fr_pie_0", "fr_radar_0" };
        foreach (var scene in document.RootElement.GetProperty("scenes").EnumerateArray())
        {
            string name = scene.GetProperty("name").GetString()!;
            if (!selected.Contains(name)) continue;
            string kind = scene.GetProperty("kind").GetString()!;
            DitherView control = kind switch
            {
                "gradient" => new DitherGradient(), "background" => new DitherBackground(), "avatar" => new DitherAvatar(),
                "swatch" => new DitherSwatch(), "cartesian" => new DitherChart { Labels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun", "Mon", "Tue", "Wed"] },
                "sparkline" => new DitherSparkline(), "pie" => new DitherPie(), "radar" => new DitherRadar { Labels = ["Speed", "Quality", "Range", "Value", "Power", "Ease"] },
                _ => throw new InvalidOperationException(kind)
            };
            // Encode from scene PARAMETERS, never the stored expected bytecode.
            control.Properties = scene.EnumerateObject().Where(p => p.Name != "program").ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            control.Width = kind == "avatar" ? 80 : Math.Max(240, scene.GetProperty("w").GetInt32() * 4);
            control.Height = kind is "cartesian" or "pie" or "radar" ? 260 : Math.Max(48, scene.GetProperty("h").GetInt32() * 3);
            control.AccessibleLabel = name.Replace('_', ' ');
            page.Children.Add(Card(name.Replace("fr_", "").Replace('_', ' '), control));
        }
        page.Children.Add(FutureRetro.Label("Near AI blue on void · 3-pixel cells · 24 fps · animations off holds a still frame"));
        window.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = FutureRetro.Void, RequestedTheme = ElementTheme.Dark };
        window.Activate();
    }

    private static Border Card(string title, UIElement child)
    {
        var card = new StackPanel { Spacing = 12 };
        card.Children.Add(FutureRetro.Label(title));
        card.Children.Add(child);
        return FutureRetro.Card(card);
    }
}
