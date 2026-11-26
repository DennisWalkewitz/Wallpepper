// @name: Zen Garden
// @tags: meditative, calming, nature
// A raked sand garden seen from above. The grooves run straight across and curve into rings around the stones.
// The sun moves slowly, so the tiny shadows in the grooves and the long shadows of the stones wander over the day.
// Cloud shadows pass, and now and then a blossom petal drifts across.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define STONES 3              // number of stones (0..4)
#define STONE_SIZE 1.0        // size of the stones
#define RAKE_SPACING 1.0      // distance between the grooves
#define RAKE_DEPTH 1.0        // how deep the grooves look
#define RAKE_ANGLE 8.0        // direction of the straight grooves in degrees
#define RAKE_WAVE 0.4         // gentle waviness of the straight grooves
#define RING_REACH 1.0        // how far the rings around the stones extend
#define SUN_ANGLE 40.0        // direction of the sunlight in degrees
#define SUN_SPEED 1.0         // how fast the sun moves round (0 = fixed)
#define SUN_HEIGHT 0.45       // lower sun = longer shadows
#define CLOUD_SHADOWS 0.6     // soft shadows of passing clouds
#define MOSS 0.4              // moss on the stones
#define PETALS 1.0            // drifting blossom petals (0 = none)
#define GRAIN 1.0             // sand grain
#define SAND_R 0.86           // sand colour: red
#define SAND_G 0.80           // sand colour: green
#define SAND_B 0.68           // sand colour: blue
#define STONE_R 0.56          // stone colour: red
#define STONE_G 0.55          // stone colour: green
#define STONE_B 0.54          // stone colour: blue
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

float2 stonePos(int i, float aspect)
{
    if (i == 0) return float2(0.30 * aspect, 0.42);
    if (i == 1) return float2(0.68 * aspect, 0.62);
    if (i == 2) return float2(0.76 * aspect, 0.30);
    return float2(0.18 * aspect, 0.74);
}

float stoneRad(int i) { return (i == 0 ? 0.095 : (i == 1 ? 0.068 : (i == 2 ? 0.045 : 0.034))) * STONE_SIZE; }

// signed distance to stone i (slightly irregular outline)
float stoneDist(float2 p, int i, float aspect)
{
    float2 d = p - stonePos(i, aspect);
    float a = atan2(d.y, d.x);
    float wob = 1.0 + 0.10 * sin(a * 3.0 + float(i) * 2.1) + 0.05 * sin(a * 7.0 + float(i) * 5.3);
    return length(d * float2(1.0, 1.0 + 0.25 * sin(float(i) * 3.7))) - stoneRad(i) * wob;
}

