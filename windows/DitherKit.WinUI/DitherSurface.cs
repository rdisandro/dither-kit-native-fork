using System.Diagnostics;
using DitherKit.IR;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace DitherKit.WinUI;

/// <summary>A native CanvasControl rendering a display list at logical dither resolution.</summary>
public class DitherSurface : UserControl
{
    private readonly CanvasControl canvas = new();
    private readonly UISettings settings = new();
    private readonly Stopwatch clock = new();
    // Future-Retro canvases run at film rate.
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1 / Motion.FilmFps) };
    private TimeSpan lastTick;
    private float phase = Motion.StillPhase;
    private float? fieldEnergy;
    private CanvasRenderTarget? target;
    private double cell = 3;
    private string component = "gradient";
    private bool dirty = true;
    private Dictionary<string, object?> properties = new();
    private bool animate;
    public bool Animate
    {
        get => animate;
        set
        {
            animate = value;
            if (IsLoaded && value && settings.AnimationsEnabled) timer.Start();
            else if (!value && (!AnimateEntrance || clock.Elapsed >= Motion.Reveal)) timer.Stop();
        }
    }
    public bool AnimateEntrance { get; set; }
    /// <summary>Multiplies the field's phase rate (0 holds the still frame).</summary>
    public double Speed { get; set; } = 1;
    /// <summary>Idle and hover energy for animated fields.</summary>
    public Energy Energy { get; set; } = Energy.Field;
    /// <summary>Set by the owning control: hover adds energy, not movement.</summary>
    public bool Hovered { get; set; }
    public string Component { get => component; set { component = value; Invalidate(); } }
    public double Cell { get => cell; set { cell = double.IsFinite(value) ? Math.Max(1, value) : 3; Invalidate(); } }
    public Dictionary<string, object?> Properties { get => properties; set { properties = value ?? new(); Invalidate(); } }
    private float[]? program;
    public float[]? Program { get => program; set { program = value; Invalidate(); } }
    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }

    public DitherSurface()
    {
        Content = canvas;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Dither illustration");
        canvas.Draw += Draw;
        canvas.SizeChanged += (_, _) => Invalidate();
        canvas.CreateResources += (_, _) => Invalidate();
        timer.Tick += (_, _) =>
        {
            if (!settings.AnimationsEnabled) { timer.Stop(); Invalidate(); return; }
            if (!Animate && clock.Elapsed >= Motion.Reveal) { timer.Stop(); Invalidate(); return; }
            var now = clock.Elapsed;
            var dt = (float)Math.Clamp((now - lastTick).TotalSeconds, 0, .12) * (float)Speed;
            lastTick = now;
            (phase, var level) = Energy.Step(phase, fieldEnergy ?? Energy.Idle, Hovered, dt);
            fieldEnergy = level;
            Invalidate();
        };
        Loaded += (_, _) => { clock.Restart(); lastTick = TimeSpan.Zero; if (settings.AnimationsEnabled && (Animate || AnimateEntrance)) timer.Start(); Invalidate(); };
        Unloaded += (_, _) => { timer.Stop(); clock.Stop(); target?.Dispose(); target = null; dirty = true; };
    }

    public void SetOption(string name, object? value) { properties[name] = value; Invalidate(); }
    public void Invalidate() { dirty = true; canvas.Invalidate(); }

    private void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        int w = Math.Max(1, (int)Math.Floor(sender.ActualWidth / Cell));
        int h = Math.Max(1, (int)Math.Floor(sender.ActualHeight / Cell));
        if (Component == "avatar") { w = 8; h = 8; }
        if (dirty || target is null || w != GridWidth || h != GridHeight)
        {
            var options = new Dictionary<string, object?>(Properties) { ["w"] = w, ["h"] = h };
            // Fields advance by energy; with animations off they hold the still frame.
            if (Animate) options["time"] = settings.AnimationsEnabled && Speed != 0 ? phase : Motion.StillPhase;
            if (AnimateEntrance)
            {
                double t = settings.AnimationsEnabled ? Math.Clamp(clock.Elapsed / Motion.Reveal, 0, 1) : 1;
                options["progress"] = Motion.EaseOutCubic((float)t);
            }
            float[] data = Program ?? Encoder.Encode(Component, options).ToArray();
            target?.Dispose();
            target = DitherRenderer.Render(sender.Device, data, w, h);
            GridWidth = w; GridHeight = h; dirty = false;
        }
        // Win2D coordinates are DIPs. Its drawing-session DPI performs the one
        // required conversion to device pixels; multiplying by DPI here would double it.
        double cw = Component == "avatar" ? Math.Min(sender.ActualWidth, sender.ActualHeight) / 8 : Cell;
        args.DrawingSession.DrawImage(target, new Rect(0, 0, w * cw, h * cw),
            new Rect(0, 0, w, h), 1, CanvasImageInterpolation.NearestNeighbor);
    }
}
