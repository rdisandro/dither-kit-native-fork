// Port of rust/dither-gtk/shaders/dither.glsl and rust/dither-ir/src/vm.rs.
// Colors remain byte-valued straight alpha until Execute; bloom only reevaluates
// the BASE list and visits each Chebyshev ring in reference iteration order.
using ComputeSharp;
using ComputeSharp.D2D1;
namespace DitherKit.WinUI.Shaders;

[D2DInputCount(0)]
[D2DRequiresScenePosition]
#if DITHER_RUNTIME_SHADER
[D2DEnableRuntimeCompilation]
#else
[D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
#endif
// This large interpreter contains bounded VM/gamut loops nested inside bloom.
// Long walks expose their data-dependent exit in the loop header for FXC.
// Skip FXC optimization for this interpreter and omit optional linking, which
// compiles the whole shader twice.
// Keep IEEE-strict arithmetic and normal validation; signed division/modulo is
// intentional for parity, so do not enable /WX for FXC's X3556 performance hints.
// Revisit optimization/linking only with measured build, render and golden results.
[D2DCompileOptions(D2D1CompileOptions.SkipOptimization | D2D1CompileOptions.IeeeStrictness | D2D1CompileOptions.PackMatrixRowMajor)]
[D2DGeneratedPixelShaderDescriptor]
public readonly partial struct DitherShader : ID2D1PixelShader
{
    [D2DResourceTextureIndex(0)]
    private readonly D2D1ResourceTexture1D<float> data;
    private readonly int width;
    private readonly int height;
    private readonly int straight;
    private readonly int programSplit;

    public DitherShader(int width, int height, bool straightAlpha)
        : this(width, height, straightAlpha, 0) { }

    public DitherShader(int width, int height, bool straightAlpha, int programSplit)
    {
        this.data = default;
        this.width = width;
        this.height = height;
        this.straight = straightAlpha ? 1 : 0;
        this.programSplit = programSplit;
    }

    public float4 Execute()
    {
        int2 p = (int2)D2D.GetScenePosition().XY;
        if (p.X < 0 || p.Y < 0 || p.X >= width || p.Y >= height) return new float4(0f, 0f, 0f, 0f);
        float4 c = postPixel(p.X, p.Y);
        if (c.W < 0f) return new float4(0f, 0f, 0f, 0f);
        float alpha = c.W / 255f;
        return straight != 0 ? c / 255f : new float4(c.RGB / 255f * alpha, alpha);
    }
private const float TAU = 6.28318530717958647692f;
private const float PI = 3.14159265358979323846f;
// Future-Retro field units per grid cell (3-point cells, 160 points per unit).
private const float LIGHT_SCALE = 0.01875f;
private const float NONE = -1.0e9f;
private static float4 Empty() { return new float4(0f, 0f, 0f, -1f); }

private const int OP_LINE = 1;
private const int OP_DOT = 2;
private const int OP_AREA_BODY = 3;
private const int OP_AREA_STROKE = 4;
private const int OP_BAR = 5;
private const int OP_PIE = 6;
private const int OP_POLY = 7;
private const int OP_GRADIENT = 8;
private const int OP_BACKGROUND = 9;
private const int OP_BUTTON = 10;
private const int OP_METER = 11;
private const int OP_AVATAR = 12;
private const int OP_SWATCH = 13;
private const int OP_BLOOM = 14;

private static int BAYER2(int i) { if (i == 0) return 0; if (i == 1) return 2; if (i == 2) return 3; return 1; }
private static int BAYER4(int i) { if (i == 0) return 0; if (i == 1) return 8; if (i == 2) return 2; if (i == 3) return 10; if (i == 4) return 12; if (i == 5) return 4; if (i == 6) return 14; if (i == 7) return 6; if (i == 8) return 3; if (i == 9) return 11; if (i == 10) return 1; if (i == 11) return 9; if (i == 12) return 15; if (i == 13) return 7; if (i == 14) return 13; return 5; }
private static int BAYER8(int i) { if (i == 0) return 0; if (i == 1) return 32; if (i == 2) return 8; if (i == 3) return 40; if (i == 4) return 2; if (i == 5) return 34; if (i == 6) return 10; if (i == 7) return 42; if (i == 8) return 48; if (i == 9) return 16; if (i == 10) return 56; if (i == 11) return 24; if (i == 12) return 50; if (i == 13) return 18; if (i == 14) return 58; if (i == 15) return 26; if (i == 16) return 12; if (i == 17) return 44; if (i == 18) return 4; if (i == 19) return 36; if (i == 20) return 14; if (i == 21) return 46; if (i == 22) return 6; if (i == 23) return 38; if (i == 24) return 60; if (i == 25) return 28; if (i == 26) return 52; if (i == 27) return 20; if (i == 28) return 62; if (i == 29) return 30; if (i == 30) return 54; if (i == 31) return 22; if (i == 32) return 3; if (i == 33) return 35; if (i == 34) return 11; if (i == 35) return 43; if (i == 36) return 1; if (i == 37) return 33; if (i == 38) return 9; if (i == 39) return 41; if (i == 40) return 51; if (i == 41) return 19; if (i == 42) return 59; if (i == 43) return 27; if (i == 44) return 49; if (i == 45) return 17; if (i == 46) return 57; if (i == 47) return 25; if (i == 48) return 15; if (i == 49) return 47; if (i == 50) return 7; if (i == 51) return 39; if (i == 52) return 13; if (i == 53) return 45; if (i == 54) return 5; if (i == 55) return 37; if (i == 56) return 63; if (i == 57) return 31; if (i == 58) return 55; if (i == 59) return 23; if (i == 60) return 61; if (i == 61) return 29; if (i == 62) return 53; return 21; }
// Future-Retro ramps (nearai/future-retro brand/tokens/tokens.css).
private static float4 Slate() { return new float4(38f, 38f, 43f, 255f); }
private static float4 Marble() { return new float4(250f, 250f, 247f, 255f); }
private static float4 NearBlueInk() { return new float4(3f, 92f, 163f, 255f); }
private static float4 Faint() { return new float4(193f, 193f, 193f, 23f); }
private static float4 rampColor(int id, int step) {
    if (step < 1 || step > 4) return Slate();
    if (id == 2) {
        if (step == 1) return Slate();
        if (step == 2) return new float4(128f, 128f, 136f, 255f);
        if (step == 3) return new float4(193f, 193f, 193f, 255f);
        return Marble();
    }
    if (step == 1) return NearBlueInk();
    if (step == 2) return new float4(0f, 145f, 253f, 255f);
    if (step == 3) return new float4(131f, 220f, 255f, 255f);
    return Marble();
}
private static int HALFTONE4(int i) { if (i == 0) return 12; if (i == 1) return 5; if (i == 2) return 6; if (i == 3) return 13; if (i == 4) return 4; if (i == 5) return 0; if (i == 6) return 1; if (i == 7) return 7; if (i == 8) return 11; if (i == 9) return 3; if (i == 10) return 2; if (i == 11) return 8; if (i == 12) return 15; if (i == 13) return 10; if (i == 14) return 9; return 14; }

private float D(int i) { return data[i]; }
private int DI(int i) { return (int)(D(i)); }

private int emod(int a, int b) { int r = a % b; return r < 0 ? r + b : r; }
private float rnd(float v) { return Hlsl.Sign(v) * Hlsl.Floor(Hlsl.Abs(v) + 0.5f); }

// ---- dithering --------------------------------------------------------------

private uint hash2(int x, int y) {
    uint h = (Hlsl.AsUInt(x) * 0x9E3779B1u) ^ (Hlsl.AsUInt(y) * 0x85EBCA77u);
    h ^= h >> 15;
    h *= 0x2C1B3C6Du;
    h ^= h >> 12;
    h *= 0x297A2D39u;
    return h ^ (h >> 15);
}

private float hash01(int x, int y) { return (float)(hash2(x, y)) / 4294967296.0f; }

private float threshold(int m, int x, int y) {
    int xu = emod(x, 8), yu = emod(y, 8);
    if (m == 0) return ((float)(BAYER2((yu % 2) * 2 + xu % 2)) + 0.5f) / 4.0f;
    if (m == 1) return ((float)(BAYER4((yu % 4) * 4 + xu % 4)) + 0.5f) / 16.0f;
    if (m == 3) return ((float)(HALFTONE4((yu % 4) * 4 + xu % 4)) + 0.5f) / 16.0f;
    if (m == 4) return ((float)(hash2(x, y)) + 0.5f) / 4294967296.0f;
    return ((float)(BAYER8(yu * 8 + xu)) + 0.5f) / 64.0f;
}

private bool on(int m, int x, int y, float i) { return i > threshold(m, x, y); }

// Coverage: -1 when uncovered, else the pixel's rank among covered pixels.
private float cover(int fill, int x, int y, float i) {
    if (fill == 1) {
        if (emod(x, 2) == 0 && emod(y + emod(x / 2, 2), 2) == 0) {
            float t1 = threshold(1, x / 2, y / 2), k1 = i * 1.4f;
            if (k1 > t1) return t1 / k1;
        }
        return -1.0f;
    }
    if (fill == 2) {
        float t2 = threshold(1, x, y), k2 = 0.25f + i;
        return (emod(x + y, 3) == 0 && k2 > t2) ? t2 / k2 : -1.0f;
    }
    if (fill == 3) return i > 0.02f ? threshold(1, x, y) : -1.0f;
    if (fill == 4) return (emod(x, 2) == 0 && emod(y, 2) == 0 && i > 0.02f) ? threshold(1, x / 2, y / 2) : -1.0f;
    float t = threshold(2, x, y);
    return i > t ? t / i : -1.0f;
}

private bool covers(int fill, int x, int y, float i) { return cover(fill, x, y, i) >= 0.0f; }

// ---- color ------------------------------------------------------------------

private float enc(float v) {
    v = Hlsl.Clamp(v, 0.0f, 1.0f);
    float s = v <= 0.0031308f ? 12.92f * v : 1.055f * Hlsl.Pow(v, 1.0f / 2.4f) - 0.055f;
    return rnd(s * 255.0f);
}

private bool oklch_rgb(float l, float c, float cos_h, float sin_h, out float4 outc) {
    outc = default;
    float a = c * cos_h, b = c * sin_h;
    float l_ = l + 0.39633778f * a + 0.21580376f * b;
    float m_ = l - 0.105561346f * a - 0.06385417f * b;
    float s_ = l - 0.08948418f * a - 1.2914855f * b;
    float l3 = l_ * l_ * l_, m3 = m_ * m_ * m_, s3 = s_ * s_ * s_;
    float r = 4.0767417f * l3 - 3.3077116f * m3 + 0.23096994f * s3;
    float g = -1.268438f * l3 + 2.6097574f * m3 - 0.34131938f * s3;
    float bb = -0.0041960863f * l3 - 0.7034186f * m3 + 1.7076147f * s3;
    const float eps = 1e-4f;
    if (r < -eps || r > 1.0f + eps || g < -eps || g > 1.0f + eps || bb < -eps || bb > 1.0f + eps) return false;
    outc = new float4(enc(r), enc(g), enc(bb), 255.0f);
    return true;
}

private float4 shade(float hue, float chroma, float level) {
    level = Hlsl.Clamp(level, 0.0f, 1.0f);
    float l = 0.22f + level * 0.72f;
    float c = Hlsl.Max(chroma * Hlsl.Clamp(1.0f - Hlsl.Abs(level - 0.55f) * 1.3f, 0.25f, 1.0f), 0.0f);
    // FXC /Od cannot be relied on to lift these invariant transcendentals.
    float hr = Hlsl.Radians(hue);
    float cos_h = Hlsl.Cos(hr), sin_h = Hlsl.Sin(hr);
    float4 o;
    int k = 0;
    while (!(c < 0.0f) && k < 200) {
        if (oklch_rgb(l, c, cos_h, sin_h, out o)) return o;
        c -= 0.005f;
        if (c <= 0.0f) break;
        k++;
    }
    if (oklch_rgb(l, 0.0f, cos_h, sin_h, out o)) return o;
    return new float4(0.0f, 0.0f, 0.0f, 255.0f);
}

private float4 fade(float4 c, float f) { return new float4(c.RGB, rnd(c.W * Hlsl.Clamp(f, 0.0f, 1.0f))); }

// Upstream tones ignore u; a ramp (negative chroma) dithers between steps.
private float4 shadeAt(float hue, float chroma, float level, float u) {
    if (chroma >= 0.0f) return shade(hue, chroma, level);
    float p = Hlsl.Clamp(1.0f + (level - 0.3f) * 4.0f, 1.0f, 4.0f);
    float s = Hlsl.Floor(p);
    int step = (int)(s) + (u < p - s ? 1 : 0);
    return rampColor((int)(-chroma), Hlsl.Min(step, 4));
}

// 12% white mixed in: the Future-Retro hover fill.
private float4 lift(float4 c) {
    return new float4(rnd(c.X + (255.0f - c.X) * 0.12f), rnd(c.Y + (255.0f - c.Y) * 0.12f), rnd(c.Z + (255.0f - c.Z) * 0.12f), rnd(c.W + (255.0f - c.W) * 0.12f));
}

private float4 colorAt(int i) { return new float4(D(i), D(i + 1), D(i + 2), D(i + 3)); }

// ---- geometry helpers ---------------------------------------------------------

// Step index of (px, py) on the Bresenham line a -> b, or -1.
private int lineStep(int x0, int y0, int x1, int y1, int px, int py) {
    int adx = Hlsl.Abs(x1 - x0), ady = Hlsl.Abs(y1 - y0);
    int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    if (adx >= ady) {
        int k = (px - x0) * sx;
        if (k < 0 || k > adx) return -1;
        int m = adx == 0 ? 0 : (2 * k * ady + adx) / (2 * adx);
        return py == y0 + sy * m ? k : -1;
    }
    int ky = (py - y0) * sy;
    if (ky < 0 || ky > ady) return -1;
    int my = (2 * ky * adx + ady) / (2 * ady);
    return px == x0 + sx * my ? ky : -1;
}

private float smooth01(float a, float b, float x) {
    float t = Hlsl.Clamp((x - a) / (b - a), 0.0f, 1.0f);
    return t * t * (3.0f - 2.0f * t);
}

private float valueNoise(float x, float y) {
    float fx0 = Hlsl.Floor(x), fy0 = Hlsl.Floor(y);
    int xi = (int)(fx0), yi = (int)(fy0);
    float fx = x - fx0, fy = y - fy0;
    float sx = fx * fx * (3.0f - 2.0f * fx);
    float sy = fy * fy * (3.0f - 2.0f * fy);
    float n00 = hash01(xi, yi), n10 = hash01(xi + 1, yi);
    float n01 = hash01(xi, yi + 1), n11 = hash01(xi + 1, yi + 1);
    float top = n00 + (n10 - n00) * sx;
    float bot = n01 + (n11 - n01) * sx;
    return top + (bot - top) * sy;
}

private float fbm(float x, float y, int octaves) {
    float s = 0.0f, amp = 0.5f, f = 1.0f, n = 0.0f;
    for (int k = 0; k < 4; k++) {
        if (k >= octaves) break;
        s += amp * valueNoise(x * f, y * f);
        n += amp;
        amp *= 0.5f;
        f *= 2.03f;
    }
    return s / n;
}

// Future-Retro fields: 5 clouds, 6 marble, 7 rings, 8 caustics.
private float lightField(int f, int x, int y, int w, int h, float t) {
    float fx = (float)(x) * LIGHT_SCALE, fy = (float)(y) * LIGHT_SCALE;
    float v;
    if (f == 6) {
        float n6 = fbm(fx * 0.8f + t * 0.06f, fy * 0.8f - t * 0.03f, 4);
        v = 0.5f + 0.42f * Hlsl.Sin((fx * 0.8f + fy * 0.35f + n6 * 4.2f + t * 0.35f) * PI);
    } else if (f == 7) {
        float dx = ((float)(x) - (float)(w) * 0.5f) * LIGHT_SCALE;
        float dy = ((float)(y) - (float)(h) * 0.5f) * LIGHT_SCALE;
        float d = Hlsl.Sqrt(dx * dx + dy * dy);
        v = 0.5f + 0.45f * Hlsl.Sin(d * 7.0f - t * 2.2f + fbm(fx, fy, 2) * 3.2f);
    } else if (f == 8) {
        float n8 = fbm(fx * 0.9f + t * 0.12f, fy * 0.9f - t * 0.08f, 3);
        float sa = Hlsl.Abs(Hlsl.Sin((fx * 1.3f + n8 * 3.1f + t * 0.4f) * PI));
        float sb = Hlsl.Abs(Hlsl.Sin((fy * 1.1f - n8 * 2.7f - t * 0.3f) * PI));
        float k = 1.0f - sa * sb;
        float k2 = k * k;
        v = 0.12f + k2 * k2 * k2 * k * 0.95f;
    } else {
        v = fbm(fx + t * 0.31f, fy - t * 0.17f, 3) * 0.7f + valueNoise(fx * 2.1f - t * 0.23f, fy * 2.1f + t * 0.19f) * 0.3f;
        v = 0.5f + (v - 0.5f) * 1.9f;
    }
    return Hlsl.Clamp((0.5f + (v - 0.5f) * 1.3f) * 1.25f, 0.0f, 1.0f);
}

private float field(int f, float u, float v, float t) {
    float s;
    if (f == 1) {
        float crest = 0.5f + 0.18f * Hlsl.Sin(u * 6.0f + t * 1.1f) + 0.08f * Hlsl.Sin(u * 15.0f - t * 1.7f);
        s = Hlsl.Clamp(1.0f - Hlsl.Abs(v - crest) * 2.6f, 0.0f, 1.0f) * 0.9f + v * 0.15f;
    } else if (f == 2) {
        float d = Hlsl.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
        s = 0.5f + 0.5f * Hlsl.Sin(d * 30.0f - t * 3.0f) * Hlsl.Max(1.0f - d * 1.2f, 0.0f);
    } else if (f == 3) {
        s = valueNoise(u * 6.0f + t * 0.35f, v * 4.0f - t * 0.2f) * 0.7f + valueNoise(u * 13.0f - t * 0.5f, v * 9.0f) * 0.3f;
    } else if (f == 4) {
        float curtain = valueNoise(u * 5.0f + t * 0.25f, t * 0.15f) * 0.6f + 0.4f * Hlsl.Abs(Hlsl.Sin(u * 9.0f + t * 0.6f));
        float fall = Hlsl.Clamp(1.0f - Hlsl.Max(v - 0.35f * curtain, 0.0f) * 1.6f, 0.0f, 1.0f);
        s = curtain * fall;
    } else {
        float a = Hlsl.Sin(u * 7.0f + t * 0.9f);
        float b = Hlsl.Sin(v * 6.0f - t * 0.7f);
        float c = Hlsl.Sin((u + v) * 5.0f + t * 0.5f);
        float d = Hlsl.Sin(Hlsl.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 12.0f - t * 1.2f);
        s = (a + b + c + d) / 8.0f + 0.5f;
    }
    return Hlsl.Clamp(s, 0.0f, 1.0f);
}

private float direction(int d, int x, int y, int w, int h) {
    float fx = w > 1 ? (float)(x) / (float)(w - 1) : 0.5f;
    float fy = h > 1 ? (float)(y) / (float)(h - 1) : 0.5f;
    if (d == 1) return fy;
    if (d == 2) return 1.0f - fx;
    if (d == 3) return fx;
    if (d == 4) {
        float dx = fx - 0.5f, dy = fy - 0.5f;
        return Hlsl.Min(Hlsl.Sqrt(dx * dx + dy * dy) / 0.70710678118654752f, 1.0f);
    }
    return 1.0f - fy;
}

// ---- ops ------------------------------------------------------------------

private float4 evalOp(int i, int tb, int x, int y, float4 cur) {
    int code = DI(i);
    int a = i + 2; // first argument
    if (code == OP_LINE) {
        int k = lineStep(DI(a), DI(a + 1), DI(a + 2), DI(a + 3), x, y);
        if (k < 0) return Empty();
        int dash = DI(a + 4);
        return (dash <= 1 || k % dash == 0) ? colorAt(a + 5) : Empty();
    }
    if (code == OP_DOT) {
        int dx = x - DI(a), dy = y - DI(a + 1);
        if (Hlsl.Abs(dx) > 1 || Hlsl.Abs(dy) > 1) return Empty();
        return (dx == 0 && dy == 0) ? colorAt(a + 6) : colorAt(a + 2);
    }
    if (code == OP_AREA_BODY) {
        int off = DI(a), w = DI(a + 1), h = DI(a + 2);
        if (x >= w) return Empty();
        float top = D(tb + off + x * 2);
        float bottom = D(tb + off + x * 2 + 1);
        if (top == NONE) return Empty();
        if (y < (int)(Hlsl.Ceil(top)) + 1 || y > (int)(bottom)) return Empty();
        float span = (float)(h - 1) - top;
        float t = 1.0f - ((float)(y) - top) / Hlsl.Max(span, 1.0f);
        float u = cover(DI(a + 3), x, y, (0.1f + 0.9f * t) * D(a + 6));
        if (u < 0.0f) return Empty();
        return fade(shadeAt(D(a + 4), D(a + 5), 0.32f + 0.4f * t, u), D(a + 7));
    }
    if (code == OP_AREA_STROKE) {
        int off = DI(a), w = DI(a + 1);
        if (x >= w) return Empty();
        float top = D(tb + off + x * 2);
        if (top == NONE) return Empty();
        int ty = (int)(rnd(top));
        int from = ty;
        if (x > 0) {
            float prev = D(tb + off + (x - 1) * 2);
            if (prev != NONE) from = (int)(rnd(prev));
        }
        return (y >= Hlsl.Min(from, ty) && y <= Hlsl.Max(from, ty)) ? colorAt(a + 2) : Empty();
    }
    if (code == OP_BAR) {
        int bx0 = DI(a), bx1 = DI(a + 1), bottom = DI(a + 3), h = DI(a + 4);
        float top = D(a + 2);
        int ty = (int)(rnd(top));
        if (x < bx0 || x > Hlsl.Max(bx1, bx0) || y > bottom) return Empty();
        if (DI(a + 5) == 4) {
            // Dot matrix: lit from the axis, unlit dots a faint grid above.
            if (emod(x - bx0, 2) != 0 || emod(h - 1 - y, 2) != 0) return Empty();
            if (y < ty) return Faint();
            float sm = (float)(bottom - y + 1) / (float)(Hlsl.Max(bottom - ty + 1, 1));
            float um = threshold(1, (x - bx0) / 2, (h - 1 - y) / 2);
            return fade(shadeAt(D(a + 6), D(a + 7), 0.3f + 0.55f * sm, um), D(a + 8));
        }
        if (y < ty) return Empty();
        float t = 1.0f - ((float)(y) - top) / Hlsl.Max((float)(h - 1) - top, 1.0f);
        float u = y == ty ? threshold(1, x, y) : cover(DI(a + 5), x, y, 0.3f + 0.7f * t);
        if (u < 0.0f) return Empty();
        float level = y == ty ? 0.82f : 0.32f + 0.38f * t;
        return fade(shadeAt(D(a + 6), D(a + 7), level, u), D(a + 8));
    }
    if (code == OP_PIE) {
        float cx = D(a), cy = D(a + 1), radius = D(a + 2), inner = D(a + 3);
        bool multi = DI(a + 4) != 0;
        int hover = DI(a + 5), n = DI(a + 6), off = DI(a + 7);
        float dx = (float)(x) + 0.5f - cx, dy = (float)(y) + 0.5f - cy;
        float r = Hlsl.Sqrt(dx * dx + dy * dy);
        float ang = Hlsl.Atan2(dx, -dy);
        ang = ang - TAU * Hlsl.Floor(ang / TAU);
        int idx = -1;
        for (int s = 0; s < n; s++) {
            if (ang >= D(tb + off + s * 5) && ang < D(tb + off + s * 5 + 1)) { idx = s; break; }
        }
        if (idx < 0) return Empty();
        int row = tb + off + idx * 5;
        float outer = radius - 2.0f + (hover == idx ? 2.0f : 0.0f);
        if (r < inner || r > outer) return Empty();
        float a0 = D(row), a1 = D(row + 1);
        if (multi && Hlsl.Min(ang - a0, a1 - ang) * r < 0.9f) return Empty();
        float dim = (hover >= 0 && hover != idx) ? 0.5f : 1.0f;
        float t = Hlsl.Clamp((r - inner) / Hlsl.Max(outer - inner, 1.0f), 0.0f, 1.0f);
        bool rim = outer - r < 1.2f;
        float u = rim ? threshold(1, x, y) : cover(DI(row + 4), x, y, 0.3f + 0.7f * t);
        if (u >= 0.0f) {
            return fade(shadeAt(D(row + 2), D(row + 3), rim ? 0.8f : 0.3f + 0.4f * t, u), dim);
        }
        return Empty();
    }
    if (code == OP_POLY) {
        float cx = D(a), cy = D(a + 1), radius = D(a + 2);
        int n = DI(a + 7), off = DI(a + 8);
        float px = (float)(x) + 0.5f, py = (float)(y) + 0.5f;
        bool inside = false;
        int j = n - 1;
        for (int k = 0; k < n; k++) {
            float xi = D(tb + off + k * 2), yi = D(tb + off + k * 2 + 1);
            float xj = D(tb + off + j * 2), yj = D(tb + off + j * 2 + 1);
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
            j = k;
        }
        if (!inside) return Empty();
        float d = Hlsl.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) / radius;
        float u = cover(DI(a + 3), x, y, 0.2f + 0.55f * d);
        if (u < 0.0f) return Empty();
        return fade(shadeAt(D(a + 4), D(a + 5), 0.32f + 0.35f * d, u), D(a + 6));
    }
    if (code == OP_GRADIENT) {
        int w = DI(a), h = DI(a + 1), dir = DI(a + 2), m = DI(a + 3);
        float density = D(a + 4);
        if (x >= w || y >= h) return Empty();
        float pos = direction(dir, x, y, w, h);
        if (DI(a + 7) != 0) {
            bool second = on(m, x, y, pos);
            float th = second ? D(a + 8) : D(a + 5);
            float tc = second ? D(a + 9) : D(a + 6);
            float t2 = threshold(m, x + 3, y + 5), k2 = density * (0.55f + 0.45f * pos);
            return k2 > t2 ? shadeAt(th, tc, 0.35f + 0.35f * pos, t2 / k2) : Empty();
        }
        float t = threshold(m, x, y), k = (1.0f - pos) * density;
        return k > t ? shadeAt(D(a + 5), D(a + 6), 0.3f + 0.4f * (1.0f - pos), t / k) : Empty();
    }
    if (code == OP_BACKGROUND) {
        int w = DI(a), h = DI(a + 1), f = DI(a + 2), m = DI(a + 3);
        float density = D(a + 4), time = D(a + 5);
        if (x >= w || y >= h) return Empty();
        float it;
        if (f >= 5) {
            it = lightField(f, x, y, w, h, time);
        } else {
            float u = (float)(x) / (float)(Hlsl.Max(w, 1)), v = (float)(y) / (float)(Hlsl.Max(h, 1));
            it = smooth01(0.18f, 0.82f, field(f, u, v, time));
        }
        float t = threshold(m, x, y), k = it * density;
        if (!(k > t)) return Empty();
        bool accent = DI(a + 8) != 0 && on(1, x + 1, y + 2, (it - 0.62f) * 2.2f);
        float level = f >= 5 ? 0.3f + 0.42f * it : 0.3f + 0.5f * it;
        return accent ? shadeAt(D(a + 9), D(a + 10), level, t / k) : shadeAt(D(a + 6), D(a + 7), level, t / k);
    }
    if (code == OP_BUTTON) {
        int w = DI(a), h = DI(a + 1), style = DI(a + 2);
        float density = D(a + 3), time = D(a + 4), th = D(a + 5), tc = D(a + 6);
        if (x >= w || y >= h) return Empty();
        bool edge = x == 0 || y == 0 || x + 1 == w || y + 1 == h;
        if (tc < 0.0f) {
            // Future-Retro buttons are flat: no texture, checker or sparkles.
            float4 baseColor = rampColor((int)(-tc), 2);
            if (style == 0) return density > 0.4f ? lift(baseColor) : baseColor;
            if (style == 1) return (edge || density > 0.0f) ? Marble() : Empty();
            return Empty();
        }
        float4 c = Empty();
        if (edge && style != 2) {
            if ((x + y) % 2 == 0 || style == 0) c = shade(th, tc, 0.6f);
        } else {
            float fy = h > 1 ? (float)(y) / (float)(h - 1) : 0.5f;
            float it = density * (1.05f - 0.45f * fy);
            if (covers(0, x, y, it)) c = shade(th, tc, 0.45f + 0.25f * (1.0f - fy));
        }
        if (density > 0.4f && hash01(x, y ^ 0x5bd1) < 0.04f * density && (c.W >= 0.0f || cur.W >= 0.0f)) {
            float phase = hash01(y, x ^ 0x1b56) * TAU;
            float speed = 1.5f + 2.5f * hash01(x ^ 0x77, y);
            float s = Hlsl.Max(Hlsl.Sin(time * speed + phase), 0.0f);
            float s2 = s * s, s4 = s2 * s2, s8 = s4 * s4;
            float wink = s8 * s4;
            if (wink > 0.35f) c = fade(shade(th, tc, 0.97f), wink);
        }
        return c;
    }
    if (code == OP_METER) {
        int w = DI(a), h = DI(a + 1), fill = DI(a + 3);
        float value = D(a + 2);
        if (x >= w || y >= h) return Empty();
        int filled = (int)(rnd(Hlsl.Clamp(value, 0.0f, 1.0f) * (float)(w)));
        float th = D(a + 4), tc = D(a + 5);
        if (fill == 4) {
            if (emod(x, 2) != 0 || emod(y, 2) != 0) return Empty();
            if (x < filled) {
                float sm = (float)(x + 1) / (float)(Hlsl.Max(filled, 1));
                return shadeAt(th, tc, 0.3f + 0.55f * sm, threshold(1, x / 2, y / 2));
            }
            return Faint();
        }
        if (x < filled) {
            float it = 0.35f + 0.65f * ((float)(x) + 1.0f) / (float)(Hlsl.Max(filled, 1));
            float u = cover(fill, x, y, it);
            if (u >= 0.0f || x + 1 == filled) return shadeAt(th, tc, 0.4f + 0.35f * it, u >= 0.0f ? u : threshold(1, x, y));
            return Empty();
        }
        if (tc < 0.0f) return (emod(x, 2) == 0 && emod(y, 2) == 0) ? Faint() : Empty();
        if ((x + y) % 4 == 0) return fade(shade(260.0f, 0.012f, 0.35f), 0.6f);
        return Empty();
    }
    if (code == OP_AVATAR) {
        if (x < 0 || x > 7 || y < 0 || y > 7) return Empty();
        uint bits = (uint)(DI(a)) | ((uint)(DI(a + 1)) << 16);
        bool mirrorX = DI(a + 2) != 0;
        uint acc = (uint)(DI(a + 4)) | ((uint)(DI(a + 5)) << 16);
        int ai = mirrorX ? y * 4 + (x < 4 ? x : 7 - x) : (y < 4 ? y : 7 - y) * 8 + x;
        bool lit = ((bits >> ai) & 1u) == 1u;
        bool accent = lit && ((acc >> (ai % 23)) & 1u) == 1u && ((acc >> ((ai + 7) % 23)) & 1u) == 1u;
        float hue = D(a + 3);
        if (hue < 0.0f) {
            int id = (int)(-hue);
            return accent ? rampColor(id, 3) : (lit ? rampColor(id, 2) : Slate());
        }
        return shade(hue, 0.19f, accent ? 0.92f : (lit ? 0.62f : 0.16f));
    }
    if (code == OP_SWATCH) {
        if (D(a + 1) < 0.0f) return shadeAt(D(a), D(a + 1), 0.6f, threshold(1, x, y));
        return shade(D(a), D(a + 1), (x + y) % 2 == 0 ? 0.72f : 0.48f);
    }
    return Empty();
}

private float4 evalList(int start, int end, int x, int y, float4 cur) {
    int tb = 1 + DI(0);
    int i = start;
    int guard = 0;
    while (i < end && guard < 65536) {
        if (DI(i) == OP_BLOOM) break;
        float4 c = evalOp(i, tb, x, y, cur);
        if (c.W >= 0.0f) cur = c;
        i += DI(i + 1);
        guard++;
    }
    return cur;
}

private int bloomIndex() {
    if (programSplit > 0) return programSplit;
    int end = 1 + DI(0);
    int i = 1;
    int guard = 0;
    while (i < end && guard < 65536) {
        if (DI(i) == OP_BLOOM) return i;
        i += DI(i + 1);
        guard++;
    }
    return end;
}

private float4 lookupBase(int x, int y) {
    if (x < 0 || y < 0 || x >= width || y >= height) return Empty();
    float4 c = evalList(1, bloomIndex(), x, y, Empty());
    // The reference base image stores zero-alpha pixels as transparent black.
    return c.W > 0f ? c : Empty();
}

private float4 postPixel(int x, int y) {
    int end = 1 + DI(0);
    int b = bloomIndex();
    float4 cur = lookupBase(x, y);
    if (b < end) {
        int radius = DI(b + 2);
        float strength = D(b + 3);
        bool rampGlow = DI(b + 1) >= 5 && D(b + 4) == 1.0f;
        if (cur.W < 0.0f && radius > 0) {
            bool done = false;
            for (int d = 1; d <= Hlsl.Min(radius, 4); d++) {
                if (done) break;
                for (int oy = -d; oy <= d; oy++) {
                    if (done) break;
                    for (int ox = -d; ox <= d; ox++) {
                        if (Hlsl.Abs(ox) != d && Hlsl.Abs(oy) != d) continue;
                        float4 c = lookupBase(x + ox, y + oy);
                        if (c.W > 160.0f) {
                            float glow = strength * (1.0f - ((float)(d) - 1.0f) / (float)(radius));
                            if (on(1, x, y, glow)) cur = rampGlow ? NearBlueInk() : fade(c, 0.35f + 0.25f * glow);
                            done = true;
                            break;
                        }
                    }
                }
            }
        }
        cur = evalList(b + DI(b + 1), end, x, y, cur);
    }
    return cur;
}


}
