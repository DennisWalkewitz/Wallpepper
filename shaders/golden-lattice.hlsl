// @name: Golden Lattice
// @tags: 3d, abstract, featured
// @light: SAMPLES=1, FLOOR_REFLECTION=0
// A slowly turning sphere cut from a gyroid lattice, lit like a product shot: physically based metal (GGX),
// studio soft boxes reflected in the surface, soft shadows, ambient occlusion, a glowing core that lights the
// lattice from inside, and a polished floor with a real reflection.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define SPIN_SPEED 0.22       // rotation speed of the sphere
#define TUMBLE 0.45           // how much it also tips over while spinning
#define LATTICE_SCALE 8.5     // number of lattice cells across the sphere
#define LATTICE_THICKNESS 0.20 // thickness of the lattice walls
#define MORPH 0.35            // slow breathing of the lattice (0 = rigid)
#define ORB_SIZE 1.0          // size of the sphere
#define ROUGHNESS 0.30        // 0 = mirror, 1 = matte
#define METALLIC 1.0          // 1 = metal, 0 = lacquer
#define METAL_R 1.00          // surface colour: red
#define METAL_G 0.72          // surface colour: green
#define METAL_B 0.30          // surface colour: blue
#define CORE_GLOW 1.0         // brightness of the glowing core
#define CORE_R 1.00           // core colour: red
#define CORE_G 0.42           // core colour: green
#define CORE_B 0.12           // core colour: blue
#define KEY_LIGHT 1.0         // main soft box
#define FILL_LIGHT 1.0        // cool fill and rim lights
#define SHADOWS 1.0           // strength of the shadows (0..1, 0 = off, faster)
#define FLOOR_REFLECTION 1.0  // reflection in the floor (0 = off, faster)
#define CAMERA_DISTANCE 5.8   // distance of the camera
#define CAMERA_HEIGHT 0.70    // height of the camera
#define CAMERA_ORBIT 0.05     // speed of the camera circling the sphere
#define BACKDROP_R 0.014      // backdrop colour: red
#define BACKDROP_G 0.017      // backdrop colour: green
#define BACKDROP_B 0.028      // backdrop colour: blue
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

// ---- the object -----------------------------------------------------------------------------------------
float map(float3 p, float t)
{
    float r = length(p);
    float3 q = p;
    q.xz = rot(q.xz, t * SPIN_SPEED);
    q.xy = rot(q.xy, t * SPIN_SPEED * TUMBLE + 0.4);
    float k = LATTICE_SCALE / ORB_SIZE;
    float3 a = q * k;
    float g = dot(sin(a), cos(a.yzx)) - 0.55 * MORPH * sin(t * 0.21);
    float lattice = (abs(g) - LATTICE_THICKNESS) / k * 0.60;
    float d = max(lattice, r - ORB_SIZE);                 // cut to a sphere
    d = max(d, ORB_SIZE * 0.36 - r);                      // hollow middle
    return min(d, r - ORB_SIZE * 0.15);                   // the core
}

float3 normalAt(float3 p, float t)
{
    float2 e = float2(1.0, -1.0) * 0.0012;
    return normalize(e.xyy * map(p + e.xyy, t) + e.yyx * map(p + e.yyx, t) + e.yxy * map(p + e.yxy, t) + e.xxx * map(p + e.xxx, t));
}

// marches only inside the sphere that bounds the object; returns the hit distance or -1
float march(float3 ro, float3 rd, float t, int steps, out float glow)
{
    glow = 0.0;
    float b = dot(ro, rd);
    float rr = ORB_SIZE + 0.01;
    float disc = b * b - dot(ro, ro) + rr * rr;
    if (disc <= 0.0) return -1.0;
    float sq = sqrt(disc);
    float t0 = max(-b - sq, 0.0), t1 = -b + sq;
    if (t1 <= 0.0) return -1.0;
    float tt = t0;
    float hit = -1.0;
    for (int i = 0; i < steps; i++)
    {
        float3 p = ro + rd * tt;
        float d = map(p, t);
        glow += max(d, 0.004) * exp(-length(p) * 5.0 / ORB_SIZE);     // light of the core scattered in the air
        if (d < 0.0007 * tt + 0.0004) { hit = tt; break; }
        tt += d;
        if (tt > t1) break;
    }
    return hit;
}

float softShadow(float3 p, float3 l, float t)
{
    float res = 1.0, s = 0.03;
    for (int i = 0; i < 22; i++)
    {
        float3 q = p + l * s;
        if (dot(q, q) > ORB_SIZE * ORB_SIZE * 1.05) break;
        float d = map(q, t);
        res = min(res, 9.0 * d / s);
        if (res < 0.02) break;
        s += clamp(d, 0.02, 0.14);
    }
    return saturate(res);
}

