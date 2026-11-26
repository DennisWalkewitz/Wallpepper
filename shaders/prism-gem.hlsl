// @name: Prism Gem
// @tags: 3d, abstract, featured
// @light: SAMPLES=1, BOUNCES=3, FLOOR_REFLECTION=0, GLARE=0
// A brilliant-cut gemstone turning above a dark polished floor, rendered with real optics: light is refracted into
// the 57 facets, bounces around inside and leaves again split into colours, in a studio of soft boxes and tiny spots.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define SPIN_SPEED 0.16       // how fast the stone turns around its own axis (0..0.8)
#define TILT 0.42             // how far the stone leans towards you (0..1.2, 0 = upright)
#define SWAY_SPEED 0.11       // how fast the lean sways from side to side (0..0.5)
#define GEM_SIZE 1.0          // size of the stone (0.4..1.6)
#define REFRACTION 2.2        // how strongly the stone bends light (1.3..3.0; glass 1.5, sapphire 1.77, diamond 2.42)
#define DISPERSION 0.045      // how far colours are split apart inside the stone, its "fire" (0..0.15)
#define BOUNCES 5             // how often light is followed bouncing around inside the stone (2..8, more = richer and slower)
#define GEM_R 0.86            // colour of the stone: red
#define GEM_G 0.94            // colour of the stone: green
#define GEM_B 1.00            // colour of the stone: blue
#define COLOUR_DEPTH 0.35     // how deeply the stone is coloured (0..2, 0 = clear like a diamond)
#define KEY_LIGHT 1.0         // main soft box
#define FILL_LIGHT 1.0        // strip lights and coloured accent lights
#define SPARKLE 1.0           // brightness of the many tiny spot lights that make the stone sparkle
#define GLARE 1.0             // star-shaped glints on facets that mirror a light straight at you (0 = off, a little faster)
#define FLOOR_REFLECTION 1.0  // reflection of the stone in the floor (0 = off, a little faster)
#define CAUSTICS 1.0          // rainbow flecks of light the stone throws onto the floor
#define SHADOWS 1.0           // strength of the shadow under the stone (0..1)
#define CAMERA_DISTANCE 6.4   // distance of the camera (3.5..14)
#define CAMERA_HEIGHT 1.25    // height of the camera above the middle of the stone (0..5)
#define CAMERA_ORBIT 0.03     // speed of the camera circling the stone (0..0.3)
#define BACKDROP_R 0.040      // backdrop colour: red
#define BACKDROP_G 0.048      // backdrop colour: green
#define BACKDROP_B 0.075      // backdrop colour: blue
#define EXPOSURE 1.0          // overall brightness
#define SAMPLES 2             // anti-aliasing samples per pixel (1..4, each costs the same again)
#define TEXT_VEIL 0.5         // darkening behind terminal text

float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 rot(float2 v, float a) { float c = cos(a), s = sin(a); return float2(c * v.x - s * v.y, s * v.x + c * v.y); }

float3 keyDir() { return normalize(float3(-0.50, 0.78, 0.38)); }

float gemSize() { return max(GEM_SIZE, 0.05); }
float gemIndex() { return max(REFRACTION, 1.05); }

// ---- the stone ------------------------------------------------------------------------------------------
// The stone is the space inside 57 flat facets plus a round girdle, so a ray is cut against every facet plane:
// the last plane it enters through and the first it leaves through give the exact way in and out.
void facet(float3 n, float h, float3 o, float3 d, inout float tN, inout float3 nN, inout float tF, inout float3 nF)
{
    float dn = dot(n, d);
    float dist = h - dot(n, o);
    if (abs(dn) < 0.000001) dn = 0.000001;
    float tt = dist / dn;
    if (dn < 0.0) { if (tt > tN) { tN = tt; nN = n; } }
    else { if (tt < tF) { tF = tt; nF = n; } }
}

// does the ray come anywhere near the stone? (o, d in the stone's own space)
bool nearGem(float3 o, float3 d)
{
    float bb = dot(o, d);
    return bb * bb - dot(o, o) + 1.03 > 0.0;
}

