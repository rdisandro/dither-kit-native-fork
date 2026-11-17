// dither-kit-native: GLSL interpreter for display-list programs.
//
// A line-by-line port of rust/dither-ir/src/vm.rs (the reference). The host
// prepends a `#version` line (330 core or 300 es) before compiling.
//
// Pass 0 (base): paints every op before BLOOM into a w x h RGBA8 target,
//   straight alpha; unlit pixels are (0,0,0,0).
// Pass 1 (post): reads the base target, applies bloom, paints the overlay
//   ops after BLOOM, and writes premultiplied alpha for compositing.
//
// Colors are vec4 in 0..255 (exact integers, like the reference's u8s);
// alpha < 0 means "no pixel".

#ifdef GL_ES
precision highp float;
precision highp int;
precision highp sampler2D;
#endif

uniform sampler2D uData; // R32F, DATA_W texels per row
uniform sampler2D uBase; // pass 1 only
uniform int uPass;
uniform ivec2 uGrid;
uniform int uStraight; // pass 1: 1 = straight alpha (tests), 0 = premultiplied

out vec4 fragColor;

const int DATA_W = 1024;
const float TAU = 6.28318530717958647692;
const float PI = 3.14159265358979323846;
// Future-Retro field units per grid cell (3-point cells, 160 points per unit).
const float LIGHT_SCALE = 0.01875;
const float NONE = -1.0e9;
const vec4 EMPTY = vec4(0.0, 0.0, 0.0, -1.0);

const int OP_LINE = 1;
const int OP_DOT = 2;
const int OP_AREA_BODY = 3;
const int OP_AREA_STROKE = 4;
const int OP_BAR = 5;
const int OP_PIE = 6;
const int OP_POLY = 7;
const int OP_GRADIENT = 8;
const int OP_BACKGROUND = 9;
const int OP_BUTTON = 10;
const int OP_METER = 11;
const int OP_AVATAR = 12;
const int OP_SWATCH = 13;
const int OP_BLOOM = 14;

