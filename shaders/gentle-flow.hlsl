// @name: Gentle Flow
// @tags: meditative, calming, abstract
// Soft veils of light drifting past each other like silk in slow water. Each ribbon rises and falls on its
// own long rhythm, widens and thins, and carries fine threads that travel along it. Nothing here moves fast.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define RIBBONS 6             // number of veils (1..10)
#define FLOW_SPEED 1.0        // how fast everything drifts
#define WAVE_HEIGHT 1.0       // how far the veils rise and fall
#define WAVE_LENGTH 1.0       // length of the waves (higher = longer, calmer)
#define THICKNESS 1.0         // width of the veils
#define SOFTNESS 1.0          // softness of the edges
#define THREADS 0.6           // fine threads inside the veils
#define SPARKLE 0.4           // tiny lights travelling along the veils
#define TILT 0.0              // slant of the whole flow (-1..1)
#define COLOR_A_R 0.35        // colour A: red
#define COLOR_A_G 0.70        // colour A: green
#define COLOR_A_B 0.90        // colour A: blue
#define COLOR_B_R 0.75        // colour B: red
#define COLOR_B_G 0.55        // colour B: green
#define COLOR_B_B 0.95        // colour B: blue
#define COLOR_C_R 1.00        // colour C: red
#define COLOR_C_G 0.72        // colour C: green
#define COLOR_C_B 0.62        // colour C: blue
#define BACK_TOP_R 0.03       // background top: red
#define BACK_TOP_G 0.05       // background top: green
#define BACK_TOP_B 0.11       // background top: blue
#define BACK_LOW_R 0.07       // background bottom: red
#define BACK_LOW_G 0.05       // background bottom: green
#define BACK_LOW_B 0.10       // background bottom: blue
#define GLOW 1.0              // overall glow of the veils
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
    return lerp(lerp(hash12(i), hash12(i + float2(1.0, 0.0)), f.x), lerp(hash12(i + float2(0.0, 1.0)), hash12(i + float2(1.0, 1.0)), f.x), f.y);
}

float3 palette(float x)
{
    float3 a = float3(COLOR_A_R, COLOR_A_G, COLOR_A_B);
    float3 b = float3(COLOR_B_R, COLOR_B_G, COLOR_B_B);
    float3 c = float3(COLOR_C_R, COLOR_C_G, COLOR_C_B);
    float f = frac(x) * 3.0;
    float3 col = lerp(a, b, smoothstep(0.0, 1.0, f));
    col = lerp(col, c, smoothstep(1.0, 2.0, f));
    return lerp(col, a, smoothstep(2.0, 3.0, f));
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2((tex.x - 0.5) * aspect, tex.y - 0.5);
    p.y += p.x * TILT * 0.35;
    float t = Time * 0.12 * FLOW_SPEED;

    float3 col = lerp(float3(BACK_TOP_R, BACK_TOP_G, BACK_TOP_B), float3(BACK_LOW_R, BACK_LOW_G, BACK_LOW_B), tex.y);
    col += palette(t * 0.05) * 0.035 * vnoise(p * 1.3 + t * 0.2);            // the background breathes a little

    for (int i = 0; i < RIBBONS; i++)
    {
        float fi = float(i);
        float h = hash11(fi * 3.7 + 1.0);
        float h2 = hash11(fi * 9.1 + 4.0);
        float k = (1.1 + 1.4 * h) / max(WAVE_LENGTH, 0.2);
        float x = p.x * k;
        float ph = fi * 1.9;
        // centre line: three slow waves that never quite repeat
        float y = (h2 - 0.5) * 0.62
                + WAVE_HEIGHT * (0.10 * sin(x + t * (0.7 + h) + ph)
                               + 0.05 * sin(x * 2.3 - t * (0.5 + h2) + ph * 1.7)
                               + 0.03 * sin(x * 4.1 + t * 1.3 + ph * 0.6));
        float w = (0.075 + 0.110 * (0.5 + 0.5 * sin(x * 0.9 - t * 0.6 + ph * 2.3))) * THICKNESS * (0.7 + 0.6 * h2);
        float d = (p.y - y) / w;                                             // -1..1 across the veil
        float veil = exp(-d * d * (2.0 / max(SOFTNESS, 0.2)));
        // light gathers on the upper edge, like folded silk
        float fold = 0.55 + 0.45 * smoothstep(-1.0, 0.6, -d);
        // threads: fine lines that follow the veil and slide along it
        float thr = 0.5 + 0.5 * sin(d * (16.0 + 10.0 * h) + sin(x * 3.0 + t * 2.0 + ph) * 1.5);
        float threads = lerp(1.0, 0.62 + 0.60 * thr * thr, saturate(THREADS));
        // tiny lights riding the centre of the veil
        float sx = p.x * 7.0 - t * (2.0 + 3.0 * h) * (h2 > 0.5 ? 1.0 : -1.0) + fi * 5.0;
        float2 sq = float2((frac(sx) - 0.5) / 7.0, (p.y - y) - w * 0.5 * (hash11(floor(sx) * 2.1 + fi) - 0.5));
        float sp = exp(-dot(sq, sq) * 60000.0) * step(0.55, hash11(floor(sx) * 1.3 + fi));
        float3 c = palette(fi / max(float(RIBBONS), 1.0) + t * 0.04 + p.x * 0.08);
        float strength = (0.30 + 0.20 * h) * (0.75 + 0.25 * sin(t * 0.9 + ph));          // each veil swells and fades
        col += c * veil * fold * threads * strength * GLOW;
        col += lerp(c, float3(1.0, 1.0, 1.0), 0.6) * sp * 1.2 * SPARKLE;
    }

    col *= BRIGHTNESS;
    col = 1.0 - exp(-col * 1.35);
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