// o, d in the stone's own space (girdle radius 1, axis along y). It is a hit when tN < tF.
void gemSpan(float3 o, float3 d, out float tN, out float3 nN, out float tF, out float3 nF)
{
    tN = -1000000.0; tF = 1000000.0;
    nN = float3(0.0, 1.0, 0.0); nF = float3(0.0, 1.0, 0.0);
    facet(float3(0.0, 1.0, 0.0), 0.312, o, d, tN, nN, tF, nF);     // the table

    float ca = max(dot(d.xz, d.xz), 0.0000001), cb = dot(o.xz, d.xz), cc = dot(o.xz, o.xz) - 1.0;   // the round girdle
    float disc = cb * cb - ca * cc;
    if (disc > 0.0)
    {
        float sq = sqrt(disc);
        float t0 = (-cb - sq) / ca, t1 = (-cb + sq) / ca;
        if (t0 > tN) { tN = t0; nN = float3(o.x + d.x * t0, 0.0, o.z + d.z * t0); }
        if (t1 < tF) { tF = t1; nF = float3(o.x + d.x * t1, 0.0, o.z + d.z * t1); }
    }
    else { tN = 1000000.0; tF = -1000000.0; }                      // passes beside the girdle: a miss

    float2 az = float2(1.0, 0.0);
    [loop] for (int k = 0; k < 8; k++)
    {
        float2 a1 = float2(az.x * 0.980785 - az.y * 0.195090, az.x * 0.195090 + az.y * 0.980785);   // + 11.25 degrees
        float2 a2 = float2(az.x * 0.923880 - az.y * 0.382683, az.x * 0.382683 + az.y * 0.923880);   // + 22.5
        float2 a3 = float2(az.x * 0.831470 - az.y * 0.555570, az.x * 0.555570 + az.y * 0.831470);   // + 33.75
        facet(float3(az.x * 0.5592, 0.8290, az.y * 0.5592), 0.5691, o, d, tN, nN, tF, nF);          // crown main (kite)
        facet(float3(a2.x * 0.4084, 0.9128, a2.y * 0.4084), 0.4943, o, d, tN, nN, tF, nF);          // star
        facet(float3(a1.x * 0.6832, 0.7302, a1.y * 0.6832), 0.6789, o, d, tN, nN, tF, nF);          // upper girdle
        facet(float3(a3.x * 0.6832, 0.7302, a3.y * 0.6832), 0.6789, o, d, tN, nN, tF, nF);
        facet(float3(az.x * 0.6528, -0.7576, az.y * 0.6528), 0.6619, o, d, tN, nN, tF, nF);         // pavilion main
        facet(float3(a1.x * 0.6692, -0.7431, a1.y * 0.6692), 0.6653, o, d, tN, nN, tF, nF);         // lower girdle
        facet(float3(a3.x * 0.6692, -0.7431, a3.y * 0.6692), 0.6653, o, d, tN, nN, tF, nF);
        az = float2((az.x - az.y) * 0.707107, (az.x + az.y) * 0.707107);                            // + 45
    }
}