const int BAYER2[4] = int[4](0, 2, 3, 1);
const int BAYER4[16] = int[16](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
const int BAYER8[64] = int[64](
    0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20,
    62, 30, 54, 22, 3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39, 13, 45, 5, 37,
    63, 31, 55, 23, 61, 29, 53, 21);
const int HALFTONE4[16] = int[16](12, 5, 6, 13, 4, 0, 1, 7, 11, 3, 2, 8, 15, 10, 9, 14);

// Future-Retro ramps (nearai/future-retro brand/tokens/tokens.css).
const vec4 NEAR_RAMP[4] = vec4[4](vec4(3.0, 92.0, 163.0, 255.0), vec4(0.0, 145.0, 253.0, 255.0), vec4(131.0, 220.0, 255.0, 255.0), vec4(250.0, 250.0, 247.0, 255.0));
const vec4 INK_RAMP[4] = vec4[4](vec4(38.0, 38.0, 43.0, 255.0), vec4(128.0, 128.0, 136.0, 255.0), vec4(193.0, 193.0, 193.0, 255.0), vec4(250.0, 250.0, 247.0, 255.0));
const vec4 SLATE = vec4(38.0, 38.0, 43.0, 255.0);
const vec4 MARBLE = vec4(250.0, 250.0, 247.0, 255.0);
const vec4 NEAR_BLUE_INK = vec4(3.0, 92.0, 163.0, 255.0);
const vec4 FAINT = vec4(193.0, 193.0, 193.0, 23.0);

float D(int i) { return texelFetch(uData, ivec2(i % DATA_W, i / DATA_W), 0).r; }
int DI(int i) { return int(D(i)); }

int emod(int a, int b) { int r = a % b; return r < 0 ? r + b : r; }
float rnd(float v) { return sign(v) * floor(abs(v) + 0.5); }

// ---- dithering --------------------------------------------------------------

uint hash2(int x, int y) {
    uint h = (uint(x) * 0x9E3779B1u) ^ (uint(y) * 0x85EBCA77u);
    h ^= h >> 15u;
    h *= 0x2C1B3C6Du;
    h ^= h >> 12u;
    h *= 0x297A2D39u;
    return h ^ (h >> 15u);
}

float hash01(int x, int y) { return float(hash2(x, y)) / 4294967296.0; }

float threshold(int m, int x, int y) {
    int xu = emod(x, 8), yu = emod(y, 8);
    if (m == 0) return (float(BAYER2[(yu % 2) * 2 + xu % 2]) + 0.5) / 4.0;
    if (m == 1) return (float(BAYER4[(yu % 4) * 4 + xu % 4]) + 0.5) / 16.0;
    if (m == 3) return (float(HALFTONE4[(yu % 4) * 4 + xu % 4]) + 0.5) / 16.0;
    if (m == 4) return (float(hash2(x, y)) + 0.5) / 4294967296.0;
    return (float(BAYER8[yu * 8 + xu]) + 0.5) / 64.0;
}

bool on(int m, int x, int y, float i) { return i > threshold(m, x, y); }

// Coverage: -1 when uncovered, else the pixel's rank among covered pixels.
float cover(int fill, int x, int y, float i) {
    if (fill == 1) {
        if (emod(x, 2) == 0 && emod(y + emod(x / 2, 2), 2) == 0) {
            float t = threshold(1, x / 2, y / 2), k = i * 1.4;
            if (k > t) return t / k;
        }
        return -1.0;
    }
    if (fill == 2) {
        float t = threshold(1, x, y), k = 0.25 + i;
        return (emod(x + y, 3) == 0 && k > t) ? t / k : -1.0;
    }
    if (fill == 3) return i > 0.02 ? threshold(1, x, y) : -1.0;
    if (fill == 4) return (emod(x, 2) == 0 && emod(y, 2) == 0 && i > 0.02) ? threshold(1, x / 2, y / 2) : -1.0;
    float t = threshold(2, x, y);
    return i > t ? t / i : -1.0;
}

bool covers(int fill, int x, int y, float i) { return cover(fill, x, y, i) >= 0.0; }

// ---- color ------------------------------------------------------------------

float enc(float v) {
    v = clamp(v, 0.0, 1.0);
    float s = v <= 0.0031308 ? 12.92 * v : 1.055 * pow(v, 1.0 / 2.4) - 0.055;
    return rnd(s * 255.0);
}

bool oklch_rgb(float l, float c, float h, out vec4 outc) {
    float hr = radians(h);
    float a = c * cos(hr), b = c * sin(hr);
    float l_ = l + 0.39633778 * a + 0.21580376 * b;
    float m_ = l - 0.105561346 * a - 0.06385417 * b;
    float s_ = l - 0.08948418 * a - 1.2914855 * b;
    float l3 = l_ * l_ * l_, m3 = m_ * m_ * m_, s3 = s_ * s_ * s_;
    float r = 4.0767417 * l3 - 3.3077116 * m3 + 0.23096994 * s3;
    float g = -1.268438 * l3 + 2.6097574 * m3 - 0.34131938 * s3;
    float bb = -0.0041960863 * l3 - 0.7034186 * m3 + 1.7076147 * s3;
    const float eps = 1e-4;
    if (r < -eps || r > 1.0 + eps || g < -eps || g > 1.0 + eps || bb < -eps || bb > 1.0 + eps) return false;
    outc = vec4(enc(r), enc(g), enc(bb), 255.0);
    return true;
}

vec4 shade(float hue, float chroma, float level) {
    level = clamp(level, 0.0, 1.0);
    float l = 0.22 + level * 0.72;
    float c = max(chroma * clamp(1.0 - abs(level - 0.55) * 1.3, 0.25, 1.0), 0.0);
    vec4 o;
    for (int k = 0; k < 200; k++) {
        if (oklch_rgb(l, c, hue, o)) return o;
        c -= 0.005;
        if (c <= 0.0) break;
    }
    if (oklch_rgb(l, 0.0, hue, o)) return o;
    return vec4(0.0, 0.0, 0.0, 255.0);
}

vec4 fade(vec4 c, float f) { return vec4(c.rgb, rnd(c.a * clamp(f, 0.0, 1.0))); }

vec4 rampColor(int id, int step) {
    if (step < 1 || step > 4) return SLATE;
    return id == 2 ? INK_RAMP[step - 1] : NEAR_RAMP[step - 1];
}

// Upstream tones ignore u; a ramp (negative chroma) dithers between steps.
vec4 shadeAt(float hue, float chroma, float level, float u) {
    if (chroma >= 0.0) return shade(hue, chroma, level);
    float p = clamp(1.0 + (level - 0.3) * 4.0, 1.0, 4.0);
    float s = floor(p);
    int step = int(s) + (u < p - s ? 1 : 0);
    return rampColor(int(-chroma), min(step, 4));
}

// 12% white mixed in: the Future-Retro hover fill.
vec4 lift(vec4 c) { return vec4(rnd(c.r + (255.0 - c.r) * 0.12), rnd(c.g + (255.0 - c.g) * 0.12), rnd(c.b + (255.0 - c.b) * 0.12), rnd(c.a + (255.0 - c.a) * 0.12)); }

vec4 colorAt(int i) { return vec4(D(i), D(i + 1), D(i + 2), D(i + 3)); }

// ---- geometry helpers ---------------------------------------------------------

// Step index of (px, py) on the Bresenham line a -> b, or -1.
int lineStep(int x0, int y0, int x1, int y1, int px, int py) {
    int adx = abs(x1 - x0), ady = abs(y1 - y0);
    int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
    if (adx >= ady) {
        int k = (px - x0) * sx;
        if (k < 0 || k > adx) return -1;
        int m = adx == 0 ? 0 : (2 * k * ady + adx) / (2 * adx);
        return py == y0 + sy * m ? k : -1;
    }
    int k = (py - y0) * sy;
    if (k < 0 || k > ady) return -1;
    int m = (2 * k * adx + ady) / (2 * ady);
    return px == x0 + sx * m ? k : -1;
}

float smooth01(float a, float b, float x) {
    float t = clamp((x - a) / (b - a), 0.0, 1.0);
    return t * t * (3.0 - 2.0 * t);
}

float valueNoise(float x, float y) {
    float fx0 = floor(x), fy0 = floor(y);
    int xi = int(fx0), yi = int(fy0);
    float fx = x - fx0, fy = y - fy0;
    float sx = fx * fx * (3.0 - 2.0 * fx);
    float sy = fy * fy * (3.0 - 2.0 * fy);
    float n00 = hash01(xi, yi), n10 = hash01(xi + 1, yi);
    float n01 = hash01(xi, yi + 1), n11 = hash01(xi + 1, yi + 1);
    float top = n00 + (n10 - n00) * sx;
    float bot = n01 + (n11 - n01) * sx;
    return top + (bot - top) * sy;
}

float fbm(float x, float y, int octaves) {
    float s = 0.0, amp = 0.5, f = 1.0, n = 0.0;
    for (int k = 0; k < 4; k++) {
        if (k >= octaves) break;
        s += amp * valueNoise(x * f, y * f);
        n += amp;
        amp *= 0.5;
        f *= 2.03;
    }
    return s / n;
}

// Future-Retro fields: 5 clouds, 6 marble, 7 rings, 8 caustics.
float lightField(int f, int x, int y, int w, int h, float t) {
    float fx = float(x) * LIGHT_SCALE, fy = float(y) * LIGHT_SCALE;
    float v;
    if (f == 6) {
        float n = fbm(fx * 0.8 + t * 0.06, fy * 0.8 - t * 0.03, 4);
        v = 0.5 + 0.42 * sin((fx * 0.8 + fy * 0.35 + n * 4.2 + t * 0.35) * PI);
    } else if (f == 7) {
        float dx = (float(x) - float(w) * 0.5) * LIGHT_SCALE;
        float dy = (float(y) - float(h) * 0.5) * LIGHT_SCALE;
        float d = sqrt(dx * dx + dy * dy);
        v = 0.5 + 0.45 * sin(d * 7.0 - t * 2.2 + fbm(fx, fy, 2) * 3.2);
    } else if (f == 8) {
        float n = fbm(fx * 0.9 + t * 0.12, fy * 0.9 - t * 0.08, 3);
        float sa = abs(sin((fx * 1.3 + n * 3.1 + t * 0.4) * PI));
        float sb = abs(sin((fy * 1.1 - n * 2.7 - t * 0.3) * PI));
        float k = 1.0 - sa * sb;
        float k2 = k * k;
        v = 0.12 + k2 * k2 * k2 * k * 0.95;
    } else {
        v = fbm(fx + t * 0.31, fy - t * 0.17, 3) * 0.7 + valueNoise(fx * 2.1 - t * 0.23, fy * 2.1 + t * 0.19) * 0.3;
        v = 0.5 + (v - 0.5) * 1.9;
    }
    return clamp((0.5 + (v - 0.5) * 1.3) * 1.25, 0.0, 1.0);
}

float field(int f, float u, float v, float t) {
    float s;
    if (f == 1) {
        float crest = 0.5 + 0.18 * sin(u * 6.0 + t * 1.1) + 0.08 * sin(u * 15.0 - t * 1.7);
        s = clamp(1.0 - abs(v - crest) * 2.6, 0.0, 1.0) * 0.9 + v * 0.15;
    } else if (f == 2) {
        float d = sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5));
        s = 0.5 + 0.5 * sin(d * 30.0 - t * 3.0) * max(1.0 - d * 1.2, 0.0);
    } else if (f == 3) {
        s = valueNoise(u * 6.0 + t * 0.35, v * 4.0 - t * 0.2) * 0.7 + valueNoise(u * 13.0 - t * 0.5, v * 9.0) * 0.3;
    } else if (f == 4) {
        float curtain = valueNoise(u * 5.0 + t * 0.25, t * 0.15) * 0.6 + 0.4 * abs(sin(u * 9.0 + t * 0.6));
        float fall = clamp(1.0 - max(v - 0.35 * curtain, 0.0) * 1.6, 0.0, 1.0);
        s = curtain * fall;
    } else {
        float a = sin(u * 7.0 + t * 0.9);
        float b = sin(v * 6.0 - t * 0.7);
        float c = sin((u + v) * 5.0 + t * 0.5);
        float d = sin(sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)) * 12.0 - t * 1.2);
        s = (a + b + c + d) / 8.0 + 0.5;
    }
    return clamp(s, 0.0, 1.0);
}

