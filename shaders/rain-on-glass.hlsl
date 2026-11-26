// @name: Rain on Glass
// @tags: water, rain, nature, featured
// A rainy evening seen through a window: fogged glass blurs warm and cold city lights (bokeh),
// drops slide down with a stick-slip motion, leave a clear wet trail and small beads behind,
// and refract a sharper, upside-down image of the lights.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define RAIN_AMOUNT 1.0       // number of sliding drops (0..1.5)
#define DROP_SIZE 1.0         // size of the drops
#define FALL_SPEED 1.0        // how fast drops slide
#define FOG 0.75              // how fogged the glass is (0 = clear)
#define LIGHTS 1.0            // brightness of the city lights
#define WARMTH 0.6            // 0 = cold blue night, 1 = warm amber
#define LIGHT_SIZE 1.0        // size of the blurred lights
#define LIGHT_AMOUNT 0.55     // how many lights are on (0..1)
#define REFRACTION 1.0        // how strongly drops bend the view
#define SMALL_DROPS 1.0       // tiny standing beads (0 = none)
#define SKY_R 0.015           // night sky: red
#define SKY_G 0.025           // night sky: green
#define SKY_B 0.055           // night sky: blue
#define STREET_GLOW 1.0       // warm glow at the bottom
#define VIGNETTE 0.7          // darker corners
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

// ---- background: out-of-focus city lights ----------------------------------------------------
float3 bokehLayer(float2 uv, float scale, float seed, float soft, float t)
{
    float a = seed * 0.7;                                          // rotate each layer: no visible grid
    float2 ruv = float2(uv.x * cos(a) - uv.y * sin(a), uv.x * sin(a) + uv.y * cos(a));
    float2 g = ruv * scale + float2(t * 0.004 * scale, seed);
    g.x += floor(g.y) * 0.37;                                      // stagger rows
    float2 id = floor(g);
    float2 f = frac(g) - 0.5;
    float h = hash12(id + seed * 13.0);
    if (h < 1.0 - LIGHT_AMOUNT) return float3(0.0, 0.0, 0.0);                    // most cells stay dark
    float2 p = (hash22(id + seed) - 0.5) * 0.45;
    float r = (0.12 + 0.16 * hash12(id + 5.3 + seed)) * LIGHT_SIZE;
    float d = length(f - p);
    float edge = 0.015 + soft * 0.22;
    float disk = 1.0 - smoothstep(r - edge, r, d);
    float rim = 1.0 + 0.6 * smoothstep(r - edge - 0.06, r - edge, d) * (1.0 - soft);   // brighter rim when sharp
    float flick = 0.85 + 0.15 * sin(t * (0.5 + h * 2.0) + h * 40.0);
    float3 warm = lerp(float3(1.0, 0.55, 0.18), float3(1.0, 0.78, 0.45), hash12(id + 2.1));
    float3 cold = lerp(float3(0.35, 0.55, 1.0), float3(0.75, 0.35, 0.9), hash12(id + 8.4));
    float3 c = lerp(cold, warm, step(1.0 - WARMTH, hash12(id + 4.7)));
    return c * disk * rim * flick * (0.25 + 0.75 * h);
}

float3 scene(float2 uv, float soft, float t)
{
    // dusk gradient: deep blue sky, warm street glow at the bottom
    float3 col = lerp(float3(0.10, 0.07, 0.06), float3(SKY_R, SKY_G, SKY_B), saturate(uv.y * 1.1 + 0.2));
    col += float3(0.10, 0.05, 0.02) * saturate(1.0 - uv.y * 2.0) * WARMTH * STREET_GLOW;
    col += bokehLayer(uv, 3.0, 1.0, soft, t) * 0.55;
    col += bokehLayer(uv, 5.0, 7.0, soft, t) * 0.45;
    col += bokehLayer(uv, 8.0, 3.0, saturate(soft + 0.2), t) * 0.30;
    return col * LIGHTS;
}