// ---- lighting -------------------------------------------------------------------------------------------
// The studio around the stone as the stone sees it: a few big lights, many tiny ones, and darkness in between
// (a stone only sparkles against contrast). Looking down, it sees the same studio mirrored dimly in the floor.
float3 studio(float3 d)
{
    float3 q = float3(d.x, abs(d.y), d.z);
    float3 c = lerp(float3(0.010, 0.012, 0.018), float3(0.050, 0.062, 0.090), smoothstep(0.0, 1.0, q.y));

    float3 kd = keyDir();                                             // big soft box
    float3 k1 = normalize(cross(kd, float3(0.0, 1.0, 0.0)));
    float3 k2 = cross(k1, kd);
    float kz = dot(q, kd);
    float2 ku = float2(dot(q, k1), dot(q, k2)) / max(kz, 0.001);
    float key = smoothstep(0.36, 0.30, abs(ku.x)) * smoothstep(0.26, 0.21, abs(ku.y)) * step(0.0, kz);
    key *= 0.25 + 0.75 * smoothstep(0.006, 0.020, abs(ku.x)) * smoothstep(0.006, 0.020, abs(ku.y));   // frame of the soft box
    c += float3(1.00, 0.95, 0.88) * key * (2.6 + 4.4 * smoothstep(0.42, 0.0, length(ku))) * KEY_LIGHT;

    float s1 = smoothstep(0.050, 0.030, abs(q.x * 0.819 - q.z * 0.574)) * step(0.0, q.x * 0.574 + q.z * 0.819);
    float s2 = smoothstep(0.040, 0.024, abs(q.x * 0.342 + q.z * 0.940)) * step(0.0, q.x * 0.940 - q.z * 0.342);
    float s3 = smoothstep(0.045, 0.028, abs(q.x * 0.643 - q.z * 0.766)) * step(0.0, -q.x * 0.766 - q.z * 0.643);
    float tall = smoothstep(0.04, 0.12, q.y) * smoothstep(0.86, 0.72, q.y);
    c += float3(0.86, 0.93, 1.00) * (s1 + s2) * tall * 9.0 * FILL_LIGHT;                        // tall strip lights
    c += float3(1.00, 0.90, 0.80) * s3 * tall * 6.0 * FILL_LIGHT;

    float rim = smoothstep(0.955, 0.975, dot(q, normalize(float3(0.85, 0.30, -0.48))));
    c += float3(0.45, 0.68, 1.00) * rim * 5.0 * FILL_LIGHT;                                      // cool rim light
    float warm = smoothstep(0.972, 0.985, dot(q, normalize(float3(-0.62, 0.36, -0.70))));
    c += float3(1.00, 0.42, 0.16) * warm * 5.0 * FILL_LIGHT;                                     // amber accent
    float rose = smoothstep(0.978, 0.990, dot(q, normalize(float3(0.35, 0.55, 0.76))));
    c += float3(1.00, 0.25, 0.55) * rose * 4.0 * FILL_LIGHT;                                     // rose accent

    float2 g = q.xz / (q.y + 0.22) * 3.3;                                                        // ceiling of tiny spots
    float2 cell = floor(g);
    float rnd = hash12(cell + 17.0);
    float2 jit = float2(hash12(cell + 3.7), hash12(cell + 9.1)) - 0.5;
    float spot = smoothstep(0.115, 0.045, length(g - cell - 0.5 - jit * 0.55));
    float3 tint = lerp(float3(1.00, 0.86, 0.70), float3(0.72, 0.86, 1.00), frac(rnd * 7.31));
    c += tint * spot * step(0.42, rnd) * (18.0 + 44.0 * frac(rnd * 3.17)) * smoothstep(0.10, 0.25, q.y) * SPARKLE;

    float mirror = 0.05 + 0.60 * pow(saturate(1.0 - q.y), 4.0);
    return c * lerp(1.0, mirror, step(d.y, 0.0));
}

// reflectance of a clear surface for a ray i meeting the surface normal n; eta = index before / index after.
// tdir is the bent ray that continues on the other side (reflectance 1 = nothing gets through).
float fresnel(float3 i, float3 n, float eta, out float3 tdir)
{
    float ci = saturate(-dot(i, n));
    float c2 = 1.0 - eta * eta * (1.0 - ci * ci);                  // below zero: total reflection
    float ct = sqrt(max(c2, 0.0));
    tdir = eta * i + (eta * ci - ct) * n;
    float rs = (eta * ci - ct) / max(eta * ci + ct, 0.000001);
    float rp = (ci - eta * ct) / max(ci + eta * ct, 0.000001);
    return c2 > 0.0 ? 0.5 * (rs * rs + rp * rp) : 1.0;
}

float3 toWorld(float3 v, float3 bx, float3 by, float3 bz) { return bx * v.x + by * v.y + bz * v.z; }
float3 toGem(float3 v, float3 bx, float3 by, float3 bz) { return float3(dot(v, bx), dot(v, by), dot(v, bz)); }

