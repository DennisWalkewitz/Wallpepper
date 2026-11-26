// One triangle that covers the whole target; no vertex buffer needed.
struct VSOut { float4 pos : SV_POSITION; float2 tex : TEXCOORD; };

VSOut main(uint id : SV_VertexID)
{
    VSOut o;
    o.tex = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.tex * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}
