// @name: Path Tracer (RTX benchmark)
// @tags: 3d, benchmark, tech
// @light: SAMPLES=1, BOUNCES=2, FOG_DEPTH=1, SLABS=4
// A dark rotunda rendered by a real Monte Carlo path tracer: a glass sphere on a plinth under a beam of light
// from the oculus, a half circle of bronze slabs, chrome, gold and lacquer, a tall window of cold light and a
// small glowing orb that circles through the haze. Every frame is traced from scratch; the same shader is the
// workload of Wallpepper's GPU benchmark.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define BENCHMARK 0           // 1 = fixed reference workload for the GPU benchmark (ignores all the settings below)
#define SAMPLES 4             // paths per pixel (1..16): more = less grain, each costs the same again; try 2 on weaker graphics cards
#define BOUNCES 3             // how often light may bounce (1..8): more = richer reflections and glass, slower
#define FOG_DEPTH 2           // haze in reflections (0..8): 1 = only in front of the camera (faster), 2 = also in mirrors and glass
#define GRAIN_ANIMATE 1       // 1 = grain changes every frame (looks smoother), 0 = grain stands still
#define FIREFLY_CLAMP 2.0     // limit for very bright bounced light (0.5..20): lower = calmer image, higher = more sparkle
#define ANIMATION_SPEED 1.0   // overall speed of everything that moves (0..3)
#define ORB_SPEED 1.0         // how fast the glowing orb circles the glass sphere (0..4)
#define CAMERA_ANGLE 0.12     // where the camera stands on its circle around the hall (-3.14..3.14, 0 = facing the window)
#define CAMERA_SWAY 0.35      // how far the camera slowly swings left and right (0..1)
#define CAMERA_ORBIT 0.0      // speed of continuous circling around the hall (-0.2..0.2, 0 = sway only)
#define CAMERA_DISTANCE 10.5  // distance of the camera from the centre (7.5..15)
#define CAMERA_HEIGHT 2.5     // height of the camera above the floor (0.3..11)
#define FIELD_OF_VIEW 42.0    // vertical field of view in degrees (15..100)
#define APERTURE 0.08         // lens opening (0..0.5): 0 = everything sharp, larger = stronger blur away from the focus
#define FOCUS_DISTANCE 0.0    // distance that is in focus (0..25, 0 = always on the glass sphere)
#define FOG 1.0               // haze in the air that makes the light visible (0..4, 0 = clear air, faster)
#define SKYLIGHT 1.0          // brightness of the light from the oculus in the ceiling (0..3)
#define SKYLIGHT_FOCUS 0.5    // shape of that light (0..1): 0 = soft daylight everywhere, 1 = tight beam onto the plinth
#define SKY_R 1.00            // oculus light colour: red
#define SKY_G 0.84            // oculus light colour: green
#define SKY_B 0.62            // oculus light colour: blue
#define PORTAL 1.0            // brightness of the tall window (0..3)
#define PORTAL_R 0.25         // window light colour: red
#define PORTAL_G 0.55         // window light colour: green
#define PORTAL_B 1.00         // window light colour: blue
#define ORB 1.0               // brightness of the circling orb (0..3)
#define ORB_R 1.00            // orb colour: red
#define ORB_G 0.36            // orb colour: green
#define ORB_B 0.10            // orb colour: blue
#define GLASS_R 0.80          // glass tint: red
#define GLASS_G 0.95          // glass tint: green
#define GLASS_B 0.92          // glass tint: blue
#define GLASS_IOR 1.5         // how strongly the glass bends light (1..2.5): 1.5 = glass, 2.4 = diamond
#define SLABS 6               // number of standing slabs around the plinth (0..12, each one costs a little)
#define SLAB_TWIST 0.45       // how far the slabs are turned out of their circle (-1.5..1.5)
#define METAL_R 0.72          // slab metal colour: red
#define METAL_G 0.43          // slab metal colour: green
#define METAL_B 0.21          // slab metal colour: blue
#define METAL_ROUGHNESS 0.28  // slab finish (0..1): 0 = polished mirror, 1 = matte
#define MIRROR_ROUGHNESS 0.03 // finish of the chrome sphere (0..1): 0 = perfect mirror
#define ACCENT_R 0.50         // lacquered sphere colour: red
#define ACCENT_G 0.02         // lacquered sphere colour: green
#define ACCENT_B 0.04         // lacquered sphere colour: blue
#define FLOOR_ROUGHNESS 0.10  // floor finish (0..1): 0 = wet mirror, 1 = matte stone
#define WALL_R 0.44           // wall colour: red
#define WALL_G 0.40           // wall colour: green
#define WALL_B 0.36           // wall colour: blue
#define EXPOSURE 1.0          // overall brightness (0..3)
#define VIGNETTE 0.35         // darkening towards the corners (0..1)
#define TEXT_VEIL 0.5         // darkening behind terminal text (0..1)

