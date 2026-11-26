// @name: Ocean at Dusk
// @tags: water, nature, featured
// A living sea at sunset: real 3D swell (ray-marched height field) with sharp crests, foam that catches the
// last light, turquoise glow where the sun shines through the wave tops, a burning sky with lit clouds,
// a sailboat on the horizon, a lighthouse on a far island and gulls drifting past.

Texture2D shaderTexture;
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;
    float  Scale;
    float2 Resolution;
    float4 Background;
};

#define WAVE_HEIGHT 1.0       // swell size
#define WAVE_SPEED 1.0        // how fast the sea moves
#define CHOPPINESS 1.0        // sharpness of the small waves
#define FOAM 1.0              // foam on the crests
#define WATER_GLOW 1.0        // turquoise light shining through the wave tops
#define GLITTER 1.0           // sun glitter path
#define SUN_HEIGHT 0.07       // sun elevation (0 = on the horizon)
#define SUN_X 0.22            // sun direction left/right (-1..1)
#define HORIZON 0.40          // horizon position from top (0..1)
#define CAMERA_HEIGHT 2.6     // eye height above the water
#define DRIFT_SPEED 1.0       // forward drift over the water
#define CLOUDS 0.6            // cloud cover
#define CLOUD_SPEED 1.0       // cloud drift
#define STARS 0.6             // first stars in the upper sky
#define SAILBOAT 1            // 1 = a sailboat crosses the horizon
#define LIGHTHOUSE 1          // 1 = island with a blinking lighthouse
#define GULLS 5               // number of gulls (0..8)
#define SUN_R 1.00            // sun and glow colour: red
#define SUN_G 0.55            // sun and glow colour: green
#define SUN_B 0.22            // sun and glow colour: blue
#define HORIZON_R 1.00        // sky at the horizon: red
#define HORIZON_G 0.42        // sky at the horizon: green
#define HORIZON_B 0.16        // sky at the horizon: blue
#define MID_R 0.42            // sky above the horizon: red
#define MID_G 0.14            // sky above the horizon: green
#define MID_B 0.34            // sky above the horizon: blue
#define ZENITH_R 0.03         // sky overhead: red
#define ZENITH_G 0.05         // sky overhead: green
#define ZENITH_B 0.17         // sky overhead: blue
#define WATER_R 0.01          // deep water colour: red
#define WATER_G 0.05          // deep water colour: green
#define WATER_B 0.09          // deep water colour: blue
#define HAZE 1.0              // haze towards the horizon
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
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { s += a * vnoise(p); p = float2(p.x * 1.6 - p.y * 1.2, p.x * 1.2 + p.y * 1.6); a *= 0.5; }
    return s;
}

// swell only (cheap): used to find where the view ray meets the water
float seaLow(float2 p, float t)
{
    float h = 0.0;
    float freq = 0.42, amp = 0.50 * WAVE_HEIGHT, speed = 1.0 * WAVE_SPEED, ang = 0.35;
    for (int i = 0; i < 5; i++)
    {
        float2 dir = float2(cos(ang), sin(ang));
        float x = dot(dir, p) * freq + t * speed;
        float e = exp(sin(x) - 1.0);
        h += amp * e;
        p -= dir * (amp * e * cos(x)) * 0.22;
        freq *= 1.32; amp *= 0.70; speed *= 1.09; ang += 2.39996;
    }
    return h;
}

// full detail: height (x) and slope (yz); small waves fade with distance instead of shimmering
float3 seaHigh(float2 p, float t, float dist)
{
    float h = 0.0;
    float2 grad = float2(0.0, 0.0);
    float freq = 0.42, amp = 0.50 * WAVE_HEIGHT, speed = 1.0 * WAVE_SPEED, ang = 0.35;
    for (int i = 0; i < 13; i++)
    {
        float2 dir = float2(cos(ang), sin(ang));
        float x = dot(dir, p) * freq + t * speed;
        float e = exp(sin(x) - 1.0);
        float keep = saturate(1.3 - dist * freq * 0.012);
        float chop = i < 5 ? 1.0 : CHOPPINESS;
        h += amp * e * keep;
        grad += dir * (amp * freq * e * cos(x) * keep * chop);
        p -= dir * (amp * e * cos(x)) * 0.22;
        freq *= 1.32; amp *= 0.72; speed *= 1.09; ang += 2.39996;
    }
    return float3(h, grad);
}