// ---- drops --------------------------------------------------------------------------------------
// returns float3(refraction offset xy, clear mask)
float3 slidingDrops(float2 uv, float scale, float t, float seed)
{
    float2 cell = float2(1.0, 2.2);                                // tall cells
    float2 g = uv * scale / cell;
    float colId = floor(g.x);
    g.y += hash12(float2(colId, seed)) * 7.0;                       // de-sync columns
    float2 id = floor(g);
    float2 f = frac(g) - 0.5;
    float n = hash12(id + seed);
    if (n > RAIN_AMOUNT * 0.75) return float3(0.0, 0.0, 0.0);

    float tt = t * FALL_SPEED * (0.6 + n) + n * 6.2831;
    float y = -sin(tt + sin(tt + sin(tt) * 0.5)) * 0.42;            // stick-slip: hangs, then slides fast
    float x = (n - 0.5) * 0.6 + sin(g.y * 9.0 + n * 20.0) * 0.06 + sin(g.y * 23.0) * 0.015;   // wobble
    float2 dp = (f - float2(x, y)) * cell;
    float r = 0.12 * DROP_SIZE;
    float d = length(dp * float2(1.0, 0.85));
    float drop = smoothstep(r, r * 0.55, d);

    // trail above the drop: small beads + a clear wet streak
    float above = smoothstep(-0.02, 0.02, f.y - y);
    float fade = saturate(1.0 - (f.y - y) * 1.6);
    float bead_y = frac(f.y * 9.0) - 0.5;
    float2 bp = float2((f.x - x) * cell.x, bead_y * cell.y / 9.0);
    float bead = smoothstep(0.035 * DROP_SIZE, 0.012, length(bp)) * above * fade * fade * step(0.35, hash12(float2(colId, floor(f.y * 9.0) + id.y * 9.0)));
    float streak = smoothstep(0.07 * DROP_SIZE, 0.01, abs((f.x - x) * cell.x)) * above * fade;

    float2 off = dp * drop * 2.4 + bp * bead * 1.5;
    return float3(off, saturate(drop + bead * 0.9 + streak * 0.6));
}

float3 staticBeads(float2 uv, float scale, float t)
{
    float2 g = uv * scale;
    float2 id = floor(g);
    float2 f = frac(g) - 0.5;
    float2 p = (hash22(id) - 0.5) * 0.7;
    float life = frac(t * 0.08 + hash12(id + 3.0));                 // beads appear and evaporate
    float fade = smoothstep(0.0, 0.05, life) * smoothstep(1.0, 0.75, life);
    float r = (0.06 + 0.12 * hash12(id + 9.0)) * DROP_SIZE;
    float2 dp = f - p;
    float m = smoothstep(r, r * 0.4, length(dp)) * fade * step(0.6, hash12(id + 1.0));
    return float3(dp * m * 1.8, m);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 uv = float2(tex.x * aspect, 1.0 - tex.y);               // y up, square units
    float t = Time;

    float3 a = slidingDrops(uv, 5.0, t, 1.0);
    float3 b = slidingDrops(uv * 1.4 + 0.37, 5.0, t * 1.1, 4.0);
    float3 s = staticBeads(uv, 22.0, t);
    s *= SMALL_DROPS;
    float clear = saturate(a.z + b.z + s.z);
    float2 off = (a.xy + b.xy * 0.7 + s.xy) * 0.06 * REFRACTION;

    // fogged glass = blurry, milky; through water = sharper and refracted
    float soft = FOG * (1.0 - clear);
    float3 col = scene(uv - off, soft, t);
    col = lerp(col, col * 0.85 + float3(0.035, 0.04, 0.05), FOG * (1.0 - clear) * 0.6);
    // highlight on the drop edge (light from above)
    col += float3(0.9, 0.85, 0.8) * saturate(-off.y * 18.0) * clear * 0.25;

    // filmic + gentle vignette
    col = 1.0 - exp(-col * 1.6 * BRIGHTNESS);
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * VIGNETTE;
    col = pow(saturate(col), float3(0.85, 0.85, 0.85));

    // terminal text on top
    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution + frac(t)) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