#if BENCHMARK
// ---- reference workload ----------------------------------------------------------------------------------
// Fixed on purpose, whatever the settings above say (see docs/BENCHMARK.md, "Workload"): 3 paths per pixel,
// 4 bounces, haze lit at every bounce, 7 slabs, everything else as shipped. No path ever ends early: one that
// has run into a light simply carries on with no weight. The values are written in brackets so that the app
// does not list them as controls and never rewrites them.
#undef SAMPLES
#undef BOUNCES
#undef FOG_DEPTH
#undef GRAIN_ANIMATE
#undef FIREFLY_CLAMP
#undef ANIMATION_SPEED
#undef ORB_SPEED
#undef CAMERA_ANGLE
#undef CAMERA_SWAY
#undef CAMERA_ORBIT
#undef CAMERA_DISTANCE
#undef CAMERA_HEIGHT
#undef FIELD_OF_VIEW
#undef APERTURE
#undef FOCUS_DISTANCE
#undef FOG
#undef SKYLIGHT
#undef SKYLIGHT_FOCUS
#undef SKY_R
#undef SKY_G
#undef SKY_B
#undef PORTAL
#undef PORTAL_R
#undef PORTAL_G
#undef PORTAL_B
#undef ORB
#undef ORB_R
#undef ORB_G
#undef ORB_B
#undef GLASS_R
#undef GLASS_G
#undef GLASS_B
#undef GLASS_IOR
#undef SLABS
#undef SLAB_TWIST
#undef METAL_R
#undef METAL_G
#undef METAL_B
#undef METAL_ROUGHNESS
#undef MIRROR_ROUGHNESS
#undef ACCENT_R
#undef ACCENT_G
#undef ACCENT_B
#undef FLOOR_ROUGHNESS
#undef WALL_R
#undef WALL_G
#undef WALL_B
#undef EXPOSURE
#undef VIGNETTE
#undef TEXT_VEIL
#define SAMPLES (3)
#define BOUNCES (4)
#define FOG_DEPTH (4)
#define GRAIN_ANIMATE (1)
#define FIREFLY_CLAMP (2.0)
#define ANIMATION_SPEED (1.0)
#define ORB_SPEED (1.0)
#define CAMERA_ANGLE (0.12)
#define CAMERA_SWAY (0.35)
#define CAMERA_ORBIT (0.0)
#define CAMERA_DISTANCE (10.5)
#define CAMERA_HEIGHT (2.5)
#define FIELD_OF_VIEW (42.0)
#define APERTURE (0.08)
#define FOCUS_DISTANCE (0.0)
#define FOG (1.0)
#define SKYLIGHT (1.0)
#define SKYLIGHT_FOCUS (0.5)
#define SKY_R (1.00)
#define SKY_G (0.84)
#define SKY_B (0.62)
#define PORTAL (1.0)
#define PORTAL_R (0.25)
#define PORTAL_G (0.55)
#define PORTAL_B (1.00)
#define ORB (1.0)
#define ORB_R (1.00)
#define ORB_G (0.36)
#define ORB_B (0.10)
#define GLASS_R (0.80)
#define GLASS_G (0.95)
#define GLASS_B (0.92)
#define GLASS_IOR (1.5)
#define SLABS (7)
#define SLAB_TWIST (0.45)
#define METAL_R (0.72)
#define METAL_G (0.43)
#define METAL_B (0.21)
#define METAL_ROUGHNESS (0.28)
#define MIRROR_ROUGHNESS (0.03)
#define ACCENT_R (0.50)
#define ACCENT_G (0.02)
#define ACCENT_B (0.04)
#define FLOOR_ROUGHNESS (0.10)
#define WALL_R (0.44)
#define WALL_G (0.40)
#define WALL_B (0.36)
#define EXPOSURE (1.0)
#define VIGNETTE (0.35)
#define TEXT_VEIL (0.5)
#endif

// The hall: floor y = 0, ceiling y = 12, round wall of radius 16. Oculus: disc of radius 3 in the ceiling.
// Window: 2.6 wide, 7 tall, in the wall at x = -16. Object numbers used below:
// 0 floor, 1 ceiling (oculus inside), 2 wall (window inside), 3 plinth, 4 glass sphere, 5 chrome sphere,
// 6 gold sphere, 7 lacquered sphere, 8 orb, 10.. slabs.

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
// by the golden ratio), so that even a handful of paths per pixel covers every choice fairly and the grain
// stays fine and even. Decisions with a single choice use the first number.
float2 rnd2(float2 pix, float dim, float sIdx, float tseed)
{
    float2 h = hash23(float3(pix.x, pix.y, dim * 7.0 + tseed));
    return frac(h + sIdx * float2(1.0 / float(SAMPLES), 0.6180340));
}

float lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

void basis(float3 n, out float3 t1, out float3 t2)
{
    float3 up = abs(n.y) < 0.9 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    t1 = normalize(cross(up, n));
    t2 = cross(n, t1);
}

