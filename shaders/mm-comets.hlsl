// @name: Comet Run
// @tags: multimonitor, space, calming
// Made for several monitors in Span mode: comets cross the whole desktop from screen to screen, and every
// time one passes a monitor edge it kicks off a shockwave ring there. On one monitor it is a calm comet sky.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};
cbuffer MonitorLayout : register(b1) {
    float4 Monitors[8];       // x, y, w, h of each monitor in span UV
    float  MonitorCount;      // 0 when not provided (Windows Terminal)
    float  MonitorIndex;
    float2 MonitorPad;
};

#define COMET_COUNT 6         // number of comets (1..10)
#define COMET_SPEED 1.0       // travel speed
#define COMET_SIZE 1.0        // head size
#define TAIL_LENGTH 1.0       // tail length
#define PATH_WAVE 1.0         // how much the paths curve
#define DIRECTION_MIX 0.3     // share of comets flying right-to-left (0..1)
#define HEAD_R 0.75           // head colour: red
#define HEAD_G 0.90           // head colour: green
#define HEAD_B 1.00           // head colour: blue
#define TAIL_R 0.20           // tail colour: red
#define TAIL_G 0.45           // tail colour: green
#define TAIL_B 1.00           // tail colour: blue
#define COLOR_VARIETY 0.5     // hue difference between comets (0..1)
#define EDGE_SHOCKWAVE 1.0    // ring when a comet crosses a monitor edge (0 = off)
#define STARS 1.0             // star brightness
#define NEBULA 0.6            // background nebula strength
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

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

float3 hueRotate(float3 c, float turns)
{
    float a = turns * 6.2831853;
    float3 k = float3(0.57735, 0.57735, 0.57735);
    return c * cos(a) + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - cos(a));
}

// comet centre at phase s (0..1 across the span), in height units
float2 cometPos(float s, float fi, float aspect, float dirSign)
{
    float x = (dirSign > 0.0 ? s : 1.0 - s) * (aspect + 0.6) - 0.3;
    float y = 0.15 + 0.7 * hash11(fi * 7.3 + 1.0) + PATH_WAVE * 0.07 * sin(x * (1.5 + hash11(fi) * 2.0) + fi * 2.1);
    return float2(x, y);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, tex.y);
    float t = Time;

    // background: deep space gradient, nebula, twinkling stars
    float3 col = lerp(float3(0.010, 0.012, 0.030), float3(0.020, 0.010, 0.035), tex.y);
    float neb = vnoise(p * 1.3 + float2(t * 0.01, 0.0)) * vnoise(p * 2.9 - float2(0.0, t * 0.008));
    col += float3(0.10, 0.06, 0.22) * neb * NEBULA + float3(0.02, 0.10, 0.12) * vnoise(p * 0.8 + 9.0) * neb * NEBULA;
    float2 sp = p * 90.0;
    float2 sid = floor(sp);
    float sh = hash12(sid);
    float sd = length(frac(sp) - 0.5 - (hash12(sid + 5.0) - 0.5) * 0.6);
    col += (1.0 - smoothstep(0.0, 0.10, sd)) * step(0.975, sh) * (0.6 + 0.4 * sin(t * (1.0 + sh * 4.0) + sh * 60.0)) * STARS * 0.8;

    [loop]
    for (int i = 0; i < COMET_COUNT; i++)
    {
        float fi = float(i);
        float dirSign = hash11(fi * 3.7 + 0.5) < DIRECTION_MIX ? -1.0 : 1.0;
        float speed = (0.035 + 0.035 * hash11(fi * 1.9)) * COMET_SPEED;
        float s = frac(t * speed + hash11(fi * 5.1));
        float2 c = cometPos(s, fi, aspect, dirSign);
        float2 cPrev = cometPos(s - 0.01, fi, aspect, dirSign);
        float2 dir = normalize(c - cPrev);
        float2 rel = p - c;
        float along = dot(rel, dir);                                   // > 0 in front of the head
        float perp = dot(rel, float2(-dir.y, dir.x));
        float hueT = (hash11(fi * 9.7) - 0.5) * COLOR_VARIETY * 0.4;
        float3 headCol = hueRotate(float3(HEAD_R, HEAD_G, HEAD_B), hueT);
        float3 tailCol = hueRotate(float3(TAIL_R, TAIL_G, TAIL_B), hueT);

        float size = 0.012 * COMET_SIZE * (0.7 + 0.6 * hash11(fi * 2.3));
        float head = exp(-dot(rel, rel) / (size * size)) * 2.0 + exp(-length(rel) / (size * 4.0)) * 0.35;
        float back = max(-along, 0.0);
        float len = 0.55 * TAIL_LENGTH;
        float width = size * (0.6 + back * 3.0);
        float tail = exp(-(perp * perp) / (width * width)) * exp(-back / len * 3.0) * step(along, 0.0);
        float sparkle = 0.75 + 0.25 * vnoise(float2(back * 60.0 - t * 8.0, fi * 13.0 + perp * 200.0));
        col += headCol * head + tailCol * tail * sparkle * 0.9;

        // shockwave where this comet crossed a monitor edge (needs the monitor layout from Wallpepper)
        if (EDGE_SHOCKWAVE > 0.0 && MonitorCount > 1.5)
        {
            [loop]
            for (int m = 1; m < 8; m++)
            {
                if (float(m) >= MonitorCount) break;
                float edgeX = Monitors[m].x * aspect;
                float sEdge = (edgeX + 0.3) / (aspect + 0.6);
                if (dirSign < 0.0) sEdge = 1.0 - sEdge;
                float ds = s - sEdge;                                 // phase since crossing
                if (ds < 0.0) ds += 1.0;
                float age = ds / speed;                               // seconds since crossing
                if (age < 2.5)
                {
                    float2 ec = cometPos(sEdge, fi, aspect, dirSign);
                    float r = length(p - ec);
                    float ring = exp(-pow((r - age * 0.18) * 60.0, 2.0)) * (1.0 - age / 2.5);
                    col += headCol * ring * 0.7 * EDGE_SHOCKWAVE;
                }
            }
        }
    }

    col = 1.0 - exp(-col * 1.4 * BRIGHTNESS);

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
