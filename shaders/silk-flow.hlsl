// @name: Silk Flow
// @tags: calming, abstract, featured
// Slow, silky colour ribbons: domain-warped noise blended through a three-colour palette you can set freely.
// Very cheap to render; good as an always-on background.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define FLOW_SPEED 1.0        // animation speed
#define FLOW_SCALE 1.0        // size of the ribbons (lower = larger shapes)
#define WARP 1.0              // how strongly the pattern folds into itself
#define RIBBONS 1.0           // fine silk lines (0 = smooth gradient only)
#define COLOR_A_R 0.05        // colour A: red
#define COLOR_A_G 0.10        // colour A: green
#define COLOR_A_B 0.35        // colour A: blue
#define COLOR_B_R 0.55        // colour B: red
#define COLOR_B_G 0.15        // colour B: green
#define COLOR_B_B 0.60        // colour B: blue
#define COLOR_C_R 1.00        // colour C: red
#define COLOR_C_G 0.55        // colour C: green
#define COLOR_C_B 0.35        // colour C: blue
#define HUE_CYCLE 0.0         // hue rotations per minute (0 = fixed palette)
#define CONTRAST 1.0          // palette contrast
#define SHEEN 0.5             // glossy highlights on the folds
#define GRAIN 0.3             // film grain
#define VIGNETTE 0.5          // darker corners
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
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);                       // quintic: no visible grid
    return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), f.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), f.x), f.y);
}

float fbm(float2 p)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = float2(p.x * 1.7 - p.y * 1.1, p.x * 1.1 + p.y * 1.7); a *= 0.5; }
    return s;
}

float3 hueRotate(float3 c, float turns)
{
    float a = turns * 6.2831853;
    float3 k = float3(0.57735, 0.57735, 0.57735);
    return c * cos(a) + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - cos(a));
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, tex.y) * 1.4 * FLOW_SCALE;
    float t = Time * 0.05 * FLOW_SPEED;

    float2 q = float2(fbm(p + float2(0.0, t)), fbm(p + float2(5.2, 1.3) - t));
    float2 r = float2(fbm(p + 3.0 * WARP * q + float2(1.7, 9.2) + t * 0.7), fbm(p + 3.0 * WARP * q + float2(8.3, 2.8) - t * 0.5));
    float f = fbm(p + 3.0 * WARP * r);

    float3 ca = float3(COLOR_A_R, COLOR_A_G, COLOR_A_B);
    float3 cb = float3(COLOR_B_R, COLOR_B_G, COLOR_B_B);
    float3 cc = float3(COLOR_C_R, COLOR_C_G, COLOR_C_B);
    float k = saturate((f - 0.5) * CONTRAST * 1.6 + 0.5);
    float3 col = lerp(ca, cb, smoothstep(0.0, 0.6, k + (q.x - 0.5) * 0.5));
    col = lerp(col, cc, smoothstep(0.45, 1.0, k * (0.6 + r.y)));

    float lines = sin((f * 14.0 + r.x * 6.0) * 6.2831853) * 0.5 + 0.5;    // silk threads follow the folds
    col *= 1.0 + (lines - 0.5) * 0.18 * RIBBONS;
    col += pow(saturate(r.x * 1.4 - 0.2), 4.0) * SHEEN * 0.5;             // sheen

    if (HUE_CYCLE != 0.0) col = hueRotate(col, HUE_CYCLE * Time / 60.0);
    col *= BRIGHTNESS;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * VIGNETTE * 1.4;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution + frac(Time) * 100.0) - 0.5) * (1.0 / 255.0 + GRAIN * 0.04);
    return float4(saturate(col), 1.0);
}
