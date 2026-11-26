// @name: Tesseract
// @tags: 3d, math, featured
// @light: SAMPLES=1, GLASS_FACES=0, FLOOR_REFLECTION=0
// A four-dimensional cube turning through the fourth dimension, built as a solid frame of polished gold: every
// edge is a real tube lit by studio soft boxes, with shadows, faint glass faces and a mirror image in the floor.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define SPIN_XW 0.21          // turning through the fourth dimension, sideways (this makes the cube turn inside out) (0..0.6)
#define SPIN_YW 0.13          // turning through the fourth dimension, up and down (0..0.6)
#define SPIN_ZW 0.08          // turning through the fourth dimension, front to back (0..0.6)
#define SPIN_XY 0.05          // ordinary turning, like a wheel facing you (0..0.6)
#define DEPTH_4D 3.0          // distance of the eye in the fourth dimension (2.3..8, near = strong perspective, far = nearly flat)
#define FRAME_SIZE 1.0        // size of the whole figure (0.4..1.5)
#define TUBE_SIZE 0.034       // thickness of the edges (0.01..0.1)
#define VERTEX_SIZE 1.7       // size of the balls at the corners, compared to the edges (1..4, 1 = no balls)
#define METAL_R 1.00          // metal colour: red
#define METAL_G 0.72          // metal colour: green
#define METAL_B 0.30          // metal colour: blue
#define ROUGHNESS 0.20        // how matte the metal is (0..1, 0 = mirror)
#define W_GLOW 0.6            // glow of the parts that are far away in the fourth dimension (0 = off)
#define W_TINT_R 1.00         // colour of that glow: red
#define W_TINT_G 0.30         // colour of that glow: green
#define W_TINT_B 0.06         // colour of that glow: blue
#define GLASS_FACES 0.6       // faint glass panes in the 24 square faces (0 = off, a little faster)
#define KEY_LIGHT 1.0         // main soft box
#define FILL_LIGHT 1.0        // cool fill, rim and strip lights
#define SHADOWS 1.0           // strength of the shadows (0..1, 0 = off, faster)
#define FLOOR_REFLECTION 1.0  // reflection in the floor (0 = off, faster)
#define CAMERA_DISTANCE 5.9   // distance of the camera (3.5..14)
#define CAMERA_HEIGHT 0.90    // height of the camera above the middle of the figure (0..5)
#define CAMERA_ORBIT 0.04     // speed of the camera circling the figure (0..0.3)
#define BACKDROP_R 0.040      // backdrop colour: red
#define BACKDROP_G 0.046      // backdrop colour: green
#define BACKDROP_B 0.070      // backdrop colour: blue
#define EXPOSURE 1.0          // overall brightness
#define SAMPLES 2             // anti-aliasing samples per pixel (1..4, each costs the same again)
#define TEXT_VEIL 0.5         // darkening behind terminal text

float hash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 turn(float2 v, float2 cs) { return float2(cs.x * v.x - cs.y * v.y, cs.y * v.x + cs.x * v.y); }

float3 keyDir() { return normalize(float3(-0.50, 0.78, 0.38)); }

// ---- four dimensions ------------------------------------------------------------------------------------
float depth4() { return max(DEPTH_4D, 2.25); }

// keeps the figure about the same size on screen whatever the 4D perspective is
float sizeScale() { float d = depth4(); return 0.52 * FRAME_SIZE * sqrt(d * d - 4.0) / d; }

float tubeSize() { return max(TUBE_SIZE, 0.002); }

float boundRadius() { return 1.05 * FRAME_SIZE + tubeSize() * 4.0; }

// one axis of the cube after turning it in four dimensions (k1..k4 = cosine and sine of the four angles)
float4 spin4(float4 c, float2 k1, float2 k2, float2 k3, float2 k4)
{
    c.xw = turn(c.xw, k1);
    c.yw = turn(c.yw, k2);
    c.zw = turn(c.zw, k3);
    c.xy = turn(c.xy, k4);
    c.xz = turn(c.xz, float2(0.9004, 0.4350));
    return c;
}

// perspective from four dimensions into three: xyz = where it ends up, w = how thick things are there
// (nearer in 4D = bigger and thicker, but the thickness is evened out a little so near edges do not get fat)
float4 project4(float4 p)
{
    float f = depth4() / (depth4() - p.w);
    return float4(p.xyz * (f * sizeScale()), min(f, 0.55 + 0.45 * f));
}

