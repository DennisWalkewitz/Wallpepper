// @name: Digital Rain
// @tags: retro, tech, fun, featured
// @light: DEPTH_LAYERS=1
// The classic green code rain: columns of mirrored signs light up cell by cell behind a white-hot head and fade
// down a long phosphor trail, in up to three layers of depth. The 48 signs are drawn as strokes, with no font
// and no texture, so they stay crisp at any size.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define GLYPH_SIZE 28.0       // height of one sign in pixels on a 1080p screen; it scales with the resolution (10..64)
#define COLUMN_SPACING 1.0    // distance between the columns: lower packs them tighter (0.7..1.8)
#define STROKE_WEIGHT 1.0     // thickness of the strokes the signs are drawn with (0.5..2)
#define MIRROR_GLYPHS 1       // 1 = signs are mirrored as in the film, 0 = the right way round
#define FALL_SPEED 1.0        // how fast the streams fall (0..4)
#define SPEED_VARIATION 0.6   // how much the speed differs from column to column (0..1)
#define TRAIL_LENGTH 1.0      // length of the fading trail behind each head (0.3..3)
#define TRAIL_FALLOFF 1.0     // how quickly a trail darkens: low = evenly lit, high = short bright tip (0.3..3)
#define STREAM_DENSITY 0.7    // how much of each column is filled with streams (0.05..1)
#define EXTRA_STREAMS 0.4     // share of columns with a second, faster stream that overtakes the first (0..1)
#define CLUSTERING 0.6        // how unevenly the streams are spread: 0 = the same everywhere, 1 = busy and quiet areas (0..1)
#define GLYPH_CHANGE 1.0      // how often the changing signs swap to another sign (0..5)
#define CHANGING_SHARE 0.3    // share of the lit signs that keep changing; the rest hold still (0..1)
#define HEAD_FLICKER 1.0      // how fast the leading sign of a stream flickers through signs (0..3)
#define HEAD_BRIGHTNESS 1.0   // brightness of the leading sign (0..2)
#define HEAD_FADE 3.0         // how many signs behind the head still carry some of its colour (0..10)
#define SPARKLE 0.5           // occasional brighter signs inside a trail (0..2)
#define SHIMMER 0.35          // slight flicker of each sign, like an unsteady phosphor (0..1)
#define GLOW 1.0              // soft bloom around bright signs (0..2)
#define GLOW_SIZE 1.0         // how far the bloom reaches (0.5..1.6)
#define TRAIL_SMEAR 1.0       // faint vertical streak of light along each trail (0..3)
#define DEPTH_LAYERS 3        // layers of rain behind each other (1..3)
#define DEPTH_SCALE 0.6       // size of each farther layer compared with the one in front of it (0.4..0.85)
#define DEPTH_DIM 0.36        // brightness of each farther layer compared with the one in front of it (0.1..1)
#define DEPTH_BLUR 1.0        // how much the farther layers are out of focus (0..3)
#define TRAIL_R 0.08          // trail colour: red
#define TRAIL_G 1.00          // trail colour: green
#define TRAIL_B 0.24          // trail colour: blue
#define HEAD_R 0.78           // colour of the leading sign: red
#define HEAD_G 1.00           // colour of the leading sign: green
#define HEAD_B 0.84           // colour of the leading sign: blue
#define BACK_R 0.000          // background colour: red
#define BACK_G 0.012          // background colour: green
#define BACK_B 0.006          // background colour: blue
#define HAZE 1.0              // faint drifting mist in the trail colour (0..4)
#define COLOUR_VARIATION 0.0  // 0 = one colour, 1 = every column has its own hue (0..1)
#define GLITCH 0.4            // how often a whole column flashes and scrambles (0..3)
#define PULSE 0.0             // slow waves of brightness travelling through the rain (0..1)
#define DRIFT 0.0             // slow sideways sway of the view; near layers move more than far ones (0..2)
#define TILT 0.0              // lean the columns, in degrees (-20..20)
#define SCANLINES 0.12        // dark horizontal lines as on a tube monitor (0..1)
#define VIGNETTE 0.6          // darker corners (0..1.5)
#define BRIGHTNESS 1.0        // overall brightness (0.2..2.5)
#define TEXT_REACT 1          // 1 = the rain reacts to terminal text: it flares just above the letters and thins out under them, 0 = off
#define TEXT_VEIL 0.6         // darkening behind terminal text (0..1)