// o, d: ray in the stone's space that enters it at distance tN through a facet with normal nN
float3 shadeGem(float3 o, float3 d, float tN, float3 nN, float maxBounces, float hue, float3 bx, float3 by, float3 bz)
{
    float3 p = o + d * tN;
    float3 gemC = float3(GEM_R, GEM_G, GEM_B);
    float3 tdir;
    float f0 = fresnel(d, nN, 1.0 / gemIndex(), tdir);
    float3 col = studio(toWorld(reflect(d, nN), bx, by, bz)) * f0;                // mirror image on the facet
    float3 dens = 1.6 * COLOUR_DEPTH * (float3(1.0, 1.0, 1.0) - gemC);            // how fast the stone swallows each colour

    [loop] for (int ch = 0; ch < 3; ch++)                                         // red, green and blue bend differently
    {
        // each anti-aliasing sample follows three slightly different colours of light, so two samples
        // already give six steps of the rainbow instead of pure red, green and blue
        float wl = float(ch) - 1.0 + hue;
        float wc = clamp(wl, -1.0, 1.0);
        float3 mask = saturate(float3(1.0, 1.0, 1.0) - abs(float3(wc + 1.0, wc, wc - 1.0)));
        float ior = max(gemIndex() + DISPERSION * wl, 1.05);
        float3 dd;
        float thr = 1.0 - fresnel(d, nN, 1.0 / ior, dd);
        float3 oo = p;
        float len = 0.0;
        float3 sum = float3(0.0, 0.0, 0.0);
        [loop] for (int b = 0; b < 8; b++)
        {
            if (float(b) >= maxBounces || thr < 0.012) break;
            float e0, e1;
            float3 m0, m1;
            gemSpan(oo, dd, e0, m0, e1, m1);
            e1 = max(e1, 0.0);
            m1 = normalize(m1);
            oo += dd * e1;
            len += e1;
            float3 outD;
            float fe = fresnel(dd, -m1, ior, outD);                               // 1 = totally reflected back inside
            if (fe < 0.999)
            {
                sum += mask * exp(-dens * len) * (thr * (1.0 - fe) * dot(studio(toWorld(outD, bx, by, bz)), mask));
            }
            thr *= fe;
            dd = dd - 2.0 * dot(dd, m1) * m1;
        }
        // light still bouncing around after the last bounce: let it leave where it is, towards whatever
        // the studio has in that direction, mixed with an average bit of studio
        float3 leftW = toWorld(dd, bx, by, bz);
        float avg = 0.05 + 0.20 * smoothstep(-0.2, 0.9, leftW.y) * KEY_LIGHT + 0.10 * FILL_LIGHT;
        sum += mask * exp(-dens * (len + 0.8)) * (thr * (avg + 0.5 * dot(studio(leftW), mask)));
        col += sum;
    }
    return col;
}

float3 backdrop(float3 rd, float3 fw)
{
    float3 bg = float3(BACKDROP_R, BACKDROP_G, BACKDROP_B);
    float3 c = bg * (0.34 + 0.40 * smoothstep(-0.35, 0.08, rd.y) - 0.42 * smoothstep(0.08, 0.50, rd.y));
    c += bg * 0.55 * exp(-14.0 * (rd.y - 0.02) * (rd.y - 0.02));  // soft band of light where the wall meets the floor
    float3 hd = normalize(float3(fw.x, 0.10, fw.z));
    c += bg * 1.25 * pow(saturate(dot(rd, hd)), 40.0);            // glow on the wall behind the stone
    return c;
}

float3 spectrum(float x)
{
    float3 w = saturate(float3(1.0, 1.0, 1.0) - abs(float3(x - 0.12, x - 0.50, x - 0.88)) / 0.46);
    return w * w * (3.0 - 2.0 * w);
}

// Every facet throws its own small patch of light onto the floor, shaped like the facet and often spread
// into a rainbow. This draws one ring of such patches. r, a = polar coordinates around the stone's shadow.
float3 lightPatches(float r, float a, float count, float radius, float len, float seed)
{
    float u = a * count / 6.2831853 + 64.0;
    float id = fmod(floor(u), count);
    float rnd = hash12(float2(id * 3.7 + 1.3, seed));
    float rnd2 = frac(rnd * 7.13);
    float fu = (frac(u) - 0.5) * 2.0;
    float fv = (r - radius * (0.80 + 0.40 * rnd2)) / (len * (0.6 + 0.8 * rnd));
    float halfw = (0.20 + 0.50 * rnd2) * (1.0 - saturate(fv));                       // pointed at the far end
    float shape = smoothstep(halfw + 0.06, halfw - 0.06, abs(fu + (rnd - 0.5) * 0.5)) * smoothstep(-0.06, 0.06, fv) * step(fv, 1.0);
    float3 c = lerp(float3(1.0, 0.97, 0.92), spectrum(fv) * 1.6, smoothstep(0.25, 0.55, rnd2));
    return c * shape * (0.15 + 0.85 * rnd * rnd);
}

