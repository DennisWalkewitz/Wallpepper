// @name: Aurora Lake
// @tags: nature, space, featured
// Northern lights over layered mountain ridges, mirrored in a still lake with gentle ripples.
// Aurora: several curtain layers (noise-displaced bands with fine vertical rays), green core fading to violet.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define AURORA 1.0            // aurora brightness
#define AURORA_SPEED 1.0      // curtain motion
#define HUE_SHIFT 0.0         // 0 = green/violet, 1 = teal/pink
#define STARS 1.0             // star brightness
#define LAKE_LINE 0.72        // waterline from top (0..1)
#define RIPPLES 1.0           // lake ripples
#define CURTAINS 4            // number of aurora curtains (1..6)
#define RAY_DETAIL 1.0        // fine vertical rays in the curtains
#define AURORA_HEIGHT 1.0     // how tall the curtains are
#define LOW_R 0.15            // aurora lower colour: red
#define LOW_G 1.00            // aurora lower colour: green
#define LOW_B 0.45            // aurora lower colour: blue
#define HIGH_R 0.55           // aurora upper colour: red
#define HIGH_G 0.20           // aurora upper colour: green
#define HIGH_B 0.90           // aurora upper colour: blue
#define MOUNTAINS 1.0         // height of the mountain ridges
#define REFLECTION 1.0        // brightness of the lake reflection
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

float fbm(float2 p)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { s += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return s;
}

float3 auroraColor(float h)          // h: 0 = bottom of curtain, 1 = top
{
    float3 green = lerp(float3(LOW_R, LOW_G, LOW_B), float3(0.1, 0.9, 0.9), HUE_SHIFT);
    float3 violet = lerp(float3(HIGH_R, HIGH_G, HIGH_B), float3(1.0, 0.3, 0.6), HUE_SHIFT);
    return lerp(green, violet, smoothstep(0.25, 1.0, h));
}

// sky above the mountains, p in (x, height above horizon)
float3 skyCol(float2 p, float t)
{
    float3 col = lerp(float3(0.02, 0.04, 0.08), float3(0.005, 0.008, 0.025), saturate(p.y * 1.4));
    // stars
    float2 sp = p * 140.0;
    float2 id = floor(sp);
    float h = hash12(id);
    float d = length(frac(sp) - 0.5 - (hash12(id + 3.0) - 0.5) * 0.6);
    float tw = 0.6 + 0.4 * sin(t * (1.0 + h * 3.0) + h * 50.0);
    col += (1.0 - smoothstep(0.0, 0.12, d)) * step(0.985, h) * tw * 0.8 * STARS;

    // aurora curtains
    float3 au = float3(0.0, 0.0, 0.0);
    float ta = t * 0.06 * AURORA_SPEED;
    for (int i = 0; i < CURTAINS; i++)
    {
        float fi = float(i);
        float base = 0.02 + fi * 0.06;                                // curtain base height
        float x = p.x * (0.9 + fi * 0.25) + fi * 3.7;
        float wave = vnoise(float2(x * 0.9 + ta * (1.0 + fi * 0.3), fi * 5.0)) * 0.22
                   + sin(x * 1.7 + fi * 1.7 + ta * 4.0) * 0.06 + sin(x * 4.3 - ta * 2.0 + fi) * 0.015;   // smooth folds
        float patchiness = smoothstep(0.30, 0.65, vnoise(float2(x * 0.7 - ta * 1.5, fi * 7.0)));
        float y = p.y - base - wave;
        float edge = smoothstep(-0.03, 0.01, y);                       // soft lower edge (no hard silhouette)
        y = max(y, 0.0);
        float hgt = (0.22 + 0.18 * vnoise(float2(x * 1.5, fi + ta))) * AURORA_HEIGHT;
        float fall = exp(-y / hgt * 2.2);
        float rays = lerp(1.0, 0.45 + 0.55 * pow(vnoise(float2(x * 28.0 + ta * 6.0, fi * 9.0)), 2.0), saturate(RAY_DETAIL));
        float flick = 0.7 + 0.3 * vnoise(float2(x * 3.0 - ta * 3.0, fi * 3.3));
        au += auroraColor(y / (hgt * 1.1)) * fall * rays * flick * patchiness * edge * (0.75 - fi * 0.1);
    }
    col += au * AURORA * 0.9;
    // airglow at the horizon
    col += float3(0.05, 0.12, 0.10) * exp(-p.y * 12.0) * AURORA;
    return col;
}

float ridge(float x, float seed, float amp, float freq)
{
    // ridged noise: sharp peaks
    float s = 0.0, a = 0.6, f = freq;
    for (int i = 0; i < 5; i++) { float n = 1.0 - abs(vnoise(float2(x * f + seed, seed)) * 2.0 - 1.0); s += a * n * n; f *= 2.1; a *= 0.45; }
    return s * amp * MOUNTAINS;
}

float3 landscape(float2 p, float t, out float isLand)
{
    // p.y = height above the waterline (screen units)
    float3 col = skyCol(float2(p.x, p.y - 0.12), t);
    isLand = 0.0;
    // three ridges, far to near; farther ridges are lit faintly by the aurora
    for (int i = 0; i < 3; i++)
    {
        float fi = float(i);
        float hgt = 0.05 + ridge(p.x, 11.0 + fi * 7.0, 0.20 - fi * 0.035, 1.4 + fi * 0.9) + (fi == 0.0 ? 0.04 : 0.0);
        if (p.y < hgt)
        {
            float rim = smoothstep(0.012, 0.0, hgt - p.y) * (0.25 - fi * 0.08);
            float3 tone = fi < 0.5 ? float3(0.05, 0.09, 0.10) : (fi < 1.5 ? float3(0.025, 0.045, 0.055) : float3(0.008, 0.012, 0.018));
            col = tone + float3(0.1, 0.35, 0.25) * rim * AURORA;
            isLand = 1.0;
        }
    }
    // snow-tinted treeline glint on the nearest shore
    return col;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float x = (tex.x - 0.5) * aspect;
    float yUp = LAKE_LINE - tex.y;                                    // > 0 above water
    float t = Time;
    float3 col;
    float land;

    if (yUp >= 0.0)
    {
        col = landscape(float2(x, yUp), t, land);
    }
    else
    {
        // reflection: mirror, ripple, darken; slight blur by mixing two samples
        float depth = -yUp;
        float2 rp = float2(x, depth);
        float rip = (vnoise(float2(x * 40.0, depth * 260.0 / (depth + 0.05) * 0.05 - t * 0.6)) - 0.5) * 0.012 * RIPPLES;
        rip += sin(depth * 160.0 / (0.3 + depth * 4.0) - t * 1.5) * 0.0015 * RIPPLES;
        float3 a = landscape(float2(x + rip, depth + rip * 0.5), t, land);
        float3 b = landscape(float2(x - rip * 0.6, depth + 0.004), t, land);
        col = (a + b) * 0.5 * lerp(0.75, 0.35, saturate(depth * 2.5)) * REFLECTION;
        col = lerp(col, float3(0.01, 0.02, 0.03), saturate(depth * 1.6));
        col += float3(0.15, 0.4, 0.3) * exp(-depth * 90.0) * 0.08 * AURORA;   // bright shoreline line
    }

    col = 1.0 - exp(-col * 1.5 * BRIGHTNESS);
    col = pow(saturate(col), float3(0.92, 0.92, 0.92));

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