float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// x modulo m, always positive (fmod keeps the sign of x in HLSL but not in GLSL)
float wrap(float x, float m) { return x - m * floor(x / m); }

float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(hash12(i), hash12(i + float2(1.0, 0.0)), f.x), lerp(hash12(i + float2(0.0, 1.0)), hash12(i + float2(1.0, 1.0)), f.x), f.y);
}

float3 hue(float h)
{
    return saturate(float3(abs(h * 6.0 - 3.0) - 1.0, 2.0 - abs(h * 6.0 - 2.0), 2.0 - abs(h * 6.0 - 4.0)));
}

// ---- the signs ------------------------------------------------------------------------------------------
// A sign is up to eight straight strokes between the points of a 7 x 9 lattice (x 0..6, y 0..8 downwards).
// A lattice point is the number x + 7 y, a stroke is a + 63 b for its two ends, and two strokes share one
// float as s1 + 4096 s2: whole numbers below 2^24, which a float holds exactly. Signs with fewer strokes
// repeat their first one.

// distance in pixels from p to one packed stroke; unit = size of a lattice step in pixels
float strokeDistance(float2 p, float code, float2 unit)
{
    float eb = floor((code + 0.5) / 63.0);
    float ea = code - eb * 63.0;
    float ay = floor((ea + 0.5) / 7.0);
    float by = floor((eb + 0.5) / 7.0);
    float2 a = float2(ea - ay * 7.0, ay) * unit;
    float2 ab = float2(eb - by * 7.0, by) * unit - a;
    float2 ap = p - a;
    float h = saturate(dot(ap, ab) / max(dot(ab, ab), 0.0001));
    return length(ap - ab * h);
}

float strokePair(float2 p, float two, float2 unit)
{
    float s2 = floor(two / 4096.0);
    float s1 = two - s2 * 4096.0;
    return min(strokeDistance(p, s1, unit), strokeDistance(p, s2, unit));
}