float direction(int d, int x, int y, int w, int h) {
    float fx = w > 1 ? float(x) / float(w - 1) : 0.5;
    float fy = h > 1 ? float(y) / float(h - 1) : 0.5;
    if (d == 1) return fy;
    if (d == 2) return 1.0 - fx;
    if (d == 3) return fx;
    if (d == 4) {
        float dx = fx - 0.5, dy = fy - 0.5;
        return min(sqrt(dx * dx + dy * dy) / 0.70710678118654752, 1.0);
    }
    return 1.0 - fy;
}

// ---- ops ------------------------------------------------------------------

vec4 evalOp(int i, int tb, int x, int y, vec4 cur) {
    int code = DI(i);
    int a = i + 2; // first argument
    if (code == OP_LINE) {
        int k = lineStep(DI(a), DI(a + 1), DI(a + 2), DI(a + 3), x, y);
        if (k < 0) return EMPTY;
        int dash = DI(a + 4);
        return (dash <= 1 || k % dash == 0) ? colorAt(a + 5) : EMPTY;
    }
    if (code == OP_DOT) {
        int dx = x - DI(a), dy = y - DI(a + 1);
        if (abs(dx) > 1 || abs(dy) > 1) return EMPTY;
        return (dx == 0 && dy == 0) ? colorAt(a + 6) : colorAt(a + 2);
    }
    if (code == OP_AREA_BODY) {
        int off = DI(a), w = DI(a + 1), h = DI(a + 2);
        if (x >= w) return EMPTY;
        float top = D(tb + off + x * 2);
        float bottom = D(tb + off + x * 2 + 1);
        if (top == NONE) return EMPTY;
        if (y < int(ceil(top)) + 1 || y > int(bottom)) return EMPTY;
        float span = float(h - 1) - top;
        float t = 1.0 - (float(y) - top) / max(span, 1.0);
        float u = cover(DI(a + 3), x, y, (0.1 + 0.9 * t) * D(a + 6));
        if (u < 0.0) return EMPTY;
        return fade(shadeAt(D(a + 4), D(a + 5), 0.32 + 0.4 * t, u), D(a + 7));
    }
    if (code == OP_AREA_STROKE) {
        int off = DI(a), w = DI(a + 1);
        if (x >= w) return EMPTY;
        float top = D(tb + off + x * 2);
        if (top == NONE) return EMPTY;
        int ty = int(rnd(top));
        int from = ty;
        if (x > 0) {
            float prev = D(tb + off + (x - 1) * 2);
            if (prev != NONE) from = int(rnd(prev));
        }
        return (y >= min(from, ty) && y <= max(from, ty)) ? colorAt(a + 2) : EMPTY;
    }
    if (code == OP_BAR) {
        int bx0 = DI(a), bx1 = DI(a + 1), bottom = DI(a + 3), h = DI(a + 4);
        float top = D(a + 2);
        int ty = int(rnd(top));
        if (x < bx0 || x > max(bx1, bx0) || y > bottom) return EMPTY;
        if (DI(a + 5) == 4) {
            // Dot matrix: lit from the axis, unlit dots a faint grid above.
            if (emod(x - bx0, 2) != 0 || emod(h - 1 - y, 2) != 0) return EMPTY;
            if (y < ty) return FAINT;
            float s = float(bottom - y + 1) / float(max(bottom - ty + 1, 1));
            float u = threshold(1, (x - bx0) / 2, (h - 1 - y) / 2);
            return fade(shadeAt(D(a + 6), D(a + 7), 0.3 + 0.55 * s, u), D(a + 8));
        }
        if (y < ty) return EMPTY;
        float t = 1.0 - (float(y) - top) / max(float(h - 1) - top, 1.0);
        float u = y == ty ? threshold(1, x, y) : cover(DI(a + 5), x, y, 0.3 + 0.7 * t);
        if (u < 0.0) return EMPTY;
        float level = y == ty ? 0.82 : 0.32 + 0.38 * t;
        return fade(shadeAt(D(a + 6), D(a + 7), level, u), D(a + 8));
    }
    if (code == OP_PIE) {
        float cx = D(a), cy = D(a + 1), radius = D(a + 2), inner = D(a + 3);
        bool multi = DI(a + 4) != 0;
        int hover = DI(a + 5), n = DI(a + 6), off = DI(a + 7);
        float dx = float(x) + 0.5 - cx, dy = float(y) + 0.5 - cy;
        float r = sqrt(dx * dx + dy * dy);
        float ang = atan(dx, -dy);
        ang = ang - TAU * floor(ang / TAU);
        int idx = -1;
        for (int s = 0; s < n; s++) {
            if (ang >= D(tb + off + s * 5) && ang < D(tb + off + s * 5 + 1)) { idx = s; break; }
        }
        if (idx < 0) return EMPTY;
        int row = tb + off + idx * 5;
        float outer = radius - 2.0 + (hover == idx ? 2.0 : 0.0);
        if (r < inner || r > outer) return EMPTY;
        float a0 = D(row), a1 = D(row + 1);
        if (multi && min(ang - a0, a1 - ang) * r < 0.9) return EMPTY;
        float dim = (hover >= 0 && hover != idx) ? 0.5 : 1.0;
        float t = clamp((r - inner) / max(outer - inner, 1.0), 0.0, 1.0);
        bool rim = outer - r < 1.2;
        float u = rim ? threshold(1, x, y) : cover(DI(row + 4), x, y, 0.3 + 0.7 * t);
        if (u >= 0.0) {
            return fade(shadeAt(D(row + 2), D(row + 3), rim ? 0.8 : 0.3 + 0.4 * t, u), dim);
        }
        return EMPTY;
    }
    if (code == OP_POLY) {
        float cx = D(a), cy = D(a + 1), radius = D(a + 2);
        int n = DI(a + 7), off = DI(a + 8);
        float px = float(x) + 0.5, py = float(y) + 0.5;
        bool inside = false;
        int j = n - 1;
        for (int k = 0; k < n; k++) {
            float xi = D(tb + off + k * 2), yi = D(tb + off + k * 2 + 1);
            float xj = D(tb + off + j * 2), yj = D(tb + off + j * 2 + 1);
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
            j = k;
        }
        if (!inside) return EMPTY;
        float d = sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) / radius;
        float u = cover(DI(a + 3), x, y, 0.2 + 0.55 * d);
        if (u < 0.0) return EMPTY;
        return fade(shadeAt(D(a + 4), D(a + 5), 0.32 + 0.35 * d, u), D(a + 6));
    }
    if (code == OP_GRADIENT) {
        int w = DI(a), h = DI(a + 1), dir = DI(a + 2), m = DI(a + 3);
        float density = D(a + 4);
        if (x >= w || y >= h) return EMPTY;
        float pos = direction(dir, x, y, w, h);
        if (DI(a + 7) != 0) {
            bool second = on(m, x, y, pos);
            float th = second ? D(a + 8) : D(a + 5);
            float tc = second ? D(a + 9) : D(a + 6);
            float t2 = threshold(m, x + 3, y + 5), k2 = density * (0.55 + 0.45 * pos);
            return k2 > t2 ? shadeAt(th, tc, 0.35 + 0.35 * pos, t2 / k2) : EMPTY;
        }
        float t = threshold(m, x, y), k = (1.0 - pos) * density;
        return k > t ? shadeAt(D(a + 5), D(a + 6), 0.3 + 0.4 * (1.0 - pos), t / k) : EMPTY;
    }
    if (code == OP_BACKGROUND) {
        int w = DI(a), h = DI(a + 1), f = DI(a + 2), m = DI(a + 3);
        float density = D(a + 4), time = D(a + 5);
        if (x >= w || y >= h) return EMPTY;
        float it;
        if (f >= 5) {
            it = lightField(f, x, y, w, h, time);
        } else {
            float u = float(x) / float(max(w, 1)), v = float(y) / float(max(h, 1));
            it = smooth01(0.18, 0.82, field(f, u, v, time));
        }
        float t = threshold(m, x, y), k = it * density;
        if (!(k > t)) return EMPTY;
        bool accent = DI(a + 8) != 0 && on(1, x + 1, y + 2, (it - 0.62) * 2.2);
        float level = f >= 5 ? 0.3 + 0.42 * it : 0.3 + 0.5 * it;
        return accent ? shadeAt(D(a + 9), D(a + 10), level, t / k) : shadeAt(D(a + 6), D(a + 7), level, t / k);
    }
    if (code == OP_BUTTON) {
        int w = DI(a), h = DI(a + 1), style = DI(a + 2);
        float density = D(a + 3), time = D(a + 4), th = D(a + 5), tc = D(a + 6);
        if (x >= w || y >= h) return EMPTY;
        bool edge = x == 0 || y == 0 || x + 1 == w || y + 1 == h;
        if (tc < 0.0) {
            // Future-Retro buttons are flat: no texture, checker or sparkles.
            vec4 base = rampColor(int(-tc), 2);
            if (style == 0) return density > 0.4 ? lift(base) : base;
            if (style == 1) return (edge || density > 0.0) ? MARBLE : EMPTY;
            return EMPTY;
        }
        vec4 c = EMPTY;
        if (edge && style != 2) {
            if ((x + y) % 2 == 0 || style == 0) c = shade(th, tc, 0.6);
        } else {
            float fy = h > 1 ? float(y) / float(h - 1) : 0.5;
            float it = density * (1.05 - 0.45 * fy);
            if (covers(0, x, y, it)) c = shade(th, tc, 0.45 + 0.25 * (1.0 - fy));
        }
        if (density > 0.4 && hash01(x, y ^ 0x5bd1) < 0.04 * density && (c.a >= 0.0 || cur.a >= 0.0)) {
            float phase = hash01(y, x ^ 0x1b56) * TAU;
            float speed = 1.5 + 2.5 * hash01(x ^ 0x77, y);
            float s = max(sin(time * speed + phase), 0.0);
            float s2 = s * s, s4 = s2 * s2, s8 = s4 * s4;
            float wink = s8 * s4;
            if (wink > 0.35) c = fade(shade(th, tc, 0.97), wink);
        }
        return c;
    }
    if (code == OP_METER) {
        int w = DI(a), h = DI(a + 1), fill = DI(a + 3);
        float value = D(a + 2);
        if (x >= w || y >= h) return EMPTY;
        int filled = int(rnd(clamp(value, 0.0, 1.0) * float(w)));
        float th = D(a + 4), tc = D(a + 5);
        if (fill == 4) {
            if (emod(x, 2) != 0 || emod(y, 2) != 0) return EMPTY;
            if (x < filled) {
                float s = float(x + 1) / float(max(filled, 1));
                return shadeAt(th, tc, 0.3 + 0.55 * s, threshold(1, x / 2, y / 2));
            }
            return FAINT;
        }
        if (x < filled) {
            float it = 0.35 + 0.65 * (float(x) + 1.0) / float(max(filled, 1));
            float u = cover(fill, x, y, it);
            if (u >= 0.0 || x + 1 == filled) return shadeAt(th, tc, 0.4 + 0.35 * it, u >= 0.0 ? u : threshold(1, x, y));
            return EMPTY;
        }
        if (tc < 0.0) return (emod(x, 2) == 0 && emod(y, 2) == 0) ? FAINT : EMPTY;
        if ((x + y) % 4 == 0) return fade(shade(260.0, 0.012, 0.35), 0.6);
        return EMPTY;
    }
    if (code == OP_AVATAR) {
        if (x < 0 || x > 7 || y < 0 || y > 7) return EMPTY;
        uint bits = uint(DI(a)) | (uint(DI(a + 1)) << 16u);
        bool mirrorX = DI(a + 2) != 0;
        uint acc = uint(DI(a + 4)) | (uint(DI(a + 5)) << 16u);
        int i = mirrorX ? y * 4 + (x < 4 ? x : 7 - x) : (y < 4 ? y : 7 - y) * 8 + x;
        bool lit = ((bits >> uint(i)) & 1u) == 1u;
        bool accent = lit && ((acc >> uint(i % 23)) & 1u) == 1u && ((acc >> uint((i + 7) % 23)) & 1u) == 1u;
        float hue = D(a + 3);
        if (hue < 0.0) {
            int id = int(-hue);
            return accent ? rampColor(id, 3) : (lit ? rampColor(id, 2) : SLATE);
        }
        return shade(hue, 0.19, accent ? 0.92 : (lit ? 0.62 : 0.16));
    }
    if (code == OP_SWATCH) {
        if (D(a + 1) < 0.0) return shadeAt(D(a), D(a + 1), 0.6, threshold(1, x, y));
        return shade(D(a), D(a + 1), (x + y) % 2 == 0 ? 0.72 : 0.48);
    }
    return EMPTY;
}

