// @name: Beach Tide
// @tags: water, nature, calming, featured
// @light: SURF_STEPS=0, CAUSTICS=0, MIRROR_CLOUDS=0, CLOUD_DETAIL=3
// Standing on a tropical beach, looking out to sea: swells roll in from a deep blue horizon, rear up over the
// turquoise shallows, break and run up the sand as a thin sheet of foam, then slide back and leave wet sand that
// mirrors the sky. A palm leans in, clouds drift, a sailboat passes far out. From bright noon to golden evening.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

// ---- time of day and sky
#define SUN_HEIGHT 50.0       // time of day: height of the sun in degrees, 50 = tropical noon, 9 = golden evening (4..80)
#define SUN_DIRECTION 26.0    // where the sun stands: degrees left (-) or right (+) of the view (-80..80)
#define SUN_GLOW 1.0          // glare around the sun (0..2)
#define CLOUDS 0.45           // amount of cloud (0..1)
#define CLOUD_SPEED 1.0       // how fast the clouds drift (0..4)
#define CLOUD_DETAIL 5        // fineness of the cloud edges, fewer is cheaper (2..6)
#define HIGH_CLOUDS 0.5       // thin streaks of high cloud (0..1)
#define SKY_R 0.10            // sky overhead: red
#define SKY_G 0.38            // sky overhead: green
#define SKY_B 0.80            // sky overhead: blue
#define HAZE 1.0              // haze towards the horizon (0..3)
// ---- the sea
#define SHALLOW_R 0.14        // water over the shallows: red
#define SHALLOW_G 0.80        // water over the shallows: green
#define SHALLOW_B 0.76        // water over the shallows: blue
#define DEEP_R 0.02           // deep water: red
#define DEEP_G 0.25           // deep water: green
#define DEEP_B 0.52           // deep water: blue
#define WATER_CLARITY 1.0     // how far out you can see the sandy bottom (0.3..3)
#define REEFS 1.0             // dark patches of reef and weed under the water (0..2)
#define WAVE_HEIGHT 1.0       // height of the waves (0.3..2)
#define WAVE_PERIOD 9.0       // seconds between two waves (4..20)
#define WAVE_SETS 0.6         // irregularity: 0 = every wave alike, 1 = waves arrive in sets of big and small (0..1)
#define SURF_FOAM 1.0         // amount of white water where the waves break (0..2)
#define FOAM_LACE 1.0         // lace of foam left behind on water and sand (0..2)
#define SWASH_REACH 1.0       // how far the waves run up the sand (0.3..2)
#define SEA_RIPPLES 1.0       // small wind ripples on the water (0..2)
#define GLITTER 1.0           // sparkle of the sun on the water (0..2)
#define CAUSTICS 1.0          // net of light on the sandy bottom near the shore, 0 is cheaper (0..2)
#define SURF_STEPS 7          // precision of the wave shapes; fewer is cheaper, 0 = flat water with the waves only shaded (0..16)
// ---- the beach
#define SHORE_BENDS 1.0       // how much the waterline curves (0..2)
#define SHORE_DISTANCE 7.5    // distance from you to the waterline in metres (4..14)
#define WET_REFLECTION 1.0    // sky mirrored in the wet sand (0..1.5)
#define MIRROR_CLOUDS 1       // 1 = clouds show in the wet sand, 0 = plain sky only (cheaper)
#define WET_SAND 1.0          // darkness of the wet sand (0..1.5)
#define SAND_R 0.93           // sand colour: red
#define SAND_G 0.82           // sand colour: green
#define SAND_B 0.64           // sand colour: blue
#define SAND_RIPPLES 1.0      // strength of the ripples and hollows in the sand (0..2)
#define RIPPLE_SCALE 1.0      // size of the sand ripples (0.4..3)
#define SAND_SPARKLE 1.0      // glint of single grains (0..2)
#define SHELLS 1.0            // shells and pebbles (0..3)
#define FOOTPRINTS 1          // 1 = a trail of footprints leads down to the water
// ---- framing
#define PALM 1                // 1 = a palm leans into the picture
#define PALM_SIDE 1           // 1 = palm on the right, 0 = on the left
#define PALM_SWAY 1.0         // breeze in the palm fronds (0..3)
#define PALM_SHADOW 1.0       // strength of the palm's shadow on the sand (0..1)
#define HEADLAND 1            // 1 = the far end of the bay shows on the horizon
#define ISLAND 1              // 1 = a small island far out
#define SAILBOAT 1            // 1 = a sailboat far out
#define GULLS 4               // number of gulls (0..8)
// ---- camera and picture
#define CAMERA_HEIGHT 1.8     // height of your eyes above the sand in metres (1.2..6)
#define CAMERA_TILT 5.0       // how far the camera looks down, degrees (0..20)
#define VIEW_TURN 24.0        // turn the view along the beach, degrees: 0 = straight out to sea (-60..60)
#define HORIZON 0.40          // horizon position from the top (0.2..0.6)
#define ZOOM 1.0              // lens: below 1 = wide, above 1 = long (0.6..1.8)
#define SPEED 1.0             // speed of everything (0..3)
#define BRIGHTNESS 1.0        // overall brightness (0.5..1.5)
#define SATURATION 1.0        // colour strength (0..1.6)
#define VIGNETTE 0.3          // darker corners (0..1)
#define TEXT_VEIL 0.5         // darkening behind terminal text (0..1)

float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

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
    return lerp(lerp(hash12(i), hash12(i + float2(1.0, 0.0)), f.x), lerp(hash12(i + float2(0.0, 1.0)), hash12(i + float2(1.0, 1.0)), f.x), f.y);
}

// value noise with its gradient: x = value, yz = slope
float3 vnoised(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 w = f * f * (3.0 - 2.0 * f);
    float2 dw = 6.0 * f * (1.0 - f);
    float a = hash12(i), b = hash12(i + float2(1.0, 0.0)), c = hash12(i + float2(0.0, 1.0)), d = hash12(i + float2(1.0, 1.0));
    float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
    return float3(a + k1 * w.x + k2 * w.y + k4 * w.x * w.y, dw.x * (k1 + k4 * w.y), dw.y * (k2 + k4 * w.x));
}

float fbm3(float2 p)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 3; i++) { s += a * vnoise(p); p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6) * 1.05; a *= 0.5; }
    return s;
}

float fbmCloud(float2 p)
{
    float s = 0.0, a = 0.5;
    for (int i = 0; i < CLOUD_DETAIL; i++) { s += a * vnoise(p); p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6) * 1.08; a *= 0.5; }
    return s / (1.0 - 2.0 * a) * 0.97;                              // same cover whatever the number of layers
}

// distance between the nearest and the second nearest cell centre: small along a thin net of lines (foam lace, light on the bottom)
float cells(float2 p, float t)
{
    float2 i = floor(p), f = frac(p);
    float d1 = 8.0, d2 = 8.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(float(x), float(y));
        float2 h = hash22(i + g);
        float2 w = abs(frac(t * (0.08 + 0.08 * h) + h) * 2.0 - 1.0);
        float2 o = 0.1 + 0.8 * w * w * (3.0 - 2.0 * w);          // every centre drifts to and fro
        float d = length(g + o - f);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
    }
    return d2 - d1;
}