// distance in pixels from p to the nearest stroke of sign number id (0..47)
float glyphDistance(float2 p, float id, float2 unit)
{
    float4 g = float4(8311610.0, 14895915.0, 3384122.0, 3384122.0);
    // signs in the spirit of half-width kana
    if (id == 1.0) g = float4(5219378.0, 10663666.0, 4402862.0, 4400178.0);
    if (id == 2.0) g = float4(13460220.0, 3132858.0, 3130108.0, 3130108.0);
    if (id == 3.0) g = float4(15500538.0, 12721416.0, 5219578.0, 5219578.0);
    if (id == 4.0) g = float4(14275834.0, 8015611.0, 5221863.0, 5219578.0);
    if (id == 5.0) g = float4(10753281.0, 5250758.0, 5248257.0, 5248257.0);
    if (id == 6.0) g = float4(5224166.0, 15129003.0, 7235302.0, 7235302.0);
    if (id == 7.0) g = float4(9295098.0, 15391197.0, 5219578.0, 5219578.0);
    if (id == 8.0) g = float4(9663479.0, 7183620.0, 4158455.0, 4158455.0);
    if (id == 9.0) g = float4(9859898.0, 16158190.0, 3384122.0, 3384122.0);
    if (id == 10.0) g = float4(8340737.0, 14404751.0, 5248257.0, 5248257.0);
    if (id == 11.0) g = float4(3393254.0, 14875109.0, 7236016.0, 7235302.0);
    if (id == 12.0) g = float4(7053821.0, 14866788.0, 2085373.0, 2085373.0);
    if (id == 13.0) g = float4(6493553.0, 14875109.0, 5707121.0, 5707121.0);
    if (id == 14.0) g = float4(7053628.0, 14866802.0, 1294652.0, 1294652.0);
    if (id == 15.0) g = float4(9819834.0, 7056941.0, 7055034.0, 7055034.0);
    if (id == 16.0) g = float4(10117946.0, 14290415.0, 3384122.0, 3384122.0);
    if (id == 17.0) g = float4(5218937.0, 15354537.0, 2595831.0, 2593401.0);
    if (id == 18.0) g = float4(16047569.0, 14491089.0, 14491089.0, 14491089.0);
    if (id == 19.0) g = float4(14396431.0, 12645570.0, 12647439.0, 12647439.0);
    if (id == 20.0) g = float4(15238394.0, 14330924.0, 5219578.0, 5219578.0);
    if (id == 21.0) g = float4(10441909.0, 4939635.0, 4936885.0, 4936885.0);
    if (id == 22.0) g = float4(12590098.0, 12660578.0, 12659730.0, 12659730.0);
    if (id == 23.0) g = float4(14612894.0, 10087328.0, 10086814.0, 10086814.0);
    if (id == 24.0) g = float4(8889084.0, 14408918.0, 3130108.0, 3130108.0);
    if (id == 25.0) g = float4(8340737.0, 5250758.0, 5248257.0, 5248257.0);
    if (id == 26.0) g = float4(7053628.0, 15162284.0, 1294652.0, 1294652.0);
    if (id == 27.0) g = float4(10344669.0, 9297518.0, 9296093.0, 9296093.0);
    if (id == 28.0) g = float4(14604580.0, 10829512.0, 9586980.0, 9586980.0);
    if (id == 29.0) g = float4(8889146.0, 14875109.0, 3384122.0, 3384122.0);
    // digits
    if (id == 30.0) g = float4(5177598.0, 15682532.0, 11079298.0, 574364.0);
    if (id == 31.0) g = float4(15237324.0, 839484.0, 835788.0, 835788.0);
    if (id == 32.0) g = float4(1040524.0, 7050480.0, 16231907.0, 573580.0);
    if (id == 33.0) g = float4(6218106.0, 12556847.0, 15216372.0, 1550992.0);
    if (id == 34.0) g = float4(10725537.0, 9051848.0, 9050273.0, 9050273.0);
    if (id == 35.0) g = float4(5419014.0, 10683964.0, 15682553.0, 11079298.0);
    if (id == 36.0) g = float4(10954473.0, 15724144.0, 10779660.0, 5781072.0);
    if (id == 37.0) g = float4(14991738.0, 1548729.0, 1548666.0, 1548666.0);
    if (id == 38.0) g = float4(4923708.0, 14625385.0, 12644218.0, 320481.0);
    if (id == 39.0) g = float4(5302423.0, 532752.0, 5477236.0, 10475824.0);
    // symbols
    if (id == 40.0) g = float4(13603897.0, 4428857.0, 4428857.0, 4428857.0);
    if (id == 41.0) g = float4(12560058.0, 7055034.0, 7055034.0, 7055034.0);
    if (id == 42.0) g = float4(13461626.0, 8890490.0, 8890490.0, 8890490.0);
    if (id == 43.0) g = float4(13969530.0, 8891482.0, 8890490.0, 8890490.0);
    if (id == 44.0) g = float4(14475642.0, 8384378.0, 1548666.0, 1548666.0);
    if (id == 45.0) g = float4(15761724.0, 14716732.0, 1296444.0, 1294652.0);
    if (id == 46.0) g = float4(15381995.0, 6206955.0, 6206955.0, 6206955.0);
    if (id == 47.0) g = float4(14051056.0, 7276272.0, 7276272.0, 7276272.0);
    float d = min(strokePair(p, g.x, unit), strokePair(p, g.y, unit));
    return min(d, min(strokePair(p, g.z, unit), strokePair(p, g.w, unit)));
}

// ---- the streams ----------------------------------------------------------------------------------------
// A column is an endless tape that slides down at a constant speed. The tape is cut into equal pieces, and
// each piece carries at most one stream with its own length and position, so a single lookup tells any cell
// which stream it belongs to. Speed and piece length differ from column to column.
// y is a row number: whole for a cell, fractional for the glow.
// Returns x = rows behind the head (negative = ahead of it), y = length of the trail in rows,
// z = a random number that names the stream, or -1.0 where the piece is empty.
float3 streamAt(float colId, float seed, float y, float t, float rowsPerSecond, float busy)
{
    float hc = hash12(float2(colId, seed));
    float speed = rowsPerSecond * lerp(1.0, 0.4 + 1.2 * hc, SPEED_VARIATION);
    float longest = max(28.0 * TRAIL_LENGTH, 4.0);
    float piece = (longest * (1.15 + 1.3 * (1.0 - STREAM_DENSITY)) + 5.0) * (0.7 + 0.7 * frac(hc * 17.31));
    float w = y - t * speed - hc * 977.0;
    float s = floor(w / piece);
    float sid = wrap(s, 613.0);
    float2 key = float2(colId * 1.37 + sid * 0.71, seed * 2.3 + sid);
    float len = min(longest * (0.3 + 0.7 * hash12(key + 3.1)), piece - 6.0);
    float head = len + hash12(key + 5.7) * (piece - 3.0 - len);
    float present = step(hash12(key + 8.3), (0.3 + 0.7 * STREAM_DENSITY) * busy);
    return float3(head - (w - s * piece), len, lerp(-1.0, hash12(key + 1.9), present));
}