float ambientOcclusion(float3 p, float3 n, float t)
{
    float occ = 0.0, w = 1.0;
    for (int i = 1; i <= 5; i++)
    {
        float h = 0.035 * float(i);
        occ += (h - map(p + n * h, t)) * w;
        w *= 0.72;
    }
    return saturate(1.0 - 3.2 * occ);
}

// ---- lighting -------------------------------------------------------------------------------------------
// the studio around the object, as seen in reflections; rough surfaces see a blurrier studio
float3 studio(float3 d, float rough)
{
    float soft = 0.03 + 0.45 * rough;
    float3 c = lerp(float3(0.010, 0.012, 0.018), float3(0.060, 0.070, 0.095), smoothstep(-0.4, 0.9, d.y));
    float key = smoothstep(0.93 - soft, 0.985 + soft * 0.05, dot(d, keyDir()));
    c += float3(1.00, 0.95, 0.88) * key * 3.2 * KEY_LIGHT;                                           // big soft box
    float rim = smoothstep(0.90 - soft, 0.97, dot(d, normalize(float3(0.85, 0.22, -0.48))));
    c += float3(0.55, 0.75, 1.00) * rim * 2.6 * FILL_LIGHT;                                           // cool rim light
    float strip = smoothstep(0.10 + soft, 0.0, abs(d.x * 0.80 + d.z * 0.60 + 0.25)) * smoothstep(-0.25, 0.15, d.y) * smoothstep(0.95, 0.55, d.y);
    c += float3(1.00, 0.98, 0.95) * strip * 1.6 * FILL_LIGHT;                                         // tall strip light
    float fill = smoothstep(0.25, 1.0, dot(d, normalize(float3(-0.70, -0.15, -0.65))));
    c += float3(0.16, 0.26, 0.48) * fill * 0.9 * FILL_LIGHT;
    c += float3(CORE_R, CORE_G, CORE_B) * smoothstep(0.2, -1.0, d.y) * 0.10 * CORE_GLOW;              // warm bounce from the floor
    return c;
}

float dGGX(float noh, float a) { float a2 = a * a; float d = noh * noh * (a2 - 1.0) + 1.0; return a2 / (3.14159265 * d * d); }
float vSmith(float nov, float nol, float a) { float k = a * 0.5; return 0.25 / ((nov * (1.0 - k) + k) * (nol * (1.0 - k) + k)); }

float3 envBrdf(float3 f0, float rough, float nov)
{
    float4 r = rough * float4(-1.0, -0.0275, -0.572, 0.022) + float4(1.0, 0.0425, 1.04, -0.04);
    float a004 = min(r.x * r.x, exp2(-9.28 * nov)) * r.x + r.y;
    return f0 * (-1.04 * a004 + r.z) + float3(1.0, 1.0, 1.0) * (1.04 * a004 + r.w);
}

float3 brdf(float3 n, float3 v, float3 l, float3 diffC, float3 f0, float a)
{
    float3 hv = normalize(l + v);
    float nol = saturate(dot(n, l)), nov = saturate(dot(n, v)) + 0.0001;
    float3 f = f0 + (float3(1.0, 1.0, 1.0) - f0) * pow(1.0 - saturate(dot(v, hv)), 5.0);
    return (diffC / 3.14159265 + f * dGGX(saturate(dot(n, hv)), a) * vSmith(nov, nol, a)) * nol;
}

float3 shadeOrb(float3 p, float3 rd, float t, float detail)
{
    float3 coreC = float3(CORE_R, CORE_G, CORE_B);
    if (length(p) < ORB_SIZE * 0.17)                        // the core itself: hot, brighter towards its middle
    {
        float f = saturate(dot(normalize(p), -rd));
        return coreC * (2.0 + 9.0 * f * f) * CORE_GLOW + float3(1.0, 0.9, 0.7) * pow(f, 6.0) * 6.0 * CORE_GLOW;
    }
    float3 n = normalAt(p, t);
    float3 v = -rd;
    float3 base = float3(METAL_R, METAL_G, METAL_B);
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), base, METALLIC);
    float3 diffC = base * (1.0 - METALLIC);
    float rough = clamp(ROUGHNESS, 0.04, 1.0);
    float a = rough * rough;
    float nov = saturate(dot(n, v)) + 0.0001;

    float ao = 1.0, sh = 1.0;
    if (detail > 0.5)
    {
        ao = ambientOcclusion(p, n, t);
        if (SHADOWS > 0.0) sh = lerp(1.0, softShadow(p + n * 0.004, keyDir(), t), SHADOWS);
    }

    float3 col = brdf(n, v, keyDir(), diffC, f0, a) * float3(1.00, 0.95, 0.88) * 3.2 * KEY_LIGHT * sh;

    float3 toCore = -p;                                      // the core as a point light inside the lattice
    float dc = length(toCore);
    col += brdf(n, v, toCore / dc, diffC, f0, max(a, 0.08)) * coreC * 0.20 * CORE_GLOW / (dc * dc + 0.10);
    col += (diffC + f0 * 0.6) * coreC * 0.14 * CORE_GLOW * saturate(dot(n, toCore / dc)) / (dc * dc + 0.10);

    float3 rdir = reflect(rd, n);
    float specOcc = saturate(ao * ao + 0.25 * ao) * lerp(1.0, 0.35 + 0.65 * sh, 0.5);
    col += studio(rdir, rough) * envBrdf(f0, rough, nov) * specOcc;
    col += studio(n, 1.0) * diffC * ao;
    return col;
}

