// @name: Path Tracer: Glass Still Life
// @tags: 3d, tech, featured
// @light: SAMPLES=1, BOUNCES=2, GLASS_DEPTH=3, LIQUID_GLOW=0
// A still life in the manner of the old masters, rendered by a Monte Carlo path tracer: a globe decanter of ruby
// wine, a goblet, a crystal ball and a glass bowl on a polished table, brass and fruit beside them, lit by a tall
// window from the left and by two candles whose flames are seen through the glass. Light is tinted as it travels
// through the liquids; the flames flicker, the daylight comes and goes and the camera drifts a little.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define SAMPLES 4             // paths per pixel (1..16): more = less grain, each costs the same again; try 2 on weaker graphics cards
#define BOUNCES 3             // how often light may bounce off table, wall, metal and fruit (2..8): more = richer reflections, slower; glass is counted separately
#define GLASS_DEPTH 4         // extra steps a path may take through glass and liquid (2..12): 4 shows one vessel through another; fewer = faster, but glass behind glass turns dull
#define GRAIN_ANIMATE 1       // 1 = grain changes every frame (looks smoother), 0 = grain stands still
#define FIREFLY_CLAMP 0.3     // limit for very bright bounced light (0.1..20): lower = calmer image, higher = more sparkle
#define ANIMATION_SPEED 1.0   // overall speed of everything that moves (0..3)
#define CAMERA_ANGLE 0.05     // where the camera stands, as an angle around the table (-0.6..0.6, 0 = straight in front)
#define CAMERA_DRIFT 1.0      // how far the camera slowly wanders (0..3, 0 = stands still)
#define CAMERA_HEIGHT 2.7     // height of the camera above the table (0.4..6)
#define CAMERA_DISTANCE 10.2  // distance of the camera from the still life (6..18)
#define FIELD_OF_VIEW 27.0    // vertical field of view in degrees (12..45): small = long lens, like a painting
#define APERTURE 0.11         // lens opening (0..0.4): 0 = everything sharp, larger = stronger blur away from the focus
#define FOCUS_SHIFT 0.0       // moves the focus away from the decanter (-3..6): negative = towards the viewer, positive = towards the wall
#define FILL 0.68             // how full the decanter, goblet and bowl are (0..1, 0 = empty glass)
#define WINE_R 0.62           // decanter liquid colour: red
#define WINE_G 0.04           // decanter liquid colour: green
#define WINE_B 0.09           // decanter liquid colour: blue
#define WINE_DEPTH 1.5        // depth of colour of the decanter liquid (0..4): 0 = clear as water, higher = darker
#define AMBER_R 0.98          // goblet liquid colour: red
#define AMBER_G 0.52          // goblet liquid colour: green
#define AMBER_B 0.10          // goblet liquid colour: blue
#define AMBER_DEPTH 0.9       // depth of colour of the goblet liquid (0..4)
#define EMERALD_R 0.06        // bowl liquid colour: red
#define EMERALD_G 0.62        // bowl liquid colour: green
#define EMERALD_B 0.25        // bowl liquid colour: blue
#define EMERALD_DEPTH 1.6     // depth of colour of the bowl liquid (0..4)
#define LIQUID_IOR 1.36       // how strongly the liquids bend light (1..1.8): 1.33 = water, 1.36 = wine
#define LIQUID_GLOW 1.0       // how cloudy the liquids are (0..4): they catch the daylight and glow from inside, 0 = perfectly clear
#define GLASS_R 0.86          // glass tint (crystal ball, stopper, stem): red
#define GLASS_G 0.95          // glass tint: green
#define GLASS_B 0.92          // glass tint: blue
#define GLASS_IOR 1.52        // how strongly the glass bends and mirrors light (1.1..2.5): 1.5 = glass, 2.4 = diamond
#define CAUSTICS 1.0          // coloured light that glass and liquids throw onto the table (0..2, 0 = plain dark shadows)
#define BRASS_R 0.95          // colour of the brass (candlestick, charger, dish): red
#define BRASS_G 0.62          // colour of the brass: green
#define BRASS_B 0.24          // colour of the brass: blue
#define METAL_ROUGHNESS 0.16  // finish of the brass (0..1): 0 = polished mirror, 1 = matte
#define TABLE_R 0.20          // table colour: red
#define TABLE_G 0.075         // table colour: green
#define TABLE_B 0.026         // table colour: blue
#define TABLE_ROUGHNESS 0.13  // table finish (0..1): 0 = mirror polish, 1 = raw and matte
#define TABLE_GRAIN 0.7       // strength of the wood grain or marble veins (0..1)
#define TABLE_MARBLE 0        // 1 = black marble with veins in the table colour, 0 = polished wood
#define FRUIT_R 0.95          // orange colour: red
#define FRUIT_G 0.40          // orange colour: green
#define FRUIT_B 0.05          // orange colour: blue
#define PLUM_R 0.30           // plum colour: red
#define PLUM_G 0.07           // plum colour: green
#define PLUM_B 0.28           // plum colour: blue
#define WINDOW 1.0            // brightness of the window light (0..4)
#define WINDOW_R 1.00         // window light colour: red
#define WINDOW_G 0.93         // window light colour: green
#define WINDOW_B 0.80         // window light colour: blue
#define WINDOW_ANGLE 0.20     // where the window stands (0..1.3): 0 = exactly to the left, larger = further towards the viewer
#define WINDOW_HEIGHT 4.6     // height of the middle of the window above the table (1.5..7)
#define WINDOW_SIZE 1.0       // size of the window (0.2..2): small = hard shadows and small highlights, large = soft light; gives the same amount of light
#define WINDOW_FOCUS 0.6      // how much the daylight is aimed at the table (0..1): 0 = spills over the whole wall, 1 = a shaft of light
#define WINDOW_BARS 1         // 1 = glazing bars, visible in the reflections on the glass, 0 = one plain pane
#define DAYLIGHT_CHANGE 0.5   // how much the daylight dims and cools when clouds pass (0..1)
#define DAYLIGHT_SPEED 1.0    // how fast the daylight changes (0..5)
#define WINDOW_SWING 0.5      // how far the direction of the daylight slowly wanders (0..2)
#define FLAMES 2              // number of candles that burn (0..2): the first one stands behind the goblet
#define FLAME 1.0             // brightness of the candle flames (0..4)
#define FLAME_R 1.00          // flame colour: red
#define FLAME_G 0.52          // flame colour: green
#define FLAME_B 0.16          // flame colour: blue
#define FLICKER 1.0           // how much the flames flicker and sway (0..3)
#define FLICKER_SPEED 1.0     // how fast the flames flicker (0..4)
#define GLOW 1.0              // soft halo in the air around the flames (0..4)
#define WALL_R 0.085          // back wall or curtain colour: red
#define WALL_G 0.060          // back wall or curtain colour: green
#define WALL_B 0.040          // back wall or curtain colour: blue
#define DRAPE 0.7             // folds of the curtain behind (0..1, 0 = plain wall)
#define ROOM_LIGHT 1.0        // faint light from the rest of the room, fills the shadows (0.1..5)
#define EXPOSURE 1.0          // overall brightness (0.2..3)
#define SATURATION 1.0        // strength of the colours (0..1.5): 0 = black and white
#define VIGNETTE 0.5          // darkening towards the corners (0..1)
#define TEXT_VEIL 0.5         // darkening behind terminal text (0..1)

// ---- the arrangement --------------------------------------------------------------------------------------
// Table top: y = 0, front edge z = 2.45, back wall z = -3.6. Every vessel is a surface of revolution around a
// vertical axis, put together from zones of the form  radius^2 = A * (y - yc)^2 + C  (cylinder, cone, ellipsoid
// or hyperboloid, depending on A and C), closed with flat discs. The values in brackets are positions on the
// table and are not settings.
#define DEC_X (-1.20)
#define DEC_Z (-0.60)
#define GOB_X (0.85)
#define GOB_Z (0.50)
#define BWL_X (-2.75)
#define BWL_Z (0.95)
#define BALL_X (-0.35)
#define BALL_Z (1.50)
#define CST_X (2.35)
#define CST_Z (-1.00)
#define PIL_X (0.995)
#define PIL_Z (-1.40)
#define PLT_X (-2.55)
#define WALL_Z (-3.6)
// Object numbers: 0 table, 1 wall, 2 window, 3 nothing (the dark room), 10 decanter, 12 wine surface, 13 stopper,
// 20 goblet bowl, 21 surface of its liquid, 22 stem, 23 foot, 30 glass bowl, 31 surface of its liquid,
// 40 crystal ball, 45 brass charger, 50 brass candlestick and dish, 51 pillar candle, 52 its flame,
// 53 candle on the candlestick, 54 its flame, 60 and 61 oranges, 62 plum.

// ---- random numbers ---------------------------------------------------------------------------------------
float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Two rounds of mixing: with a single round the random numbers of different decisions are related to each other
// in a regular way, which shows up as a faint woven pattern in smooth areas.
float2 hash23(float3 p)
{
    float3 q = frac(p * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yxz + 33.33);
    q = frac((q.xxy + q.yxx) * q.zyx);
    q += dot(q, q.yzx + 33.33);
    return frac((q.xx + q.yz) * q.zy);
}

