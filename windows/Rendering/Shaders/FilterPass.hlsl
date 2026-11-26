// Second pass: upscales a frame rendered below native size and applies the picture filters and the idle dim.
Texture2D src : register(t0);
SamplerState smp : register(s0);

cbuffer Filter : register(b0)
{
    float2 texel; float brightness; float contrast;
    float saturation; float temperature; float blur; float vignette;
    float dim; float3 pad;
};

float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD) : SV_TARGET
{
    float3 c;
    if (blur > 0.002)
    {
        // 16-tap golden-angle disc, bilinear taps
        float r = blur * 28.0;
        c = src.SampleLevel(smp, uv, 0).rgb;
        for (int i = 1; i < 16; i++)
        {
            float a = i * 2.39996323;
            float d = sqrt(i / 16.0) * r;
            c += src.SampleLevel(smp, uv + float2(cos(a), sin(a)) * d * texel, 0).rgb;
        }
        c /= 16.0;
    }
    else c = src.SampleLevel(smp, uv, 0).rgb;

    c *= brightness;
    c = (c - 0.5) * contrast + 0.5;
    float l = dot(c, float3(0.2126, 0.7152, 0.0722));
    c = lerp(float3(l, l, l), c, saturation);
    c *= float3(1.0 + 0.22 * temperature, 1.0, 1.0 - 0.22 * temperature);
    float2 q = uv - 0.5;
    c *= 1.0 - vignette * smoothstep(0.2, 0.8, length(q) * 1.15);
    c *= 1.0 - dim;
    return float4(saturate(c), 1.0);
}