// ---------------------------------------------------------------- waves
// All wave work happens in shore coordinates: s = metres out to sea from the resting waterline, u = metres along it.

// size of wave number n: waves arrive in sets
float waveAmp(float n)
{
    float g = abs(frac(n * 0.131) * 2.0 - 1.0);
    return lerp(1.0, 0.48 + 0.84 * g * g * (3.0 - 2.0 * g) + 0.30 * (hash11(n * 7.13 + 2.0) - 0.5), WAVE_SETS);
}

// phase of the wave train: whole numbers are crests. Waves slow down and bunch up as the water gets shallow.
float wavePhase(float s, float tw)
{
    float p = 0.60 * (sqrt(max(s, 0.0) + 12.0) - 3.4641) + tw;
    float g = abs(frac(p * 0.33423 + 0.8614) * 2.0 - 1.0);
    return p + WAVE_SETS * 0.16 * (g * g * (6.0 - 4.0 * g) - 1.0);        // every third wave or so comes a little early or late
}

// height of the water surface: low round swell far out, steep faces near the break line bl, collapsing bores inside it
float surfHeight(float s, float tw, float bl, out float q)
{
    q = wavePhase(s, tw);
    float n = floor(q), fr = q - n;
    float sc = max(s, 0.0);
    float outside = max(sc - bl, 0.0) * 0.09;
    float steep = 1.0 / (1.0 + outside * (1.0 + 0.5 * outside));
    float fr2 = fr * fr;
    float fs = lerp(fr, fr2 * fr2, steep);                              // the face turned to the beach steepens as the wave comes in
    float a = lerp(waveAmp(n), waveAmp(n + 1.0), fs * fs * (3.0 - 2.0 * fs));
    float blw = max(bl + 5.0 * (a - 1.0), 3.0);
    float x = sc / blw;
    float y = (sc - blw) * 0.07;
    float env = x < 1.0 ? sqrt(x) * (0.34 + 0.66 * x * x * x) : 0.34 + 0.66 / (1.0 + y * (1.0 + 0.5 * y));
    env *= smoothstep(0.8, 3.0, sc) * (1.0 - smoothstep(50.0, 95.0, sc));
    float z = abs(fs * 2.0 - 1.0);
    float c = z * z * (3.0 - 2.0 * z);                                  // round swell ...
    return 0.78 * WAVE_HEIGHT * a * env * lerp(c, c * c * sqrt(c), steep);   // ... that grows a sharper crest
}

// the resting waterline is not straight: s is measured from this curve
float shoreBend(float u)
{
    return SHORE_BENDS * (2.6 * (vnoise(float2(u * 0.040 + 3.7, 1.5)) - 0.5) + 0.9 * (vnoise(float2(u * 0.13 + 9.1, 4.5)) - 0.5) + 0.22 * sin(u * 0.52));
}

// how far up the sand wave n runs at shore position u, in metres
float runupOf(float u, float n)
{
    return SWASH_REACH * 2.1 * waveAmp(n) * (0.50 + 1.0 * vnoise(float2(u * 0.24 + n * 3.37, n * 1.91)));
}

// ---------------------------------------------------------------- sky

float3 skyBase(float3 rd, float3 sunDir, float3 sunC, float gold)
{
    float y = saturate(rd.y);
    float sd = saturate(dot(rd, sunDir));
    float sdh = saturate(dot(normalize(rd.xz + float2(0.0, 1e-4)), normalize(sunDir.xz + float2(0.0, 1e-4))));
    float3 zen = lerp(float3(SKY_R, SKY_G, SKY_B), float3(0.17, 0.27, 0.52), gold * 0.85);
    float3 hor = lerp(float3(0.70, 0.87, 0.97), lerp(float3(0.93, 0.66, 0.52), float3(1.00, 0.72, 0.36), sdh * sdh), gold);
    float3 col = lerp(zen, hor, pow(1.0 - y, lerp(4.0, 5.5, gold)));
    col = lerp(col, lerp(hor, float3(0.98, 0.80, 0.62), 0.5), gold * 0.35 * pow(1.0 - y, 2.0));
    col += sunC * (pow(sd, 6.0) * 0.10 + pow(sd, 48.0) * 0.22 + pow(sd, 600.0) * 0.5) * SUN_GLOW * (0.6 + 0.9 * gold);
    return col;
}

float3 skyClouds(float3 col, float3 rd, float3 sunDir, float3 sunC, float gold, float t)
{
    if (rd.y > 0.002)
    {
        float2 cp = rd.xz / (rd.y + 0.11) * 0.62 + float2(t * 0.012 * CLOUD_SPEED, t * 0.004 * CLOUD_SPEED);
        float sd = saturate(dot(rd, sunDir));
        float3 hor = lerp(float3(0.70, 0.87, 0.97), float3(1.00, 0.72, 0.42), gold);
        // thin high cloud
        if (HIGH_CLOUDS > 0.0)
        {
            float2 hp = float2(cp.x * 0.22 + 7.0, cp.y * 0.9 - cp.x * 0.3) + t * 0.004 * CLOUD_SPEED;
            float ci = vnoise(hp) * 0.60 + vnoise(float2(hp.x * 1.9, hp.y * 3.7) + 5.0) * 0.40;
            float cirrus = smoothstep(0.40, 0.85, ci) * HIGH_CLOUDS * smoothstep(0.05, 0.28, rd.y);
            col = lerp(col, lerp(float3(1.0, 1.0, 1.0), sunC, 0.5 * gold), cirrus * 0.45);
        }
        // cumulus: white tops towards the sun, soft blue-grey bases
        float f1 = fbmCloud(cp);
        float lo = lerp(0.78, 0.40, saturate(CLOUDS));
        float dens = smoothstep(lo, lo + 0.15, f1) * step(0.001, CLOUDS);
        float sunny = 0.6;
        if (dens > 0.0)
        {
            float2 toSun = normalize(sunDir.xz / (sunDir.y + 0.11) * 0.62 - rd.xz / (rd.y + 0.11) * 0.62 + float2(1e-4, 0.0));
            sunny = saturate(0.62 + (f1 - fbm3(cp + toSun * 0.30) * 1.109) * 3.6);
        }
        float3 shadeC = lerp(float3(0.60, 0.69, 0.82), float3(0.46, 0.38, 0.50), gold);
        float3 brightC = lerp(float3(1.0, 1.0, 1.0), sunC * 1.15, 0.25 + 0.75 * gold) * (1.0 + 0.5 * gold * pow(sd, 4.0));
        float3 cloudC = lerp(shadeC, brightC, sunny) * (1.0 - 0.20 * smoothstep(lo + 0.12, lo + 0.40, f1));
        cloudC = lerp(cloudC, hor, exp(-rd.y * 11.0) * 0.55);
        col = lerp(col, cloudC, dens * smoothstep(0.035, 0.13, rd.y) * 0.96);
    }
    return col;
}

// ---------------------------------------------------------------- things