// Two random numbers for one decision (dim) of one path. Every pixel gets its own random shift per decision and
// per frame; on top of that the paths of a pixel are spread evenly (the first number in equal steps, the second
// by the golden ratio), so that even a handful of paths per pixel covers every choice fairly.
float2 rnd2(float2 pix, float dim, float sIdx, float tseed)
{
    float2 h = hash23(float3(pix.x, pix.y, dim * 7.0 + tseed));
    return frac(h + sIdx * float2(1.0 / float(SAMPLES), 0.6180340));
}

// One random number for a yes-or-no decision. Each bounce walks through the paths of a pixel with a different
// stride: otherwise two unlikely decisions in a row (a mirror image inside a mirror image on glass) would be tied
// to each other, and such a double reflection would come out as speckles that no number of paths smooths.
float rnd1(float2 pix, float dim, float sIdx, float tseed, float bounce)
{
    float m = fmod(bounce, 4.0);
    float stride = m < 0.5 ? 1.0 / float(SAMPLES) : (m < 1.5 ? 0.6180340 : (m < 2.5 ? 0.7548777 : 0.3166248));
    return frac(hash23(float3(pix.x, pix.y, dim * 7.0 + tseed)).x + sIdx * stride);
}

float lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

void basis(float3 n, out float3 t1, out float3 t2)
{
    float3 up = abs(n.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    t1 = normalize(cross(up, n));
    t2 = cross(n, t1);
}

// smooth random pattern for wood and marble
float vnoise(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);
    fp = fp * fp * (3.0 - 2.0 * fp);
    float va = hash12(ip);
    float vb = hash12(ip + float2(1.0, 0.0));
    float vc = hash12(ip + float2(0.0, 1.0));
    float vd = hash12(ip + float2(1.0, 1.0));
    return lerp(lerp(va, vb, fp.x), lerp(vc, vd, fp.x), fp.y);
}

// ---- how full the vessels are, and what is in them -----------------------------------------------------------
float decLevel() { return 0.03 + 1.57 * saturate(FILL); }
float gobLevel() { return 1.04 + 0.98 * saturate(FILL); }
float bwlLevel() { return 0.04 + 0.60 * saturate(FILL); }
float decLevelR() { float dy = decLevel() - 0.85; return sqrt(max(0.9025 - 1.1141975 * dy * dy, 0.0)); }
float gobLevelR() { float dy = gobLevel() - 1.6981411; return sqrt(max(0.36 - 0.8264463 * dy * dy, 0.0)); }
float bwlLevelR() { float dy = bwlLevel() - 0.90; return sqrt(max(0.9025 - dy * dy, 0.0)); }

// how quickly each medium swallows light, per unit of length and per colour (Beer-Lambert)
float3 sigOf(float3 c, float depth)
{
    return -log(clamp(c, float3(0.003, 0.003, 0.003), float3(1.0, 1.0, 1.0))) * max(depth, 0.0);
}
float3 wineSig() { return sigOf(float3(WINE_R, WINE_G, WINE_B), WINE_DEPTH); }
float3 amberSig() { return sigOf(float3(AMBER_R, AMBER_G, AMBER_B), AMBER_DEPTH); }
float3 emeraldSig() { return sigOf(float3(EMERALD_R, EMERALD_G, EMERALD_B), EMERALD_DEPTH); }
float3 glassSig() { return sigOf(float3(GLASS_R, GLASS_G, GLASS_B), 1.0); }
float3 glassTint() { return clamp(float3(GLASS_R, GLASS_G, GLASS_B), float3(0.0, 0.0, 0.0), float3(1.0, 1.0, 1.0)); }

// ---- ray / primitive intersections ------------------------------------------------------------------------
// 1 if the ray can come closer than r to a vertical axis (o, d: position and direction seen from above)
float nearAxis(float2 o, float2 d, float r)
{
    float a = dot(d, d);
    float b = dot(o, d);
    float c = dot(o, o) - r * r;
    return (c < 0.0 || (b < 0.0 && b * b - a * c > 0.0)) ? 1.0 : 0.0;
}

// Zone of a surface of revolution, radius^2 = A * y^2 + C between the heights y0 and y1; o = ray origin seen
// from the point of the axis where y = 0. Nearest hit in front of the origin, from outside or inside, or -1.
float iRev(float3 o, float3 rd, float A, float C, float y0, float y1)
{
    float qa = dot(rd.xz, rd.xz) - A * rd.y * rd.y;
    float qb = dot(o.xz, rd.xz) - A * o.y * rd.y;
    float qc = dot(o.xz, o.xz) - A * o.y * o.y - C;
    float h = qb * qb - qa * qc;
    float t = -1.0;
    if (h > 0.0)
    {
        // the two solutions, written so that neither loses its digits when the ray runs along the surface
        float q = -(qb + (qb < 0.0 ? -1.0 : 1.0) * sqrt(h));
        float ta = q / (abs(qa) > 1e-9 ? qa : 1e-9);
        float tb = qc / (abs(q) > 1e-12 ? q : 1e-12);
        float tn = min(ta, tb);
        float tf = max(ta, tb);
        float yn = o.y + tn * rd.y;
        float yf = o.y + tf * rd.y;
        if (tf > 0.0005 && yf > y0 && yf < y1) t = tf;
        if (tn > 0.0005 && yn > y0 && yn < y1) t = tn;
    }
    return t;
}

void hitRev(float3 o, float3 rd, float A, float C, float y0, float y1, float idNew,
            inout float tBest, inout float3 n, inout float id)
{
    float t = iRev(o, rd, A, C, y0, y1);
    if (t > 0.0 && t < tBest)
    {
        float3 q = o + rd * t;
        tBest = t;
        n = normalize(float3(q.x, -A * q.y, q.z));
        id = idNew;
    }
}

// flat disc of radius r at the height of o's origin, facing up (ny = 1) or down (ny = -1)
void hitDisc(float3 o, float3 rd, float r, float ny, float idNew, inout float tBest, inout float3 n, inout float id)
{
    float t = -o.y / (abs(rd.y) > 1e-7 ? rd.y : 1e-7);
    float2 q = o.xz + rd.xz * t;
    if (t > 0.0005 && t < tBest && dot(q, q) < r * r)
    {
        tBest = t;
        n = float3(0.0, ny, 0.0);
        id = idNew;
    }
}

// sphere around the origin of o: nearest hit in front of the ray origin (also from inside)
void hitSphere(float3 o, float3 rd, float r, float idNew, inout float tBest, inout float3 n, inout float id)
{
    float b = dot(o, rd);
    float h = b * b - dot(o, o) + r * r;
    if (h > 0.0)
    {
        h = sqrt(h);
        float t = -b - h;
        if (t < 0.0005) t = -b + h;
        if (t > 0.0005 && t < tBest)
        {
            tBest = t;
            n = (o + rd * t) / r;
            id = idNew;
        }
    }
}

// box of half size hs around the origin, seen from outside: entry distance or -1, and the face normal
float iBox(float3 ro, float3 rd, float3 hs, out float3 n)
{
    float3 sg = float3(rd.x < 0.0 ? -1.0 : 1.0, rd.y < 0.0 ? -1.0 : 1.0, rd.z < 0.0 ? -1.0 : 1.0);
    float3 m = sg / max(abs(rd), float3(1e-7, 1e-7, 1e-7));
    float3 k = abs(m) * hs;
    float3 t1 = -ro * m - k;
    float3 t2 = -ro * m + k;
    float tN = max(max(t1.x, t1.y), t1.z);
    float tF = min(min(t2.x, t2.y), t2.z);
    float nx = step(t1.y, t1.x) * step(t1.z, t1.x);
    float ny = (1.0 - nx) * step(t1.z, t1.y);
    n = -sg * float3(nx, ny, 1.0 - nx - ny);
    return (tN < tF && tN > 0.0005) ? tN : -1.0;
}

// ---- lights -----------------------------------------------------------------------------------------------
// The window: an upright rectangle around wc that faces the table (wn), wu = its horizontal direction.
float2 windowHalf()
{
    float s = clamp(WINDOW_SIZE, 0.1, 3.0);
    return float2(1.5 * s, min(2.0 * s, max(WINDOW_HEIGHT, 1.0) - 0.15));
}

// panes of light between dark glazing bars; q = position on the window, -1..1 both ways
float windowPanes(float2 q)
{
    float2 pane = abs(frac(q * float2(1.0, 1.5) + 0.5) - 0.5);
    float bars = step(pane.x, 0.465) * step(pane.y, 0.475) * step(abs(q.x), 0.97) * step(abs(q.y), 0.98);
    return WINDOW_BARS > 0 ? bars : 1.0;
}

// A flame is an upright ellipsoid, 0.055 wide at its middle; fl = its centre and how strongly it burns just now.
float flameHalf(float k) { return 0.13 + 0.05 * k; }