float3 render(float3 ro, float3 rd, float3 fw, float t, float hue, float3 bx, float3 by, float3 bz)
{
    float gs = gemSize();
    float shade = saturate(SHADOWS);
    float floorY = -1.04 * gs;
    float3 col = backdrop(rd, fw);

    float tN = 1000000.0, tF = -1000000.0;
    float3 nN = float3(0.0, 1.0, 0.0), nF = float3(0.0, 1.0, 0.0);
    float3 go = toGem(ro, bx, by, bz) / gs;
    float3 gd = toGem(rd, bx, by, bz);
    if (nearGem(go, gd)) gemSpan(go, gd, tN, nN, tF, nF);
    float weight = 0.0;
    float bounces = float(BOUNCES);
    float3 under = float3(0.0, 0.0, 0.0);

    if (tN < tF && tN > 0.0)
    {
        weight = 1.0;
    }
    else if (rd.y < -0.001)
    {
        // polished floor: soft shadow, rainbow flecks, and the mirror image of the stone
        float tf = (floorY - ro.y) / rd.y;
        float3 pf = ro + rd * tf;
        float3 kd = keyDir();
        float3 gc = by * (-0.16 * gs);
        float tca = dot(gc - pf, kd);
        float dd = length(pf + kd * tca - gc);
        float shadow = lerp(1.0, 0.38 + 0.62 * smoothstep(0.35 * gs, 1.05 * gs, dd), shade * step(0.0, tca));
        float contact = 1.0 - 0.50 * shade * exp(-dot(pf.xz, pf.xz) / (gs * gs * 0.7));

        float3 floorC = float3(0.022, 0.024, 0.032);
        float3 fcol = floorC * (0.15 + 1.1 * saturate(kd.y) * KEY_LIGHT * shadow) * contact;

        // light that went through the stone lands in and around its shadow as small rainbows
        float3 sc = gc - kd * ((gc.y - floorY) / kd.y);
        float2 cq = (pf.xz - sc.xz) / gs;
        float cr = length(cq);
        if (cr < 2.6)
        {
            float ang = atan2(cq.y, cq.x);
            float spin = t * SPIN_SPEED;
            float3 ca = lightPatches(cr, ang + spin * 2.0, 8.0, 0.55, 0.42, 1.0);
            ca += lightPatches(cr, ang - spin + 0.9, 16.0, 1.05, 0.36, 2.0) * 0.8;
            ca += lightPatches(cr, ang + spin + 2.1, 16.0, 1.55, 0.50, 3.0) * 0.5;
            ca += lightPatches(cr, ang - spin * 2.0 + 0.4, 24.0, 1.95, 0.40, 4.0) * 0.25;
            float3 gemC = lerp(float3(1.0, 1.0, 1.0), float3(GEM_R, GEM_G, GEM_B), saturate(0.3 + COLOUR_DEPTH));
            fcol += ca * gemC * smoothstep(2.5, 0.9, cr) * 0.085 * CAUSTICS * KEY_LIGHT;
        }

        float3 rr = float3(rd.x, -rd.y, rd.z);
        float fres = 0.045 + 0.955 * pow(1.0 - saturate(-rd.y), 5.0);
        float fog = saturate(1.0 - exp(-tf * tf * 0.006));
        float refl = fres * 2.0 * FLOOR_REFLECTION * contact;

        go = toGem(pf, bx, by, bz) / gs;
        gd = toGem(rr, bx, by, bz);
        tN = 1000000.0; tF = -1000000.0;
        if (FLOOR_REFLECTION > 0.001 && nearGem(go, gd)) gemSpan(go, gd, tN, nN, tF, nF);
        if (tN < tF && tN > 0.0)
        {
            weight = refl * (1.0 - fog);
            bounces = min(bounces, 4.0);
        }
        else
        {
            fcol += backdrop(rr, fw) * 0.6 * refl;
        }
        under = lerp(fcol, col, fog);
        col = under;
    }

    if (weight > 0.0)
    {
        col = under + weight * shadeGem(go, gd, tN, normalize(nN), bounces, hue, bx, by, bz);
    }
    return col;
}

// A facet that mirrors one of the lights straight into the camera flares up: a soft glow with a thin star
// around the middle of that facet. Done for the table and the 16 big crown facets, in one pass.
float3 glint(float3 cG, float3 nG, float2 uv, float3 ro, float3 fw, float3 rt, float3 upv, float3 bx, float3 by, float3 bz)
{
    float3 pc = toWorld(cG, bx, by, bz) * gemSize();
    float3 nW = toWorld(nG, bx, by, bz);
    float3 v = normalize(ro - pc);
    float ndv = dot(nW, v);
    float3 rel = pc - ro;
    float z = dot(rel, fw);
    float2 dq = (uv - 1.9 * float2(dot(rel, rt), dot(rel, upv)) / z) * z / (1.9 * gemSize());
    float r2 = dot(dq, dq);
    float3 res = float3(0.0, 0.0, 0.0);
    if (ndv > 0.02 && r2 < 1.5)                                                       // facet faces us and is near this pixel
    {
        float3 lightC = studio(2.0 * ndv * nW - v);
        lightC *= smoothstep(2.0, 7.0, max(lightC.x, max(lightC.y, lightC.z)));      // only real lights flare
        float f0 = (gemIndex() - 1.0) / (gemIndex() + 1.0);
        float fres = f0 * f0 + (1.0 - f0 * f0) * pow(1.0 - saturate(ndv), 5.0);
        float2 e = float2(dq.x * 0.940 - dq.y * 0.342, dq.x * 0.342 + dq.y * 0.940);
        float star = exp(-abs(e.x) * 4.5 - e.y * e.y * 2600.0) + exp(-abs(e.y) * 4.5 - e.x * e.x * 2600.0);
        float glow = 0.42 * exp(-r2 * 34.0) + 0.05 / (1.0 + r2 * 40.0);
        res = lightC * fres * (glow + 0.55 * star) * smoothstep(1.5, 0.5, r2);
    }
    return res;
}