float3 backdrop(float3 rd)
{
    float3 bg = float3(BACKDROP_R, BACKDROP_G, BACKDROP_B);
    float3 c = bg * (0.35 + 1.3 * smoothstep(-0.25, 0.75, rd.y));
    c += bg * 1.2 * exp(-8.0 * (rd.y - 0.02) * (rd.y - 0.02));    // soft band of light where the wall meets the floor
    return c;
}

float3 render(float3 ro, float3 rd, float t)
{
    float3 coreC = float3(CORE_R, CORE_G, CORE_B);
    float floorY = -ORB_SIZE * 1.32;
    float3 col = backdrop(rd);

    float glow;
    float th = march(ro, rd, t, 96, glow);
    if (th > 0.0)
    {
        col = shadeOrb(ro + rd * th, rd, t, 1.0);
    }
    else if (rd.y < -0.001)
    {
        // polished floor: shadow of the sphere, the pool of light under it, and its reflection
        float tf = (floorY - ro.y) / rd.y;
        float3 pf = ro + rd * tf;
        float3 fn = float3(0.0, 1.0, 0.0);
        float3 kd = keyDir();
        float tca = dot(-pf, kd);
        float dd = length(pf + kd * tca);
        float shadow = lerp(1.0, 0.30 + 0.70 * smoothstep(ORB_SIZE * 0.55, ORB_SIZE * 1.20, dd), SHADOWS * step(0.0, tca));
        float contact = 1.0 - 0.55 * SHADOWS * exp(-dot(pf.xz, pf.xz) / (ORB_SIZE * ORB_SIZE * 0.9));

        float3 floorC = float3(0.055, 0.058, 0.066);
        float3 fcol = floorC * (0.15 + 1.1 * saturate(kd.y) * KEY_LIGHT * shadow) * contact;
        float3 toC = -pf;
        float dcf = length(toC);
        fcol += (floorC * 6.0 + float3(0.02, 0.02, 0.02)) * coreC * CORE_GLOW * 0.9 * saturate(toC.y / dcf) / (dcf * dcf);

        float3 rr = reflect(rd, fn);
        float fres = 0.04 + 0.96 * pow(1.0 - saturate(-rd.y), 5.0);
        if (FLOOR_REFLECTION > 0.0)
        {
            float g2;
            float tr = march(pf, rr, t, 64, g2);
            float3 refl = tr > 0.0 ? shadeOrb(pf + rr * tr, rr, t, 0.0) : backdrop(rr) * 0.6 + studio(rr, 0.35) * 0.05;
            refl += coreC * g2 * 2.4 * CORE_GLOW;
            fcol += refl * fres * 2.2 * FLOOR_REFLECTION * contact;
        }

        float fog = 1.0 - exp(-tf * tf * 0.006);
        col = lerp(fcol, col, saturate(fog));
    }
    col += coreC * glow * 1.5 * CORE_GLOW;
    return col;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float t = Time;
    float ang = t * CAMERA_ORBIT + 0.6;
    float3 ro = float3(sin(ang) * CAMERA_DISTANCE, CAMERA_HEIGHT, cos(ang) * CAMERA_DISTANCE);
    float3 fw = normalize(float3(0.0, -0.22 * ORB_SIZE, 0.0) - ro);
    float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
    float3 upv = cross(rt, fw);

    float3 col = float3(0.0, 0.0, 0.0);
    for (int sIdx = 0; sIdx < SAMPLES; sIdx++)
    {
        float2 off = (float2(frac(float(sIdx) * 0.5 + 0.25), frac(float(sIdx) * 0.75 + 0.125)) - 0.5) * step(1.5, float(SAMPLES));
        float2 uv = float2((tex.x - 0.5) * aspect, 0.5 - tex.y) + off / Resolution.y;
        float3 rd = normalize(fw * 1.9 + rt * uv.x + upv * uv.y);
        col += render(ro, rd, t);
    }
    col /= float(SAMPLES);

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