// light leaving the flame at q (seen from its centre) in or against the direction d: a bright core, a softer rim,
// a dim blue foot and a deeper tip
float3 flameLe(float3 q, float3 d, float k)
{
    float hb = flameHalf(k);
    float yr = q.y / hb;
    float3 nrm = normalize(float3(q.x, q.y * 0.003025 / (hb * hb), q.z));
    float facing = abs(dot(nrm, d));
    float prof = smoothstep(-1.0, -0.3, yr) * (1.0 - 0.85 * smoothstep(-0.1, 1.0, yr));
    float3 warm = float3(FLAME_R, FLAME_G, FLAME_B);
    float3 c = lerp(warm, float3(1.0, 0.92, 0.78), 0.6 * prof * facing) * (prof * (0.10 + 0.90 * facing * facing));
    c += float3(0.03, 0.08, 0.30) * (1.0 - smoothstep(-1.0, -0.45, yr));
    return c * (130.0 * FLAME * k);
}

// environment: the rest of the room, which is not modelled; a little lighter towards the ceiling.
// roomC = its colour and brightness just now (it follows the daylight and the candles)
float3 roomLe(float3 rd, float3 roomC)
{
    float up = saturate(rd.y * 1.4 + 0.35);
    return roomC * (0.12 + 0.88 * up * up);
}

// Each of these picks a direction wi towards its light from the point p and reports the distance, the
// probability density of that direction (per solid angle) and the light arriving from there.
void sampleWindow(float3 p, float2 u, float3 wc, float3 wn, float3 wu, float3 wLe,
                  out float3 wi, out float dist, out float pdf, out float3 le)
{
    float2 hw = windowHalf();
    float2 q = 2.0 * u - 1.0;
    float3 wv = cross(wn, wu);
    float3 d = wc + wu * (q.x * hw.x) + wv * (q.y * hw.y) - p;
    float dd = max(dot(d, d), 1e-8);
    dist = sqrt(dd);
    wi = d / dist;
    float cosL = -dot(wi, wn);
    pdf = cosL > 1e-4 ? dd / (cosL * 4.0 * hw.x * hw.y) : 0.0;
    le = wLe * (windowPanes(q) * pow(saturate(cosL), 6.0 * saturate(WINDOW_FOCUS)));
}

// Flame: squeeze space along y until the ellipsoid is a sphere, aim into the cone under which that sphere is
// seen, and stretch the direction back. The density changes by the squeeze factor times the cube of the stretch.
void sampleFlame(float3 p, float2 u, float4 fl, out float3 wi, out float dist, out float pdf, out float3 le)
{
    float hb = flameHalf(fl.w);
    float sq = 0.055 / hb;
    float3 d = fl.xyz - p;
    d.y *= sq;
    float dd = max(dot(d, d), 1e-8);
    float dc = sqrt(dd);
    float sin2 = min(0.003025 / dd, 0.999);
    float cosMax = sqrt(1.0 - sin2);
    float oneMinus = sin2 / (1.0 + cosMax);
    float cosT = 1.0 - u.x * oneMinus;
    float sinT = sqrt(max(1.0 - cosT * cosT, 0.0));
    float ph = 6.2831853 * u.y;
    float3 w = d / dc;
    float3 t1, t2;
    basis(w, t1, t2);
    float3 ws = normalize((t1 * cos(ph) + t2 * sin(ph)) * sinT + w * cosT);
    float ts = max(dc * cosT - sqrt(max(0.003025 - dd * sinT * sinT, 0.0)), 0.0);
    float3 back = float3(ws.x, ws.y / sq, ws.z);
    float stretch = length(back);
    wi = back / stretch;
    dist = ts * stretch;
    pdf = fl.w > 0.0 ? sq * stretch * stretch * stretch / (6.2831853 * oneMinus) : 0.0;
    le = flameLe(p + wi * dist - fl.xyz, wi, fl.w);
}

// the same density for a ray that found the flame by itself
float flamePdf(float3 ro, float3 rd, float4 fl)
{
    float sq = 0.055 / flameHalf(fl.w);
    float3 d = fl.xyz - ro;
    d.y *= sq;
    float sin2 = min(0.003025 / max(dot(d, d), 1e-8), 0.999);
    float oneMinus = sin2 / (1.0 + sqrt(1.0 - sin2));
    float m = length(float3(rd.x, rd.y * sq, rd.z));
    return sq / (6.2831853 * oneMinus * m * m * m);
}

// Halo: light of one flame scattered towards the eye by the air along a ray, in closed form (no shadows).
float3 flameGlow(float3 ro, float3 rd, float tHit, float4 fl)
{
    float dl = dot(fl.xyz - ro, rd);
    float D = max(length(ro + rd * dl - fl.xyz), 0.06);
    float th = atan2(min(tHit, 60.0) - dl, D) - atan2(-dl, D);
    return float3(FLAME_R, FLAME_G, FLAME_B) * (0.0040 * GLOW * FLAME * fl.w * th / D);
}

// ---- the scene --------------------------------------------------------------------------------------------
// closest surface along the ray: distance, outward normal and object number
float traceScene(float3 ro, float3 rd, float3 wc, float3 wn, float3 wu, float4 fA, float4 fB, float seeWindow,
                 out float3 n, out float id)
{
    float tBest = 1e5;
    n = -rd;
    id = 3.0;

    // table: a thick slab with a front edge
    float3 bn;
    float tb = iBox(ro - float3(0.0, -0.21, -0.725), rd, float3(60.0, 0.21, 3.175), bn);
    if (tb > 0.0) { tBest = tb; n = bn; id = 0.0; }

    // back wall
    float tw = (WALL_Z - ro.z) / min(rd.z, -1e-7);
    if (rd.z < 0.0 && tw > 0.0005 && tw < tBest) { tBest = tw; n = float3(0.0, 0.0, 1.0); id = 1.0; }

    // brass charger leaning against the wall
    float3 po = ro - float3(PLT_X, 1.2187, -3.25);
    float pd = dot(rd, float3(0.0, 0.26963, 0.96296));
    if (pd < -1e-6)
    {
        float tp = -dot(po, float3(0.0, 0.26963, 0.96296)) / pd;
        float3 pq = po + rd * tp;
        if (tp > 0.0005 && tp < tBest && dot(pq, pq) < 1.5625)
        {
            tBest = tp;
            n = float3(0.0, 0.26963, 0.96296);
            id = 45.0;
        }
    }

    // window (shines to one side only; the camera itself looks straight through it, so that it never hangs
    // in the picture on very wide screens, but it shows in every reflection)
    float dn = dot(rd, wn);
    if (dn < -1e-5 && seeWindow > 0.5)
    {
        float tq = dot(wc - ro, wn) / dn;
        float3 q = ro + rd * tq - wc;
        float2 hw = windowHalf();
        if (tq > 0.0005 && tq < tBest && abs(dot(q, wu)) < hw.x && abs(dot(q, cross(wn, wu))) < hw.y)
        {
            tBest = tq;
            n = wn;
            id = 2.0;
        }
    }

    // decanter: globe, neck, base, wine surface, stopper
    float3 o = ro - float3(DEC_X, 0.0, DEC_Z);
    if (nearAxis(o.xz, rd.xz, 0.96) > 0.5)
    {
        hitRev(o - float3(0.0, 0.85, 0.0), rd, -1.1141975, 0.9025, -0.82, 0.85, 10.0, tBest, n, id);
        hitRev(o - float3(0.0, 2.90, 0.0), rd, 0.0520780, 0.0225, -1.20, 0.35, 10.0, tBest, n, id);
        hitDisc(o - float3(0.0, 0.03, 0.0), rd, 0.3915528, -1.0, 10.0, tBest, n, id);
        if (FILL > 0.001) hitDisc(o - float3(0.0, decLevel(), 0.0), rd, decLevelR(), 1.0, 12.0, tBest, n, id);
        hitSphere(o - float3(0.0, 3.3554535, 0.0), rd, 0.2, 13.0, tBest, n, id);
    }

    // goblet: foot, stem, bowl, surface of the liquid
    o = ro - float3(GOB_X, 0.0, GOB_Z);
    if (nearAxis(o.xz, rd.xz, 0.61) > 0.5)
    {
        hitRev(o - float3(0.0, 0.21862, 0.0), rd, 5.8410, 0.0, -0.19862, -0.01862, 23.0, tBest, n, id);
        hitRev(o - float3(0.0, 0.62, 0.0), rd, -0.02040816, 0.005625, -0.42, 0.42, 22.0, tBest, n, id);
        hitRev(o - float3(0.0, 1.6981411, 0.0), rd, -0.8264463, 0.36, -0.6581411, 0.4218589, 20.0, tBest, n, id);
        if (FILL > 0.001) hitDisc(o - float3(0.0, gobLevel(), 0.0), rd, gobLevelR(), 1.0, 21.0, tBest, n, id);
    }

    // glass bowl with its liquid
    o = ro - float3(BWL_X, 0.0, BWL_Z);
    if (nearAxis(o.xz, rd.xz, 0.94) > 0.5)
    {
        hitRev(o - float3(0.0, 0.90, 0.0), rd, -1.0, 0.9025, -0.86, -0.18, 30.0, tBest, n, id);
        hitDisc(o - float3(0.0, 0.04, 0.0), rd, 0.4036087, -1.0, 30.0, tBest, n, id);
        if (FILL > 0.001) hitDisc(o - float3(0.0, bwlLevel(), 0.0), rd, bwlLevelR(), 1.0, 31.0, tBest, n, id);
    }

    // crystal ball
    hitSphere(ro - float3(BALL_X, 0.5, BALL_Z), rd, 0.5, 40.0, tBest, n, id);

    // brass candlestick with its candle and flame
    o = ro - float3(CST_X, 0.0, CST_Z);
    if (nearAxis(o.xz, rd.xz, 0.56) > 0.5)
    {
        hitRev(o, rd, -3.8584184, 0.3025, 0.0, 0.2765, 50.0, tBest, n, id);
        hitRev(o - float3(0.0, 0.90, 0.0), rd, 0.01007128, 0.0036, -0.6235, 0.60, 50.0, tBest, n, id);
        hitSphere(o - float3(0.0, 0.90, 0.0), rd, 0.135, 50.0, tBest, n, id);
        hitRev(o - float3(0.0, 1.50, 0.0), rd, -32.111111, 0.1156, -0.06, 0.06, 50.0, tBest, n, id);
        hitRev(o, rd, 0.0, 0.018225, 1.50, 1.70, 50.0, tBest, n, id);
        hitRev(o, rd, 0.0, 0.011025, 1.60, 2.50, 53.0, tBest, n, id);
        hitDisc(o - float3(0.0, 2.50, 0.0), rd, 0.105, 1.0, 53.0, tBest, n, id);
    }
    if (fB.w > 0.0)
    {
        float hbB = flameHalf(fB.w);
        hitRev(ro - fB.xyz, rd, -0.003025 / (hbB * hbB), 0.003025, -hbB, hbB, 54.0, tBest, n, id);
    }

    // pillar candle on a brass dish, with its flame
    o = ro - float3(PIL_X, 0.0, PIL_Z);
    if (nearAxis(o.xz, rd.xz, 0.43) > 0.5)
    {
        hitRev(o - float3(0.0, 0.02, 0.0), rd, -144.0, 0.1764, -0.035, 0.035, 50.0, tBest, n, id);
        hitRev(o, rd, 0.0, 0.04, 0.0, 1.47, 51.0, tBest, n, id);
        hitDisc(o - float3(0.0, 1.47, 0.0), rd, 0.2, 1.0, 51.0, tBest, n, id);
    }
    if (fA.w > 0.0)
    {
        float hbA = flameHalf(fA.w);
        hitRev(ro - fA.xyz, rd, -0.003025 / (hbA * hbA), 0.003025, -hbA, hbA, 52.0, tBest, n, id);
    }

    // fruit
    hitSphere(ro - float3(1.95, 0.43, 1.55), rd, 0.43, 60.0, tBest, n, id);
    hitSphere(ro - float3(2.72, 0.40, 0.75), rd, 0.40, 61.0, tBest, n, id);
    hitSphere(ro - float3(2.78, 0.26, 1.72), rd, 0.26, 62.0, tBest, n, id);
    return tBest;
}

