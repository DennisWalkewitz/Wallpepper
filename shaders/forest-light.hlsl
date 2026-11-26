// @name: Forest Light
// @tags: nature, calming, featured
// Morning in a misty forest: rows of tree trunks fading into green-gold fog, sunbeams falling through the
// canopy, leaves swaying at the top, ferns on the ground, floating pollen and the odd falling leaf.
// The camera drifts slowly sideways so near trunks slide past the far ones.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define TREE_LAYERS 6         // rows of trees from far to near (2..8)
#define TREE_DENSITY 1.0      // trees per row
#define TRUNK_WIDTH 1.0       // thickness of the trunks
#define WALK_SPEED 1.0        // sideways camera drift
#define SUNBEAMS 1.0          // light shafts through the canopy
#define BEAM_ANGLE 28.0       // tilt of the light shafts in degrees
#define SUN_X 0.72            // where the light comes from, 0..1 of width
#define FOG 1.0               // mist between the trees
#define CANOPY 1.0            // leaves at the top
#define LEAF_SWAY 1.0         // wind in the leaves
#define FERNS 1.0             // undergrowth at the bottom
#define POLLEN 1.0            // floating specks in the light
#define FALLING_LEAVES 1.0    // drifting leaves (0 = none)
#define LIGHT_R 1.00          // sunlight colour: red
#define LIGHT_G 0.86          // sunlight colour: green
#define LIGHT_B 0.50          // sunlight colour: blue
#define FOG_R 0.42            // mist colour: red
#define FOG_G 0.58            // mist colour: green
#define FOG_B 0.38            // mist colour: blue
#define SHADE_R 0.02          // shadow colour: red
#define SHADE_G 0.06          // shadow colour: green
#define SHADE_B 0.04          // shadow colour: blue
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

float fbm(float2 p)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.07 + 13.1; a *= 0.5; }
    return s;
}