// one palm frond. q = metres from the centre of the crown, fk = which frond; returns x = cover, y = brightness of the leaflet
float2 frond(float2 q, float fk, float soft, float swS, float swC, float t)
{
    float sw = swS * cos(fk * 1.7) + swC * sin(fk * 1.7);                                // each frond sways with its own delay
    float ang = fk * 0.52 - 0.50 + 0.20 * sin(fk * 2.7);
    float len = 3.25 + 0.55 * sin(fk * 3.1);
    float2 dir = float2(cos(ang) - 0.035 * sw * sin(ang), sin(ang) + 0.035 * sw * cos(ang));
    float2 down = dir.x >= 0.0 ? float2(dir.y, -dir.x) : float2(-dir.y, dir.x);           // the side of the stem that faces the ground
    float droop = (0.150 + 0.035 * sin(fk * 4.1) + 0.012 * sw) * (0.35 + 0.65 * abs(dir.x));
    float a = dot(q, dir);
    float d = dot(q, down) - droop * a * a;                                              // the stem bends down under its own weight
    float2 res = float2(0.0, 0.5);
    if (a > 0.0 && a < len && abs(d) < 0.62 + soft)
    {
        float x = a / len;
        d += PALM_SWAY * 0.035 * sin(t * 2.6 + a * 3.0 + fk) * x;
        float li = a * 24.0 - abs(d) * 13.0 + 0.5 * step(0.0, d);
        float lid = floor(li), lf = li - lid;
        float lh = hash11(lid * 1.37 + fk * 31.0 + step(0.0, d) * 7.0);
        float wide = (1.40 * sqrt(x) * (1.0 - x) + 0.012) * (0.70 + 0.30 * lh);
        float rel = saturate(abs(d) / wide);
        float aaC = min(soft * 24.0, 0.5);
        float duty = 0.92 - 0.80 * rel * rel;                                            // leaflets touch at the stem and taper to a point
        float comb = lerp(1.0 - smoothstep(duty - aaC - 0.03, duty + aaC, abs(lf - 0.5) * 2.0), duty * 0.9, saturate(aaC * 2.5 - 0.4));
        float leaf = comb * (1.0 - smoothstep(wide - soft * 2.0 - 0.01, wide + soft, abs(d)));
        float spine = 1.0 - smoothstep(0.016, 0.022 + soft, abs(d));
        res = float2(max(leaf, spine) * (1.0 - smoothstep(0.97, 1.0, x)), saturate(0.30 + 0.45 * lh + 0.35 * rel - 0.25 * spine + 0.12 * sin(fk * 5.3)));
    }
    return res;
}

// palm in its own upright plane. q = metres from the centre of the crown, soft = blur of the edges in metres,
// hgt = height of the crown above the ground, side = which way the trunk leans away.
// returns x = fronds, y = brightness variation of the leaflets, z = trunk
float3 palmShape(float2 q, float soft, float hgt, float side, float t)
{
    float2 best = float2(0.0, 0.5);
    float swS = PALM_SWAY * sin(t * 0.9), swC = PALM_SWAY * cos(t * 0.9);
    if (q.x > -0.75)
    {
        // the fronds that point to the right
        for (int ka = 0; ka < 5; ka++)
        {
            float2 fa = frond(q, float(ka), soft, swS, swC, t);
            if (fa.x > best.x) best = fa;
        }
    }
    if (q.x < 0.60)
    {
        // and those that point to the left
        for (int kb = 5; kb < 9; kb++)
        {
            float2 fc = frond(q, float(kb), soft, swS, swC, t);
            if (fc.x > best.x) best = fc;
        }
    }
    // trunk: curves down and away from the crown
    float below = saturate(-q.y / hgt);
    float cx = side * 2.6 * below * sqrt(below);
    float tw = 0.12 + 0.06 * below;
    float trunk = (1.0 - smoothstep(tw - soft, tw + soft, abs(q.x - cx))) * step(q.y, 0.1) * step(-hgt, q.y);
    return float3(best.x, best.y, trunk);
}

float sailboat(float2 p, float s)
{
    float2 q = p / s;
    float hull = step(0.0, q.y) * step(q.y, 0.14) * step(abs(q.x + 0.05), 0.46 + q.y * 0.9);
    float mast = step(abs(q.x), 0.012) * step(0.14, q.y) * step(q.y, 1.22);
    float mainSail = step(0.035, q.x) * step(0.22, q.y) * step(q.y, 1.18 - (q.x - 0.035) * 1.75);
    float jib = step(q.x, -0.035) * step(0.22, q.y) * step(q.y, 1.05 + q.x * 1.9);
    return hull * 2.0 + saturate(mast + mainSail + jib);       // 2 = hull, 1 = sails
}