// ---- shadows ----------------------------------------------------------------------------------------------
// 1 if the ray reaches tmax without touching the sphere around the origin of o
float sphereClear(float3 o, float3 rd, float tmax, float r)
{
    float b = dot(o, rd);
    float h = b * b - dot(o, o) + r * r;
    float t = -b - sqrt(max(h, 0.0));
    return (h > 0.0 && t > 0.0 && t < tmax) ? 0.0 : 1.0;
}

// 1 if the ray reaches tmax without touching the zone
float revClear(float3 o, float3 rd, float A, float C, float y0, float y1, float tmax)
{
    float t = iRev(o, rd, A, C, y0, y1);
    return (t > 0.0 && t < tmax) ? 0.0 : 1.0;
}

// length of the ray (up to tmax) inside a bulging zone (A <= 0) between the heights y0 and y1
float revChord(float3 o, float3 rd, float A, float C, float y0, float y1, float tmax)
{
    float qa = max(dot(rd.xz, rd.xz) - A * rd.y * rd.y, 1e-9);
    float qb = dot(o.xz, rd.xz) - A * o.y * rd.y;
    float qc = dot(o.xz, o.xz) - A * o.y * o.y - C;
    float h = qb * qb - qa * qc;
    float len = 0.0;
    if (h > 0.0)
    {
        h = sqrt(h);
        float inv = 1.0 / (abs(rd.y) > 1e-6 ? rd.y : 1e-6);
        float ya = (y0 - o.y) * inv;
        float yb = (y1 - o.y) * inv;
        float lo = max(max((-qb - h) / qa, min(ya, yb)), 0.0);
        float hi = min(min((-qb + h) / qa, max(ya, yb)), tmax);
        len = max(hi - lo, 0.0);
    }
    return len;
}

// Light that has crossed a body of liquid: tinted by the length of its way through it and gathered towards the
// middle of the shadow, as a rounded vessel does. A stand-in for the true caustic, which a path tracer of this
// kind (paths start at the eye) cannot find. full = the longest way through.
float3 liquidThrough(float len, float full, float3 sig, float lens)
{
    float c = saturate(len / full);
    float3 thru = exp(-sig * len) * lerp(1.0, (0.12 + 2.6 * c * c * c) * CAUSTICS, lens);
    return len > 0.0 ? thru : float3(1.0, 1.0, 1.0);
}

// the same for a ball of solid glass around the origin of o
float3 ballThrough(float3 o, float3 rd, float r, float tmax)
{
    float b = dot(o, rd);
    float h = b * b - dot(o, o) + r * r;
    float t = -b - sqrt(max(h, 0.0));
    float gc = saturate(h / (r * r));
    float3 thru = glassTint() * ((0.05 + 3.3 * gc * gc * gc) * CAUSTICS);
    return (h > 0.0 && t > 0.0 && t < tmax) ? thru : float3(1.0, 1.0, 1.0);
}

// How much light gets from ro to ro + rd * tmax. Opaque things block it, thin glass dims it a little, liquids
// and solid glass tint and gather it (lens = 1) or only tint it (lens = 0, for a point inside a liquid).
// Wall, window and flames never stand in the way.
float3 shadowRay(float3 ro, float3 rd, float tmax, float lens)
{
    float3 tr = float3(1.0, 1.0, 1.0);
    float3 shell = lerp(float3(1.0, 1.0, 1.0), glassTint(), 0.3) * 0.84;
    float3 bn;

    // table (only matters for its own front edge)
    if (ro.y < 0.0)
    {
        float tb = iBox(ro - float3(0.0, -0.21, -0.725), rd, float3(60.0, 0.21, 3.175), bn);
        tr *= (tb > 0.0 && tb < tmax) ? 0.0 : 1.0;
    }

    // brass charger
    float3 po = ro - float3(PLT_X, 1.2187, -3.25);
    float pd = dot(rd, float3(0.0, 0.26963, 0.96296));
    float tp = -dot(po, float3(0.0, 0.26963, 0.96296)) / (abs(pd) > 1e-6 ? pd : 1e-6);
    float3 pq = po + rd * tp;
    tr *= (tp > 0.0 && tp < tmax && dot(pq, pq) < 1.5625) ? 0.0 : 1.0;

    // fruit
    tr *= sphereClear(ro - float3(1.95, 0.43, 1.55), rd, tmax, 0.43);
    tr *= sphereClear(ro - float3(2.72, 0.40, 0.75), rd, tmax, 0.40);
    tr *= sphereClear(ro - float3(2.78, 0.26, 1.72), rd, tmax, 0.26);

    // candlestick and candle
    float3 o = ro - float3(CST_X, 0.0, CST_Z);
    if (nearAxis(o.xz, rd.xz, 0.56) > 0.5)
    {
        float v = revClear(o, rd, -3.8584184, 0.3025, 0.0, 0.2765, tmax);
        v *= revClear(o - float3(0.0, 0.90, 0.0), rd, 0.01007128, 0.0036, -0.6235, 0.60, tmax);
        v *= sphereClear(o - float3(0.0, 0.90, 0.0), rd, tmax, 0.135);
        v *= revClear(o - float3(0.0, 1.50, 0.0), rd, -32.111111, 0.1156, -0.06, 0.06, tmax);
        v *= revClear(o, rd, 0.0, 0.018225, 1.50, 1.70, tmax);
        v *= revClear(o, rd, 0.0, 0.011025, 1.60, 2.50, tmax);
        tr *= v;
    }

    // pillar candle and dish
    o = ro - float3(PIL_X, 0.0, PIL_Z);
    if (nearAxis(o.xz, rd.xz, 0.43) > 0.5)
    {
        float v2 = revClear(o - float3(0.0, 0.02, 0.0), rd, -144.0, 0.1764, -0.035, 0.035, tmax);
        v2 *= revClear(o, rd, 0.0, 0.04, 0.0, 1.47, tmax);
        tr *= v2;
    }

    // crystal ball
    tr *= ballThrough(ro - float3(BALL_X, 0.5, BALL_Z), rd, 0.5, tmax);

    // decanter: wine, empty glass above it, stopper
    o = ro - float3(DEC_X, 0.0, DEC_Z);
    if (nearAxis(o.xz, rd.xz, 0.96) > 0.5)
    {
        float lv = decLevel() - 0.85;
        float3 og = o - float3(0.0, 0.85, 0.0);
        tr *= liquidThrough(revChord(og, rd, -1.1141975, 0.9025, -0.82, lv, tmax), 1.9, wineSig(), lens);
        float open = revClear(og, rd, -1.1141975, 0.9025, lv, 0.85, tmax);
        open *= revClear(o - float3(0.0, 2.90, 0.0), rd, 0.0520780, 0.0225, -1.20, 0.35, tmax);
        tr *= open > 0.5 ? float3(1.0, 1.0, 1.0) : shell;
        tr *= ballThrough(o - float3(0.0, 3.3554535, 0.0), rd, 0.2, tmax);
    }

    // goblet: liquid, empty glass above it, stem and foot
    o = ro - float3(GOB_X, 0.0, GOB_Z);
    if (nearAxis(o.xz, rd.xz, 0.61) > 0.5)
    {
        float lg = gobLevel() - 1.6981411;
        float3 ob = o - float3(0.0, 1.6981411, 0.0);
        tr *= liquidThrough(revChord(ob, rd, -0.8264463, 0.36, -0.6581411, lg, tmax), 1.2, amberSig(), lens);
        float openG = revClear(ob, rd, -0.8264463, 0.36, lg, 0.4218589, tmax);
        openG *= revClear(o - float3(0.0, 0.21862, 0.0), rd, 5.8410, 0.0, -0.19862, -0.01862, tmax);
        openG *= revClear(o - float3(0.0, 0.62, 0.0), rd, -0.02040816, 0.005625, -0.42, 0.42, tmax);
        tr *= openG > 0.5 ? float3(1.0, 1.0, 1.0) : shell;
    }

    // glass bowl
    o = ro - float3(BWL_X, 0.0, BWL_Z);
    if (nearAxis(o.xz, rd.xz, 0.94) > 0.5)
    {
        float lb = bwlLevel() - 0.90;
        float3 os = o - float3(0.0, 0.90, 0.0);
        tr *= liquidThrough(revChord(os, rd, -1.0, 0.9025, -0.86, lb, tmax), 1.0, emeraldSig(), lens);
        tr *= revClear(os, rd, -1.0, 0.9025, lb, -0.18, tmax) > 0.5 ? float3(1.0, 1.0, 1.0) : shell;
    }
    return tr;
}

