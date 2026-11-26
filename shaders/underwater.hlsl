// @name: Underwater
// @tags: water, nature
// Shallow sea floor: dancing caustic light (animated Voronoi edges, slight chromatic split), sand ripples,
// god rays from the surface, drifting plankton and depth fog.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define CAUSTICS 1.0          // caustic brightness
#define CAUSTIC_SCALE 1.0     // size of the caustic cells
#define GOD_RAYS 0.8          // light shafts
#define PARTICLES 1.0         // drifting plankton
#define SPEED 1.0             // animation speed
#define WARP 1.0              // how wobbly the light pattern is
#define COLOR_SPLIT 1.0       // rainbow fringes on the caustics
#define WATER_R 0.02          // water colour: red
#define WATER_G 0.30          // water colour: green
#define WATER_B 0.36          // water colour: blue
#define SAND_R 0.42           // sand colour: red
#define SAND_G 0.38           // sand colour: green
#define SAND_B 0.28           // sand colour: blue
#define SAND_RIPPLES 1.0      // ripple marks in the sand
#define DEPTH_FOG 1.0         // how quickly the distance fades into blue
#define VIGNETTE 0.8          // darker corners
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

// F2 - F1 of animated Voronoi: thin bright network = caustics
float caustic(float2 p, float t)
{
    float2 i = floor(p), f = frac(p);
    float d1 = 8.0, d2 = 8.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 h = hash22(i + g);
        float2 o = 0.5 + 0.42 * sin(t * (0.6 + 0.5 * h) + 6.2831 * h);
        float d = length(g + o - f);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
    }
    float e = d2 - d1;
    return pow(saturate(1.0 - e * 2.2), 5.0);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 uv = float2(tex.x * aspect, tex.y);
    float t = Time * SPEED;

    // sea floor seen at an angle: compress towards the top (farther away)
    float depth = lerp(3.0, 1.0, tex.y);                            // top = far
    float2 fp = float2((tex.x - 0.5) * aspect * depth, depth * 2.0 - t * 0.05);

    // warp for wobbly water surface
    float2 w = float2(vnoise(fp * 0.9 + t * 0.15), vnoise(fp * 0.9 - t * 0.13 + 7.0)) - 0.5;
    w += 0.5 * (float2(vnoise(fp * 2.3 - t * 0.2 + 3.0), vnoise(fp * 2.3 + t * 0.22 + 11.0)) - 0.5);
    float2 cp = fp * 3.4 / CAUSTIC_SCALE + w * 1.5 * WARP;
    float cr = caustic(cp + float2(0.012 * COLOR_SPLIT, 0.0), t);
    float cg = caustic(cp, t);
    float cb = saturate(2.0 * cg - cr);                                 // mirrored fringe without a third lookup
    float cc2 = pow(saturate(vnoise(cp * 0.9 + 3.7 + t * 0.2) * vnoise(cp * 1.7 - t * 0.15) * 2.2), 2.0);   // broad shimmer (cheap)
    float3 caus = float3(cr, cg, cb) * 0.8 + cc2 * 0.45;

    // sand: warm, rippled
    float ripple = sin(fp.y * 9.0 + vnoise(fp * 2.0) * 4.0) * 0.5 + 0.5;
    float3 sandCol = float3(SAND_R, SAND_G, SAND_B);
    float3 sand = lerp(sandCol * 0.72, sandCol, ripple * 0.6 * SAND_RIPPLES + vnoise(fp * 6.0) * 0.4);
    float3 col = sand * (0.55 + caus * CAUSTICS * 1.8);

    // water colour + depth fog (far = top = bluer and darker)
    float3 waterCol = float3(WATER_R, WATER_G, WATER_B);
    float3 water = lerp(waterCol, waterCol * float3(0.0, 0.4, 0.6), 1.0 - tex.y);
    col = lerp(col * float3(0.6, 0.95, 0.95), water, saturate((depth - 1.0) * 0.38 * DEPTH_FOG));

    // god rays from the top
    float ang = (tex.x - 0.5) * 2.0 + (1.0 - tex.y) * 0.35;
    float rays = vnoise(float2(ang * 7.0 + t * 0.12, t * 0.05)) * vnoise(float2(ang * 17.0 - t * 0.2, 3.0));
    rays = pow(rays, 1.5) * saturate(1.0 - tex.y * 1.1);
    col += float3(0.35, 0.65, 0.7) * rays * GOD_RAYS * 0.6;

    // plankton: small soft dots drifting up
    float2 pp = uv * 18.0 + float2(sin(t * 0.2) * 0.5, t * 0.35);
    float2 pid = floor(pp);
    float2 po = hash22(pid) * 0.8 + 0.1;
    float pd = length(frac(pp) - po);
    float part = (1.0 - smoothstep(0.0, 0.06, pd)) * step(0.82, hash12(pid + 4.0));
    col += float3(0.6, 0.85, 0.9) * part * 0.35 * PARTICLES * (0.4 + 0.6 * tex.y);

    col = 1.0 - exp(-col * 1.4 * BRIGHTNESS);
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * VIGNETTE;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