// does the ray come anywhere near the figure?
bool nearFrame(float3 ro, float3 rd)
{
    float b = dot(ro, rd);
    float rr = boundRadius();
    float disc = b * b - dot(ro, ro) + rr * rr;
    return disc > 0.0 && -b + sqrt(max(disc, 0.0)) > 0.0;
}

// The frame: 32 edges (tubes that get thinner towards the far end in 4D) and 16 corner balls, each cut
// exactly by the ray. Returns the distance of the nearest hit or -1; wv = the hit's 4th coordinate (-1..1).
float traceFrame(float3 ro, float3 rd, float4 c0, float4 c1, float4 c2, float4 c3, out float3 nrm, out float wv)
{
    nrm = float3(0.0, 1.0, 0.0);
    wv = 0.0;
    float best = 1000000.0;
    float4 e = c0, u = c1, v = c2, w = c3;
    if (nearFrame(ro, rd))                                        // nothing to do for rays that pass far away
    {
        [loop] for (int a = 0; a < 4; a++)
        {
            [loop] for (int j = 0; j < 8; j++)
            {
                float3 sg = step(0.5, frac(float(j) * float3(0.5, 0.25, 0.125))) * 2.0 - 1.0;
                float4 p4 = u * sg.x + v * sg.y + w * sg.z - e;
                float4 q4 = p4 + 2.0 * e;
                float4 pa = project4(p4), pb = project4(q4);
                float ra = tubeSize() * pa.w, rb = tubeSize() * pb.w;
                float3 ba = pb.xyz - pa.xyz;
                float3 oa = ro - pa.xyz;
                float rr = ra - rb;
                float m0 = dot(ba, ba), m1 = dot(ba, oa), m2 = dot(ba, rd), m3 = dot(rd, oa), m5 = dot(oa, oa);
                float d2 = m0 - rr * rr;
                float k2 = d2 - m2 * m2;
                float k1 = d2 * m3 - m1 * m2 + m2 * rr * ra;
                float k0 = d2 * m5 - m1 * m1 + m1 * rr * ra * 2.0 - m0 * ra * ra;
                float h = k1 * k1 - k0 * k2;
                if (h > 0.0 && k2 > 0.0)
                {
                    float tt = (-sqrt(h) - k1) / k2;
                    float y = m1 - ra * rr + tt * m2;
                    if (y > 0.0 && y < d2 && tt > 0.0 && tt < best)
                    {
                        float s = y / d2;
                        best = tt;
                        nrm = (oa + rd * tt - ba * s) / (ra - rr * s);
                        wv = lerp(p4.w, q4.w, s) * 0.5;
                    }
                }
                if (a == 0)                                           // these 8 edges end in all 16 corners
                {
                    float vr = max(VERTEX_SIZE, 1.0);
                    float hs = m3 * m3 - m5 + ra * ra * vr * vr;
                    if (hs > 0.0)
                    {
                        float ts = -m3 - sqrt(hs);
                        if (ts > 0.0 && ts < best) { best = ts; nrm = (oa + rd * ts) / (ra * vr); wv = p4.w * 0.5; }
                    }
                    float3 ob = ro - pb.xyz;
                    float m6 = dot(ob, rd);
                    hs = m6 * m6 - dot(ob, ob) + rb * rb * vr * vr;
                    if (hs > 0.0)
                    {
                        float ts = -m6 - sqrt(hs);
                        if (ts > 0.0 && ts < best) { best = ts; nrm = (ob + rd * ts) / (rb * vr); wv = q4.w * 0.5; }
                    }
                }
            }
            float4 tmp = e; e = u; u = v; v = w; w = tmp;
        }
    }
    return best < 100000.0 ? best : -1.0;
}