float3 skyColor(float3 rd, float3 sunDir, float t, float detail)
{
    float y = max(rd.y, 0.0);
    float3 zen = float3(ZENITH_R, ZENITH_G, ZENITH_B);
    float3 mid = float3(MID_R, MID_G, MID_B);
    float3 hor = float3(HORIZON_R, HORIZON_G, HORIZON_B);
    float3 sunC = float3(SUN_R, SUN_G, SUN_B);
    float sd = saturate(dot(rd, sunDir));
    float3 col = lerp(hor, mid, smoothstep(0.0, 0.13, y));
    col = lerp(col, zen, smoothstep(0.07, 0.42, y));
    col += sunC * (pow(sd, 10.0) * 0.22 + pow(sd, 60.0) * 0.7);
    col += lerp(sunC, float3(1.0, 1.0, 1.0), 0.5) * smoothstep(0.99925, 0.99965, sd) * 4.0;    // sun disc
    if (detail > 0.5)
    {
        // first stars high up
        float2 sp = rd.xy / (rd.z + 1.0) * 160.0;
        float2 sid = floor(sp);
        float sh = hash12(sid);
        col += (1.0 - smoothstep(0.0, 0.12, length(frac(sp) - 0.5))) * step(0.985, sh) * smoothstep(0.25, 0.6, y) * STARS * (0.6 + 0.4 * sin(t * (1.0 + sh * 3.0) + sh * 40.0));
    }
    if (rd.y > 0.004)
    {
        // clouds on a high plane, lit from the sun side: bright burning undersides, dusky tops
        float2 cp = rd.xz / (rd.y + 0.10) * 0.42 + float2(t * 0.006 * CLOUD_SPEED, 0.0);
        float lo = 1.0 - CLOUDS * 0.8;
        float d = smoothstep(lo, lo + 0.38, fbm(cp * 1.25) + 0.22);
        float2 toSun = normalize(sunDir.xz - rd.xz * 0.6 + float2(1e-4, 0.0));
        float d2 = smoothstep(lo, lo + 0.38, fbm((cp + toSun * 0.16) * 1.25) + 0.22);
        float rim = saturate(0.5 + (d - d2) * 1.8);
        float3 dusk = lerp(mid, zen, 0.6) * 0.45 + float3(0.02, 0.012, 0.03);
        float3 fire = lerp(float3(1.0, 0.45, 0.35), sunC * 1.5, pow(sd, 2.0)) * (0.35 + 1.4 * pow(sd, 3.0));
        float3 cloud = lerp(dusk, fire, rim * (0.25 + 0.75 * pow(sd, 2.5)));
        col = lerp(col, cloud, d * smoothstep(0.0, 0.10, rd.y) * 0.92);
    }
    return col;
}

// sailboat silhouette; p = position relative to the waterline under the mast, s = size; returns coverage
float sailboat(float2 p, float s)
{
    float2 q = p / s;
    float hull = step(0.0, q.y) * step(q.y, 0.16) * step(abs(q.x + 0.05), 0.48 + q.y * 0.9);
    float mast = step(abs(q.x), 0.012) * step(0.16, q.y) * step(q.y, 1.22);
    float mainSail = step(0.035, q.x) * step(0.24, q.y) * step(q.y, 1.18 - (q.x - 0.035) * 1.75);
    float jib = step(q.x, -0.035) * step(0.24, q.y) * step(q.y, 1.05 + q.x * 1.9);
    return saturate(hull + mast + mainSail + jib);
}