float3 glare(float2 uv, float3 ro, float3 fw, float3 rt, float3 upv, float3 bx, float3 by, float3 bz)
{
    float3 acc = float3(0.0, 0.0, 0.0);
    float zc = dot(-ro, fw);
    float2 dc = uv - 1.9 * float2(dot(-ro, rt), dot(-ro, upv)) / zc;
    float reach = 2.1 * 1.9 * gemSize() / zc;
    if (dot(dc, dc) < reach * reach)                                                  // only near the stone
    {
        acc += glint(float3(0.0, 0.312, 0.0), float3(0.0, 1.0, 0.0), uv, ro, fw, rt, upv, bx, by, bz);
        float2 az = float2(1.0, 0.0);
        [loop] for (int k = 0; k < 8; k++)
        {
            float2 a2 = float2(az.x * 0.923880 - az.y * 0.382683, az.x * 0.382683 + az.y * 0.923880);
            acc += glint(float3(az.x * 0.79, 0.165, az.y * 0.79), float3(az.x * 0.5592, 0.8290, az.y * 0.5592), uv, ro, fw, rt, upv, bx, by, bz);
            acc += glint(float3(a2.x * 0.63, 0.270, a2.y * 0.63), float3(a2.x * 0.4084, 0.9128, a2.y * 0.4084), uv, ro, fw, rt, upv, bx, by, bz);
            az = float2((az.x - az.y) * 0.707107, (az.x + az.y) * 0.707107);
        }
    }
    return acc * 0.5 * GLARE;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float t = Time;
    float ang = t * CAMERA_ORBIT + 0.45;
    float3 ro = float3(sin(ang) * CAMERA_DISTANCE, CAMERA_HEIGHT, cos(ang) * CAMERA_DISTANCE);
    float3 fw = normalize(float3(0.0, -0.74 * gemSize(), 0.0) - ro);
    float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
    float3 upv = cross(rt, fw);

    // the stone's own axes in the room: spin around its axis, lean over, and let the lean wander
    float spin = t * SPIN_SPEED;
    float lean = -ang + 0.85 * sin(t * SWAY_SPEED + 0.6);
    float3 bx = float3(cos(spin), 0.0, -sin(spin));
    float3 by = float3(0.0, 1.0, 0.0);
    float3 bz = float3(sin(spin), 0.0, cos(spin));
    bx.yz = rot(bx.yz, TILT); by.yz = rot(by.yz, TILT); bz.yz = rot(bz.yz, TILT);
    bx.xz = rot(bx.xz, lean); by.xz = rot(by.xz, lean); bz.xz = rot(bz.xz, lean);

    float3 col = float3(0.0, 0.0, 0.0);
    for (int sIdx = 0; sIdx < SAMPLES; sIdx++)
    {
        float2 off = (float2(frac(float(sIdx) * 0.5 + 0.25), frac(float(sIdx) * 0.75 + 0.125)) - 0.5) * step(1.5, float(SAMPLES));
        float2 uv = float2((tex.x - 0.5) * aspect, 0.5 - tex.y) + off / Resolution.y;
        float3 rd = normalize(fw * 1.9 + rt * uv.x + upv * uv.y);
        float hue = (float(sIdx) + 0.5) / float(SAMPLES) - 0.5;
        col += render(ro, rd, fw, t, hue, bx, by, bz);
    }
    col /= float(SAMPLES);
    if (GLARE > 0.001) col += glare(float2((tex.x - 0.5) * aspect, 0.5 - tex.y), ro, fw, rt, upv, bx, by, bz);

    // filmic tone curve, then display gamma
    col *= EXPOSURE;
    col = saturate(col * (2.51 * col + 0.03) / (col * (2.43 * col + 0.59) + 0.14));
    col = pow(col, float3(0.4545, 0.4545, 0.4545));
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.45;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