// Soft shadow of the frame: how close does the way to the light pass by each edge? 1 = fully lit.
float shadowFrame(float3 p, float3 l, float4 c0, float4 c1, float4 c2, float4 c3)
{
    float res = 1.0;
    float4 e = c0, u = c1, v = c2, w = c3;
    if (nearFrame(p, l))
    {
        [loop] for (int a = 0; a < 4; a++)
        {
            [loop] for (int j = 0; j < 8; j++)
            {
                float3 sg = step(0.5, frac(float(j) * float3(0.5, 0.25, 0.125))) * 2.0 - 1.0;
                float4 p4 = u * sg.x + v * sg.y + w * sg.z - e;
                float4 pa = project4(p4), pb = project4(p4 + 2.0 * e);
                float3 ba = pb.xyz - pa.xyz;
                float3 oa = p - pa.xyz;
                float baba = dot(ba, ba), bal = dot(ba, l), baoa = dot(ba, oa), loa = dot(l, oa);
                float s = saturate((baoa - bal * loa) / max(baba - bal * bal, 0.00001));
                float tt = max(bal * s - loa, 0.0);
                s = saturate((baoa + bal * tt) / baba);
                float dist = length(oa + l * tt - ba * s) - tubeSize() * lerp(pa.w, pb.w, s);
                if (tt > 0.05) res = min(res, 0.5 + 0.5 * dist / (tt * 0.10 + 0.004));
            }
            float4 tmp = e; e = u; u = v; v = w; w = tmp;
        }
    }
    res = saturate(res);
    return res * res * (3.0 - 2.0 * res);
}