// ---- ray / primitive intersections ------------------------------------------------------------------------
// nearest hit in front of the ray origin (also from inside the sphere), or -1
float iSphere(float3 ro, float3 rd, float3 c, float r)
{
    float3 oc = ro - c;
    float b = dot(oc, rd);
    float h = b * b - dot(oc, oc) + r * r;
    float t = -1.0;
    if (h > 0.0)
    {
        h = sqrt(h);
        t = -b - h;
        if (t < 0.0005) t = -b + h;
        if (t < 0.0005) t = -1.0;
    }
    return t;
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

float slabHeight(float k) { return 3.0 + 2.0 * frac(k * 0.618034 + 0.35); }

// closest surface along the ray: distance, outward normal and object number
float traceScene(float3 ro, float3 rd, float3 orbC, out float3 n, out float id)
{
    // floor or ceiling
    float down = rd.y < 0.0 ? 1.0 : 0.0;
    float tBest = (12.0 * (1.0 - down) - ro.y) / (abs(rd.y) + 1e-7) * (1.0 - 2.0 * down);
    tBest = tBest > 0.0 ? tBest : 1e5;
    n = float3(0.0, 2.0 * down - 1.0, 0.0);
    id = 1.0 - down;

    // round wall, seen from inside
    float wa = max(dot(rd.xz, rd.xz), 1e-7);
    float wb = dot(ro.xz, rd.xz);
    float wc = dot(ro.xz, ro.xz) - 256.0;
    float tw = (-wb + sqrt(max(wb * wb - wa * wc, 0.0))) / wa;
    if (tw > 0.0005 && tw < tBest)
    {
        tBest = tw;
        float2 wp = ro.xz + tw * rd.xz;
        n = float3(-wp.x, 0.0, -wp.y) * 0.0625;
        id = 2.0;
    }

    // plinth
    float3 bn;
    float tb = iBox(ro - float3(0.0, 0.35, 0.0), rd, float3(1.0, 0.35, 1.0), bn);
    if (tb > 0.0 && tb < tBest) { tBest = tb; n = bn; id = 3.0; }

    // spheres: glass, chrome, gold, lacquer, orb
    float ts = iSphere(ro, rd, float3(0.0, 1.752, 0.0), 1.05);
    if (ts > 0.0 && ts < tBest) { tBest = ts; n = (ro + rd * ts - float3(0.0, 1.752, 0.0)) / 1.05; id = 4.0; }
    ts = iSphere(ro, rd, float3(1.2, 0.8, 2.9), 0.8);
    if (ts > 0.0 && ts < tBest) { tBest = ts; n = (ro + rd * ts - float3(1.2, 0.8, 2.9)) / 0.8; id = 5.0; }
    ts = iSphere(ro, rd, float3(2.2, 0.5, -2.3), 0.5);
    if (ts > 0.0 && ts < tBest) { tBest = ts; n = (ro + rd * ts - float3(2.2, 0.5, -2.3)) / 0.5; id = 6.0; }
    ts = iSphere(ro, rd, float3(-2.9, 0.62, 1.5), 0.62);
    if (ts > 0.0 && ts < tBest) { tBest = ts; n = (ro + rd * ts - float3(-2.9, 0.62, 1.5)) / 0.62; id = 7.0; }
    ts = iSphere(ro, rd, orbC, 0.16);
    if (ts > 0.0 && ts < tBest) { tBest = ts; n = (ro + rd * ts - orbC) / 0.16; id = 8.0; }

    // horseshoe of slabs, open towards the camera: the ray is turned from one slab's frame into the next,
    // the slab itself never moves
    float stepA = 4.2831853 / float(max(SLABS - 1, 1));
    float cs = cos(stepA), sn = sin(stepA);
    float c0 = cos(1.0 + SLAB_TWIST), s0 = sin(1.0 + SLAB_TWIST);
    float2 o2 = float2(c0 * ro.x + s0 * ro.z, -s0 * ro.x + c0 * ro.z);
    float2 d2 = float2(c0 * rd.x + s0 * rd.z, -s0 * rd.x + c0 * rd.z);
    float2 ax = float2(c0, s0);
    float2 cen = 5.4 * float2(cos(SLAB_TWIST), -sin(SLAB_TWIST));
    [loop] for (int k = 0; k < SLABS; k++)
    {
        float hh = 0.5 * slabHeight(float(k));
        float3 sbn;
        float tk = iBox(float3(o2.x - cen.x, ro.y - hh, o2.y - cen.y), float3(d2.x, rd.y, d2.y), float3(0.15, hh, 0.72), sbn);
        if (tk > 0.0 && tk < tBest)
        {
            tBest = tk;
            n = float3(sbn.x * ax.x - sbn.z * ax.y, sbn.y, sbn.x * ax.y + sbn.z * ax.x);
            id = 10.0 + float(k);
        }
        o2 = float2(cs * o2.x + sn * o2.y, -sn * o2.x + cs * o2.y);
        d2 = float2(cs * d2.x + sn * d2.y, -sn * d2.x + cs * d2.y);
        ax = float2(cs * ax.x - sn * ax.y, sn * ax.x + cs * ax.y);
    }
    return tBest;
}

// 1 if the ray reaches tmax without touching the sphere
float sphereClear(float3 ro, float3 rd, float tmax, float3 c, float r)
{
    float3 oc = ro - c;
    float b = dot(oc, rd);
    float h = b * b - dot(oc, oc) + r * r;
    float t = -b - sqrt(max(h, 0.0));
    return (h > 0.0 && t > 0.0 && t < tmax) ? 0.0 : 1.0;
}

// How much light gets from ro to ro + rd * tmax. The room itself never blocks (it is convex). Every object in
// it is tested on every call, without an early way out, so a shadow ray always costs the same. Light through the
// glass sphere is not blocked
// but gathered towards the middle of its shadow: a cheap stand-in for the caustic a sphere lens really throws.
float3 shadowRay(float3 ro, float3 rd, float tmax, float3 orbC)
{
    float vis = sphereClear(ro, rd, tmax, float3(1.2, 0.8, 2.9), 0.8);
    vis *= sphereClear(ro, rd, tmax, float3(2.2, 0.5, -2.3), 0.5);
    vis *= sphereClear(ro, rd, tmax, float3(-2.9, 0.62, 1.5), 0.62);
    vis *= sphereClear(ro, rd, tmax, orbC, 0.16);

    float3 bn;
    float tb = iBox(ro - float3(0.0, 0.35, 0.0), rd, float3(1.0, 0.35, 1.0), bn);
    vis *= (tb > 0.0 && tb < tmax) ? 0.0 : 1.0;

    float stepA = 4.2831853 / float(max(SLABS - 1, 1));
    float cs = cos(stepA), sn = sin(stepA);
    float c0 = cos(1.0 + SLAB_TWIST), s0 = sin(1.0 + SLAB_TWIST);
    float2 o2 = float2(c0 * ro.x + s0 * ro.z, -s0 * ro.x + c0 * ro.z);
    float2 d2 = float2(c0 * rd.x + s0 * rd.z, -s0 * rd.x + c0 * rd.z);
    float2 cen = 5.4 * float2(cos(SLAB_TWIST), -sin(SLAB_TWIST));
    [loop] for (int ks = 0; ks < SLABS; ks++)
    {
        float hh = 0.5 * slabHeight(float(ks));
        float tk = iBox(float3(o2.x - cen.x, ro.y - hh, o2.y - cen.y), float3(d2.x, rd.y, d2.y), float3(0.15, hh, 0.72), bn);
        vis *= (tk > 0.0 && tk < tmax) ? 0.0 : 1.0;
        o2 = float2(cs * o2.x + sn * o2.y, -sn * o2.x + cs * o2.y);
        d2 = float2(cs * d2.x + sn * d2.y, -sn * d2.x + cs * d2.y);
    }

    float3 oc = ro - float3(0.0, 1.752, 0.0);
    float gb = dot(oc, rd);
    float gh = gb * gb - dot(oc, oc) + 1.1025;
    float gt = -gb - sqrt(max(gh, 0.0));
    float gc = saturate(gh / 1.1025);
    float3 through = float3(GLASS_R, GLASS_G, GLASS_B) * (0.06 + 3.3 * gc * gc * gc);
    float3 tr = (gh > 0.0 && gt > 0.0 && gt < tmax) ? through : float3(1.0, 1.0, 1.0);
    return tr * vis;
}

// ---- lights -----------------------------------------------------------------------------------------------
float3 orbLe() { return float3(ORB_R, ORB_G, ORB_B) * (150.0 * ORB); }

// the tall window: panes of cold light between dark glazing bars
float3 portalLe(float3 p)
{
    float2 pane = abs(frac(float2(p.z / 1.3 + 0.5, p.y / 1.75)) - 0.5);
    float bars = step(pane.x, 0.47) * step(pane.y, 0.48);
    return float3(PORTAL_R, PORTAL_G, PORTAL_B) * (6.0 * PORTAL * bars);
}

// The oculus shines mostly straight down; cosL = cosine between the light direction and straight down.
// Scaled so that the pool of light under it keeps its brightness when the beam is narrowed.
float beamPower() { return 1.0 + 200.0 * SKYLIGHT_FOCUS; }
float3 oculusLe(float cosL)
{
    return float3(SKY_R, SKY_G, SKY_B) * (SKYLIGHT * 2.8 * (beamPower() + 1.0) * pow(saturate(cosL), beamPower()));
}

// A direction that leans away from straight up by a given angle meets the ceiling on a circle around the point
// above p. Only the part of that circle inside the oculus is worth sampling: this returns half its angle
// (pi = the whole circle is inside, 0 = none of it). rb = radius of the circle.
float oculusArc(float3 p, float rb)
{
    float rho = length(p.xz);
    return acos(clamp((rho * rho + rb * rb - 9.0) / max(2.0 * rho * rb, 1e-6), -1.0, 1.0));
}

// density (per solid angle) with which sampleOculus picks a direction at p; wiy = its upward part,
// d = distance to the ceiling along it
float oculusPdf(float3 p, float wiy, float d)
{
    float k = beamPower();
    float cy = clamp(wiy, 1e-4, 1.0);
    float arc = oculusArc(p, d * sqrt(1.0 - cy * cy));
    return (k + 1.0) * 0.5 * pow(cy, k) / max(arc, 1e-3);
}

// Each of these picks a direction wi towards its light from the point p and reports the distance, the
// probability density of that direction (per solid angle) and the light arriving from there.
// Oculus: lean away from straight up exactly as often as the beam is bright in that direction, then turn towards
// the part of the opening that can be reached. Light and density then cancel almost completely, which is why
// the beam and the edge of its pool of light stay calm with few paths.
void sampleOculus(float3 p, float2 u, out float3 wi, out float dist, out float pdf, out float3 le)
{
    float hgt = max(11.99 - p.y, 1e-3);
    float cb = pow(max(u.x, 1e-6), 1.0 / (beamPower() + 1.0));
    float sb = sqrt(max(1.0 - cb * cb, 0.0));
    float turn = (2.0 * u.y - 1.0) * oculusArc(p, hgt * sb / cb);
    float rho = length(p.xz);
    float2 e = rho > 1e-4 ? -p.xz / rho : float2(1.0, 0.0);
    float2 side = e * cos(turn) + float2(-e.y, e.x) * sin(turn);
    wi = float3(side.x * sb, cb, side.y * sb);
    dist = hgt / cb;
    float2 q = p.xz + wi.xz * dist;
    float inside = (dot(q, q) < 9.0 && p.y < 11.98) ? 1.0 : 0.0;
    pdf = oculusPdf(p, wi.y, dist) * inside;
    le = oculusLe(wi.y);
}

void samplePortal(float3 p, float2 u, out float3 wi, out float dist, out float pdf, out float3 le)
{
    float3 d = float3(-15.94, 7.0 * u.x, 2.6 * (u.y - 0.5)) - p;
    float dd = dot(d, d);
    dist = sqrt(dd);
    wi = d / dist;
    pdf = wi.x < -1e-4 ? dd / (-wi.x * 18.2) : 0.0;
    le = portalLe(p + d);
}

void sampleOrb(float3 p, float2 u, float3 orbC, out float3 wi, out float dist, out float pdf, out float3 le)
{
    float3 d = orbC - p;
    float dd = dot(d, d);
    float dc = sqrt(dd);
    float sin2 = min(0.0256 / dd, 0.999);
    float cosMax = sqrt(1.0 - sin2);
    float oneMinus = sin2 / (1.0 + cosMax);
    float cosT = 1.0 - u.x * oneMinus;
    float sinT = sqrt(max(1.0 - cosT * cosT, 0.0));
    float ph = 6.2831853 * u.y;
    float3 w = d / dc;
    float3 t1, t2;
    basis(w, t1, t2);
    wi = normalize((t1 * cos(ph) + t2 * sin(ph)) * sinT + w * cosT);
    dist = max(dc * cosT - sqrt(max(0.0256 - dd * sinT * sinT, 0.0)), 0.0);
    pdf = 1.0 / (6.2831853 * oneMinus);
    le = orbLe();
}

// ---- materials --------------------------------------------------------------------------------------------
// kind: 0 = solid surface (base colour, metal or not, roughness), 1 = glass, 2 = light
void surfaceAt(float id, float3 p, float3 rd,
               out float3 base, out float metal, out float rough, out float3 emis, out float kind)
{
    float3 gold = float3(1.0, 0.72, 0.30);
    base = float3(0.5, 0.5, 0.5);
    metal = 0.0;
    rough = 0.5;
    emis = float3(0.0, 0.0, 0.0);
    kind = 0.0;
    float rr = length(p.xz);
    float ang = atan2(p.z, p.x);
    if (id < 0.5)
    {
        // floor: rings of polished dark stone in two tones, with two inlaid brass circles
        float ring = floor(rr / 1.35);
        float sector = floor((ang + 3.1415927) * 3.8197 + ring * 0.5);
        float chk = abs(fmod(ring + sector, 2.0));
        float2 cell = abs(frac(float2(rr / 1.35, (ang + 3.1415927) * 3.8197 + ring * 0.5)) - 0.5);
        float joint = step(0.5 - 0.012, cell.x) + step(0.5 - 0.012 * 4.0 / max(rr, 1.0), cell.y);
        base = lerp(float3(0.030, 0.031, 0.034), float3(0.060, 0.056, 0.052), chk);
        rough = clamp(FLOOR_ROUGHNESS * (0.7 + 0.9 * chk), 0.0, 1.0);
        if (joint > 0.5) { base = float3(0.012, 0.012, 0.012); rough = 0.6; }
        if (min(abs(rr - 4.05), abs(rr - 8.1)) < 0.045) { base = gold; metal = 1.0; rough = 0.22; }
    }
    else if (id < 1.5)
    {
        base = float3(0.10, 0.095, 0.09);
        rough = 0.9;
        if (rr < 3.0) { kind = 2.0; emis = oculusLe(rd.y); }
    }
    else if (id < 2.5)
    {
        // wall: tall plaster panels over a dark stone skirting with a brass rail
        float pan = frac(ang * 6.3662);
        float groove = smoothstep(0.0, 0.012, min(pan, 1.0 - pan));
        base = float3(WALL_R, WALL_G, WALL_B) * (0.55 + 0.45 * groove);
        rough = 0.85;
        if (p.y < 1.2) { base = float3(0.035, 0.035, 0.04); rough = 0.25; }
        if (abs(p.y - 1.26) < 0.06) { base = gold; metal = 1.0; rough = 0.25; }
        if (p.x < 0.0 && abs(p.z) < 1.3 && p.y < 7.0) { kind = 2.0; emis = portalLe(p); }
    }
    else if (id < 3.5) { base = float3(0.035, 0.035, 0.04); rough = 0.08; }
    else if (id < 4.5) { kind = 1.0; }
    else if (id < 5.5) { base = float3(0.96, 0.96, 0.97); metal = 1.0; rough = MIRROR_ROUGHNESS; }
    else if (id < 6.5) { base = gold; metal = 1.0; rough = 0.28; }
    else if (id < 7.5) { base = float3(ACCENT_R, ACCENT_G, ACCENT_B); rough = 0.07; }
    else if (id < 8.5) { kind = 2.0; emis = orbLe(); }
    else
    {
        // slabs: stacked plates of metal, each polished a little differently, with a dark seam between them
        float plate = floor(p.y * 1.1 + 0.35);
        float seam = abs(frac(p.y * 1.1 + 0.35) - 0.5);
        base = float3(METAL_R, METAL_G, METAL_B);
        metal = 1.0;
        rough = clamp(METAL_ROUGHNESS * (0.6 + 0.9 * frac(plate * 0.618034 + (id - 10.0) * 0.381966)), 0.0, 1.0);
        if (seam > 0.488) { base *= 0.12; rough = 0.5; }
    }
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

// ---- haze -------------------------------------------------------------------------------------------------
float fogDensity() { return 0.018 * FOG; }

// Light scattered towards the eye by the haze along one ray segment. One point on the ray is drawn per light,
// placed where that light is strongest (near the orb, inside the beam under the oculus, anywhere for the window),
// and each gets a shadow ray, so the slabs cast real shafts of shadow into the haze.
float3 fogLight(float3 ro, float3 rd, float tHit, float3 orbC, float2 pix, float dim, float sIdx, float tseed)
{
    float sig = fogDensity();
    float2 ua = rnd2(pix, dim, sIdx, tseed);
    float2 ub = rnd2(pix, dim + 1.0, sIdx, tseed);
    float2 uc = rnd2(pix, dim + 2.0, sIdx, tseed);
    float2 ud = rnd2(pix, dim + 3.0, sIdx, tseed);

    // near the orb: points spread evenly in the angle under which they are seen from the orb
    float dl = dot(orbC - ro, rd);
    float D = max(length(ro + rd * dl - orbC), 0.16);
    float thA = atan2(-dl, D);
    float thB = atan2(tHit - dl, D);
    float tA = clamp(dl + D * tan(lerp(thA, thB, ua.x)), 0.0, tHit);
    float pdfA = D / max((thB - thA) * (D * D + (tA - dl) * (tA - dl)), 1e-9);
    float3 wiA, leA;
    float distA, lpA;
    sampleOrb(ro + rd * tA, float2(ua.y, ub.y), orbC, wiA, distA, lpA, leA);
    float3 cA = leA * (exp(-sig * (tA + distA)) / max(lpA * pdfA, 1e-9));

    // inside the column of light under the oculus: evenly along the part of the ray that crosses it
    float ha = max(dot(rd.xz, rd.xz), 1e-6);
    float hb = dot(ro.xz, rd.xz);
    float hdisc = hb * hb - ha * (dot(ro.xz, ro.xz) - 20.25);
    float hs = sqrt(max(hdisc, 0.0));
    float tB0 = clamp((-hb - hs) / ha, 0.0, tHit);
    float tB1 = clamp((-hb + hs) / ha, 0.0, tHit);
    float lenB = hdisc > 0.0 ? tB1 - tB0 : 0.0;
    float tB = tB0 + lenB * ub.x;
    float3 wiB, leB;
    float distB, lpB;
    sampleOculus(ro + rd * tB, uc, wiB, distB, lpB, leB);
    float3 cB = leB * (exp(-sig * (tB + distB)) * lenB / max(lpB, 1e-9)) * step(1e-7, lpB);

    // anywhere along the ray, for the window
    float tC = tHit * ud.x;
    float3 wiC, leC;
    float distC, lpC;
    samplePortal(ro + rd * tC, float2(ud.y, ud.x * 7.0 - floor(ud.x * 7.0)), wiC, distC, lpC, leC);
    float3 cC = leC * (exp(-sig * (tC + distC)) * tHit / max(lpC, 1e-9)) * step(1e-7, lpC);

    float3 res = float3(0.0, 0.0, 0.0);
    [loop] for (int lf = 0; lf < 3; lf++)
    {
        float3 cS = cC;
        float3 wiS = wiC;
        float tS = tC, distS = distC;
        if (lf == 0) { cS = cA; wiS = wiA; tS = tA; distS = distA; }
        if (lf == 1) { cS = cB; wiS = wiB; tS = tB; distS = distB; }
        res += cS * shadowRay(ro + rd * tS, wiS, distS - 0.01, orbC);
    }
    return res * (sig * 0.0795775);
}

// ---- one light path ---------------------------------------------------------------------------------------
float3 tracePath(float3 roIn, float3 rdIn, float3 orbC, float2 pix, float sIdx, float tseed)
{
    float3 ro = roIn;
    float3 rd = rdIn;
    float3 L = float3(0.0, 0.0, 0.0);
    float3 thr = float3(1.0, 1.0, 1.0);
    float prevPdf = -1.0;                      // density of the last bounce; negative = mirror-sharp or camera
    float soft = 0.0;                          // bounces since the path first left a matte or frosted surface
    float fogLeft = float(FOG_DEPTH);          // how many more bounces the haze is lit for; glass does not count
    float3 glassSig = -log(max(float3(GLASS_R, GLASS_G, GLASS_B), float3(0.01, 0.01, 0.01))) / 2.1;

    [loop] for (int b = 0; b < BOUNCES; b++)
    {
        float dim = 2.0 + 9.0 * float(b);
        float3 n;
        float id;
        float t = traceScene(ro, rd, orbC, n, id);
        float3 p = ro + rd * t;

        float3 base, emis;
        float metal, rough, kind;
        surfaceAt(id, p, rd, base, metal, rough, emis, kind);

        // what the ray crossed on its way: haze, or the body of the glass sphere
        float inGlass = (id > 3.5 && id < 4.5 && dot(rd, n) > 0.0) ? 1.0 : 0.0;
        float3 Lb = float3(0.0, 0.0, 0.0);              // light gathered at this bounce by aiming at the lights
        float3 Lhit = float3(0.0, 0.0, 0.0);            // light found by running into a light
        if (fogLeft > 0.5 && FOG > 0.0)
        {
            Lb += thr * fogLight(ro, rd, t, orbC, pix, dim + 5.0, sIdx, tseed) * (1.0 - inGlass);
        }
        thr *= exp(-lerp(float3(1.0, 1.0, 1.0) * fogDensity(), glassSig, inGlass) * t);

        // The ray ran into a light: count it, weighted against the chance that direct sampling had found it.
        // For the two big lights the weights blend smoothly. The orb is so small that blending leaves sparkles
        // around its reflections, so there whichever method is the more likely one takes it all.
        if (kind > 1.5)
        {
            float lp = 0.0;
            float wHit = 1.0;
            if (id < 1.5) lp = oculusPdf(ro, rd.y, t);
            else if (id < 2.5) lp = t * t / (max(-rd.x, 1e-4) * 18.2);
            else
            {
                float3 od = orbC - ro;
                float s2 = min(0.0256 / dot(od, od), 0.999);
                lp = (1.0 + sqrt(1.0 - s2)) / (6.2831853 * s2);
            }
            if (prevPdf >= 0.0) wHit = id > 7.5 ? step(lp, prevPdf) : prevPdf * prevPdf / (prevPdf * prevPdf + lp * lp);
            Lhit = thr * emis * wHit;
            thr = float3(0.0, 0.0, 0.0);
        }

        float3 v = -rd;
        float3 ns = dot(n, v) < 0.0 ? -n : n;
        float3 f0 = lerp(float3(0.04, 0.04, 0.04), base, metal);
        float3 diff = base * (1.0 - metal);
        float a = max(rough * rough, 0.002);
        float a2 = a * a;
        float nov = max(dot(ns, v), 1e-4);
        float wSpec = lum(f0 + (float3(1.0, 1.0, 1.0) - f0) * pow(1.0 - nov, 5.0));
        float wDiff = lum(diff) * (1.0 - wSpec);
        float pSpec = wDiff > 0.0 ? clamp(wSpec / (wSpec + wDiff), 0.1, 0.9) : 1.0;

        // direct light: one direction towards each light, each checked with its own shadow ray and weighted
        // against the chance that the bounce below finds the same light by itself (multiple importance sampling)
        float2 u0 = rnd2(pix, dim, sIdx, tseed);
        float2 u1 = rnd2(pix, dim + 1.0, sIdx, tseed);
        float2 u2 = rnd2(pix, dim + 2.0, sIdx, tseed);
        float3 wi0, wi1, wi2, le0, le1, le2;
        float di0, di1, di2, lp0, lp1, lp2, bp0, bp1, bp2;
        sampleOculus(p, u0, wi0, di0, lp0, le0);
        samplePortal(p, u1, wi1, di1, lp1, le1);
        sampleOrb(p, u2, orbC, wi2, di2, lp2, le2);
        float3 c0 = le0 * evalBsdf(ns, v, wi0, diff, f0, a2, pSpec, bp0);
        float3 c1 = le1 * evalBsdf(ns, v, wi1, diff, f0, a2, pSpec, bp1);
        float3 c2 = le2 * evalBsdf(ns, v, wi2, diff, f0, a2, pSpec, bp2);
        c0 *= lp0 / max(lp0 * lp0 + bp0 * bp0, 1e-12);
        c1 *= lp1 / max(lp1 * lp1 + bp1 * bp1, 1e-12);
        c2 *= (1.0 - step(lp2, bp2)) / max(lp2, 1e-9);                 // tiny orb: all or nothing, see above
        float3 direct = float3(0.0, 0.0, 0.0);
        [loop] for (int li = 0; li < 3; li++)
        {
            float3 cS = c2;
            float3 wiS = wi2;
            float diS = di2;
            if (li == 0) { cS = c0; wiS = wi0; diS = di0; }
            if (li == 1) { cS = c1; wiS = wi1; diS = di1; }
            direct += cS * shadowRay(p + ns * 0.002, wiS, diS - 0.004, orbC);
        }
        Lb += thr * direct * (kind < 0.5 ? 1.0 : 0.0);

        // What the camera sees directly or in a mirror, and the reflection of a light in something frosted, stay
        // as bright as they are. Light that arrives by a detour over matte or frosted surfaces is limited: that
        // trades the rare, very bright sample (a "firefly") for a tiny loss of light.
        float capB = soft > 0.5 ? FIREFLY_CLAMP : 64.0;
        float capE = soft > 1.5 ? FIREFLY_CLAMP : 64.0;
        float maxB = max(Lb.x, max(Lb.y, Lb.z));
        float maxE = max(Lhit.x, max(Lhit.y, Lhit.z));
        L += Lb * (maxB > capB ? capB / maxB : 1.0) + Lhit * (maxE > capE ? capE / maxE : 1.0);

        // continue the path
        fogLeft -= 1.0;
        float2 u4 = rnd2(pix, dim + 3.0, sIdx, tseed);
        float2 u5 = rnd2(pix, dim + 4.0, sIdx, tseed);
        if (kind > 0.5 && kind < 1.5)
        {
            // glass: mirror or refract, chosen by how much light each takes (Fresnel)
            float entering = dot(rd, n) < 0.0 ? 1.0 : 0.0;
            float3 nn = entering > 0.5 ? n : -n;
            float eta = entering > 0.5 ? 1.0 / GLASS_IOR : GLASS_IOR;
            float cosi = saturate(-dot(rd, nn));
            float sin2t = eta * eta * (1.0 - cosi * cosi);
            float cost = sqrt(max(1.0 - sin2t, 0.0));
            float rs = (eta * cosi - cost) / (eta * cosi + cost + 1e-7);
            float rp = (cosi - eta * cost) / (cosi + eta * cost + 1e-7);
            float fr = sin2t < 1.0 ? 0.5 * (rs * rs + rp * rp) : 1.0;
            // the mirror image is chosen more often than its share of the light and counted less in return:
            // bright highlights on the glass then need fewer paths to come out clean
            float pr = fr < 1.0 ? clamp(fr, 0.2, 0.8) : 1.0;
            if (u5.x < pr)
            {
                thr *= fr / pr;
                rd = reflect(rd, nn);
                ro = p + nn * 0.002;
            }
            else
            {
                thr *= (1.0 - fr) / (1.0 - pr);
                rd = normalize(rd * eta + nn * (eta * cosi - cost));
                ro = p - nn * 0.002;
            }
            prevPdf = -1.0;
            soft += step(0.5, soft);
            fogLeft += 1.0;
        }
        else
        {
            float3 l = sampleBsdf(ns, v, a, pSpec, u4, u5.x);
            float bp;
            float3 f = evalBsdf(ns, v, l, diff, f0, a2, pSpec, bp);
            thr *= f / max(bp, 1e-6);
            prevPdf = bp;
            soft += (u5.x >= pSpec || rough > 0.09 || soft > 0.5) ? 1.0 : 0.0;
            rd = l;
            ro = p + ns * 0.002;
        }
    }
    return L;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float2 pix = floor(tex * Resolution);
    float aspect = Resolution.x / Resolution.y;
    float tm = Time * ANIMATION_SPEED;
    float tseed = fmod(Time, 61.0) * 59.0 * float(GRAIN_ANIMATE);

    // the orb circles the glass sphere on a tilted path
    float oa = tm * 0.30 * ORB_SPEED + 4.1;
    float3 orbC = float3(cos(oa) * 2.45, 2.15 + 0.55 * sin(oa * 0.73 + 1.0), sin(oa) * 2.45);

    // camera
    float ang = CAMERA_ANGLE + CAMERA_SWAY * sin(tm * 0.045) + CAMERA_ORBIT * tm;
    float cd = clamp(CAMERA_DISTANCE, 7.5, 15.0);
    float3 eye = float3(cos(ang) * cd, clamp(CAMERA_HEIGHT, 0.3, 11.0), sin(ang) * cd);
    float3 fw = normalize(float3(0.0, 1.7, 0.0) - eye);
    float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
    float3 upv = cross(rt, fw);
    float fl = 1.0 / tan(radians(clamp(FIELD_OF_VIEW, 10.0, 120.0)) * 0.5);
    fl *= min(1.0, aspect / 1.5);              // on upright screens widen the view, so the scene still fits
    float focus = FOCUS_DISTANCE > 0.0 ? FOCUS_DISTANCE : length(float3(0.0, 1.752, 0.0) - eye) - 0.6;

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
        float3 L = tracePath(ro, rd, orbC, pix, sf, tseed);
        col += clamp(L, float3(0.0, 0.0, 0.0), float3(64.0, 64.0, 64.0));
    }
    col /= float(SAMPLES);

    // filmic tone curve (ACES fit), display gamma, vignette
    col *= EXPOSURE;
    col = saturate(col * (2.51 * col + 0.03) / (col * (2.43 * col + 0.59) + 0.14));
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