// height of the raked sand (0..1): straight grooves far from the stones, rings close to them,
// with a narrow smooth band where the two patterns meet
float sandHeight(float2 p, float aspect)
{
    float ra = radians(RAKE_ANGLE);
    float spacing = 0.021 * RAKE_SPACING;
    float fs = (p.x * sin(ra) + p.y * cos(ra) + 0.010 * RAKE_WAVE * sin(p.x * 5.0) + 0.006 * RAKE_WAVE * sin(p.x * 11.0 + 2.0)) / spacing;
    float straight = 0.5 - 0.5 * cos(fs * 6.2831853);
    float h = straight;
    float wStraight = 1.0;
    float rings = 0.0;
    for (int i = 0; i < STONES; i++)
    {
        float d = stoneDist(p, i, aspect);
        float reach = (0.045 + stoneRad(i) * 0.8) * RING_REACH;
        float wr = 1.0 - smoothstep(reach * 0.80, reach, d);
        rings = max(rings, wr * (0.5 - 0.5 * cos(d / spacing * 6.2831853)));
        wStraight *= smoothstep(reach, reach * 1.25, d);
    }
    return max(straight * wStraight, rings);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2(tex.x * aspect, tex.y);
    float t = Time;
    float3 sandC = float3(SAND_R, SAND_G, SAND_B);
    float3 stoneC = float3(STONE_R, STONE_G, STONE_B);

    float sa = radians(SUN_ANGLE) + t * 0.01 * SUN_SPEED;
    float3 L = normalize(float3(cos(sa), sin(sa), SUN_HEIGHT));

    // sand: light the grooves from the side
    float e = 0.0012;
    float h0 = sandHeight(p, aspect);
    float hx = sandHeight(p + float2(e, 0.0), aspect);
    float hy = sandHeight(p + float2(0.0, e), aspect);
    float3 n = normalize(float3(-(hx - h0) / e * 0.0022 * RAKE_DEPTH, -(hy - h0) / e * 0.0022 * RAKE_DEPTH, 1.0));
    float lit = clamp(dot(n, L) / max(L.z, 0.2), 0.0, 1.6);
    float grain = (hash12(floor(p * Resolution.y)) - 0.5) * 0.07 + (vnoise(p * 90.0) - 0.5) * 0.06;
    float3 col = sandC * (0.62 + 0.36 * lit) * (1.0 + grain * GRAIN) * (0.95 + 0.05 * h0);
    col *= 0.95 + 0.10 * vnoise(p * 2.5);                                    // gentle unevenness

    // stones: long soft shadows first, then the stones themselves
    for (int i = 0; i < STONES; i++)
    {
        float R = stoneRad(i);
        float shLen = R * 0.9 / max(SUN_HEIGHT, 0.12);
        float sh = 0.0;
        for (int k = 1; k <= 4; k++)
            sh = max(sh, (1.0 - smoothstep(-0.004, 0.012 + 0.004 * float(k), stoneDist(p + L.xy * shLen * float(k) / 4.0, i, aspect))) * (1.0 - float(k) * 0.14));
        col *= 1.0 - sh * 0.42;
    }
    for (int j = 0; j < STONES; j++)
    {
        float d = stoneDist(p, j, aspect);
        col *= 1.0 - 0.28 * exp(-max(d, 0.0) * 140.0);                       // contact shadow
        float R = stoneRad(j);
        if (d < 0.004)
        {
            float2 q = (p - stonePos(j, aspect)) / R;
            float dome = sqrt(saturate(1.0 - dot(q, q) * 0.72));
            float3 sn = normalize(float3(q * 1.25 + (float2(vnoise(p * 45.0), vnoise(p * 45.0 + 9.0)) - 0.5) * 0.7, dome * 0.9 + 0.12));
            float sl = pow(saturate(dot(sn, L) * 0.5 + 0.5), 1.6) * 1.35 + 0.12;
            sl *= 0.55 + 0.45 * smoothstep(0.0, 0.35, dome);                 // darker towards the ground
            float speck = vnoise(p * 140.0) * 0.25 + vnoise(p * 30.0 + float(j) * 7.0) * 0.35;
            float3 sc = stoneC * (0.80 + speck * 0.7) * sl;
            sc += float3(1.0, 0.97, 0.9) * pow(saturate(dot(sn, normalize(L + float3(0.0, 0.0, 1.0)))), 24.0) * 0.18;
            float moss = smoothstep(0.62, 0.80, vnoise(p * 22.0 + float(j) * 13.0) + 0.15 * (1.0 - dome)) * saturate(MOSS * 1.6);
            sc = lerp(sc, float3(0.22, 0.38, 0.12) * sl * (0.8 + 0.4 * vnoise(p * 200.0)), saturate(moss) * 0.85);
            col = lerp(col, sc, 1.0 - smoothstep(-0.003, 0.003, d));
        }
    }

    // shadows of slow clouds
    float cloud = vnoise(p * 1.1 + float2(t * 0.012, t * 0.004)) * 0.6 + vnoise(p * 2.7 + float2(t * 0.018, 0.0)) * 0.4;
    col *= 1.0 - smoothstep(0.50, 0.80, cloud) * 0.28 * CLOUD_SHADOWS;

    // blossom petals drifting across, each with a small shadow
    if (PETALS > 0.0)
    {
        for (int k2 = 0; k2 < 6; k2++)
        {
            float fk = float(k2);
            float life = frac(t * (0.010 + 0.006 * hash11(fk * 3.3)) * PETALS + hash11(fk * 7.1));
            float2 c = float2(life * (aspect + 0.2) - 0.1, hash11(fk * 5.9 + floor(t * (0.010 + 0.006 * hash11(fk * 3.3)) * PETALS + hash11(fk * 7.1))) + 0.04 * sin(life * 20.0 + fk));
            float a = life * 25.0 + fk * 2.0;
            float2 dq = p - c;
            float2 ds = dq - L.xy * -0.012;
            dq = float2(dq.x * cos(a) - dq.y * sin(a), dq.x * sin(a) + dq.y * cos(a));
            ds = float2(ds.x * cos(a) - ds.y * sin(a), ds.x * sin(a) + ds.y * cos(a));
            float2 sz = float2(0.011, 0.007 * (0.6 + 0.4 * abs(sin(life * 30.0 + fk))));
            col *= 1.0 - 0.30 * (1.0 - smoothstep(0.6, 1.2, length(ds / sz)));
            col = lerp(col, float3(1.0, 0.80, 0.84) * (0.75 + 0.25 * lit), 1.0 - smoothstep(0.8, 1.0, length(dq / sz)));
        }
    }

    col *= BRIGHTNESS;
    col = col * col * (3.0 - 2.0 * col) * 0.2 + col * 0.8;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.35;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    return float4(saturate(col), 1.0);
}
