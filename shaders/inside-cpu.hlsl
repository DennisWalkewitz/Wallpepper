// @name: Inside the CPU
// @tags: abstract, retro, tech, featured
// A flight over a living chip: bundles of traces carrying pulses of data, memory arrays flickering,
// processor cores glowing hotter under load, and a clock pulse rippling across the whole die.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define ZOOM 1.25             // size of the blocks (lower = closer)
#define SCROLL_SPEED 1.0      // flight speed over the chip
#define SCROLL_ANGLE 25.0     // flight direction in degrees
#define TILT 0.55             // perspective tilt (0 = straight from above)
#define TRACE_DENSITY 1.0     // traces per bundle
#define DATA_SPEED 1.0        // speed of the data pulses
#define ACTIVITY 0.7          // share of traces carrying data (0..1)
#define CORES 0.16            // share of blocks that are processor cores (0..0.5)
#define MEMORY 0.20           // share of blocks that are memory arrays (0..0.5)
#define CORE_HEAT 1.0         // how hot the cores glow under load
#define MEMORY_FLICKER 1.0    // how busy the memory cells are
#define CLOCK_PULSE 1.0       // ripple of the clock signal (0 = off)
#define CLOCK_RATE 1.0        // clock ticks
#define GLOW 1.0              // glow around traces and pulses
#define TRACE_R 0.05          // trace colour: red
#define TRACE_G 0.60          // trace colour: green
#define TRACE_B 0.58          // trace colour: blue
#define DATA_R 0.65           // data pulse colour: red
#define DATA_G 1.00           // data pulse colour: green
#define DATA_B 0.95           // data pulse colour: blue
#define HOT_R 1.00            // hot core colour: red
#define HOT_G 0.42            // hot core colour: green
#define HOT_B 0.08            // hot core colour: blue
#define BOARD_R 0.008         // silicon colour: red
#define BOARD_G 0.030         // silicon colour: green
#define BOARD_B 0.040         // silicon colour: blue
#define DEPTH_FOG 1.0         // fade towards the far end
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.55        // darkening behind terminal text

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

float boxLine(float2 f, float inset, float w)      // outline of an inset square inside a 0..1 cell
{
    float2 d = abs(f - 0.5) - (0.5 - inset);
    float sd = max(d.x, d.y);
    return 1.0 - smoothstep(w * 0.5, w, abs(sd));
}