// ---- lighting -------------------------------------------------------------------------------------------
// the studio around the figure, as seen in reflections; rough surfaces see a blurrier studio
float3 studio(float3 d, float rough)
{
    float soft = 0.03 + 0.45 * rough;
    float3 c = lerp(float3(0.020, 0.022, 0.030), float3(0.100, 0.110, 0.140), smoothstep(-0.4, 0.9, d.y));
    c += float3(0.30, 0.26, 0.22) * smoothstep(0.55, -0.10, abs(d.y - 0.12)) * (0.5 + 0.5 * d.x) * FILL_LIGHT;        // lit wall on one side
    float key = smoothstep(0.86 - soft, 0.95 + soft * 0.1, dot(d, keyDir()));
    c += float3(1.00, 0.95, 0.88) * key * 6.0 * KEY_LIGHT;                                           // big soft box
    float rim = smoothstep(0.90 - soft, 0.97, dot(d, normalize(float3(0.85, 0.22, -0.48))));
    c += float3(0.72, 0.84, 1.00) * rim * 3.2 * FILL_LIGHT;                                           // cool rim light
    float tall = smoothstep(-0.25, 0.15, d.y) * smoothstep(0.95, 0.55, d.y);
    float strip = smoothstep(0.10 + soft, 0.0, abs(d.x * 0.80 + d.z * 0.60 + 0.25)) * tall;
    c += float3(1.00, 0.98, 0.95) * strip * 1.6 * FILL_LIGHT;                                         // tall strip lights
    float strip2 = smoothstep(0.07 + soft, 0.0, abs(d.x * 0.45 - d.z * 0.89 - 0.35)) * tall;
    c += float3(0.80, 0.90, 1.00) * strip2 * 1.2 * FILL_LIGHT;
    float top = smoothstep(0.93 - soft, 0.99, d.y);
    c += float3(1.00, 0.97, 0.92) * top * 2.5 * KEY_LIGHT;                                            // light from above
    float fill = smoothstep(0.25, 1.0, dot(d, normalize(float3(-0.70, -0.15, -0.65))));
    c += float3(0.16, 0.26, 0.48) * fill * 0.9 * FILL_LIGHT;
    c += float3(W_TINT_R, W_TINT_G, W_TINT_B) * smoothstep(0.2, -1.0, d.y) * 0.06 * W_GLOW;           // bounce from the floor
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

float3 shadeFrame(float3 n, float3 rd, float wv, float sh)
{
    float3 v = -rd;
    float3 f0 = float3(METAL_R, METAL_G, METAL_B);
    float rough = clamp(ROUGHNESS, 0.05, 1.0);
    float a = rough * rough;
    float nov = saturate(dot(n, v)) + 0.0001;
    float3 l = keyDir();
    float3 hv = normalize(l + v);
    float nol = saturate(dot(n, l));
    float3 f = f0 + (float3(1.0, 1.0, 1.0) - f0) * pow(1.0 - saturate(dot(v, hv)), 5.0);
    float3 col = f * dGGX(saturate(dot(n, hv)), a) * vSmith(nov, nol, a) * nol * float3(1.00, 0.95, 0.88) * 3.2 * KEY_LIGHT * sh;
    col += studio(reflect(rd, n), rough) * envBrdf(f0, rough, nov) * (0.55 + 0.45 * sh);
    // parts that are far away in the fourth dimension glow, as if still hot
    float far4 = saturate(0.5 - 0.5 * wv * 1.25);
    far4 = far4 * far4 * far4;
    col *= 1.0 - 0.30 * far4 * saturate(W_GLOW);
    col += float3(W_TINT_R, W_TINT_G, W_TINT_B) * W_GLOW * far4 * (0.12 + 1.4 * nov * nov * nov);
    return col;
}

float3 backdrop(float3 rd, float3 fw)
{
    float3 bg = float3(BACKDROP_R, BACKDROP_G, BACKDROP_B);
    float3 c = bg * (0.34 + 0.40 * smoothstep(-0.35, 0.08, rd.y) - 0.42 * smoothstep(0.08, 0.50, rd.y));
    c += bg * 0.55 * exp(-14.0 * (rd.y - 0.02) * (rd.y - 0.02));  // soft band of light where the wall meets the floor
    float3 hd = normalize(float3(fw.x, 0.10, fw.z));
    c += bg * 1.25 * pow(saturate(dot(rd, hd)), 40.0);            // glow on the wall behind the figure
    return c;
}

// Faint glass panes in the 24 square faces: each adds the sheen of the studio lights it mirrors.
// Only panes in front of distance tmax count (the rest is hidden behind the frame).
float3 glassFaces(float3 ro, float3 rd, float tmax, float4 c0, float4 c1, float4 c2, float4 c3)
{
    float3 acc = float3(0.0, 0.0, 0.0);
    float4 e = c0, u = c1, v = c2, w = c3;
    if (nearFrame(ro, rd))
    {
        [loop] for (int a = 0; a < 6; a++)                       // the 6 pairs of directions a face can lie in
        {
            [loop] for (int j = 0; j < 4; j++)
            {
                float2 sg = step(0.5, frac(float(j) * float2(0.5, 0.25))) * 2.0 - 1.0;
                float4 p4 = v * sg.x + w * sg.y - e - u;
                float4 p4b = p4 + 2.0 * e, p4c = p4b + 2.0 * u, p4d = p4 + 2.0 * u;
                float dd = depth4(), ks = dd * sizeScale();
                float3 qa = p4.xyz * (ks / (dd - p4.w));
                float3 qb = p4b.xyz * (ks / (dd - p4b.w));
                float3 qc = p4c.xyz * (ks / (dd - p4c.w));
                float3 qd = p4d.xyz * (ks / (dd - p4d.w));
                float3 n = cross(qb - qa, qd - qa);
                float dn = dot(rd, n);
                float tt = dot(qa - ro, n) / (abs(dn) > 0.0000001 ? dn : 0.0000001);
                if (tt > 0.0 && tt < tmax)
                {
                    float3 hp = ro + rd * tt;
                    float inside = min(min(dot(cross(qb - qa, hp - qa), n), dot(cross(qc - qb, hp - qb), n)),
                                       min(dot(cross(qd - qc, hp - qc), n), dot(cross(qa - qd, hp - qd), n)));
                    if (inside > 0.0)
                    {
                        float3 nn = normalize(n) * (dn > 0.0 ? -1.0 : 1.0);
                        float ci = saturate(-dot(rd, nn));
                        float fres = 0.05 + 0.95 * pow(saturate(1.0 - ci), 5.0);
                        acc += studio(reflect(rd, nn), 0.10) * fres * 0.55 + float3(METAL_R, METAL_G, METAL_B) * 0.004;
                    }
                }
            }
            float4 tmp = e;
            if (a < 3) { e = u; u = v; v = w; w = tmp; }
            else if (a == 3) { e = c0; u = c2; v = c1; w = c3; }
            else { e = c1; u = c3; v = c0; w = c2; }
        }
    }
    return acc * GLASS_FACES;
}

float3 render(float3 ro, float3 rd, float3 fw, float4 c0, float4 c1, float4 c2, float4 c3)
{
    float floorY = -1.08 * FRAME_SIZE;
    float3 col = backdrop(rd, fw);
    float3 kd = keyDir();

    float3 n;
    float wv;
    float th = traceFrame(ro, rd, c0, c1, c2, c3, n, wv);
    if (th > 0.0)
    {
        float3 p = ro + rd * th;
        float sh = 1.0;
        if (SHADOWS > 0.001) sh = lerp(1.0, shadowFrame(p + n * 0.002, kd, c0, c1, c2, c3), saturate(SHADOWS));
        col = shadeFrame(n, rd, wv, sh);
    }
    else if (rd.y < -0.001)
    {
        // polished floor: shadow of the frame, and its reflection
        float tf = (floorY - ro.y) / rd.y;
        float3 pf = ro + rd * tf;
        float shadow = 1.0;
        if (SHADOWS > 0.001) shadow = lerp(1.0, 0.25 + 0.75 * shadowFrame(pf, kd, c0, c1, c2, c3), saturate(SHADOWS));
        float contact = 1.0 - 0.40 * saturate(SHADOWS) * exp(-dot(pf.xz, pf.xz) / (FRAME_SIZE * FRAME_SIZE * 1.4));

        float3 floorC = float3(0.030, 0.032, 0.038);
        float3 fcol = floorC * (0.15 + 1.1 * saturate(kd.y) * KEY_LIGHT * shadow) * contact;
        float3 tintC = float3(W_TINT_R, W_TINT_G, W_TINT_B);
        fcol += (floorC * 5.0 + float3(0.01, 0.01, 0.01)) * tintC * W_GLOW * 0.5 / (1.0 + dot(pf.xz, pf.xz) * 1.2);

        float3 rr = float3(rd.x, -rd.y, rd.z);
        float fres = 0.07 + 0.93 * pow(1.0 - saturate(-rd.y), 5.0);
        float3 n2 = float3(0.0, 1.0, 0.0);
        float w2 = 0.0;
        float tr = -1.0;
        if (FLOOR_REFLECTION > 0.001) tr = traceFrame(pf, rr, c0, c1, c2, c3, n2, w2);
        float3 refl = backdrop(rr, fw) * 0.6;
        if (tr > 0.0) refl = shadeFrame(n2, rr, w2, 1.0);
        fcol += refl * fres * 1.9 * FLOOR_REFLECTION * contact;

        float fog = 1.0 - exp(-tf * tf * 0.006);
        col = lerp(fcol, col, saturate(fog));
    }
    if (GLASS_FACES > 0.001) col += glassFaces(ro, rd, th > 0.0 ? th : 1000000.0, c0, c1, c2, c3);
    return col;
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float t = Time;
    float ang = t * CAMERA_ORBIT + 0.6;
    float3 ro = float3(sin(ang) * CAMERA_DISTANCE, CAMERA_HEIGHT, cos(ang) * CAMERA_DISTANCE);
    float3 fw = normalize(float3(0.0, -0.48 * FRAME_SIZE, 0.0) - ro);
    float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
    float3 upv = cross(rt, fw);

    // the four axes of the cube, turned in 4D
    float2 k1 = float2(cos(t * SPIN_XW + 0.35), sin(t * SPIN_XW + 0.35));
    float2 k2 = float2(cos(t * SPIN_YW + 0.20), sin(t * SPIN_YW + 0.20));
    float2 k3 = float2(cos(t * SPIN_ZW), sin(t * SPIN_ZW));
    float2 k4 = float2(cos(t * SPIN_XY + 0.30), sin(t * SPIN_XY + 0.30));
    float4 c0 = spin4(float4(1.0, 0.0, 0.0, 0.0), k1, k2, k3, k4);
    float4 c1 = spin4(float4(0.0, 1.0, 0.0, 0.0), k1, k2, k3, k4);
    float4 c2 = spin4(float4(0.0, 0.0, 1.0, 0.0), k1, k2, k3, k4);
    float4 c3 = spin4(float4(0.0, 0.0, 0.0, 1.0), k1, k2, k3, k4);

    float3 col = float3(0.0, 0.0, 0.0);
    for (int sIdx = 0; sIdx < SAMPLES; sIdx++)
    {
        float2 off = (float2(frac(float(sIdx) * 0.5 + 0.25), frac(float(sIdx) * 0.75 + 0.125)) - 0.5) * step(1.5, float(SAMPLES));
        float2 uv = float2((tex.x - 0.5) * aspect, 0.5 - tex.y) + off / Resolution.y;
        float3 rd = normalize(fw * 1.9 + rt * uv.x + upv * uv.y);
        col += render(ro, rd, fw, c0, c1, c2, c3);
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