// how busy a column is: neighbouring columns are alike, so the rain has crowded and quiet areas
float columnBusy(float colId, float seed)
{
    float x = colId * 0.13 + seed * 3.7;
    float f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(1.0, 0.25 + 1.5 * lerp(hash11(floor(x)), hash11(floor(x) + 1.0), f), CLUSTERING);
}

// brightness of a cell that is `behind` rows behind the head of a stream with a trail of `len` rows
float trailLight(float behind, float len)
{
    float x = saturate(behind / len);
    float body = pow(1.0 - x, 1.35 * TRAIL_FALLOFF) * (0.78 + 0.22 * exp(-behind * 0.3));
    return body * step(0.0, behind) * step(behind, len);
}

// the same without the hard front edge: light spills a little ahead of the head
float glowLight(float behind, float len)
{
    return behind < 0.0 ? exp(behind * 2.5) : trailLight(behind, len);
}

// Light that the two streams of a column throw on a pixel dx columns from the column's centre line:
// x = glow of the trails, y = glow of the heads. y is a fractional row number.
float2 columnGlow(float colId, float seed, float y, float t, float rowsPerSecond, float dx, float aspect)
{
    float busy = columnBusy(colId, seed);
    float3 sa = streamAt(colId, seed, y - 0.5, t, rowsPerSecond, busy);
    float3 sb = streamAt(colId, seed + 41.0, y - 0.5, t, rowsPerSecond * 1.8, busy);
    float hasA = step(0.0, sa.z);
    float hasB = step(hash12(float2(colId, seed + 7.0)), EXTRA_STREAMS) * step(0.0, sb.z);
    float fade = saturate(1.0 - dx * dx / 2.25);                  // nothing reaches past one and a half columns
    fade *= fade;
    float across = dx * aspect / GLOW_SIZE;
    float ya = (y - floor(sa.x + y - 0.5) - 0.5) / GLOW_SIZE;     // rows from the centre of the head's cell
    float yb = (y - floor(sb.x + y - 0.5) - 0.5) / GLOW_SIZE;
    float heads = hasA * exp(-(across * across + ya * ya) * 3.4) + hasB * exp(-(across * across + yb * yb) * 3.4);
    float trails = max(glowLight(sa.x, sa.y) * hasA, glowLight(sb.x, sb.y) * hasB) * exp(-across * across * 3.0);
    return float2(trails, heads) * fade;
}