// ---- materials --------------------------------------------------------------------------------------------
// kind: 0 = solid surface (base colour, metal or not, roughness, own glow), 1 = boundary of a body of glass or
// liquid (sig = what it swallows, ior = how it bends light, iorF = how strongly its surface mirrors),
// 3 = thin wall of empty glass (metal then marks its rim, where the glass is thick and mirrors more).
// ns = the normal to shade with.
void surfaceAt(float id, float3 p, float3 ng, float4 fA, float4 fB,
               out float3 ns, out float3 base, out float metal, out float rough, out float3 emis, out float kind,
               out float3 sig, out float ior, out float iorF)
{
    ns = ng;
    base = float3(0.5, 0.5, 0.5);
    metal = 0.0;
    rough = 0.5;
    emis = float3(0.0, 0.0, 0.0);
    kind = 0.0;
    sig = float3(0.0, 0.0, 0.0);
    ior = max(LIQUID_IOR, 1.0);
    iorF = max(GLASS_IOR, 1.0);
    float3 brass = float3(BRASS_R, BRASS_G, BRASS_B);
    float3 flameC = float3(FLAME_R, FLAME_G, FLAME_B) * FLAME;
    if (id < 0.5)
    {
        // table top and front edge: boards running left to right, or one slab of veined marble
        float2 q = ng.y > 0.5 ? p.xz : float2(p.x, p.y * 1.0 + 9.0);
        float3 tc = float3(TABLE_R, TABLE_G, TABLE_B);
        float grain = saturate(TABLE_GRAIN);
        if (TABLE_MARBLE > 0)
        {
            float w1 = vnoise(q * 0.9 + 3.7);
            float w2 = vnoise(q * 2.3 + float2(w1 * 2.0, 9.1));
            float w3 = vnoise(q * 6.1 + float2(4.2, w2 * 2.0));
            float vein = pow(max(1.0 - abs(sin(q.x * 1.1 + q.y * 0.8 + 3.2 * w1 + 1.6 * w2)), 0.0), 14.0);
            float vein2 = pow(max(1.0 - abs(sin(q.x * 0.5 - q.y * 1.9 + 4.0 * w2 + 1.5 * w3)), 0.0), 26.0);
            base = float3(0.016, 0.016, 0.019) * (1.0 + grain * (w2 - 0.5))
                 + (tc * 1.6 + 0.10) * (grain * (0.55 * vein + 0.35 * vein2) * (0.5 + 0.5 * w3));
            rough = clamp(TABLE_ROUGHNESS * (0.55 + 0.5 * w3 * grain), 0.0, 1.0);
        }
        else
        {
            float board = floor(q.y / 1.15 + 0.35);
            float joint = abs(frac(q.y / 1.15 + 0.35) - 0.5);
            float2 g = float2(q.x * 0.22 + board * 7.31, q.y * 1.9 + board * 3.17);
            float warp = vnoise(g * float2(1.3, 1.0));
            float rings = vnoise(float2(g.x * 0.9 + 2.0 * warp, g.y * 7.0 + 3.0 * warp));
            float fine = vnoise(float2(q.x * 3.1 + board * 5.0, q.y * 85.0));
            float tone = 0.62 + 0.55 * rings + 0.22 * (fine - 0.5) + 0.25 * (frac(board * 0.618034) - 0.5);
            base = tc * lerp(1.0, tone, grain);
            base = lerp(base, base * float3(1.25, 0.95, 0.75), grain * warp * 0.5);
            rough = clamp(TABLE_ROUGHNESS * (1.0 + grain * (0.9 * rings - 0.35)), 0.0, 1.0);
            if (joint > 0.492) { base *= 0.25; rough = 0.6; }
        }
    }
    else if (id < 1.5)
    {
        // wall, or the soft folds of a heavy curtain: only the shading normal follows the folds
        float fold = saturate(DRAPE);
        float ph1 = p.x * 1.55 + 0.45 * sin(p.y * 0.55 + 1.0);
        float ph2 = p.x * 3.9 + 1.3 + 0.6 * sin(p.y * 0.8 + 2.0);
        float ph3 = p.x * 8.3 + 0.7 * sin(p.y * 0.4);
        float slope = fold * (0.75 * cos(ph1) + 0.45 * cos(ph2) + 0.16 * cos(ph3));
        ns = normalize(float3(-slope, 0.0, 1.0));
        float valley = 0.5 - 0.5 * (0.6 * sin(ph1) + 0.3 * sin(ph2) + 0.1 * sin(ph3));
        base = float3(WALL_R, WALL_G, WALL_B) * (1.0 - 0.45 * fold * valley);
        rough = 0.92;
    }
    else if (id < 13.5)
    {
        // decanter
        kind = 1.0;
        if (id > 12.5) { sig = glassSig(); ior = iorF; }
        else if (id > 11.5) { sig = wineSig(); iorF = ior; }
        else if (p.y < decLevel()) { sig = wineSig(); }
        else { kind = 3.0; metal = smoothstep(3.215, 3.25, p.y); }
    }
    else if (id < 23.5)
    {
        // goblet: hollow foot, solid stem, bowl
        kind = 1.0;
        if (id > 22.5) { kind = 3.0; metal = 1.0 - smoothstep(0.02, 0.04, p.y); }
        else if (id > 21.5) { sig = glassSig(); ior = iorF; }
        else if (id > 20.5) { sig = amberSig(); iorF = ior; }
        else if (p.y < gobLevel()) { sig = amberSig(); }
        else { kind = 3.0; metal = smoothstep(2.095, 2.12, p.y); }
    }
    else if (id < 31.5)
    {
        // glass bowl
        kind = 1.0;
        if (id > 30.5) { sig = emeraldSig(); iorF = ior; }
        else if (p.y < bwlLevel()) { sig = emeraldSig(); }
        else { kind = 3.0; metal = smoothstep(0.698, 0.72, p.y); }
    }
    else if (id < 40.5)
    {
        kind = 1.0;
        sig = glassSig();
        ior = iorF;
    }
    else if (id < 50.5)
    {
        // brass, with fine turned grooves on the stem
        base = brass;
        metal = 1.0;
        rough = clamp(METAL_ROUGHNESS, 0.0, 1.0);
        if (id < 47.0)
        {
            // the charger: a raised boss in the middle, a broad rim and a beaded edge, worked into the normal
            float3 pq = p - float3(PLT_X, 1.2187, -3.25);
            float pr = length(pq);
            float slope = 0.5 * smoothstep(0.05, 0.30, pr) * (1.0 - smoothstep(0.30, 0.42, pr))
                        - 0.45 * smoothstep(0.78, 0.86, pr) * (1.0 - smoothstep(0.86, 0.94, pr))
                        + 0.25 * sin(pr * 90.0) * step(1.12, pr);
            ns = normalize(ng + pq * (slope / max(pr, 1e-4)));
            rough = clamp(METAL_ROUGHNESS * 0.7, 0.0, 1.0);
        }
        else
        {
            float groove = abs(frac(p.y * 5.0 + 0.25) - 0.5);
            if (groove < 0.035 && p.y > 0.28 && p.y < 1.45) { base *= 0.35; rough = max(rough, 0.45); }
        }
    }
    else if (id < 53.5)
    {
        // candle wax, glowing from within near its flame
        float top = id < 52.0 ? 1.47 : 2.50;
        float burning = id < 52.0 ? fA.w : fB.w;
        base = float3(0.70, 0.63, 0.48);
        rough = 0.55;
        emis = base * flameC * (burning * 0.40 * exp(-(top - p.y) * 5.5));
    }
    else if (id < 61.5)
    {
        // oranges: mottled peel
        float mott = vnoise(p.xy * 9.0 + p.zx * 7.0);
        base = float3(FRUIT_R, FRUIT_G, FRUIT_B) * (0.90 + 0.16 * mott);
        if (id > 60.5) base *= float3(1.0, 0.86, 0.75);
        float navel = dot(ng, id > 60.5 ? float3(0.3162, 0.6325, 0.7071) : float3(-0.4082, 0.8165, 0.4082));
        if (navel > 0.9982) base = float3(0.16, 0.15, 0.05);
        rough = 0.36 + 0.15 * mott;
        // dimpled peel
        float3 pp = p * 26.0;
        float3 dimple = float3(vnoise(pp.yz + 3.0), vnoise(pp.zx + 7.0), vnoise(pp.xy + 11.0)) - 0.5;
        ns = normalize(ng + dimple * 0.22);
    }
    else
    {
        // plum: dusty bloom
        float bloom = vnoise(p.xy * 9.0 + p.zy * 7.0);
        base = lerp(float3(PLUM_R, PLUM_G, PLUM_B), float3(0.30, 0.30, 0.42), 0.35 * bloom);
        rough = 0.38 + 0.3 * bloom;
    }
}

