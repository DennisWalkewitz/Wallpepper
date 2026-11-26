// @name: Drifting Clouds
// @tags: nature, calming
// Slow clouds drifting through a sky you can set to any time of day: sky gradient, sun position and colour,
// cloud cover, softness and wind are all adjustable. Clouds are lit from the sun side with soft shadows.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define CLOUD_COVER 0.5       // 0 = clear sky, 1 = overcast
#define CLOUD_SOFTNESS 0.5    // edge softness
#define CLOUD_SCALE 1.0       // size of the clouds
#define WIND_SPEED 1.0        // drift speed
#define WIND_ANGLE 0.0        // drift direction in degrees
#define EVOLVE 1.0            // how fast clouds change shape
#define SUN_X 0.72            // sun position, 0..1 of width
#define SUN_Y 0.30            // sun position, 0..1 from top
#define SUN_GLOW 1.0          // sun and halo brightness
#define SKY_TOP_R 0.12        // sky top: red
#define SKY_TOP_G 0.30        // sky top: green
#define SKY_TOP_B 0.62        // sky top: blue
#define SKY_LOW_R 0.62        // sky near the horizon: red
#define SKY_LOW_G 0.76        // sky near the horizon: green
#define SKY_LOW_B 0.92        // sky near the horizon: blue
#define SUN_R 1.00            // sunlight colour: red
#define SUN_G 0.90            // sunlight colour: green
#define SUN_B 0.72            // sunlight colour: blue
#define SHADOW 0.55           // darkness of the cloud undersides
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

float fbm(float2 p, float t)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 6; i++)
    {
        s += a * vnoise(p + t * (0.3 + float(i) * 0.15));
        p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6) * 1.03;
        a *= 0.5;
    }
    return s;
}

float density(float2 p, float t)
{
    float n = fbm(p, t);
    float lo = 1.0 - CLOUD_COVER;
    return smoothstep(lo * 0.75, lo * 0.75 + 0.08 + CLOUD_SOFTNESS * 0.45, n);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, tex.y);
    float t = Time;

    float3 skyTop = float3(SKY_TOP_R, SKY_TOP_G, SKY_TOP_B);
    float3 skyLow = float3(SKY_LOW_R, SKY_LOW_G, SKY_LOW_B);
    float3 sunCol = float3(SUN_R, SUN_G, SUN_B);
    float2 sunPos = float2(SUN_X * aspect, SUN_Y);
    float sd = length(p - sunPos);

    float3 col = lerp(skyTop, skyLow, pow(saturate(tex.y), 0.8));
    col += sunCol * (exp(-sd * 3.0) * 0.35 + exp(-sd * 12.0) * 0.5 + (1.0 - smoothstep(0.035, 0.04, sd)) * 1.5) * SUN_GLOW;

    // clouds: perspective squeeze towards the bottom (horizon) for depth
    float wa = radians(WIND_ANGLE);
    float2 wind = float2(cos(wa), sin(wa)) * t * 0.012 * WIND_SPEED;
    float persp = 1.0 + tex.y * 1.6;
    float2 cp = float2((p.x - aspect * 0.5) * persp, tex.y * persp * 1.8) / CLOUD_SCALE * 1.6 + wind * 3.0;
    float te = t * 0.01 * EVOLVE;
    float d = density(cp, te);
    // light: compare density a step towards the sun
    float2 toSun = normalize(sunPos - p + float2(1e-4, 0.0));
    float dl = density(cp + toSun * 0.12, te);
    float lit = saturate(0.55 + (d - dl) * 1.6);
    float3 cloudLit = lerp(float3(1.0, 1.0, 1.0), sunCol, 0.45) * 1.05;
    float3 cloudDark = lerp(skyTop, float3(0.45, 0.47, 0.55), 0.6) * (1.0 - SHADOW * 0.6);
    float3 cloud = lerp(cloudDark, cloudLit, lit);
    cloud += sunCol * exp(-sd * 4.0) * 0.35 * (1.0 - d * 0.5) * SUN_GLOW;       // silver lining near the sun
    col = lerp(col, cloud, d * 0.95);

    col = lerp(col, skyLow, smoothstep(0.85, 1.05, tex.y) * 0.5);          // haze at the horizon
    col *= BRIGHTNESS;
    col = col / (1.0 + max(col - 0.9, 0.0));                               // soft highlight roll-off
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.25;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