// One layer of rain. Returns its light in rgb and how much it covers the layers behind it in a.
float4 rainLayer(float2 px, float size, float seed, float t, float blur, float depth)
{
    float cellH = max(floor(size + 0.5), 4.0);
    float cellW = max(floor(size * 0.64 * COLUMN_SPACING + 0.5), 3.0);
    float2 cell = float2(cellW, cellH);
    float2 gp = px / cell;
    float colId = floor(gp.x);
    float row = floor(gp.y);
    float2 f = gp - float2(colId, row);
    float rowsPerSecond = 9.0 * FALL_SPEED * (1.0 - 0.18 * depth);
    float3 trailC = float3(TRAIL_R, TRAIL_G, TRAIL_B);
    float3 headC = float3(HEAD_R, HEAD_G, HEAD_B);
    float colHash = hash12(float2(colId, seed + 7.0));
    if (COLOUR_VARIATION > 0.0)
    {
        float3 own = hue(frac(colHash * 7.31 + seed * 0.13));
        trailC = lerp(trailC, own, COLOUR_VARIATION);
        headC = lerp(headC, lerp(own, float3(1.0, 1.0, 1.0), 0.7), COLOUR_VARIATION);
    }

    // the two streams this cell may belong to: the column's own and, in some columns, a faster one
    float busy = columnBusy(colId, seed);
    float3 sa = streamAt(colId, seed, row, t, rowsPerSecond, busy);
    float3 sb = streamAt(colId, seed + 41.0, row, t, rowsPerSecond * 1.8, busy);
    float hasB = step(colHash, EXTRA_STREAMS) * step(0.0, sb.z);
    float la = trailLight(sa.x, sa.y) * step(0.0, sa.z);
    float lb = trailLight(sb.x, sb.y) * hasB;
    float3 s = lb > la ? sb : sa;
    float light = max(la, lb);
    float behind = s.x;
    float isHead = step(0.0, behind) * step(behind, 1.0) * step(0.0001, light);
    float afterglow = saturate(1.0 - (behind - 1.0) / max(HEAD_FADE, 0.01));
    float pale = max(isHead, step(1.0, behind) * afterglow * afterglow * step(0.0001, light));

    // a glitch takes a whole column for a moment: it flashes, and every cell shows a scrambling sign
    float slot = t * 0.7 + colHash * 13.0;
    float slotId = wrap(floor(slot), 997.0);
    float glitch = step(hash12(float2(colId + seed, slotId)), 0.035 * GLITCH) * pow(1.0 - frac(slot), 3.0);
    glitch *= 0.6 + 0.4 * step(0.5, hash11(wrap(floor(t * 24.0), 977.0) + colId));

    // every cell has its own character: a brightness, a chance to sparkle, a timer that swaps its sign
    float cellHash = hash12(float2(colId * 1.13 + seed, row * 0.71 + s.z * 57.0));
    float cellRand = hash12(float2(row * 1.91 + seed * 3.0, colId * 0.53 + 11.0));
    float spark = step(1.0 - 0.07 * SPARKLE, cellHash);
    float level = light * (0.62 + 0.38 * cellRand) * (1.0 + 1.1 * spark);
    level *= 1.0 - SHIMMER * 0.45 * hash12(float2(colId + row * 0.37, wrap(floor(t * 9.0 + cellRand * 9.0), 811.0)));
    level = lerp(level, 1.0, isHead);
    pale = saturate(pale + 0.22 * spark * light);
    level = max(level, 0.22 * glitch) * (1.0 + 2.0 * glitch);
    if (PULSE > 0.0)
    {
        float2 wp = px / Resolution.y;
        float wave = 0.5 + 0.5 * sin(wp.y * 5.0 - t * 0.45 + sin(wp.x * 2.3 + t * 0.11) * 1.6);
        level *= 1.0 + PULSE * (2.2 * pow(wave, 6.0) - 0.35);
    }

    float changing = step(cellRand, CHANGING_SHARE);
    float tick = floor(t * GLYPH_CHANGE * (0.35 + 1.6 * cellHash) + cellRand * 31.0) * changing;
    tick = lerp(tick, floor(t * 11.0 * HEAD_FLICKER + cellRand * 5.0), isHead);
    tick += floor(t * 18.0) * step(0.001, glitch);
    float pick = hash12(float2(colId * 0.37 + wrap(tick, 541.0) * 1.93, row * 1.17 + s.z * 89.0 + seed));
    // three in four are kana-like signs (0..29), the rest digits (30..39) and a few symbols (40..47)
    float id = floor(pick * 39.47);
    if (pick >= 0.76) id = 30.0 + floor((pick - 0.76) * 62.49);
    if (pick >= 0.92) id = 40.0 + floor((pick - 0.92) * 99.99);

    // the sign itself: anti-aliased strokes of constant thickness
    float halfW = max(0.052 * cellH * STROKE_WEIGHT, 0.35);
    float glyphH = 0.76 * cellH - 2.0 * halfW;
    float glyphW = max(min(0.50 * cellH, cellW - 2.0 * halfW - 0.17 * cellH), 2.0);
    float2 p = f * cell - 0.5 * (cell - float2(glyphW, glyphH));
    if (MIRROR_GLYPHS > 0) p.x = glyphW - p.x;
    float d = 1000.0;
    [branch] if (level > 0.002) d = glyphDistance(p, id, float2(glyphW / 6.0, glyphH / 8.0));
    float edge = 0.5 + 0.5 * blur;
    float ink = 1.0 - smoothstep(halfW * (1.0 + 0.12 * isHead) - edge, halfW * (1.0 + 0.12 * isHead) + edge, d);
    // a tight halo around the strokes that ends at the cell's edge, so neighbouring cells never show a seam
    float2 border = min(f, 1.0 - f) * cell;
    float inside = smoothstep(0.0, 0.32 * cellW, border.x) * smoothstep(0.0, 0.2 * cellH, border.y);
    float halo = inside * exp(-max(d - halfW, 0.0) / (0.085 * cellH * GLOW_SIZE + 0.5 * blur));

    pale *= saturate(HEAD_BRIGHTNESS * 2.0) / (1.0 + 0.8 * depth);       // far heads are less white
    float3 tint = lerp(trailC * 1.6, headC * (0.6 + 0.9 * HEAD_BRIGHTNESS), pale);
    float3 col = tint * level * (ink + 0.5 * GLOW * halo * (0.35 + 0.65 * level));

    // bloom from the column's own light and from its neighbours, and the white glow of the heads
    float aspect = cellW / cellH;
    float2 g0 = columnGlow(colId, seed, gp.y, t, rowsPerSecond, f.x - 0.5, aspect);
    float2 g1 = columnGlow(colId - 1.0, seed, gp.y, t, rowsPerSecond, f.x + 0.5, aspect);
    float2 g2 = columnGlow(colId + 1.0, seed, gp.y, t, rowsPerSecond, f.x - 1.5, aspect);
    float2 g = g0 + g1 + g2;
    col += trailC * g.x * 0.10 * TRAIL_SMEAR;
    col += lerp(trailC, headC, 0.6) * g.y * 0.34 * GLOW * HEAD_BRIGHTNESS / (1.0 + depth);

    return float4(col, saturate(ink * level * 1.5 + g.y * 0.5));
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float t = Time;
    float unit = min(Resolution.x, Resolution.y) / 1080.0;             // 1.0 on a 1080p screen
    float size = clamp(GLYPH_SIZE, 6.0, 200.0) * unit;
    float2 centre = 0.5 * Resolution;
    float2 px = tex * Resolution - centre;
    float lean = radians(TILT) + 0.012 * DRIFT * sin(t * 0.083);
    float2 rot = float2(cos(lean), sin(lean));
    px = float2(px.x * rot.x - px.y * rot.y, px.x * rot.y + px.y * rot.x);
    float sway = DRIFT * size * (1.6 * sin(t * 0.061) + 0.7 * sin(t * 0.137 + 1.3));
    float3 trailC = float3(TRAIL_R, TRAIL_G, TRAIL_B);

    // deep black with a breath of mist
    float mist = vnoise(float2(px.x / (Resolution.y * 0.42) + t * 0.013, px.y / (Resolution.y * 0.55) - t * 0.021));
    float3 col = float3(BACK_R, BACK_G, BACK_B) + trailC * HAZE * 0.022 * mist * mist;

    // far layers first; each nearer layer covers a part of what is behind it
    for (int i = 0; i < DEPTH_LAYERS; i++)
    {
        float depth = float(DEPTH_LAYERS - 1 - i);                        // 0 = nearest
        float shrink = pow(clamp(DEPTH_SCALE, 0.2, 1.0), depth);
        float2 lp = px + centre + float2(sway * shrink + depth * 37.0 * unit, depth * 53.0 * unit);
        float4 layer = rainLayer(lp, size * shrink, 11.0 + depth * 17.0, t, DEPTH_BLUR * depth * 0.9 * unit, depth);
        col = col * (1.0 - 0.75 * layer.a) + layer.rgb * pow(DEPTH_DIM, depth);
    }

    col = 1.0 - exp(-col * 1.5 * BRIGHTNESS);
    float lines = max(2.0, floor(3.0 * unit + 0.5));
    col *= 1.0 - SCANLINES * (0.5 - 0.5 * cos(tex.y * Resolution.y / lines * 6.28318));
    float2 vc = tex - 0.5;
    col *= saturate(1.0 - dot(vc, vc) * 1.5 * VIGNETTE);

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    // the rain reacts to terminal text: bright signs flare where they are about to hit a letter, and the
    // letters keep the rows right under them dry
    float2 down = float2(0.0, size / Resolution.y);
    float under = max(shaderTexture.Sample(samplerState, tex + down * 0.5).a,
                      shaderTexture.Sample(samplerState, tex + down * 1.1).a);
    float above = max(shaderTexture.Sample(samplerState, tex - down * 0.7).a,
                      shaderTexture.Sample(samplerState, tex - down * 1.6).a * 0.6);
    float react = float(TEXT_REACT) * (1.0 - cover);
    col *= 1.0 - 0.6 * above * react;
    float lum = dot(col, float3(0.3, 0.6, 0.1));
    col += float3(HEAD_R, HEAD_G, HEAD_B) * smoothstep(0.1, 0.5, lum) * 0.5 * under * react;
    col *= 1.0 - TEXT_VEIL * cover;                     // darken behind text so it stays readable
    col = col * (1.0 - fg.a) + fg.rgb;                  // premultiplied text on top
    col += (hash12(tex * Resolution) - 0.5) / 255.0;    // dither against banding
    return float4(saturate(col), 1.0);
}