// share of the light that a smooth boundary mirrors; cosi = cosine on the thin side, ior = thick over thin
float fresnel(float cosi, float ior)
{
    float sin2t = (1.0 - cosi * cosi) / (ior * ior);
    float cost = sqrt(max(1.0 - sin2t, 0.0));
    float rs = (cosi - ior * cost) / (cosi + ior * cost + 1e-7);
    float rp = (ior * cosi - cost) / (ior * cosi + cost + 1e-7);
    return saturate(0.5 * (rs * rs + rp * rp));
}

// ---- surface reflection (GGX microfacets plus a diffuse base) ---------------------------------------------
float smithG1(float nx, float a2) { return 2.0 * nx / (nx + sqrt(a2 + (1.0 - a2) * nx * nx)); }

// Light reflected from direction l towards v, already multiplied by the cosine at the surface.
// pdf = probability density with which sampleBsdf would have chosen l.
float3 evalBsdf(float3 n, float3 v, float3 l, float3 diff, float3 f0, float a2, float pSpec, out float pdf)
{
    float nol = dot(n, l);
    float nov = max(dot(n, v), 1e-4);
    float3 res = float3(0.0, 0.0, 0.0);
    pdf = 0.0;
    if (nol > 1e-5)
    {
        float3 hv = normalize(v + l);
        float noh = saturate(dot(n, hv));
        float voh = saturate(dot(v, hv));
        float dd = noh * noh * (a2 - 1.0) + 1.0;
        float D = a2 / (3.14159265 * dd * dd);
        float g1v = smithG1(nov, a2);
        float g1l = smithG1(nol, a2);
        float fw = pow(1.0 - voh, 5.0);
        float3 F = f0 + (float3(1.0, 1.0, 1.0) - f0) * fw;
        float3 spec = F * (D * g1v * g1l / (4.0 * nov));               // the 1 / nol cancels against the cosine
        res = spec + diff * (1.0 - lum(F)) * (nol / 3.14159265);
        pdf = pSpec * g1v * D / (4.0 * nov) + (1.0 - pSpec) * nol / 3.14159265;
    }
    return res;
}

// new direction: either a mirror-like bounce off a random microfacet (visible normals only) or a diffuse one
float3 sampleBsdf(float3 n, float3 v, float a, float pSpec, float2 u, float pick)
{
    float3 t1, t2;
    basis(n, t1, t2);
    float ph = 6.2831853 * u.x;
    float3 l = n;
    if (pick < pSpec)
    {
        float3 vl = float3(dot(v, t1), dot(v, t2), dot(v, n));
        float3 vh = normalize(float3(a * vl.x, a * vl.y, max(vl.z, 1e-4)));
        float lensq = vh.x * vh.x + vh.y * vh.y;
        float3 b1 = lensq > 1e-12 ? float3(-vh.y, vh.x, 0.0) / sqrt(lensq) : float3(1.0, 0.0, 0.0);
        float3 b2 = cross(vh, b1);
        float r = sqrt(u.y);
        float p1 = r * cos(ph);
        float p2 = r * sin(ph);
        float s = 0.5 * (1.0 + vh.z);
        p2 = (1.0 - s) * sqrt(max(1.0 - p1 * p1, 0.0)) + s * p2;
        float3 nh = b1 * p1 + b2 * p2 + vh * sqrt(max(1.0 - p1 * p1 - p2 * p2, 0.0));
        float3 hl = normalize(float3(a * nh.x, a * nh.y, max(nh.z, 1e-6)));
        float3 hw = t1 * hl.x + t2 * hl.y + n * hl.z;
        l = reflect(-v, hw);
    }
    else
    {
        float sz = sqrt(u.y);
        l = normalize((t1 * cos(ph) + t2 * sin(ph)) * sz + n * sqrt(1.0 - u.y));
    }
    return l;
}

