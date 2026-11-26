// @name: Wind
// @tags: meditative, calming, nature
// Wind over a meadow: gusts roll through the tall grass as waves of light, from the horizon to your feet.
// Clouds drift the same way, and dandelion seeds are carried past.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define WIND_SPEED 1.0        // how fast the gusts travel
#define WIND_DIRECTION 1      // 1 = left to right, 0 = right to left
#define GUSTS 1.0             // how strongly the gusts bend the grass
#define GUST_SIZE 1.0         // size of the waves running through the field
#define SHIVER 1.0            // fine trembling of the blades
#define GRASS_HEIGHT 1.0      // height of the grass
#define GRASS_DENSITY 1.0     // blades per row
#define FIELD_LAYERS 16       // rows of grass from horizon to foreground (6..22)
#define HORIZON 0.46          // height of the horizon (0 = bottom, 1 = top)
#define HILLS 1.0             // distant hills (0 = flat horizon)
#define CLOUDS 1.0            // amount of cloud
#define CLOUD_SPEED 1.0       // how fast the clouds drift
#define SEEDS 1.0             // dandelion seeds in the air (0 = none, 2 = many)
#define SEED_SIZE 1.0         // size of the seeds
#define AIR_STREAKS 0.3       // faint lines of moving air (0 = none)
#define SUN_GLOW 0.8          // warm light in the sky
#define HAZE 1.0              // how much the distance fades into the sky
#define SKY_TOP_R 0.22        // sky top: red
#define SKY_TOP_G 0.42        // sky top: green
#define SKY_TOP_B 0.66        // sky top: blue
#define SKY_LOW_R 0.96        // sky at the horizon: red
#define SKY_LOW_G 0.84        // sky at the horizon: green
#define SKY_LOW_B 0.68        // sky at the horizon: blue
#define GRASS_R 0.19          // grass colour: red
#define GRASS_G 0.43          // grass colour: green
#define GRASS_B 0.13          // grass colour: blue
#define SHEEN_R 0.74          // colour of the bent, sunlit grass: red
#define SHEEN_G 0.87          // colour of the bent, sunlit grass: green
#define SHEEN_B 0.50          // colour of the bent, sunlit grass: blue
#define BRIGHTNESS 1.0        // overall brightness
#define TEXT_VEIL 0.5         // darkening behind terminal text

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

float fbm(float2 p)
{
    float a = 0.5, s = 0.0;
    for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.03 + float2(17.3, 9.1); a *= 0.5; }
    return s;
}

