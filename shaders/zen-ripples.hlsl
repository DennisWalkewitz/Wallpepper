// @name: Zen Ripples
// @tags: calming, water, nature
// A still pond at twilight. Now and then a drop falls and rings spread slowly across the surface,
// bending the reflection of the sky and a low moon.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define DROP_RATE 1.0         // how often drops fall
#define RIPPLE_SPEED 1.0      // how fast rings spread
#define RIPPLE_STRENGTH 1.0   // how much rings bend the reflection
#define RING_COUNT 1.0        // number of rings per drop
#define RIPPLE_LIFE 1.0       // how long rings last
#define SKY_TOP_R 0.03        // sky top: red
#define SKY_TOP_G 0.05        // sky top: green
#define SKY_TOP_B 0.14        // sky top: blue
#define SKY_LOW_R 0.55        // sky near the horizon: red
#define SKY_LOW_G 0.32        // sky near the horizon: green
#define SKY_LOW_B 0.42        // sky near the horizon: blue
#define MOON 1.0              // moon brightness (0 = none)
#define MOON_X 0.68           // moon position, 0..1 of width
#define HORIZON 0.30          // waterline from top (0..1)
#define MIST 0.5              // mist over the water
#define STARS 1.0             // star brightness
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), f.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), f.x), f.y);
}

float3 skyAt(float2 d, float aspect, float t)       // d.x across, d.y = height above the horizon (0..1)
{
    float3 top = float3(SKY_TOP_R, SKY_TOP_G, SKY_TOP_B);
    float3 low = float3(SKY_LOW_R, SKY_LOW_G, SKY_LOW_B);
    float3 col = lerp(low, top, pow(saturate(d.y * 2.2), 0.5));
    float2 sp = d * 110.0;
    float2 sid = floor(sp);
    float sh = hash12(sid);
    col += (1.0 - smoothstep(0.0, 0.10, length(frac(sp) - 0.5))) * step(0.985, sh) * (0.6 + 0.4 * sin(t * (1.0 + sh * 3.0) + sh * 50.0)) * STARS * smoothstep(0.1, 0.5, d.y);
    float md = length(d - float2(MOON_X * aspect, 0.16));
    col += float3(1.0, 0.95, 0.85) * ((1.0 - smoothstep(0.030, 0.033, md)) + exp(-md * 10.0) * 0.3) * MOON;
    return col;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float t = Time;
    float x = tex.x * aspect;
    float3 col;

    if (tex.y < HORIZON)
    {
        col = skyAt(float2(x, (HORIZON - tex.y)), aspect, t);
    }
    else
    {
        // water plane in perspective: z = distance from the viewer
        float below = (tex.y - HORIZON) / (1.0 - HORIZON);                // 0 at the horizon, 1 at the bottom
        float z = 1.0 / (below + 0.06);
        float2 wp = float2((x - aspect * 0.5) * z, z) * 0.6;              // world position on the water

        // ripples from drops: grid of cells, each drops periodically
        float2 rp = wp * 2.4;                                             // ripple space (denser drops)
        float2 g = floor(rp * 0.7);
        float2 slope = float2(0.0, 0.0);
        for (int cy = -1; cy <= 1; cy++)
        for (int cx = -1; cx <= 1; cx++)
        {
            float2 cell = g + float2(cx, cy);
            float h = hash12(cell);
            float period = 9.0 / max(DROP_RATE, 0.05) * (0.6 + h);
            float age = fmod(t + h * 50.0, period);
            float life = 7.0 * RIPPLE_LIFE;
            if (age > life) continue;
            float2 c = (cell + 0.2 + 0.6 * float2(hash12(cell + 2.0), hash12(cell + 9.0))) / 0.7;
            float2 dv = rp - c;
            float r = length(dv);
            float front = age * 0.35 * RIPPLE_SPEED;
            float env = exp(-abs(r - front) * 3.0 / max(RING_COUNT, 0.2)) * (1.0 - age / life) / (1.0 + r * 0.8);
            float w = cos((r - front) * 16.0) * env * step(r, front + 0.3);
            slope += dv / max(r, 1e-3) * w;
        }
        slope += (float2(vnoise(wp * 3.0 + t * 0.2), vnoise(wp * 3.0 - t * 0.17 + 4.0)) - 0.5) * 0.06;   // gentle breeze

        // reflect the sky, bent by the ripples
        float2 rd = float2(x + slope.x * 0.10 * RIPPLE_STRENGTH, (tex.y - HORIZON) + slope.y * 0.16 * RIPPLE_STRENGTH * (0.3 + below));
        col = skyAt(float2(rd.x, max(rd.y, 0.0)), aspect, t) * lerp(0.85, 0.45, below);
        col += float3(0.6, 0.65, 0.8) * max(slope.y, 0.0) * 0.6 * RIPPLE_STRENGTH;       // light catches the crests
        col = lerp(col, float3(SKY_TOP_R, SKY_TOP_G, SKY_TOP_B) * 0.6, below * 0.35);     // darker near the viewer
    }

    // mist along the horizon
    float mistBand = exp(-abs(tex.y - HORIZON) * 22.0) * (0.6 + 0.4 * vnoise(float2(x * 2.0 + t * 0.03, 1.0)));
    col += float3(SKY_LOW_R, SKY_LOW_G, SKY_LOW_B) * mistBand * MIST * 0.3;

    col = 1.0 - exp(-col * 1.5 * BRIGHTNESS);
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.6;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