// ---- one light path ---------------------------------------------------------------------------------------
// Glass and solid things are counted separately. A look through a vessel takes two steps (in and out), through
// one vessel at another four, and only then comes the surface behind, which is what gets lit. With one shared
// count of 3, as in a scene with a single glass ball, everything behind glass would stay black; so BOUNCES counts
// the solid surfaces only and GLASS_DEPTH the steps through glass, which are cheap (no shadow rays).
float3 tracePath(float3 roIn, float3 rdIn, float3 wc, float3 wn, float3 wu, float3 wLe, float3 roomC,
                 float4 fA, float4 fB, float2 pix, float sIdx, float tseed)
{
    float3 ro = roIn;
    float3 rd = rdIn;
    float3 L = float3(0.0, 0.0, 0.0);
    float3 thr = float3(1.0, 1.0, 1.0);
    float prevPdf = -1.0;                      // density of the last bounce; negative = mirror-sharp or camera
    float soft = 0.0;                          // bounces since the path first left a matte or frosted surface
    float surfLeft = float(BOUNCES);           // bounces off solid things that are still allowed
    float glassLeft = float(GLASS_DEPTH);      // steps through glass that are still allowed
    float3 curSig = float3(0.0, 0.0, 0.0);     // what the medium around the ray swallows (zero in air)
    float inAir = 1.0;
    float curScat = 0.0;                       // how much the medium around the ray scatters (liquids only)
    float2 hw = windowHalf();

    [loop] for (int b = 0; b < BOUNCES + GLASS_DEPTH; b++)
    {
        float dim = 2.0 + 6.0 * float(b);
        float3 n;
        float id;
        float t = traceScene(ro, rd, wc, wn, wu, fA, fB, b > 0 ? 1.0 : 0.0, n, id);
        float3 p = ro + rd * t;

        // what the ray crossed on its way: air with the halo of the flames, or glass or liquid
        float3 Lb = thr * (flameGlow(ro, rd, t, fA) + flameGlow(ro, rd, t, fB)) * inAir;
        float3 Lhit = float3(0.0, 0.0, 0.0);
        // Shadow rays this step will need: at most two (jc = the light each would bring if nothing is in its way,
        // jo, jd, jt = from where, in which direction and how far it has to look). They are all sent from one
        // place further down, so that the long shadow routine exists only once in the compiled shader.
        float3 jc0 = float3(0.0, 0.0, 0.0);
        float3 jc1 = float3(0.0, 0.0, 0.0);
        float3 jo0 = ro;
        float3 jo1 = ro;
        float3 jd0 = rd;
        float3 jd1 = rd;
        float jt0 = 0.0;
        float jt1 = 0.0;
        float jlens0 = 1.0;

        // A liquid is never perfectly clear: at one point of the way through it the daylight that reaches that
        // point is scattered towards the eye. That is the glow of wine against the light.
        float2 u0 = rnd2(pix, dim, sIdx, tseed);
        float2 u1 = rnd2(pix, dim + 1.0, sIdx, tseed);
        if (curScat > 0.0 && t < 50.0)
        {
            float tsc = t * u1.x;
            float3 psc = ro + rd * tsc;
            float3 wiC, leC;
            float diC, lpC;
            sampleWindow(psc, float2(u0.x, u1.y), wc, wn, wu, wLe, wiC, diC, lpC, leC);
            if (lpC > 0.0)
            {
                jc0 = thr * exp(-curSig * tsc) * leC * (curScat * t * 0.0795775 / lpC);
                jo0 = psc;
                jd0 = wiC;
                jt0 = diC - 0.004;
                jlens0 = 0.0;
            }
        }
        thr *= exp(-curSig * min(t, 100.0));

        // The ray left the scene or ran into a light. A light found by a bounce is weighted against the chance
        // that direct sampling had found it. Light that reaches a matte surface through glass is already
        // counted by the shadow rays (see liquidThrough), so a path that finds it again must not count it twice.
        bool done = false;
        if (id > 2.5 && id < 3.5)
        {
            Lhit = thr * roomLe(rd, roomC);
            done = true;
        }
        else if (id > 1.5 && id < 2.5)
        {
            float3 q = p - wc;
            float2 qw = float2(dot(q, wu) / hw.x, dot(q, cross(wn, wu)) / hw.y);
            float lp = t * t / (max(-dot(rd, wn), 1e-4) * 4.0 * hw.x * hw.y);
            float wHit = prevPdf >= 0.0 ? prevPdf * prevPdf / (prevPdf * prevPdf + lp * lp) : (soft > 0.5 ? 0.0 : 1.0);
            Lhit = thr * wLe * (windowPanes(qw) * wHit * pow(saturate(-dot(rd, wn)), 6.0 * saturate(WINDOW_FOCUS)));
            done = true;
        }
        else if (id > 51.5 && id < 52.5)
        {
            float wA = prevPdf >= 0.0 ? step(flamePdf(ro, rd, fA), prevPdf) : (soft > 0.5 ? 0.0 : 1.0);
            Lhit = thr * flameLe(p - fA.xyz, rd, fA.w) * wA;
            done = true;
        }
        else if (id > 53.5 && id < 54.5)
        {
            float wB = prevPdf >= 0.0 ? step(flamePdf(ro, rd, fB), prevPdf) : (soft > 0.5 ? 0.0 : 1.0);
            Lhit = thr * flameLe(p - fB.xyz, rd, fB.w) * wB;
            done = true;
        }

        float2 u4 = rnd2(pix, dim + 3.0, sIdx, tseed);
        float pick = rnd1(pix, dim + 4.0, sIdx, tseed, float(b));
        if (!done)
        {
            float3 ns, base, emis, sig;
            float metal, rough, kind, ior, iorF;
            surfaceAt(id, p, n, fA, fB, ns, base, metal, rough, emis, kind, sig, ior, iorF);
            float3 v = -rd;

            if (kind > 0.5 && glassLeft < 0.5)
            {
                // no steps through glass left: end the path with the dim light of the room instead of black
                Lhit = thr * roomLe(rd, roomC);
                done = true;
            }
            else if (kind > 2.5)
            {
                // thin wall of empty glass: mirrors a little on both of its faces, otherwise lets the ray
                // pass without bending it
                float3 nn = dot(rd, n) < 0.0 ? n : -n;
                float cosi = saturate(-dot(rd, nn));
                float f1 = fresnel(cosi, iorF);
                float fr = lerp(2.0 * f1 / (1.0 + f1), 0.8, metal);
                float pr = clamp(fr, 0.3, 0.8);
                if (pick < pr)
                {
                    thr *= fr / pr;
                    rd = reflect(rd, nn);
                    ro = p + nn * 0.002;
                }
                else
                {
                    thr *= pow(glassTint(), float3(0.12, 0.12, 0.12)) * ((1.0 - fr) / (1.0 - pr));
                    ro = p - nn * 0.002;
                }
                prevPdf = -1.0;
                soft += step(0.5, soft);
                glassLeft -= 1.0;
            }
            else if (kind > 0.5)
            {
                // body of glass or liquid: mirror or refract, chosen by how much light each takes (Fresnel)
                float entering = dot(rd, n) < 0.0 ? 1.0 : 0.0;
                float3 nb = entering > 0.5 ? n : -n;
                float eta = entering > 0.5 ? 1.0 / ior : ior;
                float cosb = saturate(-dot(rd, nb));
                float sin2t = eta * eta * (1.0 - cosb * cosb);
                float cost = sqrt(max(1.0 - sin2t, 0.0));
                float frb = sin2t < 1.0 ? fresnel(entering > 0.5 ? cosb : cost, iorF) : 1.0;
                // the mirror image is chosen more often than its share of the light and counted less in return:
                // bright highlights on the glass then need fewer paths to come out clean
                float prb = frb < 1.0 ? clamp(frb, 0.3, 0.8) : 1.0;
                if (pick < prb)
                {
                    thr *= frb / prb;
                    rd = reflect(rd, nb);
                    ro = p + nb * 0.002;
                }
                else
                {
                    thr *= (1.0 - frb) / (1.0 - prb);
                    rd = normalize(rd * eta + nb * (eta * cosb - cost));
                    ro = p - nb * 0.002;
                    curSig = sig * entering;
                    inAir = 1.0 - entering;
                    bool solidGlass = (id > 12.5 && id < 13.5) || (id > 21.5 && id < 22.5) || id > 39.5;
                    curScat = solidGlass ? 0.0 : 0.16 * max(LIQUID_GLOW, 0.0) * entering;
                }
                prevPdf = -1.0;
                soft += step(0.5, soft);
                glassLeft -= 1.0;
            }
            else
            {
                // solid surface
                curSig = float3(0.0, 0.0, 0.0);
                curScat = 0.0;
                inAir = 1.0;
                Lhit = thr * emis;
                float3 nf = dot(n, v) < 0.0 ? -n : n;
                float3 nsf = dot(ns, v) < 0.0 ? nf : ns;
                float3 f0 = lerp(float3(0.04, 0.04, 0.04), base, metal);
                float3 diff = base * (1.0 - metal);
                float a = max(rough * rough, 0.002);
                float a2 = a * a;
                float nov = max(dot(nsf, v), 1e-4);
                float wSpec = lum(f0 + (float3(1.0, 1.0, 1.0) - f0) * pow(1.0 - nov, 5.0));
                float wDiff = lum(diff) * (1.0 - wSpec);
                float pSpec = wDiff > 0.0 ? clamp(wSpec / (wSpec + wDiff), 0.1, 0.9) : 1.0;

                // Direct light, each direction checked with a shadow ray and weighted against the chance that
                // the bounce below finds the same light by itself. What the camera sees first gets a ray to
                // the window and one to a flame; later bounces get a single ray, to the window or to a flame.
                // Of the two flames the one that sends more light to this point is chosen more often.
                float2 u2 = rnd2(pix, dim + 2.0, sIdx, tseed);
                float sel = rnd1(pix, dim + 5.0, sIdx, tseed, float(b) + 2.0);
                float3 dA = fA.xyz - p;
                float3 dB = fB.xyz - p;
                float gA = fA.w / max(dot(dA, dA), 0.01);
                float gB = fB.w / max(dot(dB, dB), 0.01);
                float anyFlame = gA + gB > 0.0 ? 1.0 : 0.0;
                float pA = (gA > 0.0 && gB > 0.0) ? clamp(gA / (gA + gB), 0.1, 0.9) : (gA > 0.0 ? 1.0 : 0.0);
                float useA = u2.x < pA ? 1.0 : 0.0;
                float4 fl = useA > 0.5 ? fA : fB;
                float pFl = useA > 0.5 ? pA : 1.0 - pA;
                float uFl = useA > 0.5 ? u2.x / max(pA, 1e-4) : (u2.x - pA) / max(1.0 - pA, 1e-4);
                float first = surfLeft > float(BOUNCES) - 0.5 ? 1.0 : 0.0;
                float pWin = first > 0.5 ? 1.0 : lerp(1.0, 0.6, anyFlame);
                float pFlame = first > 0.5 ? 1.0 : 0.4;
                float doWin = (first > 0.5 || sel < pWin) ? 1.0 : 0.0;
                float doFlame = (first > 0.5 || sel >= pWin) ? anyFlame : 0.0;
                float3 wi0, wi1, le0, le1;
                float di0, di1, lp0, lp1, bp0, bp1;
                sampleWindow(p, u0, wc, wn, wu, wLe, wi0, di0, lp0, le0);
                sampleFlame(p, float2(saturate(uFl), u2.y), fl, wi1, di1, lp1, le1);
                float3 c0 = le0 * evalBsdf(nsf, v, wi0, diff, f0, a2, pSpec, bp0);
                float3 c1 = le1 * evalBsdf(nsf, v, wi1, diff, f0, a2, pSpec, bp1);
                c0 *= lp0 / max(lp0 * lp0 + bp0 * bp0, 1e-12) * (doWin / pWin);
                c1 *= (1.0 - step(lp1, bp1)) / max(lp1 * pFl, 1e-9) * (doFlame / pFlame);   // tiny flames: all or nothing
                jc0 = thr * c0 * step(0.0, dot(wi0, nf));
                jc1 = thr * c1 * step(0.0, dot(wi1, nf));
                jo0 = p + nf * 0.002;
                jo1 = jo0;
                jd0 = wi0;
                jd1 = wi1;
                jt0 = di0 - 0.004;
                jt1 = di1 - 0.004;
                jlens0 = 1.0;

                // continue the path, unless this was its last bounce or so little light is left on it that
                // nothing it could still find would show
                surfLeft -= 1.0;
                soft += (pick >= pSpec || rough > 0.09 || soft > 0.5) ? 1.0 : 0.0;
                done = surfLeft < 0.5;
                if (!done)
                {
                    float3 l = sampleBsdf(nsf, v, a, pSpec, u4, pick);
                    float bp;
                    float3 f = evalBsdf(nsf, v, l, diff, f0, a2, pSpec, bp);
                    thr *= f / max(bp, 1e-6);
                    prevPdf = bp;
                    rd = l;
                    ro = p + nf * 0.002;
                    done = dot(l, nf) <= 0.0 || max(thr.x, max(thr.y, thr.z)) < 0.1;
                }
            }
        }

        // the shadow rays of this step
        [loop] for (int li = 0; li < 2; li++)
        {
            float3 cS = li == 0 ? jc0 : jc1;
            float3 oS = li == 0 ? jo0 : jo1;
            float3 dS = li == 0 ? jd0 : jd1;
            float tS = li == 0 ? jt0 : jt1;
            float lensS = li == 0 ? jlens0 : 1.0;
            if (max(cS.x, max(cS.y, cS.z)) > 1e-6)
            {
                Lb += cS * shadowRay(oS, dS, tS, lensS);
            }
        }

        // What the camera sees directly or in a mirror, and the reflection of a light in something frosted, stay
        // as bright as they are. Light that arrives by a detour over matte or frosted surfaces is limited: that
        // trades the rare, very bright sample (a "firefly") for a tiny loss of light.
        float capB = soft > 1.5 ? FIREFLY_CLAMP : 64.0;
        float capE = soft > 1.5 ? FIREFLY_CLAMP : 64.0;
        float maxB = max(Lb.x, max(Lb.y, Lb.z));
        float maxE = max(Lhit.x, max(Lhit.y, Lhit.z));
        L += Lb * (maxB > capB ? capB / maxB : 1.0) + Lhit * (maxE > capE ? capE / maxE : 1.0);
        if (done) break;
    }
    return L;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float2 pix = floor(tex * Resolution);
    float aspect = Resolution.x / Resolution.y;
    float tm = Time * ANIMATION_SPEED;
    float tseed = fmod(Time, 61.0) * 59.0 * float(GRAIN_ANIMATE);

    // flames: each flickers and sways in its own rhythm
    float tf = tm * FLICKER_SPEED;
    float fk1 = 0.5 * sin(tf * 3.7) + 0.35 * sin(tf * 8.9 + 1.0) + 0.4 * sin(tf * 1.9 + 2.0) + 0.12 * sin(tf * 19.0);
    float fk2 = 0.5 * sin(tf * 3.1 + 4.0) + 0.35 * sin(tf * 10.3 + 2.5) + 0.4 * sin(tf * 1.6 + 0.7) + 0.12 * sin(tf * 17.0 + 1.0);
    float kA = (FLAMES > 0 && FLAME > 0.0) ? clamp(1.0 + 0.15 * FLICKER * fk1, 0.25, 1.8) : 0.0;
    float kB = (FLAMES > 1 && FLAME > 0.0) ? clamp(1.0 + 0.15 * FLICKER * fk2, 0.25, 1.8) : 0.0;
    float2 swA = 0.012 * FLICKER * float2(sin(tf * 2.1 + 1.0) + 0.5 * sin(tf * 5.7), sin(tf * 1.7) + 0.5 * sin(tf * 4.3 + 2.0));
    float2 swB = 0.012 * FLICKER * float2(sin(tf * 1.9 + 3.0) + 0.5 * sin(tf * 6.3), sin(tf * 2.3 + 1.0) + 0.5 * sin(tf * 4.9));
    swA = clamp(swA, float2(-0.04, -0.04), float2(0.04, 0.04));
    swB = clamp(swB, float2(-0.04, -0.04), float2(0.04, 0.04));
    float4 fA = float4(PIL_X + swA.x, 1.47 + 0.03 + flameHalf(kA), PIL_Z + swA.y, kA);
    float4 fB = float4(CST_X + swB.x, 2.50 + 0.03 + flameHalf(kB), CST_Z + swB.y, kB);

    // window: clouds pass (dimmer and cooler, then brighter and warmer again), and its direction wanders
    float tw = tm * DAYLIGHT_SPEED;
    float cloud = 0.5 + 0.5 * sin(tw * 0.11 + 1.7 * sin(tw * 0.043) + 0.6);
    float change = saturate(DAYLIGHT_CHANGE);
    float3 wLe = float3(WINDOW_R, WINDOW_G, WINDOW_B) * (30.0 * WINDOW * (1.0 - 0.55 * change * cloud));
    wLe *= lerp(float3(1.0, 1.0, 1.0), float3(0.78, 0.94, 1.22), change * cloud);
    float3 roomC = (wLe * 0.0016 + float3(0.008, 0.007, 0.006)
                 + float3(FLAME_R, FLAME_G, FLAME_B) * (0.004 * FLAME * (kA + kB))) * max(ROOM_LIGHT, 0.0)
                 + float3(0.0040, 0.0036, 0.0032);       // never pitch dark, even with every light put out
    wLe *= 3.0 / (windowHalf().x * windowHalf().y);       // a smaller window shines brighter: the same light in all
    float wang = clamp(WINDOW_ANGLE + 0.09 * WINDOW_SWING * sin(tw * 0.071 + 2.0), 0.0, 1.35);
    float3 wc = float3(-0.35 - 7.0 * cos(wang), clamp(WINDOW_HEIGHT, 1.0, 9.0), 7.0 * sin(wang));
    float3 wn = normalize(float3(0.4, 0.8, 0.0) - wc);
    float3 wu = normalize(cross(float3(0.0, 1.0, 0.0), wn));

    // camera
    float ang = clamp(CAMERA_ANGLE, -1.2, 1.2) + CAMERA_DRIFT * 0.045 * sin(tm * 0.07);
    float cd = clamp(CAMERA_DISTANCE, 4.5, 30.0) * (1.0 + CAMERA_DRIFT * 0.02 * sin(tm * 0.043 + 1.0));
    float ch = clamp(CAMERA_HEIGHT, 0.2, 9.0) + CAMERA_DRIFT * 0.10 * sin(tm * 0.053 + 2.0);
    float narrow = 1.0 - min(1.0, aspect / 1.78);                // 0 on wide screens, towards 1 on upright ones
    float3 target = float3(-0.35, 1.38 + 4.5 * narrow, 0.0);    // upright: look higher, the table stays low in the picture
    float3 eye = float3(target.x + sin(ang) * cd, max(ch, 0.15), cos(ang) * cd);
    float3 fw = normalize(target - eye);
    float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
    float3 upv = cross(rt, fw);
    float fl = 1.0 / tan(radians(clamp(FIELD_OF_VIEW, 8.0, 100.0)) * 0.5);
    fl *= min(1.0, aspect / 1.78);             // on upright screens widen the view, so the still life still fits
    float focus = max(dot(float3(DEC_X, 1.0, DEC_Z + 0.9) - eye, fw) + FOCUS_SHIFT, 2.0);

    float3 col = float3(0.0, 0.0, 0.0);
    [loop] for (int sIdx = 0; sIdx < SAMPLES; sIdx++)
    {
        float sf = float(sIdx);
        float2 uj = rnd2(pix, 0.0, sf, tseed);
        float2 ul = rnd2(pix, 1.0, sf, tseed);
        float2 uv = float2((tex.x - 0.5) * 2.0 * aspect, 1.0 - 2.0 * tex.y) + (uj - 0.5) * 2.0 / Resolution.y;
        float3 dir = normalize(fw * fl + rt * uv.x + upv * uv.y);
        float3 focal = eye + dir * (focus / dot(dir, fw));
        float lr = APERTURE * sqrt(ul.x);
        float la = 6.2831853 * ul.y;
        float3 ro = eye + (rt * cos(la) + upv * sin(la)) * lr;
        float3 rd = normalize(focal - ro);
        float3 L = tracePath(ro, rd, wc, wn, wu, wLe, roomC, fA, fB, pix, sf, tseed);
        col += clamp(L, float3(0.0, 0.0, 0.0), float3(64.0, 64.0, 64.0));
    }
    col /= float(SAMPLES);

    // filmic tone curve (ACES fit), saturation, display gamma, vignette
    col *= EXPOSURE;
    col = saturate(col * (2.51 * col + 0.03) / (col * (2.43 * col + 0.59) + 0.14));
    float grey = lum(col);
    col = saturate(float3(grey, grey, grey) + (col - float3(grey, grey, grey)) * SATURATION);
    col = pow(col, float3(0.4545, 0.4545, 0.4545));
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