float gull(float2 p, float s, float flap)
{
    float2 q = p / s;
    float wing = abs(q.x) * (0.25 + 0.55 * flap) - q.x * q.x * 0.35;
    return (1.0 - smoothstep(0.045, 0.11, abs(q.y - wing))) * step(abs(q.x), 1.0);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float t = Time * SPEED;
    float3 sandC = float3(SAND_R, SAND_G, SAND_B);
    float3 shallowC = float3(SHALLOW_R, SHALLOW_G, SHALLOW_B);
    float3 deepC = float3(DEEP_R, DEEP_G, DEEP_B);

    // ---- one camera for everything: eye above the sand, looking out to sea, horizon level
    float fit = max(1.0, 0.85 / aspect) * lerp(1.0, 0.82, smoothstep(2.2, 3.6, aspect));   // tall screens see more, very wide ones zoom in a little
    float flip = VIEW_TURN < 0.0 ? -1.0 : 1.0;                     // a view turned to the left is the mirror image
    float focal = 1.35 * ZOOM;
    float tilt = CAMERA_TILT * 0.0174533;
    float ct = cos(tilt), st = sin(tilt);
    float halfW = aspect * 0.5 * fit;
    float2 uv = float2((tex.x - 0.5) * aspect * flip, HORIZON - tex.y) * fit;
    float3 rd = normalize(float3(uv.x, (uv.y + focal * st / ct) * ct - focal * st, (uv.y + focal * st / ct) * st + focal * ct));
    float camH = CAMERA_HEIGHT;
    float turn = abs(VIEW_TURN) * 0.0174533;
    float ca = cos(turn), sa = sin(turn);
    float pxAng = fit / (focal * Resolution.y);                    // angle of one pixel

    // ---- light
    float sunEl = SUN_HEIGHT * 0.0174533, sunAz = SUN_DIRECTION * 0.0174533 * flip;
    float3 sunDir = float3(sin(sunAz) * cos(sunEl), sin(sunEl), cos(sunAz) * cos(sunEl));
    float gold = 1.0 - smoothstep(7.0, 32.0, SUN_HEIGHT);          // 0 = midday, 1 = golden hour
    float3 sunC = lerp(float3(1.0, 0.97, 0.90), float3(1.0, 0.66, 0.34), gold);
    float3 lightC = lerp(float3(1.0, 1.0, 1.0), float3(1.04, 0.84, 0.66), gold);
    float2 sunUS = float2(sunDir.z * sa + sunDir.x * ca, sunDir.z * ca - sunDir.x * sa);   // sun direction in shore coordinates (u, s)
    float3 hazeC = skyBase(normalize(float3(rd.x, 0.012, rd.z)), sunDir, sunC, gold);
    float tw0 = t / WAVE_PERIOD;

    float3 col;
    float2 gp = float2(0.0, 0.0);          // where the view ray meets the ground (camera space x, z)
    float onGround = 0.0;

    if (rd.y < -0.0006)
    {
        onGround = 1.0;
        float tPl = camH / -rd.y;
        gp = rd.xz * tPl;
        float uB = gp.y * sa + gp.x * ca;
        float sB = gp.y * ca - gp.x * sa - SHORE_DISTANCE - shoreBend(uB);
        float tw = tw0 + 0.20 * (vnoise(float2(uB * 0.06, 7.7 + t * 0.01)) - 0.5);       // crests are not quite parallel to the shore
        float bl = 12.0 + 7.0 * (vnoise(float2(uB * 0.07 + 2.0, 3.3)) - 0.5) + 3.0 * (vnoise(float2(uB * 0.23, 8.1)) - 0.5);   // where the waves break here

        // the swash: the wave that arrived last runs up the sand and slides back
        float q0 = wavePhase(0.0, tw);
        float n0 = floor(q0), fr0 = q0 - n0;
        float gsw = fr0 / (fr0 + 0.6 * (1.0 - fr0));
        float f0 = sin(3.14159 * gsw);                              // quick run-up, slow slide back
        float r0 = 0.0, r1 = 0.0, r2 = 0.0, edge = 0.0;
        if (sB < 9.0)
        {
            r0 = runupOf(uB, n0); r1 = runupOf(uB, n0 - 1.0); r2 = runupOf(uB, n0 - 2.0);
            edge = -r0 * f0 + 0.10 * (vnoise(float2(uB * 2.3, t * 0.25 + n0)) - 0.5) * f0;
        }

        // ---- sand under everything near the shore
        float2 W = float2(uB, sB);
        float fA = tPl * pxAng;                                     // size of a pixel on the ground: across
        float fD = fA * tPl / camH;                                 // and in depth
        float tone = 0.5;
        if (sB < 16.0) tone = fbm3(W * 0.33 + 3.0);
        float3 sand = sandC * (0.90 + 0.18 * tone);

        if (sB > edge)
        {
            // ================= water
            float sH = sB, uH = uB, tau = tPl, hgt = 0.0;
            float qH = 0.0;
            float hmax = 0.78 * WAVE_HEIGHT * 1.42;
            float tTop = max((camH - hmax) / -rd.y, 0.0);
            float2 gA = rd.xz * tTop;
            float uA = gA.y * sa + gA.x * ca;
            float sA0 = gA.y * ca - gA.x * sa - SHORE_DISTANCE;
            float qa = 0.0, qb = 0.0;
            float slopeS = 0.0;
            if (sA0 < 95.0 && sB > 0.8)
            {
                if (SURF_STEPS > 0)
                {
                    // short march through the surf: find where the ray meets the wave surface
                    float sA = sA0 - shoreBend(uA);
                    float yTop = camH + rd.y * tTop;
                    float qm = 0.0;
                    float prevD = yTop - surfHeight(sA, tw, bl, qm);
                    float fLo = 0.0, fHi = 1.0, dLo = prevD, dHi = -1e-3, qLo = qm;
                    for (int i = 1; i <= SURF_STEPS; i++)
                    {
                        float fi = float(i) / max(float(SURF_STEPS), 1.0);
                        float dd = yTop * (1.0 - fi) - surfHeight(lerp(sA, sB, fi), tw, bl, qm);
                        if (dd < 0.0) { fHi = fi; dHi = dd; break; }
                        float crest = max(floor(qLo), floor(qm));
                        if (crest > min(qLo, qm))
                        {
                            // a crest lies between this point and the last one: make sure the ray does not slip over it unseen
                            float qc = 0.0;
                            float fc = lerp(fLo, fi, saturate((crest - qLo) / (qm - qLo)));
                            float dc = yTop * (1.0 - fc) - surfHeight(lerp(sA, sB, fc), tw, bl, qc);
                            fc = clamp(fc + (crest - qc) * (fi - fLo) / (qm - qLo), fLo, fi);
                            dc = yTop * (1.0 - fc) - surfHeight(lerp(sA, sB, fc), tw, bl, qc);
                            if (dc < 0.0) { fHi = fc; dHi = dc; break; }
                        }
                        fLo = fi; dLo = dd; qLo = qm;
                    }
                    // close in on the surface between the last point above the water and the first below it
                    for (int j = 0; j < 3; j++)
                    {
                        float fm = 0.5 * (fLo + fHi);
                        float dm = yTop * (1.0 - fm) - surfHeight(lerp(sA, sB, fm), tw, bl, qm);
                        if (dm < 0.0) { fHi = fm; dHi = dm; } else { fLo = fm; dLo = dm; }
                    }
                    float fHit = lerp(fLo, fHi, dLo / max(dLo - dHi, 1e-5));
                    sH = lerp(sA, sB, fHit); uH = lerp(uA, uB, fHit); tau = lerp(tTop, tPl, fHit);
                }
                hgt = surfHeight(sH, tw, bl, qH);
                float ha = surfHeight(sH - 0.15, tw, bl, qa), hb = surfHeight(sH + 0.15, tw, bl, qb);
                slopeS = (hb - ha) / 0.30;
            }
            else
            {
                qH = wavePhase(sH, tw); qa = wavePhase(sH - 0.15, tw); qb = wavePhase(sH + 0.15, tw);
            }
            float lam = 0.30 / max(qb - qa, 1e-4);                  // local wavelength in metres
            float sc = max(sH, 0.0);
            float nW = floor(qH), frW = qH - nW;
            float aBehind = waveAmp(nW), aAhead = waveAmp(nW + 1.0);
            float dAhead = (1.0 - frW) * lam;                       // metres in front of the crest that comes next
            float dBehind = frW * lam;                              // metres behind the crest that just passed
            float brokeA = smoothstep(-0.5, 2.5, bl + 5.0 * (aAhead - 1.0) - sH);     // has the coming wave broken at this spot?
            float brokeB = smoothstep(-0.5, 2.5, bl + 5.0 * (aBehind - 1.0) - sH);    // had the passed wave broken when it came by?
            float dEdge = sH - edge;
            float fDh = tau * pxAng * tau / camH;

            // small wind waves: slopes in (u, s), fading with distance before they turn to shimmer
            float k1 = 1.0 - smoothstep(0.25, 0.9, fDh), k2 = 1.0 - smoothstep(0.07, 0.25, fDh);
            float3 n1 = float3(0.5, 0.0, 0.0), n2 = float3(0.5, 0.0, 0.0);
            if (k1 > 0.0) n1 = vnoised(float2(uH * 0.55 + t * 0.10, sH * 0.85 + t * 0.32));
            if (k2 > 0.0) n2 = vnoised(float2(uH * 1.9 - t * 0.22, sH * 2.6 + t * 0.55) + 13.0);
            float calm = saturate(dEdge * 0.7) * SEA_RIPPLES;
            float2 slopeUS = n1.yz * 0.085 * k1 * calm + n2.yz * 0.060 * k2 * calm;
            slopeUS.y += slopeS;
            float2 slopeXZ = float2(slopeUS.x * ca - slopeUS.y * sa, slopeUS.x * sa + slopeUS.y * ca);
            float3 nrm = normalize(float3(-slopeXZ.x, 1.0, -slopeXZ.y));

            // colour of the water body: sand showing through at the edge, turquoise shallows, deep blue far out
            float depth = sc * 0.075 + 0.02 + 0.04 * saturate(dEdge) + hgt * 0.8;
            float seeBottom = exp(-depth * 1.5 / max(WATER_CLARITY, 0.05));
            float3 waterC = lerp(shallowC, deepC, 1.0 - exp(-sc / 48.0));
            float rf = vnoise(float2(uH * 0.045 + 11.0, sH * 0.060 + 4.0)) * 0.7 + vnoise(float2(uH * 0.13, sH * 0.17) + 20.0) * 0.3;
            float reef = smoothstep(0.46, 0.62, rf) * smoothstep(10.0, 30.0, sc) * REEFS;
            waterC = lerp(waterC, waterC * float3(0.26, 0.42, 0.56), saturate(reef * 0.8));
            waterC *= 0.90 + 0.25 * smoothstep(0.35, 0.75, 1.0 - rf) * smoothstep(6.0, 20.0, sc);      // pale sand bars between them
            float3 bottom = sand * float3(0.80, 0.78, 0.74) * lerp(float3(1.0, 1.0, 1.0), shallowC * 1.5 + 0.12, saturate(depth * 2.2));
            if (CAUSTICS > 0.0 && seeBottom > 0.08 && fDh < 0.5)
            {
                float2 cpos = float2(uH, sH) * 1.5 + n1.yz * 0.5;
                float cst = pow(saturate(1.0 - cells(cpos, t * 1.1) * 1.5), 3.0);
                bottom *= 1.0 + cst * 0.75 * CAUSTICS * saturate(depth * 6.0) * (1.0 - smoothstep(0.15, 0.5, fDh));
            }
            float3 body = lerp(waterC * (0.80 + 0.25 * saturate(sunDir.y * 1.4)), bottom, seeBottom) * lightC;
            // light shining through the steep face of a wave about to break
            float face = saturate(slopeS * 2.4) * smoothstep(0.03, 0.22, hgt);
            float hfr = saturate(hgt / (0.78 * WAVE_HEIGHT * max(aAhead, 0.3)));
            body *= lerp(float3(1.0, 1.0, 1.0), float3(0.42, 0.68, 0.74), face * (1.0 - 0.8 * hfr * hfr));      // the wall of water is dark at its foot
            body += shallowC * float3(0.60, 1.00, 0.80) * face * hfr * hfr * 0.50;                     // and glows where it thins towards the lip
            body *= 1.0 + 0.16 * saturate(-slopeS * 4.0);                                              // the back of the wave is turned to the sky
            // far swell lines that the march no longer shapes
            body *= 1.0 + 0.10 * cos(6.2832 * frW) * smoothstep(40.0, 90.0, sc) * (1.0 - smoothstep(0.5, 2.0, fDh / max(lam, 1.0) * 6.0));

            float cosV = saturate(dot(nrm, -rd));
            float fres = 0.02 + 0.80 * pow(1.0 - cosV, 5.0);
            float3 rfl = reflect(rd, nrm);
            rfl.y = abs(rfl.y) + 0.06;
            float3 skyR = skyBase(normalize(rfl), sunDir, sunC, gold);
            skyR = lerp(skyR, float3(1.0, 1.0, 1.0) * lightC, 0.18 * CLOUDS);
            col = lerp(body, skyR, fres);

            // sun glitter: a facet flashes when it tilts just right
            float3 hv = normalize(sunDir - rd);
            float2 need = -hv.xz / max(hv.y, 0.05) - slopeXZ;
            float rough = 0.13 + 0.10 * (1.0 - k1) + 0.04 * SEA_RIPPLES;
            float gdens = exp(-dot(need, need) / (rough * rough));
            float2 cell = floor(tex * Resolution / max(1.0, floor(Resolution.y / 700.0)));
            float tk = hash12(cell);
            float flash = pow(saturate(sin(t * (2.0 + 3.0 * tk) + tk * 40.0 + uH * 3.0) * 0.5 + 0.5), 12.0);
            col += sunC * gdens * (0.35 + 5.0 * flash * step(0.55, hash12(cell + 7.0)) * k1) * GLITTER * calm * (0.5 + 0.5 * gold + 0.5 * fres);

            // ---- foam: one density field; where it is high the foam is solid, where it thins out only a lace of lines is left
            float upK = 1.0 - smoothstep(0.30, 0.62, fr0);
            float sheet = smoothstep(2.5, 0.0, sc);
            float rollMax = lerp(0.10, 1.3, brokeA) * 1.65;
            // churned water behind a broken crest and the trail it leaves; old foam near the shore; the swash sheet
            float dens = max(brokeB * (1.15 * exp(-dBehind / 1.4) + 0.55 * exp(-dBehind / (0.28 * lam))), 0.30 * exp(-sc / 6.0));
            dens = max(dens, sheet * max(exp(-fr0 * 2.5) * 0.50, exp(-dEdge * 2.6) * upK * 1.25));
            if (dens > 0.015 || (brokeA > 0.0 && dAhead < rollMax + 2.0))
            {
                float2 fuv = float2(uH, sH);
                float fb = fbm3(fuv * 0.9 + float2(t * 0.03, t * 0.10));
                float fine = vnoise(fuv * float2(2.2, 4.5) + float2(t * 0.2, t * 0.5));
                float kL = 1.0 - smoothstep(0.10, 0.32, fDh);
                // the white roller tumbling down the face of a broken wave
                float rollW = rollMax * (0.21 + 0.42 * fb + 0.36 * fine);
                float roller = (1.0 - smoothstep(rollW * 0.6, rollW, dAhead)) * smoothstep(0.0, 0.2, brokeA);
                dens = max(dens, roller * 1.3);
                float cl = 1.0;
                if (dens > 0.02 && kL > 0.0) cl = cells(fuv * float2(2.7, 2.2) + n1.yz * 1.1 + n2.yz * 0.35 + float2(0.0, t * 0.05), t);
                float gaps = smoothstep(0.28, 0.62, fb);                                  // foam gathers in patches and leaves clear water between
                dens = dens * lerp((0.10 + 1.5 * gaps) * FOAM_LACE, 0.45 + 1.1 * fb, saturate(dens * 1.4 - 0.5)) + (fine - 0.5) * 0.30 * saturate(dens * 2.0);
                float foam = smoothstep(0.0, 0.10 + 0.10 * fine, dens - cl * (1.5 + 1.2 * fine));
                foam = lerp(saturate(dens * 1.3 - 0.12), foam, kL);
                foam = max(foam, smoothstep(0.30, 0.55, max(roller, brokeB * exp(-dBehind / 1.1)) * (0.55 + 0.7 * fb + 0.25 * fine)));      // the roller itself is solid
                foam = max(foam, sheet * exp(-dEdge * 20.0) * (0.35 + 0.65 * upK));
                foam = saturate(foam * SURF_FOAM);
                float solid = smoothstep(0.5, 1.1, dens);
                float sunOn = smoothstep(0.15, 0.75, dot(normalize(float3(-slopeXZ.x * 1.6, 1.0, -slopeXZ.y * 1.6)), sunDir) / max(sunDir.y, 0.2));
                float streak = vnoise(fuv * float2(8.0, 2.6) + float2(0.0, t * 0.8));
                float3 foamC = lerp(float3(0.80, 0.93, 0.95), float3(1.0, 1.0, 1.0), solid) * lightC * (0.74 + 0.10 * fine + 0.10 * streak + 0.08 * smoothstep(0.0, 0.3, cl) + 0.06 * saturate(0.5 - dot(n2.yz, sunUS) * 0.6));
                foamC *= lerp(float3(0.66, 0.78, 0.90), float3(1.0, 1.0, 1.0), lerp(1.0, sunOn, solid * smoothstep(0.05, 0.3, hgt)));       // the face of the roller is in shade
                foamC = lerp(foamC, foamC * float3(0.80, 0.90, 0.97), solid * smoothstep(0.55, 0.25, fb + 0.3 * fine));         // hollows in thick foam are bluish
                col *= 1.0 - 0.30 * brokeA * (1.0 - roller) * exp(-max(dAhead - rollW, 0.0) * 2.2) * smoothstep(0.05, 0.25, hgt);   // shadow in front of the roller
                col *= 1.0 - 0.20 * sheet * exp(-dEdge * 9.0) * (1.0 - foam);                                                    // dark line where the sheet meets the sand
                col = lerp(col, foamC, foam);
            }

            col = lerp(col, hazeC, saturate(1.0 - exp(-tau * 0.0011 * HAZE)));
            gp = rd.xz * tau;
        }
        else
        {
            // ================= sand
            float up = -sB;                                         // metres up the beach from the resting waterline
            float wob = (tone - 0.5) * 0.6;
            float reach0 = r0 * (fr0 < 0.375 ? f0 : 1.0);           // how far the present wave has come so far
            float damp0 = 1.0 - smoothstep(reach0 - 0.02, reach0 + 0.12, up + wob * 0.2);
            float damp1 = 1.0 - smoothstep(r1 - 0.05, r1 + 0.25, up + wob * 0.5);
            float damp2 = 1.0 - smoothstep(r2 - 0.05, r2 + 0.35, up + wob);
            float dampB = (1.0 - smoothstep(1.6, 3.2, (up + wob * 2.0) / SWASH_REACH)) * 0.60;   // the sand below the tide mark never dries
            float damp = max(max(damp0, damp1 * (1.0 - 0.25 * fr0)), max(damp2 * (0.75 - 0.75 * fr0), dampB));
            // a film of water stays for a moment where the sheet has just been, and near the waterline
            float film = saturate(damp0 * exp(-max(up + edge, 0.0) * 0.55) + damp1 * exp(-up * 0.55 - fr0 * 1.6) + exp(-up * 0.8) * 0.5);

            float dry = 1.0 - damp;
            float3 sandN = float3(0.0, 1.0, 0.0);
            float relief = 0.0;
            // hollows and bumps of dry, trodden sand; the wet sand is washed smooth
            float kA = 1.0 - smoothstep(0.10, 0.35, fD), kB = 1.0 - smoothstep(0.03, 0.10, fD);
            float3 l1 = vnoised(W * 2.3);
            float3 l2 = vnoised(W * 7.1 + 5.0);
            relief += dot(l1.yz, sunUS) * -0.16 * kA * (0.35 + 0.65 * dry) + dot(l2.yz, sunUS) * -0.06 * kB * dry;
            // wind and water ripples, in patches
            float rl = 0.20 * RIPPLE_SCALE;
            float kR = 1.0 - smoothstep(0.18 * rl, 0.55 * rl, fD);
            float rph = (W.y * 0.94 + W.x * 0.34) * 6.2832 / rl + 5.0 * l1.x + 3.0 * tone;
            float rpatch = smoothstep(0.30, 0.60, vnoise(W * 0.22 + 31.0));
            relief += sin(rph + 1.0) * 0.085 * kR * rpatch + pow(saturate(sin(rph)), 4.0) * 0.05 * kR * rpatch;
            relief *= SAND_RIPPLES;
            float grain = (hash12(floor(W * 70.0)) - 0.5) * 0.10 * (1.0 - smoothstep(0.010, 0.030, fD)) + (vnoise(W * 26.0) - 0.5) * 0.07 * (1.0 - smoothstep(0.02, 0.08, fD));
            grain += (hash12(tex * Resolution + 3.0) - 0.5) * 0.035;
            float3 drySand = sand * (1.0 + relief + grain * (0.4 + 0.6 * dry));

            // footprints: a trail from where you stand down to the water, washed out where the waves have been
            if (FOOTPRINTS > 0)
            {
                float2 fdir = float2(0.287, 0.958);
                float2 fp = gp - float2(0.25, 2.0);
                float along = dot(fp, fdir), across = dot(fp, float2(fdir.y, -fdir.x)) + 0.10 * sin(along * 0.7);
                float stepI = floor(along / 0.38);
                float lr = fmod(stepI, 2.0) < 0.5 ? -1.0 : 1.0;
                float2 pl = float2(along - (stepI + 0.5) * 0.38, across - lr * 0.085 - (hash11(stepI) - 0.5) * 0.03);
                float dBall = length((pl - float2(0.045, 0.0)) / float2(0.085, 0.050));
                float dHeel = length((pl + float2(0.075, 0.0)) / float2(0.050, 0.038));
                float dP = min(dBall, dHeel);
                float inP = 1.0 - smoothstep(0.55, 1.05, dP);
                float rim = smoothstep(0.9, 1.15, dP) * (1.0 - smoothstep(1.15, 1.7, dP));
                float2 sunCam = normalize(sunDir.xz + float2(1e-4, 0.0));
                float side = dot(normalize(pl + float2(1e-4, 0.0)), float2(dot(sunCam, fdir), dot(sunCam, float2(fdir.y, -fdir.x))));
                float keep = (1.0 - smoothstep(0.02, 0.08, fD)) * (1.0 - 0.9 * max(damp0, damp1)) * (1.0 - 0.5 * damp) * step(0.0, along);
                drySand *= 1.0 + keep * (inP * (-0.11 - 0.22 * side) + rim * 0.08 * (0.3 - side));
            }

            // wet sand: darker, cooler, and a mirror for the sky at this flat angle
            float3 wetSand = sand * float3(0.64, 0.62, 0.59) * (1.0 + relief * 0.3 + grain * 0.3);
            float dk = saturate(damp * WET_SAND);
            col = lerp(drySand, wetSand, dk * 0.92) * lightC;
            float3 rs = reflect(rd, normalize(float3(l2.y * 0.02, 1.0, l2.z * 0.02)));
            rs.y = abs(rs.y);
            float3 mirror = skyBase(rs, sunDir, sunC, gold);
            if (MIRROR_CLOUDS > 0 && film > 0.02) mirror = skyClouds(mirror, rs, sunDir, sunC, gold, t);
            float fresS = 0.06 + 0.94 * pow(1.0 - saturate(-rd.y), 3.0);
            col = lerp(col, lerp(mirror, hazeC, 0.30), saturate(film * fresS * WET_REFLECTION * 1.5));
            float sgl = pow(saturate(dot(rs, sunDir)), 180.0);
            col += sunC * sgl * film * 1.5 * WET_REFLECTION;

            // lace of foam the last waves left at their highest line, sinking into the sand
            float lineNow = exp(-abs(up - r0) * 10.0) * smoothstep(0.375, 0.55, fr0) * (1.0 - 0.4 * smoothstep(0.6, 1.0, fr0)) + exp(-abs(up - r1) * 10.0) * (1.0 - smoothstep(0.0, 0.7, fr0)) * 0.6;
            float back = (damp0 * exp(-max(up + edge, 0.0) * 3.0) * smoothstep(0.375, 0.50, fr0) + damp1 * exp(-up * 3.0 - fr0 * 5.0)) * 0.5;     // bubbles behind the retreating sheet
            if (lineNow + back > 0.2 && fD < 0.12)
            {
                float bub = vnoise(W * float2(5.0, 8.0) + 3.0), bub2 = vnoise(W * float2(17.0, 23.0));
                float laceS = smoothstep(0.42, 0.62, (lineNow * (0.7 + 0.6 * bub) + back) * (0.25 + 0.8 * bub + 0.5 * bub2)) * (1.0 - smoothstep(0.03, 0.12, fD));
                col = lerp(col, float3(1.0, 1.0, 0.98) * lightC, saturate(laceS * 0.8 * FOAM_LACE * SURF_FOAM));
            }

            // shells and pebbles, more of them along the line the waves reach
            float2 sg = W * 2.2;
            float2 sid = floor(sg);
            float sh = hash12(sid + 5.0);
            float wrack = exp(-abs(up - 1.7 * SWASH_REACH) * 0.9);
            if (sh > 1.0 - 0.07 * SHELLS * (0.5 + 2.0 * wrack) && fD < 0.04)
            {
                float2 so = hash22(sid) * 0.6 + 0.2;
                float2 sdv = (frac(sg) - so) / 2.2;
                float rot = sh * 37.0;
                sdv = float2(sdv.x * cos(rot) - sdv.y * sin(rot), sdv.x * sin(rot) + sdv.y * cos(rot));
                float size = 0.020 + 0.030 * hash12(sid + 9.0);
                float there = 1.0 - smoothstep(size * 0.25, size * 0.8, fD);
                float2 se = sdv / float2(size, size * 0.72);
                float shell = (1.0 - smoothstep(0.75, 1.0, length(se))) * there;
                float shd = (1.0 - smoothstep(0.6, 1.5, length(se + sunUS * 0.6))) * there * (1.0 - shell);
                float3 shellC = lerp(lerp(float3(0.99, 0.96, 0.91), float3(0.90, 0.72, 0.62), hash12(sid + 2.0)), float3(0.40, 0.37, 0.35), step(0.72, hash12(sid + 4.0)));
                shellC *= 0.80 + 0.30 * sin(se.x * 9.0) * sin(se.x * 9.0) + 0.25 * saturate(dot(se, sunUS));
                col *= 1.0 - 0.35 * shd;
                col = lerp(col, shellC * lightC, shell);
            }

            // single grains catch the sun
            float2 cell2 = floor(tex * Resolution / max(1.0, floor(Resolution.y / 900.0)));
            float tk2 = hash12(cell2 + 17.0);
            float3 hs = normalize(sunDir - rd);
            col += sunC * step(0.985 - 0.006 * SAND_SPARKLE, tk2) * pow(saturate(sin(t * (0.6 + tk2 * 40.0 - 39.0 * 0.985) + tk2 * 900.0) * 0.5 + 0.5), 3.0) * 0.30 * SAND_SPARKLE * (0.3 + 0.7 * dry) * saturate(hs.y * 1.5 - 0.5) * (1.0 - smoothstep(6.0, 30.0, tPl));

            col = lerp(col, hazeC, saturate(1.0 - exp(-tPl * 0.0011 * HAZE)));
        }
    }
    else
    {
        // ================= sky
        col = skyBase(rd, sunDir, sunC, gold);
        float sd = saturate(dot(rd, sunDir));
        col += lerp(sunC, float3(1.0, 1.0, 1.0), 0.6) * smoothstep(0.99982, 0.99990, sd) * 6.0;      // the sun itself
        col = skyClouds(col, rd, sunDir, sunC, gold, t);

        // far things are placed by compass direction so that they turn with the view
        float ey = rd.y / max(length(rd.xz), 1e-4);
        if (ey < 0.17)
        {
            float2 wd = float2(rd.z * ca - rd.x * sa, rd.z * sa + rd.x * ca);
            float th = atan2(wd.y, wd.x);
            // a bank of trade-wind clouds standing on the horizon: flat bases, puffy tops
            if (ey < 0.085)
            {
            float cv = fbm3(float2(th * 9.0 + t * 0.0012 * CLOUD_SPEED, ey * 9.0 + 2.0)) + 0.07 * vnoise(float2(th * 60.0 + t * 0.004 * CLOUD_SPEED, 3.0));
            float puff = cv - (0.66 - 0.32 * saturate(CLOUDS)) - (ey - 0.012) * (ey - 0.012) * 120.0;
            float bank = smoothstep(0.0, 0.025, puff) * smoothstep(0.006, 0.014, ey) * step(0.001, CLOUDS);
            float3 bankC = lerp(lerp(float3(0.66, 0.74, 0.86), float3(0.52, 0.42, 0.52), gold), lerp(float3(1.0, 1.0, 1.0), sunC * 1.1, 0.2 + 0.8 * gold), saturate(0.25 + puff * 4.0 + (ey - 0.012) * 14.0));
            col = lerp(col, lerp(bankC, hazeC, 0.35), bank * 0.9);
            }
            float3 landC = lerp(float3(0.13, 0.30, 0.17), float3(0.30, 0.42, 0.24), saturate(0.5 + 0.8 * sunUS.x)) * lightC;
            float di = th - 0.21;
            if (ISLAND > 0 && abs(di - 0.03) < 0.19 && ey < 0.05)
            {
                float hI = 0.030 * exp(-di * di / 0.0030) + 0.019 * exp(-(di - 0.075) * (di - 0.075) / 0.0012) - 0.0035;
                hI *= 0.80 + 0.40 * vnoise(float2(th * 70.0, 2.0)) + 0.10 * vnoise(float2(th * 400.0, 5.0));
                float mI = smoothstep(pxAng, -pxAng, ey - hI) * step(0.0005, hI);
                float3 cI = lerp(landC * (0.75 + 0.5 * vnoise(float2(th * 160.0, ey * 500.0))), hazeC, saturate(0.58 * HAZE));
                cI = lerp(cI, lerp(sandC, hazeC, 0.35), smoothstep(0.0028, 0.0012, ey));
                col = lerp(col, cI, mI);
            }
            if (HEADLAND > 0 && th > 0.78)
            {
                float rise = smoothstep(0.80, 1.30, th);
                float hH = 0.055 * pow(rise, 0.75) * (0.70 + 0.60 * vnoise(float2(th * 22.0, 9.0))) + 0.11 * smoothstep(1.35, 2.2, th);
                hH += (vnoise(float2(th * 420.0, 1.0)) - 0.3) * 0.004 * step(0.001, rise);
                float mH = smoothstep(pxAng, -pxAng, ey - hH) * step(0.0005, hH);
                float nearer = smoothstep(0.80, 1.6, th);
                float3 cH = lerp(landC * (0.70 + 0.6 * vnoise(float2(th * 130.0, ey * 420.0))), hazeC, saturate((0.50 - 0.25 * nearer) * HAZE));
                cH = lerp(cH, lerp(sandC, hazeC, 0.30), smoothstep(0.0034, 0.0014, ey));
                col = lerp(col, cH, mH);
            }
        }

        // gulls
        for (int g = 0; g < GULLS; g++)
        {
            float fg2 = float(g);
            float gx = (frac(t * (0.007 + 0.005 * hash11(fg2 * 3.1)) + hash11(fg2 * 7.7)) * 1.5 - 0.75) * 2.0 * halfW;
            float gy = (0.07 + 0.22 * hash11(fg2 * 5.3)) * fit + 0.012 * sin(t * 0.5 + fg2 * 2.0);
            float flap = sin(t * (3.2 + hash11(fg2) * 2.0) + fg2 * 1.7) * 0.5 + 0.5;
            float gs = 0.011 + 0.010 * hash11(fg2 * 9.1);
            col = lerp(col, lerp(float3(0.20, 0.22, 0.26), float3(0.45, 0.40, 0.38), gold), gull(float2(uv.x, uv.y + focal * st / ct * 0.0) - float2(gx, gy), gs, flap) * 0.85);
        }
    }

    // ---- sailboat far out, with a broken reflection
    if (SAILBOAT > 0)
    {
        float boatX = (frac(t * 0.0030 + 0.36) * 1.6 - 0.8) * 2.0 * halfW;
        float boatS = 0.034;
        float2 bp = float2(uv.x - boatX, uv.y + 0.0035 - 0.0012 * sin(t * 0.8));
        if (abs(bp.x) < boatS * 0.8 && abs(bp.y) < boatS * 1.4)
        {
            float bt = sailboat(float2(bp.x - bp.y * 0.05 * sin(t * 0.5), bp.y), boatS);
            float sunSide = saturate(0.5 + bp.x / boatS * sign(sunDir.x - boatX * 0.3) * 0.6);
            float3 sailC = lerp(float3(0.78, 0.82, 0.90), float3(1.0, 0.99, 0.95), sunSide) * lightC;
            float3 boatC = bt > 1.5 ? float3(0.16, 0.20, 0.27) : sailC;
            col = lerp(col, lerp(boatC, hazeC, 0.18 * HAZE), saturate(bt));
            float2 mp = float2(bp.x + 0.002 * sin(bp.y * 900.0 + t * 2.0), -bp.y * 0.8);
            float refl = saturate(sailboat(mp, boatS)) * step(bp.y, 0.0) * exp(bp.y * 45.0);
            col = lerp(col, lerp(col, sailC, 0.45), refl * 0.8);
        }
    }

    // ---- palm: its shadow on the ground, then the fronds themselves in front of everything
    if (PALM > 0)
    {
        float pside = (PALM_SIDE > 0 ? 1.0 : -1.0) * flip;
        float zp = 10.0;
        float2 cuv = float2(pside * (halfW - 0.06), HORIZON * fit + 0.075);
        float3 rc = float3(cuv.x, (cuv.y + focal * st / ct) * ct - focal * st, (cuv.y + focal * st / ct) * st + focal * ct);
        float2 crown = float2(rc.x, rc.y) * (zp / rc.z) + float2(0.0, camH);
        if (onGround > 0.5 && PALM_SHADOW > 0.0)
        {
            float lamS = (zp - gp.y) / max(sunDir.z, 0.05);
            float2 ps = float2(gp.x + sunDir.x * lamS, sunDir.y * lamS) - crown;
            if (lamS > 0.0 && ps.y < 3.9 && (abs(ps.x) < 3.9 && ps.y > -3.5 || abs(ps.x - pside * 2.6 * pow(saturate(-ps.y / crown.y), 1.5)) < 0.6))
            {
                float3 shp = palmShape(ps, 0.03 + 0.018 * lamS, crown.y, pside, t);
                float shade = max(shp.x * 0.85, shp.z);
                col *= lerp(float3(1.0, 1.0, 1.0), lerp(float3(0.50, 0.56, 0.70), float3(0.55, 0.45, 0.50), gold), shade * PALM_SHADOW * 0.85);
            }
        }
        float lamV = zp / rd.z;
        float2 pv = float2(rd.x * lamV, camH + rd.y * lamV) - crown;
        if (pv.y < 3.9 && pv.y > -crown.y && (abs(pv.x) < 3.9 && pv.y > -3.5 || abs(pv.x - pside * 2.6 * pow(saturate(-pv.y / crown.y), 1.5)) < 0.4))
        {
            float3 shp2 = palmShape(pv, lamV * pxAng * 0.8, crown.y, pside, t);
            float backlit = pow(saturate(dot(rd, sunDir) * 0.5 + 0.5), 2.0);
            float3 leafC = lerp(float3(0.045, 0.14, 0.035), float3(0.30, 0.50, 0.08), saturate(backlit * (0.35 + 0.9 * shp2.y))) * (0.75 + 0.5 * shp2.y);
            leafC = lerp(leafC, leafC * float3(1.25, 0.95, 0.55), gold * 0.6) * lightC;
            float3 trunkC = float3(0.30, 0.24, 0.18) * (0.75 + 0.35 * sin(pv.y * 26.0)) * (0.7 + 0.5 * saturate(0.5 + (pv.x - pside * 2.6 * pow(saturate(-pv.y / crown.y), 1.5)) * 5.0 * sign(sunDir.x))) * lightC;
            col = lerp(col, trunkC, shp2.z);
            col = lerp(col, leafC, shp2.x);
        }
    }

    // ---- picture
    col *= BRIGHTNESS;
    float lum = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(float3(lum, lum, lum), col, SATURATION);
    col = max(col, float3(0.0, 0.0, 0.0));
    float3 hi = max(col - 0.80, float3(0.0, 0.0, 0.0));
    col = min(col, float3(0.80, 0.80, 0.80)) + 0.20 * (1.0 - exp(-hi * 4.0));         // soft shoulder for foam, clouds and glitter
    col = col * col * (3.0 - 2.0 * col) * 0.22 + col * 0.78;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * VIGNETTE * 0.9;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
