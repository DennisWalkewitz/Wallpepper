// @name: Desktop Pong
// @tags: multimonitor, retro, fun
// A game of Pong that plays itself across ALL your monitors in Span mode: the paddles sit on the outer edges
// of the desktop, the ball travels from screen to screen and every monitor edge is drawn as a net.
// On one monitor it is a normal self-playing Pong.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};
cbuffer MonitorLayout : register(b1) {
    float4 Monitors[8];
    float  MonitorCount;
    float  MonitorIndex;
    float2 MonitorPad;
};

#define BALL_SPEED 1.0        // ball speed
#define BALL_SIZE 1.0         // ball size
#define PADDLE_SIZE 1.0       // paddle height
#define PADDLE_SKILL 0.85     // how well paddles follow the ball (0..1)
#define TRAIL 1.0             // ball trail length
#define COLOR_R 0.25          // colour: red
#define COLOR_G 1.00          // colour: green
#define COLOR_B 0.55          // colour: blue
#define GLOW 1.0              // phosphor glow
#define NET 1.0               // net brightness (drawn at every monitor edge)
#define SCANLINES 0.25        // CRT scanline strength
#define GRID 0.15             // faint background grid
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

float tri(float x) { return abs(frac(x * 0.5) * 2.0 - 1.0); }           // 0..1..0, period 2

float box(float2 p, float2 b)
{
    float2 d = abs(p) - b;
    return length(max(d, float2(0.0, 0.0))) + min(max(d.x, d.y), 0.0);
}

float2 ballAt(float t, float aspect)
{
    float margin = 0.045;
    float x = margin + tri(t * 0.23 * BALL_SPEED / max(aspect / 1.78, 1.0) + 0.3) * (aspect - 2.0 * margin);
    float y = 0.06 + tri(t * 0.31 * BALL_SPEED + 0.7) * 0.88;
    return float2(x, y);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, tex.y);
    float t = Time;
    float3 ink = float3(COLOR_R, COLOR_G, COLOR_B);
    float lum = 0.0;

    // ball with trail
    float bs = 0.012 * BALL_SIZE;
    float2 b = ballAt(t, aspect);
    lum += 1.0 - smoothstep(0.0, 0.002, box(p - b, float2(bs, bs)));
    lum += exp(-length(p - b) / (bs * 5.0)) * 0.35 * GLOW;
    for (int i = 1; i <= 8; i++)
    {
        float2 bp = ballAt(t - float(i) * 0.035 * TRAIL, aspect);
        lum += (1.0 - smoothstep(0.0, 0.002, box(p - bp, float2(bs, bs) * (1.0 - float(i) * 0.08)))) * (0.35 - float(i) * 0.04);
    }

    // paddles on the outer edges of the whole desktop
    float ph = 0.085 * PADDLE_SIZE;
    float wob = (1.0 - PADDLE_SKILL) * 0.25;
    float ly = clamp(b.y + wob * sin(t * 1.7), ph, 1.0 - ph);
    float ry = clamp(b.y + wob * sin(t * 1.3 + 2.0), ph, 1.0 - ph);
    // the paddle the ball moves away from drifts back to the middle
    float toRight = ballAt(t + 0.02, aspect).x - b.x > 0.0 ? 1.0 : 0.0;
    ly = lerp(ly, lerp(ly, 0.5, 0.6), toRight);
    ry = lerp(lerp(ry, 0.5, 0.6), ry, toRight);
    float lp = box(p - float2(0.022, ly), float2(0.006, ph));
    float rp = box(p - float2(aspect - 0.022, ry), float2(0.006, ph));
    lum += 1.0 - smoothstep(0.0, 0.002, min(lp, rp));
    lum += exp(-max(min(lp, rp), 0.0) * 60.0) * 0.25 * GLOW;

    // nets: at each monitor edge when the layout is known, otherwise in the middle
    float dash = step(0.5, frac(p.y * 18.0));
    float netD = 1e9;
    if (MonitorCount > 1.5)
    {
        for (int m = 1; m < 8; m++)
        {
            if (float(m) >= MonitorCount) break;
            netD = min(netD, abs(p.x - Monitors[m].x * aspect));
        }
    }
    else netD = abs(p.x - aspect * 0.5);
    lum += (1.0 - smoothstep(0.002, 0.004, netD)) * dash * 0.45 * NET;

    // top/bottom walls
    lum += (1.0 - smoothstep(0.004, 0.006, min(p.y, 1.0 - p.y))) * 0.5;

    // faint grid
    float2 g = abs(frac(p * 12.0) - 0.5);
    lum += (1.0 - smoothstep(0.0, 0.02, min(g.x, g.y))) * 0.05 * GRID;

    float3 col = ink * lum * BRIGHTNESS;
    col *= 1.0 - SCANLINES * (0.5 + 0.5 * sin(tex.y * Resolution.y * 3.14159));
    col += ink * 0.015;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.5;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    return float4(saturate(col), 1.0);
}