float gull(float2 p, float s, float flap)
{
    float2 q = p / s;
    float wing = abs(q.x) * (0.25 + 0.55 * flap) - q.x * q.x * 0.35;
    return (1.0 - smoothstep(0.035, 0.085, abs(q.y - wing))) * step(abs(q.x), 1.0);
}

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
{
    float aspect = Resolution.x / Resolution.y;
    float2 uv = float2((tex.x - 0.5) * aspect, HORIZON - tex.y);           // y up, horizon at 0
    float t = Time;
    float3 sunC = float3(SUN_R, SUN_G, SUN_B);
    float3 hor = float3(HORIZON_R, HORIZON_G, HORIZON_B);

    float3 ro = float3(0.0, CAMERA_HEIGHT + 1.0 * WAVE_HEIGHT, t * 1.1 * DRIFT_SPEED);
    float3 rd = normalize(float3(uv.x, uv.y, 1.5));
    float3 sunDir = normalize(float3(SUN_X, SUN_HEIGHT, 1.0));
    float3 col;

    // things that live on the horizon (screen space, in units of screen height)
    float boatX = (frac(t * 0.0035 + 0.62) * 1.5 - 0.75) * aspect;
    float boatS = 0.045;
    float2 boatP = float2(uv.x - boatX, uv.y + 0.004 - 0.0025 * sin(t * 0.9));
    float isleX = -0.34 * aspect;
    float isleH = 0.022 * exp(-pow((uv.x - isleX) / 0.11, 2.0) * 2.0) * (0.7 + 0.6 * vnoise(float2(uv.x * 60.0, 3.0)));
    float2 lampP = float2(isleX + 0.012, 0.062);
    float blink = pow(saturate(sin(t * 0.9) * 0.5 + 0.5), 12.0);

    if (rd.y < -0.0012)
    {
        // march the swell height field (sphere-tracing style with a slope guard)
        float tt = (ro.y - 2.2 * WAVE_HEIGHT) / (-rd.y);
        tt = max(tt, 0.0);
        float tmax = ro.y / (-rd.y);
        for (int i = 0; i < 22; i++)
        {
            float3 pm = ro + rd * tt;
            float hgt = pm.y - seaLow(pm.xz, t);
            if (hgt < 0.004 * tt) break;
            tt = min(tt + hgt / max(-rd.y, 0.32) * 0.72, tmax);
        }
        float3 p = ro + rd * tt;
        float3 w = seaHigh(p.xz, t, tt);
        float2 rip = float2(vnoise(p.xz * 5.0 + float2(t * 0.9, 0.0)), vnoise(p.xz * 5.0 + float2(7.0, -t * 0.8))) - 0.5;
        rip += 0.4 * (float2(vnoise(p.xz * 11.0 - t * 1.3), vnoise(p.xz * 11.0 + 31.0 + t * 1.1)) - 0.5) / (1.0 + tt * 0.15);
        float3 n = normalize(float3(-w.y - rip.x * 0.16 * CHOPPINESS / (1.0 + tt * 0.10), 1.0, -w.z - rip.y * 0.16 * CHOPPINESS / (1.0 + tt * 0.10)));
        n = normalize(lerp(n, float3(0.0, 1.0, 0.0), saturate(tt * 0.0018)));

        float hn = saturate((w.x / max(WAVE_HEIGHT, 0.05) - 0.42) / 0.62);    // 0 trough .. 1 crest
        float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, -rd)), 5.0);
        float3 refl = reflect(rd, n);
        refl.y = abs(refl.y) + 0.01;
        float3 deep = float3(WATER_R, WATER_G, WATER_B);
        float towardSun = pow(saturate(dot(normalize(rd.xz), normalize(sunDir.xz))), 3.0);
        // light through the crests: turquoise, warmer towards the sun
        float3 sss = lerp(float3(0.02, 0.30, 0.30), float3(0.30, 0.45, 0.25), towardSun) * pow(hn, 2.0) * (0.35 + 1.3 * towardSun) * WATER_GLOW;
        col = deep * (0.5 + 0.7 * saturate(dot(n, sunDir) + 0.4)) + sss;
        col = lerp(col, skyColor(normalize(refl), sunDir, t, 0.0) * 0.9, fres);

        // sun glitter: sharp sparkles plus a broad sheen
        float sr = saturate(dot(normalize(refl), sunDir));
        col += sunC * (pow(sr, 900.0) * 55.0 + pow(sr, 120.0) * 1.2 + pow(sr, 20.0) * 0.12) * GLITTER;

        // foam: on steep, high crests; broken up by noise, lit by the sky
        float steep = saturate((1.0 - n.y) * 6.0);
        float foamTex = fbm(p.xz * 2.6 + float2(t * 0.25, 0.0)) * 0.6 + vnoise(p.xz * 11.0 - t * 0.5) * 0.4;
        float foam = smoothstep(0.80, 1.0, hn + steep * 0.12) * smoothstep(0.42, 0.70, foamTex);
        foam += smoothstep(0.60, 0.85, foamTex) * smoothstep(0.55, 0.85, hn) * 0.18;      // lace trailing behind
        float3 foamCol = (float3(0.55, 0.50, 0.58) + hor * 0.35 + sunC * towardSun * 0.7) * (0.55 + 0.45 * n.y);
        col = lerp(col, foamCol, saturate(foam * FOAM) / (1.0 + tt * 0.02));

        // reflections of the boat and of the lighthouse lamp, smeared by the waves
        float2 mir = float2(uv.x + n.x * 0.05, -uv.y * 0.75 + n.z * 0.02);
        if (SAILBOAT > 0) col *= 1.0 - 0.65 * sailboat(float2(mir.x - boatX, mir.y), boatS) * exp(uv.y * 30.0);
        if (LIGHTHOUSE > 0) col += float3(1.0, 0.85, 0.5) * blink * exp(-abs(uv.x + n.x * 0.08 - lampP.x) * 90.0) * exp(uv.y * 9.0) * 0.6;

        // haze towards the horizon
        float3 hz = skyColor(normalize(float3(rd.x, 0.004, rd.z)), sunDir, t, 0.0);
        col = lerp(col, hz, saturate(1.0 - exp(-tt * 0.0045 * HAZE)));
    }
    else
    {
        col = skyColor(rd, sunDir, t, 1.0);
        float3 ink = float3(0.035, 0.02, 0.04);

        if (LIGHTHOUSE > 0)
        {
            float isle = step(uv.y, isleH) * step(0.0015, isleH);
            float tower = step(abs(uv.x - lampP.x), 0.0035) * step(uv.y, lampP.y) * step(0.0, uv.y);
            col = lerp(col, ink, saturate(isle + tower) * 0.95);
            float ld = length(uv - lampP);
            col += float3(1.0, 0.88, 0.55) * blink * (exp(-ld * 260.0) * 2.5 + exp(-ld * 28.0) * 0.35);
        }
        if (SAILBOAT > 0)
        {
            float boat = sailboat(boatP, boatS);
            float lit = saturate((boatP.x * sign(SUN_X * aspect * 0.5 - boatX)) / boatS * 1.5);   // sail edge facing the sun
            col = lerp(col, ink + sunC * 0.10 * lit, boat * 0.96);
        }
        for (int g = 0; g < GULLS; g++)
        {
            float fg2 = float(g);
            float gx = (frac(t * (0.006 + 0.004 * hash11(fg2 * 3.1)) + hash11(fg2 * 7.7)) * 1.4 - 0.7) * aspect;
            float gy = 0.10 + 0.20 * hash11(fg2 * 5.3) + 0.012 * sin(t * 0.5 + fg2 * 2.0);
            float flap = sin(t * (3.0 + hash11(fg2) * 2.0) + fg2 * 1.7) * 0.5 + 0.5;
            float gs = 0.012 + 0.010 * hash11(fg2 * 9.1);
            col = lerp(col, ink, gull(uv - float2(gx, gy), gs, flap) * 0.9);
        }
    }

    col *= BRIGHTNESS;
    col = 1.0 - exp(-col * 1.0);                                            // filmic
    col = col * col * (3.0 - 2.0 * col) * 0.3 + col * 0.7;
    col = pow(saturate(col), float3(0.92, 0.92, 0.92));
    float2 vc = tex - 0.5;
    col *= 1.0 - dot(vc, vc) * 0.55;

    float4 fg = shaderTexture.Sample(samplerState, tex);
    float2 o = 2.0 * Scale / Resolution;
    float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
    col *= 1.0 - TEXT_VEIL * cover;
    col = col * (1.0 - fg.a) + fg.rgb;
    col += (hash12(tex * Resolution) - 0.5) / 255.0;
    return float4(saturate(col), 1.0);
}
