// @name: Mandelbrot
// @tags: abstract, math, calming
// @light: ITERATIONS=90
// Endless smooth dive into the Mandelbrot set (or a morphing Julia set). The zoom breathes in and out so it
// never runs into float precision; smooth iteration colouring with an adjustable cosine palette.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define ITERATIONS 160        // detail (cost grows with this)
#define TARGET_X -0.743644    // zoom target: real part
#define TARGET_Y 0.131826     // zoom target: imaginary part
#define ZOOM_DEPTH 11.0       // how deep the dive goes (powers of two; max about 15)
#define ZOOM_SPEED 1.0        // speed of the dive
#define ROTATION 0.15         // slow rotation while zooming
#define JULIA 0               // 1 = Julia set whose constant drifts over time
#define JULIA_RADIUS 0.7885   // Julia constant radius
#define PALETTE_SHIFT 0.55    // where the colour cycle starts (0..1)
#define PALETTE_SCALE 0.045   // colour bands per iteration
#define PALETTE_SPEED 0.02    // colour cycling speed
#define COLOR_R 0.0           // palette phase: red
#define COLOR_G 0.10          // palette phase: green
#define COLOR_B 0.20          // palette phase: blue
#define INSIDE_GLOW 0.0       // brightness inside the set (0 = black)
#define SATURATION 1.0        // colour saturation
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

float3 palette(float t)
{
    float3 ph = float3(COLOR_R, COLOR_G, COLOR_B);
    return 0.5 + 0.5 * cos(6.2831853 * (t + ph));
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 p = float2((tex.x - 0.5) * aspect, 0.5 - tex.y) * 2.4;
    float t = Time * ZOOM_SPEED;

    // breathing zoom: smoothly in, smoothly out (stays inside float precision)
    float phase = 0.5 - 0.5 * cos(t * 0.07);
    float zoom = exp2(phase * ZOOM_DEPTH);
    float a = t * 0.05 * ROTATION * 6.2831853;
    float ca = cos(a), sa = sin(a);
    p = float2(p.x * ca - p.y * sa, p.x * sa + p.y * ca);

    float2 c = float2(TARGET_X, TARGET_Y) + p / zoom;
    float2 z = float2(0.0, 0.0);
    if (JULIA > 0)
    {
        z = p * (1.0 + 0.0 * zoom) * 0.75;
        float ja = Time * 0.11;
        c = JULIA_RADIUS * float2(cos(ja), sin(ja * 1.31 + 0.5));
    }

    float n = 0.0;
    float m2 = 0.0;
    float trap = 1e9;
    [loop]
    for (int i = 0; i < ITERATIONS; i++)
    {
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
        m2 = dot(z, z);
        trap = min(trap, m2);
        if (m2 > 256.0) break;
        n += 1.0;
    }

    float3 col;
    if (m2 > 256.0)
    {
        float sn = n + 1.0 - log2(max(log2(m2) * 0.5, 1e-6));          // smooth iteration count
        col = palette(sn * PALETTE_SCALE + PALETTE_SHIFT + Time * PALETTE_SPEED);
        col *= 0.35 + 0.65 * saturate(sn / 24.0);                      // darker far from the set
    }
    else
    {
        col = palette(sqrt(trap) * 1.5 + PALETTE_SHIFT) * INSIDE_GLOW; // orbit-trap glow inside
    }

    float l = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(float3(l, l, l), col, SATURATION) * BRIGHTNESS;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.7;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    return float4(saturate(col), 1.0);
}
