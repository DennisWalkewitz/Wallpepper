// @name: Mandala
// @tags: meditative, calming, abstract
// A slowly turning mandala of glowing petals. Layers rotate against each other, the whole figure breathes,
// and the colours drift around the wheel. Symmetry, layers, petal shape and colours are adjustable.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define SYMMETRY 8            // number of petals around the circle (3..24)
#define LAYERS 6              // rings of petals (1..9)
#define ROTATION_SPEED 1.0    // how fast the layers turn
#define BREATHING 1.0         // slow pulsing of the whole figure
#define PETAL_DEPTH 1.0       // how deep the petals are cut
#define PETAL_POINT 1.0       // 0 = round petals, 2 = pointed
#define LINE_GLOW 1.0         // brightness of the outlines
#define FILL 0.6              // soft colour fill inside the petals
#define DOTS 1.0              // beads at the petal tips
#define SIZE 1.0              // size of the mandala
#define HUE_DRIFT 1.0         // how fast the colours travel
#define COLOR_A_R 0.95        // colour A: red
#define COLOR_A_G 0.55        // colour A: green
#define COLOR_A_B 0.25        // colour A: blue
#define COLOR_B_R 0.45        // colour B: red
#define COLOR_B_G 0.25        // colour B: green
#define COLOR_B_B 0.85        // colour B: blue
#define COLOR_C_R 0.15        // colour C: red
#define COLOR_C_G 0.75        // colour C: green
#define COLOR_C_B 0.80        // colour C: blue
#define BACK_R 0.025          // background colour: red
#define BACK_G 0.015          // background colour: green
#define BACK_B 0.050          // background colour: blue
#define CENTER_GLOW 1.0       // light in the middle
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 wheel(float x)
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
    float t = Time;
    float breath = 1.0 + 0.035 * BREATHING * sin(t * 0.35);
    float r = length(p) / (0.46 * SIZE * breath);
    float ang = atan2(p.y, p.x) / 6.2831853;                                 // -0.5..0.5 turns
    float n = float(SYMMETRY);

    float3 back = float3(BACK_R, BACK_G, BACK_B);
    float3 col = back * (1.2 - r * 0.5) + wheel(t * 0.01 * HUE_DRIFT) * 0.03 * exp(-r * 1.5);
    float hueT = t * 0.012 * HUE_DRIFT;

    for (int i = 0; i < LAYERS; i++)
    {
        float fi = float(i);
        float li = (fi + 1.0) / float(LAYERS);                               // 0..1 from the centre outwards
        float dir = (i % 2 == 0) ? 1.0 : -1.0;
        float rot = t * 0.012 * ROTATION_SPEED * dir * (1.5 - li) + fi * 0.5 / n;
        float mult = (i % 3 == 2) ? 2.0 : 1.0;                               // every third ring has twice the petals
        float a = frac((ang + rot) * n * mult);                              // 0..1 across one petal
        float m = abs(a - 0.5) * 2.0;                                        // 0 at the tip, 1 between petals
        float shape = pow(m, 0.6 + 0.9 * PETAL_POINT);                       // petal profile
        float tipR = li * (1.0 + 0.02 * BREATHING * sin(t * 0.5 + fi));
        float edgeR = tipR * (1.0 - 0.34 * PETAL_DEPTH * shape / sqrt(mult));
        float d = r - edgeR;                                                 // < 0 inside the petal ring
        float w = 0.0035 + 0.004 * li;
        float stroke = exp(-pow(d / w, 2.0));
        float glow = exp(-abs(d) * 26.0) * 0.35;
        float3 c = wheel(li * 0.9 + hueT + fi * 0.07);
        float fill = smoothstep(0.0, -0.22 * li, d) * (1.0 - smoothstep(-0.30 * li, -0.42 * li, d));   // band inside the edge
        col += c * fill * 0.10 * FILL * (0.6 + 0.4 * (1.0 - m));
        col += c * (stroke * 0.85 + glow) * LINE_GLOW * (0.75 + 0.25 * sin(t * 0.4 + fi * 1.3));
        // bead at each petal tip
        float2 bp = float2((a - 0.5) * 6.2831853 / (n * mult) * tipR, r - tipR * 1.035);
        col += c * exp(-dot(bp, bp) * 9000.0 / (0.3 + li)) * 1.3 * DOTS;
    }

    // centre: a small flower of light and a calm halo around everything
    float core = exp(-r * r * 90.0) + 0.5 * exp(-r * 9.0);
    col += wheel(hueT + 0.1) * core * 0.55 * CENTER_GLOW;
    col += float3(1.0, 1.0, 1.0) * exp(-r * r * 500.0) * 0.6 * CENTER_GLOW;
    col += wheel(hueT + 0.5) * exp(-pow((r - 1.08) * 30.0, 2.0)) * 0.25 * LINE_GLOW;      // outer circle

    col *= BRIGHTNESS;
    col = 1.0 - exp(-col * 1.4);
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.8;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