// strength of the wind at a place in the field (wx = across, wz = distance), 0 = lull, 1 = full gust
float gustAt(float wx, float wz, float t, float dir)
{
    float k = 1.0 / max(GUST_SIZE, 0.2);
    float travel = dir * t * 0.30 * WIND_SPEED;
    float broad = vnoise(float2((wx - travel) * 1.5 * k, wz * 1.0 * k + t * 0.02));
    float fine = vnoise(float2((wx - travel * 1.5) * 4.3 * k, wz * 2.8 * k - t * 0.05));
    return smoothstep(0.34, 0.72, broad * 0.75 + fine * 0.25);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float up = 1.0 - tex.y;
    float2 p = float2(tex.x * aspect, up);
    float t = Time;
    float dir = WIND_DIRECTION > 0 ? 1.0 : -1.0;
    float px = 1.0 / Resolution.y;
    float3 skyTop = float3(SKY_TOP_R, SKY_TOP_G, SKY_TOP_B);
    float3 skyLow = float3(SKY_LOW_R, SKY_LOW_G, SKY_LOW_B);
    float3 grassC = float3(GRASS_R, GRASS_G, GRASS_B);
    float3 sheenC = float3(SHEEN_R, SHEEN_G, SHEEN_B);

    // ---- sky
    float skyT = saturate((up - HORIZON) / max(1.0 - HORIZON, 0.05));
    float3 col = lerp(skyLow, skyTop, pow(skyT, 0.75));
    float2 sunP = float2(aspect * (dir > 0.0 ? 0.70 : 0.30), HORIZON + 0.13);
    float sd = length(p - sunP);
    col += float3(1.0, 0.82, 0.55) * (exp(-sd * 3.2) * 0.34 + exp(-sd * 14.0) * 0.36) * SUN_GLOW;

    // ---- clouds: soft, stretched, drifting with the wind
    float2 cp = float2(p.x * 0.9 - dir * t * 0.022 * CLOUD_SPEED * WIND_SPEED, (up - HORIZON) * 3.2);
    float cd = fbm(cp + float2(0.0, 0.25 * fbm(cp * 2.0 + t * 0.01)));
    float cm = smoothstep(0.62 - 0.22 * CLOUDS, 0.86 - 0.14 * CLOUDS, cd) * smoothstep(0.02, 0.22, skyT) * saturate(CLOUDS);
    float lit = saturate(0.55 + 1.8 * (cd - fbm(cp + float2(0.06 * dir, 0.05))));
    float3 cloudC = lerp(float3(0.78, 0.76, 0.80), float3(1.0, 0.97, 0.90), lit) + float3(0.10, 0.05, 0.0) * exp(-sd * 2.0) * SUN_GLOW;
    col = lerp(col, cloudC, cm * 0.85);

    // ---- faint lines of moving air, only where a gust is passing
    if (AIR_STREAKS > 0.0)
    {
        float psi = up + 0.030 * sin(p.x * 1.7 + t * 0.20) + 0.014 * sin(p.x * 3.9 - t * 0.15 + up * 4.0);
        float v = psi * 16.0;
        float lane = floor(v);
        float h = hash11(lane * 1.31 + 4.0);
        float u = (p.x * dir - t * (0.35 + 0.25 * h) * WIND_SPEED) * (0.55 + 0.35 * h) + h * 37.0;
        float on = step(0.80, hash12(float2(floor(u), lane)));
        float uf = frac(u);
        float body = smoothstep(0.0, 0.75, uf) * (1.0 - smoothstep(0.80, 1.0, uf));
        float thin = 1.0 - smoothstep(0.02, 0.16, abs(frac(v) - 0.5));
        col += float3(1.0, 1.0, 1.0) * body * thin * on * 0.16 * AIR_STREAKS * smoothstep(0.0, 0.15, skyT);
    }

    // ---- distant hills, fading into the haze towards their feet
    float3 hazeC = lerp(skyLow, float3(0.52, 0.66, 0.50), 0.45);
    if (HILLS > 0.0)
    {
        float h2 = HORIZON + (0.015 + 0.150 * fbm(float2(p.x * 1.9 + 3.0, 1.0))) * HILLS;
        float h1 = HORIZON + (0.004 + 0.085 * fbm(float2(p.x * 3.1 + 11.0, 2.0))) * HILLS;
        float3 c2 = lerp(float3(0.34, 0.46, 0.62), skyLow, 0.35 + 0.35 * smoothstep(h2, HORIZON, up));
        float3 c1 = lerp(float3(0.27, 0.45, 0.36), hazeC, 0.25 + 0.45 * smoothstep(h1, HORIZON, up));
        col = lerp(col, c2, smoothstep(px, -px, up - h2));
        col = lerp(col, c1, smoothstep(px, -px, up - h1));
    }

    // ---- the field: rows of blades from the horizon to the foreground, each bent by the gust at its place
    float wxs = p.x - aspect * 0.5;
    for (int i = 0; i < FIELD_LAYERS; i++)
    {
        float d = (float(i) + 0.5) / float(FIELD_LAYERS);            // 0 = far, 1 = near
        float s = lerp(0.05, 1.06, pow(d, 1.8));                     // perspective scale of this row
        float wx = wxs / s, wz = 1.0 / s;
        float baseY = HORIZON * (1.0 - s) + 0.010 * s * sin(wx * 0.8 + float(i) * 1.7);
        float hgt = 0.17 * s * GRASS_HEIGHT;
        float yl = (up - baseY) / hgt;                               // 0 at the roots, 1 at the tallest tips
        if (yl > 1.05) continue;

        float g = gustAt(wx, wz, t, dir) * GUSTS;
        float cw = 0.011 * s / max(GRASS_DENSITY, 0.2);              // width of one blade's cell
        float bend = saturate(yl);
        float lean = dir * (0.03 + 0.60 * g) * hgt * bend * bend;    // blades curve over, more at the tip
        lean += 0.030 * hgt * SHIVER * (0.25 + g) * sin(t * (4.5 + 2.0 * hash11(float(i))) * WIND_SPEED + wx * 9.0 + float(i)) * bend * bend;
        float gx = (wxs - lean) / cw + float(i) * 19.7;
        float aa = px / cw;

        float cov = 0.0, tipness = 0.0, vary = 0.0, side = 0.0;
        for (int j = -2; j <= 2; j++)
        {
            float id = floor(gx) + float(j);
            float r1 = hash11(id * 1.73 + float(i) * 5.1), r2 = hash11(id * 3.11 + float(i) * 2.3 + 7.0);
            float bh = 0.35 + 0.65 * r1 * r1;                       // most blades are short, a few stand tall
            float yr = yl / bh;
            float xc = id + 0.5 + (r2 - 0.5) * 0.9 + (frac(r1 * 7.3) - 0.5) * 1.7 * yr * yr;   // each blade has its own tilt
            float w = 0.05 + 0.24 * (1.0 - pow(saturate(yr), 1.3)); // thin, tapering to a point
            float c = (1.0 - smoothstep(w - aa, w + aa, abs(gx - xc))) * step(yr, 1.0);
            if (c > cov) { cov = c; tipness = saturate(yr); vary = r2; side = (gx - xc) / w; }
        }
        float fill = smoothstep(0.30, 0.0, yl);                      // dense undergrowth at the roots
        float m = max(cov, fill);
        if (m <= 0.0) continue;

        float3 gc = grassC * (0.42 + 0.40 * vary) * lerp(0.52, 1.30, tipness * (1.0 - fill));
        gc *= 1.0 + 0.22 * side * dir;                               // one edge of each blade catches the sun
        gc = lerp(gc, sheenC * (0.80 + 0.25 * vary), saturate(g * (0.20 + 0.62 * tipness)));   // bent grass turns its pale side up
        gc *= 0.78 + 0.26 * d;
        gc = lerp(gc, hazeC, (1.0 - d) * (1.0 - d) * 0.80 * HAZE);  // distance fades into haze
        gc += float3(1.0, 0.80, 0.50) * exp(-abs(p.x - sunP.x) * 1.6) * 0.10 * SUN_GLOW * (1.0 - d);
        col = lerp(col, gc, m);
    }

    // ---- dandelion seeds carried by the wind
    for (int k = 0; k < 14; k++)
    {
        float fk = float(k);
        float h1 = hash11(fk * 3.17 + 1.0), h2 = hash11(fk * 7.31 + 2.0), h3 = hash11(fk * 11.9 + 3.0);
        float show = step(h3, SEEDS * 0.5);
        float sz = (0.009 + 0.012 * h2) * SEED_SIZE;
        float sp = (0.045 + 0.070 * h1) * WIND_SPEED;
        float span = aspect + 0.3;
        float sx = frac(h3 * 7.0 + dir * t * sp / span) * span - 0.15;
        float sy = 0.20 + 0.66 * h1 + 0.050 * sin(t * 0.45 + fk * 2.0) + 0.018 * sin(t * 1.25 + fk * 5.0);
        float2 q = p - float2(sx, sy);
        float tilt = -dir * (0.45 + 0.25 * sin(t * 0.9 + fk));       // the stem trails behind
        float2 ax = float2(sin(tilt), -cos(tilt));                   // direction of the stem
        float r = length(q);
        float2 qn = q / max(r, 1e-5);
        float fan = smoothstep(0.10, 0.55, -dot(qn, ax));                           // parachute of fine hairs, opposite the stem
        float hairs = 0.40 + 0.60 * pow(abs(cos(atan2(qn.y, qn.x) * 8.0 + fk)), 3.0);
        float puff = fan * hairs * (1.0 - smoothstep(sz * 0.55, sz * 1.25, r)) * 0.62;
        float al = clamp(dot(q, ax), 0.0, sz * 2.0);
        float stem = (1.0 - smoothstep(0.0, px * 1.4 + sz * 0.05, length(q - ax * al))) * 0.55;
        float seedDot = 1.0 - smoothstep(sz * 0.14, sz * 0.22, length(q - ax * sz * 2.0));
        col = lerp(col, float3(1.0, 0.99, 0.94), saturate(puff + stem * 0.5) * show);
        col = lerp(col, float3(0.42, 0.33, 0.22), seedDot * show);
    }

    col *= BRIGHTNESS;
    col = col * col * (3.0 - 2.0 * col) * 0.25 + col * 0.75;
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.35;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover2 = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover2;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