vec4 evalList(int start, int end, int x, int y, vec4 cur) {
    int tb = 1 + DI(0);
    int i = start;
    for (int guard = 0; guard < 65536; guard++) {
        if (i >= end) break;
        if (DI(i) == OP_BLOOM) break;
        vec4 c = evalOp(i, tb, x, y, cur);
        if (c.a >= 0.0) cur = c;
        i += DI(i + 1);
    }
    return cur;
}

int bloomIndex() {
    int end = 1 + DI(0);
    int i = 1;
    for (int guard = 0; guard < 65536; guard++) {
        if (i >= end) break;
        if (DI(i) == OP_BLOOM) return i;
        i += DI(i + 1);
    }
    return end;
}

vec4 lookupBase(int x, int y) {
    if (x < 0 || y < 0 || x >= uGrid.x || y >= uGrid.y) return EMPTY;
    // Base target rows are stored bottom-up (GL convention).
    vec4 c = texelFetch(uBase, ivec2(x, uGrid.y - 1 - y), 0) * 255.0;
    c = floor(c + 0.5);
    return c.a > 0.0 ? c : EMPTY;
}

vec4 postPixel(int x, int y) {
    int end = 1 + DI(0);
    int b = bloomIndex();
    vec4 cur = lookupBase(x, y);
    if (b < end) {
        int radius = DI(b + 2);
        float strength = D(b + 3);
        bool rampGlow = DI(b + 1) >= 5 && D(b + 4) == 1.0;
        if (cur.a < 0.0 && radius > 0) {
            bool done = false;
            for (int d = 1; d <= 4; d++) {
                if (d > radius || done) break;
                for (int oy = -d; oy <= d; oy++) {
                    if (done) break;
                    for (int ox = -d; ox <= d; ox++) {
                        if (abs(ox) != d && abs(oy) != d) continue;
                        vec4 c = lookupBase(x + ox, y + oy);
                        if (c.a > 160.0) {
                            float glow = strength * (1.0 - (float(d) - 1.0) / float(radius));
                            if (on(1, x, y, glow)) cur = rampGlow ? NEAR_BLUE_INK : fade(c, 0.35 + 0.25 * glow);
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

void main() {
    int x = int(gl_FragCoord.x);
    int y = uGrid.y - 1 - int(gl_FragCoord.y);
    if (uPass == 0) {
        vec4 c = evalList(1, bloomIndex(), x, y, EMPTY);
        fragColor = c.a >= 0.0 ? c / 255.0 : vec4(0.0);
    } else {
        vec4 c = postPixel(x, y);
        if (c.a < 0.0) {
            fragColor = vec4(0.0);
        } else {
            float al = c.a / 255.0;
            fragColor = uStraight == 1 ? c / 255.0 : vec4(c.rgb / 255.0 * al, al);
        }
    }
}
