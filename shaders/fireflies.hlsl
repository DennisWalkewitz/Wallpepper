// @name: Fireflies
// @tags: nature, calming
// A summer night at the forest edge: moon, stars, layered tree lines, drifting mist and blinking fireflies.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define FIREFLY_AMOUNT 1.0    // how many fireflies (0..2)
#define FIREFLY_SIZE 1.0      // glow size
#define FIREFLY_SPEED 1.0     // drifting speed
#define BLINK_SPEED 1.0       // blinking speed
#define FLY_R 0.85            // firefly colour: red
#define FLY_G 1.00            // firefly colour: green
#define FLY_B 0.35            // firefly colour: blue
#define MOON 1.0              // moon brightness (0 = no moon)
#define MOON_X 0.78           // moon position, 0..1 of width
#define MOON_Y 0.22           // moon position, 0..1 from top
#define STARS 1.0             // star brightness
#define MIST 0.6              // ground mist
#define TREE_HEIGHT 1.0       // height of the tree lines
#define SKY_R 0.03            // night sky tint: red
#define SKY_G 0.07            // night sky tint: green
#define SKY_B 0.14            // night sky tint: blue
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), f.x), lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), f.x), f.y);
}

// tree line: rolling hill + pointed conifers of random height; returns height at x
float treeLine(float x, float seed, float scale)
{
    float hill = vnoise(float2(x * 0.8 + seed, seed)) * 0.10;
    float tx = x * scale;
    float id = floor(tx);
    float f = frac(tx) - 0.5;
    float h = (0.35 + 0.65 * hash12(float2(id, seed))) / scale * 1.6;
    float tree = max(0.0, h * (1.0 - abs(f) * 2.6)) + 0.012 * step(abs(f), 0.5) * vnoise(float2(tx * 9.0, seed));
    return hill + tree * TREE_HEIGHT;
}

float fireflyLayer(float2 p, float scale, float seed, float t)
{
    float2 g = p * scale;
    float2 id = floor(g);
    float acc = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float2 cell = id + float2(x, y);
        float h = hash12(cell + seed);
        if (h > 0.35 * FIREFLY_AMOUNT) continue;
        float2 r = hash22(cell + seed * 3.0);
        float tt = t * FIREFLY_SPEED * (0.3 + r.x * 0.5);
        float2 c = cell + 0.5 + 0.38 * float2(sin(tt + r.y * 6.28) + 0.4 * sin(tt * 2.3 + r.x * 9.0), cos(tt * 0.8 + r.x * 6.28) * 0.8);
        float d = length(g - c) / scale;
        float blink = smoothstep(0.35, 1.0, sin(t * BLINK_SPEED * (0.8 + r.y) + r.x * 40.0) * 0.5 + 0.5);
        float size = 0.004 * FIREFLY_SIZE;
        acc += (exp(-d * d / (size * size)) + exp(-d / (size * 5.0)) * 0.25) * blink;
    }
    return acc;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, 1.0 - tex.y);                     // y up
    float t = Time;
    float3 sky = float3(SKY_R, SKY_G, SKY_B);

    // sky, stars, moon
    float3 col = lerp(sky * 1.8, sky * 0.35, saturate(p.y));
    float2 sp = p * 120.0;
    float2 sid = floor(sp);
    float sh = hash12(sid);
    float sd = length(frac(sp) - 0.5 - (hash12(sid + 4.0) - 0.5) * 0.6);
    col += (1.0 - smoothstep(0.0, 0.10, sd)) * step(0.98, sh) * (0.6 + 0.4 * sin(t * (1.0 + sh * 3.0) + sh * 70.0)) * STARS * smoothstep(0.25, 0.6, p.y);
    float2 mp = p - float2(MOON_X * aspect, 1.0 - MOON_Y);
    float md = length(mp);
    float moon = 1.0 - smoothstep(0.045, 0.048, md);
    float crater = 0.85 + 0.15 * vnoise(mp * 60.0);
    col += float3(1.0, 0.97, 0.88) * (moon * crater + exp(-md * 9.0) * 0.25 + exp(-md * 2.5) * 0.10) * MOON;

    // three tree lines, far to near, each darker; mist between them
    for (int i = 0; i < 3; i++)
    {
        float fi = float(i);
        float base = 0.30 - fi * 0.10;
        float h = base + treeLine(p.x + fi * 3.1, 5.0 + fi * 11.0, 14.0 - fi * 4.0);
        float mask = 1.0 - smoothstep(h - 0.003, h + 0.003, p.y);
        float3 treeCol = sky * (0.55 - fi * 0.22) + float3(0.0, 0.01, 0.0);
        col = lerp(col, treeCol, mask);
        float mist = vnoise(float2(p.x * 1.5 + t * 0.02 * (1.0 + fi), fi * 4.0 + p.y * 3.0)) * exp(-max(p.y - base, 0.0) * 14.0);
        col += sky * 2.2 * mist * MIST * (0.6 - fi * 0.15);
        // fireflies float in front of this tree line
        float glow = fireflyLayer(p, 5.0 + fi * 2.0, 20.0 + fi * 9.0, t) * smoothstep(0.75, 0.15, p.y) * (0.5 + fi * 0.25);
        col += float3(FLY_R, FLY_G, FLY_B) * glow;
    }

    col = 1.0 - exp(-col * 1.6 * BRIGHTNESS);
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