// a bundle of parallel traces. a = coordinate along the traces (global), b = across (global), f = cell-local (along, across)
// returns x: trace line, y: data pulse, z: soft glow
float3 bundle(float a, float2 f, float b, float2 id, float lanes, float t, float aaw)
{
    float lb = b * lanes;
    float li = floor(lb);
    float lf = frac(lb) - 0.5;
    float h = hash12(float2(li, id.x * 7.0 + id.y * 13.0));
    if (h < 0.22) return float3(0.0, 0.0, 0.0);                             // some lanes stay empty
    float s0 = 0.04 + 0.30 * hash11(h * 91.0);                              // each trace starts and ends inside the block
    float s1 = 0.96 - 0.30 * hash11(h * 57.0);
    float inSeg = step(s0, f.x) * step(f.x, s1);
    float w = 0.06 + aaw * lanes;
    float trace = (1.0 - smoothstep(w, w + 0.10 + aaw * lanes, abs(lf))) * inSeg;
    // pads (vias) at both ends
    float2 e0 = float2((f.x - s0) * lanes, lf);
    float2 e1 = float2((f.x - s1) * lanes, lf);
    float pad = 1.0 - smoothstep(0.20, 0.30, min(length(e0), length(e1)));
    // data: bright head with a fading tail, running along the trace
    float dir = h > 0.6 ? 1.0 : -1.0;
    float busy = step(hash12(float2(li + floor(t * 0.15 + h * 9.0), id.x + id.y * 3.0)), ACTIVITY);
    float ph = frac(a * 1.6 * dir - t * DATA_SPEED * (0.25 + 0.5 * hash11(h * 13.0)) + h * 17.0);
    float pulse = pow(ph, 10.0) * busy * inSeg * (1.0 - smoothstep(w, w + 0.25, abs(lf)));
    float glow = exp(-abs(lf) * 5.0) * inSeg * (0.10 + 1.8 * pow(ph, 6.0) * busy);
    return float3(saturate(trace + pad), pulse + pad * 0.15 * busy, glow);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float t = Time;
    float3 traceC = float3(TRACE_R, TRACE_G, TRACE_B);
    float3 dataC = float3(DATA_R, DATA_G, DATA_B);
    float3 hotC = float3(HOT_R, HOT_G, HOT_B);
    float3 boardC = float3(BOARD_R, BOARD_G, BOARD_B);

    // tilted plane: the top of the screen is farther away
    float w = 1.0 - TILT * (0.62 - tex.y);
    float2 P = float2((tex.x - 0.5) * aspect, tex.y - 0.62) / w;
    float far = saturate((1.0 / w - 1.0) * 0.9);                            // 0 near .. 1 far
    float sa = radians(SCROLL_ANGLE);
    float2 G = P * 2.6 * ZOOM + float2(cos(sa), -sin(sa)) * t * 0.06 * SCROLL_SPEED + 40.0;
    float2 id = floor(G);
    float2 f = frac(G);
    float aaw = 2.6 * ZOOM / (Resolution.y * w) * 1.2;                      // one pixel in block units

    float kind = hash12(id + 3.0);
    float lanes = floor(15.0 * TRACE_DENSITY + 7.0 * hash12(id + 9.0));
    float3 col = boardC * (0.8 + 0.5 * vnoise(G * 3.0)) + boardC * 0.6 * step(0.92, hash12(floor(G * 40.0)));

    float lineAmt = 0.0, dataAmt = 0.0, glowAmt = 0.0;
    float3 extra = float3(0.0, 0.0, 0.0);

    if (kind < CORES)
    {
        // processor core: frame, dense logic inside, heat that follows the load
        float load = 0.5 + 0.5 * sin(t * (0.5 + hash12(id) * 0.8) + hash12(id + 1.0) * 6.28);
        load = saturate(load * 0.7 + 0.3 * vnoise(float2(t * 0.8, id.x + id.y * 7.0)));
        float frame = boxLine(f, 0.08, 0.012 + aaw) + boxLine(f, 0.13, 0.006 + aaw) * 0.6;
        float2 q = (f - 0.13) / 0.74;
        float inside = step(0.0, q.x) * step(q.x, 1.0) * step(0.0, q.y) * step(q.y, 1.0);
        float2 sub = floor(q * 3.0);
        float2 sf = frac(q * 3.0);
        float horiz = step(0.5, hash12(sub + id * 3.0));
        float fine = horiz > 0.5 ? frac(sf.y * 9.0) : frac(sf.x * 9.0);
        float logic = (1.0 - smoothstep(0.25 + aaw * 27.0, 0.45 + aaw * 27.0, abs(fine - 0.5))) * step(0.06, min(min(sf.x, sf.y), min(1.0 - sf.x, 1.0 - sf.y)));
        float tick = step(hash12(sub + id + floor(t * 6.0 * (0.5 + load))), 0.35 * load);
        lineAmt = logic * inside * 0.25;
        dataAmt = logic * inside * tick * 0.45;
        float cd = length(f - 0.5);
        extra = hotC * (exp(-cd * 2.6) * (0.35 + 2.2 * load) * CORE_HEAT * inside
                        + frame * (0.5 + 1.4 * load) * CORE_HEAT + exp(-cd * 2.0) * 0.20 * load * CORE_HEAT
                        + logic * inside * load * 0.5 * CORE_HEAT);
        glowAmt = exp(-cd * 2.2) * 0.25 * load;
    }
    else if (kind < CORES + MEMORY)
    {
        // memory array: rows of cells flickering as they are read and written
        float frame = boxLine(f, 0.07, 0.008 + aaw);
        float2 q = (f - 0.10) / 0.80;
        float inside = step(0.0, q.x) * step(q.x, 1.0) * step(0.0, q.y) * step(q.y, 1.0);
        float2 n = float2(16.0, 12.0);
        float2 cid = floor(q * n);
        float2 cf = frac(q * n) - 0.5;
        float cell = (1.0 - smoothstep(0.30, 0.38 + aaw * 14.0, max(abs(cf.x), abs(cf.y)))) * inside;
        float rate = 2.0 + 6.0 * hash12(id + 5.0);
        float on = step(hash12(cid + id * 17.0 + floor(t * rate * MEMORY_FLICKER + hash12(cid) * 3.0)), 0.16);
        float sweep = exp(-abs(q.y - frac(t * 0.35 + hash12(id))) * 26.0) * inside;   // a row being read
        lineAmt = frame + cell * 0.30;
        dataAmt = cell * (on * 0.9 + sweep * 0.7);
        glowAmt = sweep * 0.25 + on * cell * 0.3;
    }
    else
    {
        // bus: a bundle of traces, running across or down
        float3 b = (hash12(id + 21.0) > 0.5) ? bundle(G.x, f, G.y, id, lanes, t, aaw)
                                           : bundle(G.y, float2(f.y, f.x), G.x, id, lanes, t, aaw);
        lineAmt = b.x; dataAmt = b.y; glowAmt = b.z;
    }

    // seams between blocks
    float2 seam = abs(f - 0.5);
    lineAmt += (1.0 - smoothstep(0.494 - aaw, 0.498, max(seam.x, seam.y))) * 0.0;

    // clock: a ring spreading from the middle of the chip on every tick
    float ck = frac(t * 0.22 * CLOCK_RATE);
    float ring = exp(-abs(length(P) - ck * 3.2) * 9.0) * (1.0 - ck) * CLOCK_PULSE;

    col += traceC * lineAmt * (0.55 + 1.3 * ring);
    col += dataC * dataAmt * (1.3 + 1.5 * ring);
    col += (traceC * 0.6 + dataC * 0.4) * glowAmt * 0.55 * GLOW;
    col += extra;
    col += dataC * ring * 0.035;

    // depth: far blocks sink into haze, with a glow on the horizon
    col = lerp(col, boardC * 1.5 + traceC * 0.10, far * 0.85 * DEPTH_FOG);
    col += traceC * exp(-tex.y * 9.0) * 0.20 * DEPTH_FOG * step(0.01, TILT);

    col *= BRIGHTNESS;
    col = 1.0 - exp(-col * 1.5);
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