// one row of trunks; returns coverage (x) and a bark shade (y)
float2 trunks(float2 p, float depth, float seed)
{
    float spacing = lerp(0.10, 0.36, depth) / max(TREE_DENSITY, 0.2);
    float gx = p.x / spacing;
    float id = floor(gx);
    float cover = 0.0, shade = 0.0;
    for (int k = -1; k <= 1; k++)
    {
        float cid = id + float(k);
        float h = hash11(cid * 1.37 + seed);
        if (h < 0.22) continue;                                             // gaps in the row
        float cx = (cid + 0.5 + (hash11(cid * 3.1 + seed) - 0.5) * 0.7) * spacing;
        float w = lerp(0.006, 0.040, depth) * (0.6 + 0.9 * h) * TRUNK_WIDTH;
        float lean = (hash11(cid * 5.7 + seed) - 0.5) * 0.10;
        float x = p.x - cx - lean * (p.y - 0.5) - 0.004 * sin(p.y * 9.0 + cid);
        w *= 1.0 + 0.45 * saturate(0.25 - p.y) * 4.0 * 0.25;                // roots flare out
        float c = 1.0 - smoothstep(w * 0.85, w, abs(x));
        if (c > cover) { cover = c; shade = 0.55 + 0.45 * vnoise(float2(x / w * 2.0 + cid, p.y * 22.0)) - 0.35 * (x / w); }
    }
    return float2(cover, shade);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, 1.0 - tex.y);                         // y up
    float t = Time;
    float3 lightC = float3(LIGHT_R, LIGHT_G, LIGHT_B);
    float3 fogC = float3(FOG_R, FOG_G, FOG_B);
    float3 shadeC = float3(SHADE_R, SHADE_G, SHADE_B);
    float2 sunP = float2(SUN_X * aspect, 1.08);

    // glowing mist behind everything: brightest where the light comes in
    float sd = length((p - sunP) * float2(0.8, 1.0));
    float3 col = fogC * (0.50 + 0.25 * p.y) + lightC * (exp(-sd * 1.3) * 1.5 + exp(-sd * 3.5) * 1.2);

    // sunbeams: bands at an angle, broken up by the canopy, fading towards the ground
    float ba = radians(BEAM_ANGLE);
    float bx = (p.x - sunP.x) * cos(ba) + (p.y - sunP.y) * sin(ba);
    float beams = vnoise(float2(bx * 9.0, t * 0.05)) * vnoise(float2(bx * 23.0 + 5.0, t * 0.08 + 2.0));
    beams = smoothstep(0.10, 0.50, beams) * smoothstep(-0.25, 0.70, p.y) * exp(-sd * 0.7);

    // rows of trees, far to near; each row sits in more fog the farther it is
    for (int i = 0; i < TREE_LAYERS; i++)
    {
        float depth = (float(i) + 0.5) / float(TREE_LAYERS);                // 0 far .. 1 near
        float2 q = p;
        q.x += t * 0.012 * WALK_SPEED * (0.2 + depth * depth * 2.5) + float(i) * 7.3;   // parallax
        float ground = lerp(0.20, 0.03, depth) + 0.02 * vnoise(float2(q.x * 3.0, float(i)));
        float2 tr = trunks(q, depth, float(i) * 17.0 + 3.0);
        float mask = max(tr.x, 1.0 - smoothstep(ground - 0.004, ground + 0.004, p.y));
        float fogAmt = saturate((1.0 - depth) * 0.95 * FOG + 0.05);
        float3 bark = lerp(shadeC * (0.6 + 0.8 * tr.y), fogC * 0.75 + lightC * 0.12, fogAmt);
        bark += lightC * 0.20 * saturate(tr.y - 0.55) * (1.0 - fogAmt) * exp(-sd * 1.2);     // rim light on the sun side
        col = lerp(col, bark, mask);
        // mist and light shafts drift in front of this row
        float mist = fbm(float2(q.x * 1.3 + t * 0.015, p.y * 2.5 + float(i))) * (1.0 - depth) * 0.22 * FOG * exp(-p.y * 1.2);
        col = lerp(col, fogC * 0.9 + lightC * 0.15, mist);
        col += lightC * beams * SUNBEAMS * 0.30 * (1.0 - depth * 0.7);
    }

    // canopy: leaves at the top, moving in the wind, with bright gaps
    float2 lp = float2(p.x * 3.0 + t * 0.012 * WALK_SPEED * 3.0, p.y * 3.0);
    lp += float2(sin(t * 0.5 + p.x * 4.0), cos(t * 0.4 + p.x * 3.0)) * 0.03 * LEAF_SWAY;
    float leaves = fbm(lp * 1.6) + 0.5 * vnoise(lp * 9.0) * 0.4;
    float canopy = smoothstep(0.62 - 0.22 * CANOPY, 0.95, p.y + (leaves - 0.5) * 0.45) * CANOPY;
    float3 leafCol = lerp(shadeC * 1.2, float3(0.10, 0.22, 0.05) + lightC * 0.25 * exp(-sd * 1.5), smoothstep(0.45, 0.8, leaves));
    col = lerp(col, leafCol, saturate(canopy));

    // ferns and grass at the bottom, swaying
    float fx = p.x * 26.0 + t * 0.012 * WALK_SPEED * 26.0 * 2.5;
    float fid = floor(fx);
    float fh = (0.05 + 0.10 * hash11(fid * 2.3)) * FERNS;
    float blade = abs(frac(fx) - 0.5 + sin(t * 1.2 + fid) * 0.08 * LEAF_SWAY * (p.y / max(fh, 0.01)));
    float fern = step(p.y, fh * (1.0 - blade * 2.0)) * step(0.001, FERNS);
    col = lerp(col, shadeC * 0.8 + float3(0.01, 0.04, 0.01), fern);

    // pollen drifting through the light
    float2 pp = p * 16.0 + float2(sin(t * 0.13) + t * 0.05, t * 0.07);
    float2 pid = floor(pp);
    float2 po = float2(hash12(pid), hash12(pid + 7.0)) * 0.7 + 0.15;
    float tw = 0.5 + 0.5 * sin(t * (0.8 + hash12(pid + 3.0) * 2.0) + hash12(pid) * 30.0);
    float speck = (1.0 - smoothstep(0.0, 0.05, length(frac(pp) - po))) * step(0.72, hash12(pid + 11.0));
    col += lightC * speck * tw * POLLEN * (0.25 + 0.9 * beams + 0.5 * exp(-sd * 1.5));

    // a few leaves spiralling down
    if (FALLING_LEAVES > 0.0)
    {
        for (int k = 0; k < 5; k++)
        {
            float fk = float(k);
            float life = frac(t * (0.030 + 0.012 * hash11(fk * 3.3)) * FALLING_LEAVES + hash11(fk * 7.1));
            float2 lc = float2(hash11(fk * 5.9 + floor(t * 0.03 + fk)) * aspect + 0.10 * sin(life * 14.0 + fk), 1.05 - life * 1.15);
            float2 dl = p - lc;
            float a = life * 20.0 + fk;
            dl = float2(dl.x * cos(a) - dl.y * sin(a), dl.x * sin(a) + dl.y * cos(a));
            float leaf = 1.0 - smoothstep(0.8, 1.0, length(dl / float2(0.012, 0.005)));
            col = lerp(col, float3(0.75, 0.40, 0.08) * (0.5 + 0.8 * exp(-sd * 1.2)), leaf * 0.9);
        }
    }

    col *= BRIGHTNESS;
    col = 1.0 - exp(-col * 1.35);
    col = col * col * (3.0 - 2.0 * col) * 0.35 + col * 0.65;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.7;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
