// @name: Neon Road
// @tags: retro, abstract
// A drive down a glowing grid road towards a huge striped retro sun, under stars, a slowly turning
// sky grid and the occasional shooting star.
#define WINDOWS_TERMINAL

Texture2D shaderTexture;
SamplerState samplerState;

// --------------------
#if defined(WINDOWS_TERMINAL)
cbuffer PixelShaderSettings {
  float  Time;
  float  Scale;
  float2 Resolution;
  float4 Background;
};

#define TIME        (Time*SPEED)
#define RESOLUTION  Resolution
#else
float time;
float2 resolution;

#define TIME        time
#define RESOLUTION  resolution
#endif
// --------------------

// ---- Wallpepper settings: every number below is a control in the app ----
#define DRIVE_SPEED 20.0      // how fast you drive, negative = backwards (-80..80)
#define GRID_SIZE 1.0         // size of the road and its grid squares (0.3..3)
#define ROAD_TILT 0.15        // how steeply the road rises towards the horizon (0..0.5)
#define ROAD_GLOW 2.0         // brightness of the road lines (0..6)
#define ROAD_FOG 0.04         // how quickly the road fades into the distance (0..0.2)
#define ROAD_HUE 0.4          // colour of the road lines, as a position on the colour wheel (0..1)
#define PULSE_HUE 0.5         // how far the colour shifts in the dim patches that wander over the road (0..1)
#define PULSE_SPEED 1.0       // how fast those patches wander (0..6)
#define LOOK_UP 2.0           // tilt of the view: higher = more sky, lower = more road (-3..8)
#define SUN 1                 // 1 = retro sun on the horizon, 0 = none
#define SUN_SIZE 0.5          // size of the sun (0.15..1.5)
#define SUN_HEIGHT 0.22       // height of the sun: lower = sunk further behind the horizon (-0.3..0.9)
#define SUN_HUE 0.7           // colour in the middle of the sun, as a position on the colour wheel (0..1)
#define SUN_GRADIENT 0.25     // how much the colour changes from the bottom to the top of the sun (-1..1)
#define HORIZON_GLOW 0.35     // brightness of the band of light along the horizon (0..1.5)
#define HORIZON_HUE 0.65      // colour of that band, as a position on the colour wheel (0..1)
#define STARS 1.0             // brightness of the stars, 0 = none (0..3)
#define STAR_LAYERS 5         // layers of stars: more = more and smaller stars (2..8)
#define TWINKLE 0.25          // how fast the stars twinkle (0..3)
#define STAR_R 0.84           // star colour: red
#define STAR_G 0.2            // star colour: green
#define STAR_B 1.0            // star colour: blue
#define SKY_GRID 1            // 1 = faint grid of lines across the sky, 0 = none
#define SKY_GRID_LINES 12     // how fine the sky grid is (4..36)
#define SKY_TURN 0.025        // how fast the sky grid turns, negative = the other way (-0.3..0.3)
#define GRID_R 0.5            // colour of the sky grid: red
#define GRID_G 0.5            // colour of the sky grid: green
#define GRID_B 1.0            // colour of the sky grid: blue
#define METEORS 1             // 1 = now and then a shooting star, 0 = none
#define METEOR_PERIOD 3.0     // seconds between two chances of a shooting star (1..30)
#define HAZE_R 0.85           // colour of the haze over the horizon: red
#define HAZE_G 0.25           // colour of the haze over the horizon: green
#define HAZE_B 1.0            // colour of the haze over the horizon: blue
// ---- picture, colour and text: these work the same in every wallpaper of the Hammster / mrange collection ----
#define SPEED 1.0             // time scale of everything: 0 = frozen, 2 = twice as fast (0..4)
#define ZOOM 1.0              // magnify the picture: below 1 = wider view, above 1 = closer (0.25..4)
#define OFFSET_X 0.0          // shift the picture sideways (-1..1)
#define OFFSET_Y 0.0          // shift the picture up or down (-1..1)
#define ROTATION 0.0          // turn the whole picture clockwise, in degrees (-180..180)
#define SPIN 0.0              // keep turning the whole picture: clockwise turns per minute, negative = anticlockwise (-6..6)
#define MIRROR 0              // 1 = kaleidoscope: the picture is mirrored into equal slices around the centre
#define MIRROR_SEGMENTS 6     // number of kaleidoscope slices when MIRROR is on; 1 = plain left-right mirror (1..16)
#define HUE_SHIFT 0.0         // rotate all colours around the colour wheel (0..1)
#define HUE_CYCLE 0.0         // keep rotating the colours: full turns per minute (-6..6)
#define SATURATION 1.0        // 0 = grey, 1 = original, 2 = vivid (0..2)
#define CONTRAST 1.0          // below 1 = flatter, above 1 = punchier (0.2..2.5)
#define BRIGHTNESS 1.0        // overall brightness (0.2..3)
#define TINT_R 1.0            // colour multiplied over the picture, white = unchanged: red
#define TINT_G 1.0            // colour multiplied over the picture, white = unchanged: green
#define TINT_B 1.0            // colour multiplied over the picture, white = unchanged: blue
#define VIGNETTE 0.0          // extra darkening towards the corners (0..1.5)
#define TEXT_VEIL 0.4         // darkening behind terminal text so it stays readable (0..1)
#define TEXT_SHADOW 1.0       // strength of the drop shadow under terminal text (0..1)

// ---- finishing helpers (identical in every wallpaper of this collection) ----
// Where a screen position looks in the original picture: rotation, kaleidoscope, zoom and shift.
// Only the generated picture goes through this, never the terminal text.
float2 finishView(float2 p)
{
    float ang = ROTATION * 0.017453293 + SPIN * TIME * 0.104719755;
    float viewCos = cos(ang), viewSin = sin(ang);
    p = float2(viewCos * p.x - viewSin * p.y, viewSin * p.x + viewCos * p.y);
    if (MIRROR > 0)
    {
        float seg = 3.141592654 / max(float(MIRROR_SEGMENTS), 1.0);
        float a = atan2(p.x, p.y + 1e-7);
        a = abs(a - 2.0 * seg * floor(a / (2.0 * seg) + 0.5));
        p = length(p) * float2(sin(a), cos(a));
    }
    return p / max(ZOOM, 0.01) - float2(OFFSET_X, OFFSET_Y);
}

// Hue, saturation, contrast, brightness, tint and vignette of the finished picture (uv = 0..1 across the screen).
float3 finishGrade(float3 col, float2 uv)
{
    float turn = (HUE_SHIFT + HUE_CYCLE * TIME / 60.0) * 6.2831853;
    float hueCos = cos(turn), hueSin = sin(turn);
    float3 axis = float3(0.57735027, 0.57735027, 0.57735027);
    col = col * hueCos + cross(axis, col) * hueSin + axis * dot(axis, col) * (1.0 - hueCos);
    float grey = dot(col, float3(0.299, 0.587, 0.114));
    col = col * SATURATION + grey * (1.0 - SATURATION);
    col = col * CONTRAST + 0.5 * (1.0 - CONTRAST);
    col *= BRIGHTNESS * float3(TINT_R, TINT_G, TINT_B);
    float2 corner = uv - 0.5;
    col *= saturate(1.0 - dot(corner, corner) * VIGNETTE * 2.0);
    return col;
}

float ditherHash12(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}
// --------------------


// --------------------
// GLSL => HLSL adapters
#define vec2  float2
#define vec3  float3
#define vec4  float4
#define mat2  float2x2
#define mat3  float3x3
#define fract frac
#define mix   lerp

float mod(float x, float y) {
  return x - y * floor(x/y);
}

vec2 mod(vec2 x, vec2 y) {
  return x - y * floor(x/y);
}

static const vec2 unit2 = vec2(1.0, 1.0);
static const vec3 unit3 = vec3(1.0, 1.0, 1.0);
static const vec4 unit4 = vec4(1.0, 1.0, 1.0, 1.0);

// --------------------

#define PI                3.141592654
#define TAU               (2.0*PI)
#define ROT(a)            mat2(cos(a), sin(a), -sin(a), cos(a))

// License: Unknown, author: nmz (twitter: @stormoid), found: https://www.shadertoy.com/view/NdfyRM
float sRGB(float t) { return mix(1.055*pow(t, 1./2.4) - 0.055, 12.92*t, step(t, 0.0031308)); }
// License: Unknown, author: nmz (twitter: @stormoid), found: https://www.shadertoy.com/view/NdfyRM
vec3 sRGB(in vec3 c) { return vec3 (sRGB(c.x), sRGB(c.y), sRGB(c.z)); }

// License: Unknown, author: Matt Taylor (https://github.com/64), found: https://64.github.io/tonemapping/
vec3 aces_approx(vec3 v) {
  v = max(v, 0.0);
  v *= 0.6f;
  float a = 2.51f;
  float b = 0.03f;
  float c = 2.43f;
  float d = 0.59f;
  float e = 0.14f;
  return clamp((v*(a*v+b))/(v*(c*v+d)+e), 0.0f, 1.0f);
}

// License: Unknown, author: Unknown, found: don't remember
float hash(float co) {
  return fract(sin(co*12.9898) * 13758.5453);
}

// License: Unknown, author: Unknown, found: don't remember
vec2 hash2(vec2 p) {
  p = vec2(dot (p, vec2 (127.1, 311.7)), dot (p, vec2 (269.5, 183.3)));
  return fract(sin(p)*43758.5453123);
}

// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
static const vec4 hsv2rgb_K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
vec3 hsv2rgb(vec3 c) {
  vec3 p = abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www);
  return c.z * mix(hsv2rgb_K.xxx, clamp(p - hsv2rgb_K.xxx, 0.0, 1.0), c.y);
}
#define HSV2RGB(c)  (c.z * mix(hsv2rgb_K.xxx, clamp(abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www) - hsv2rgb_K.xxx, 0.0, 1.0), c.y))

// License: Unknown, author: Unknown, found: don't remember
float tanh_approx(float x) {
//  return tanh(x);
  float x2 = x*x;
  return clamp(x*(27.0 + x2)/(27.0+9.0*x2), -1.0, 1.0);
}

float circle(vec2 p, float r) {
  return length(p) - r;
}

// License: MIT, author: Inigo Quilez, found: https://iquilezles.org/articles/smin
float pmin(float a, float b, float k) {
    float h = clamp( 0.5+0.5*(b-a)/k, 0.0, 1.0 );
    return mix( b, a, h ) - k*h*(1.0-h);
}

// License: MIT OR CC-BY-NC-4.0, author: mercury, found: https://mercury.sexy/hg_sdf/
float mod1(inout float p, float size) {
  float halfsize = size*0.5;
  float c = floor((p + halfsize)/size);
  p = mod(p + halfsize, size) - halfsize;
  return c;
}

// License: MIT OR CC-BY-NC-4.0, author: mercury, found: https://mercury.sexy/hg_sdf/
vec2 mod2(inout vec2 p, vec2 size) {
  vec2 c = floor((p + size*0.5)/size);
  p = mod(p + size*0.5,size) - size*0.5;
  return c;
}

// License: MIT, author: Inigo Quilez, found: https://iquilezles.org/articles/intersectors
float rayPlane(vec3 ro, vec3 rd, vec4 p) {
  return -(dot(ro,p.xyz)+p.w)/dot(rd,p.xyz);
}

vec3 toSpherical(vec3 p) {
  float r   = length(p);
  float t   = acos(p.z/r);
  float ph  = atan2(p.y, p.x);
  return vec3(r, t, ph);
}

float sun(vec2 p) {
  const float ch = 0.0125;
  vec2 sp = p;
  float d0 = circle(sp, 0.5);
  float d = d0;
  return d;
}

float segmentx(vec2 p) {
  float d0 = abs(p.y);
  float d1 = length(p);
  return p.x > 0.0 ? d0 : d1;
}

float segmentx(vec2 p, float l) {
  float hl = 0.5*l;
  p.x = abs(p.x);
  float d0 = abs(p.y);
  float d1 = length(p-vec2(hl, 0.0));
  return p.x > hl ? d1 : d0;
}

vec3 road(vec3 ro, vec3 rd, vec3 nrd, float glare, vec4 pl, out float pt) {
  const float szoom   = 0.5;
  const float bsz     = 25.0;
  const float sm      = 1.0;
  float off = abs(pl.w)*GRID_SIZE;
  float t = rayPlane(ro, rd, pl);
  pt = t;

  vec3 p  = ro+rd*t;
  vec3 np = ro+nrd*t;

  vec2 pp   = p.xz;
  vec2 npp  = np.xz;
  vec2 opp  = pp;

  float aa  = length(npp-pp)*sqrt(0.5);
  pp.y -= DRIVE_SPEED*TIME;

  vec3 gcol = unit3*(0.0);

  float dr = abs(pp.x)-off;
  vec2 cp = pp;
  mod1(cp.y, 6.0*off);
  vec2 sp = pp;
  sp.x = abs(sp.x);
  mod1(sp.y, off);
  float dcl = segmentx(cp.yx, 1.5*off);
  float dsl = segmentx((sp-vec2(0.95*off, 0.0)).yx, off*0.5);

  vec2 mp = pp;
  mod2(mp, unit2*(off*0.5));

  vec2 dp = abs(mp);
  float d = dp.x;
  d = pmin(d, dp.y, sm);
  d = max(d, -dr);
  d = min(d, dcl);
  d = min(d, dsl);
  vec2 s2 = sin(TIME*PULSE_SPEED+2.0*p.xz/off);
  float m = mix(0.75, 0.9, tanh_approx(s2.x+s2.y));
  m *= m;
  m *= m;
  m *= m;
  vec3 hsv = vec3(ROAD_HUE+mix(PULSE_HUE, 0.0, m), tanh_approx(0.15*mix(30.0, 10.0, m)*d), 1.0);
  float fo = exp(-ROAD_FOG*max(abs(t)-off*2., 0.0));
  vec3 bcol = hsv2rgb(hsv);
  gcol += ROAD_GLOW*bcol*exp(-0.1*mix(30.0, 10.0, m)*d)*fo;

  float sh;
  float sdb;
  sh = tanh_approx(sh);
  sdb *= 0.075;
  sdb *= sdb;
  sdb += 0.05;


  gcol = t > 0.0 ? gcol : unit3*(0.0);
  return gcol;
}

vec3 stars(vec2 sp, float hh) {
  vec3 col = unit3*(0.0);

  const float m = float(STAR_LAYERS);
  hh = tanh_approx(20.0*hh);

  for (float i = 0.0; i < m; ++i) {
    vec2 pp = sp+0.5*i;
    float s = i/(m-1.0);
    vec2 dim  = unit2*(mix(0.05, 0.003, s)*PI);
    vec2 np = mod2(pp, dim);
    vec2 h = hash2(np+127.0+i);
    vec2 o = -1.0+2.0*h;
    float y = sin(sp.x);
    pp += o*dim*0.5;
    pp.y *= y;
    float l = length(pp);

    float h1 = fract(h.x*1667.0);
    float h2 = fract(h.x*1887.0);
    float h3 = fract(h.x*2997.0);

    float ll = mix(0.5, 1.0, h1);
    vec3 scol = mix(8.0*h2, 0.25*h2*h2, s)*(2.0*vec3(STAR_R, STAR_G, STAR_B))*ll;

    vec3 ccol = col + exp(-(mix(6000.0, 2000.0, hh)/mix(2.0, 0.25, s))*max(l-0.001, 0.0))*scol;
    ccol *= mix(0.125, 1.0, smoothstep(1.0, 0.99, sin(TWINKLE*TIME+TAU*h.y)));
    col = h3 < y ? ccol : col;
  }

  return col;
}

vec3 meteorite(vec2 sp) {
  const float period = METEOR_PERIOD;
  float mtime = mod(TIME, period);
  float ntime = floor(TIME/period);
  float h0 = hash(ntime+123.4);
  float h1 = fract(1667.0*h0);
  float h2 = fract(9967.0*h0);
  vec2 mp = sp;
  mp.x += -1.0;
  mp.y += -0.5*h1;
  mp.y += PI*0.5;
  mp = mul(ROT(PI+mix(-PI/4.0, PI/4.0, h0)), mp);
  float m = mtime/period;
  mp.x += mix(-1.0, 2.0, m);

  float d0 = length(mp);
  float d1 = segmentx(mp);

  vec3 col = unit3*(0.0);

  col += 0.5*exp(-4.0*max(d0, 0.0))*exp(-1000.0*max(d1, 0.0));
  col *= 2.0*HSV2RGB(vec3(0.8, 0.5, 1.0));
  float fl = smoothstep(-0.5, 0.5, sin(12.0*TAU*TIME));
  col += mix(1.0, 0.5, fl)*exp(-mix(100.0, 150.0, fl)*max(d0, 0.0));

  col = h2 > 0.8 ? col: unit3*(0.0);
  return col;
}

vec3 skyGrid(vec2 sp) {
  const float m = 1.0;

  const vec2 dim = unit2*(1.0/float(SKY_GRID_LINES)*PI);
  float y = sin(sp.x);
  vec2 pp = sp;
  vec2 np = mod2(pp, dim*vec2(1.0/floor(1.0/y), 1.0));

  vec3 col = unit3*(0.0);

  float d = min(abs(pp.x), abs(pp.y*y));

  float aa = 2.0/RESOLUTION.y;

  col += 0.25*vec3(GRID_R, GRID_G, GRID_B)*exp(-2000.0*max(d-0.00025, 0.0));

  return col;
}


vec3 sunset(vec2 sp, vec2 nsp) {
  const float szoom   = SUN_SIZE;
  float aa = length(nsp-sp)*sqrt(0.5);
  sp -= vec2(vec2(0.5, -0.5)*PI);
  sp /= szoom;
  sp = sp.yx;
  sp.y += SUN_HEIGHT;
  sp.y = -sp.y;
  float ds = sun(sp)*szoom;

  vec3 bscol = hsv2rgb(vec3(fract(SUN_HUE-(SUN_GRADIENT)*(sp.y)), 1.0, 1.0));
  vec3 gscol = 0.75*sqrt(bscol)*exp(-50.0*max(ds, 0.0));
  vec3 scol = mix(gscol, bscol, smoothstep(aa, -aa, ds));
  return scol;
}

vec3 glow(vec3 ro, vec3 rd, vec2 sp, vec3 lp) {
  float ld = max(dot(normalize(lp-ro), rd),0.0);
  float y = -0.5+sp.x/PI;
  y = max(abs(y)-0.02, 0.0)+0.1*smoothstep(0.5, PI, abs(sp.y));
  float ci = pow(ld, 10.0)*2.0*exp(-25.0*y);
  float h = HORIZON_HUE;
  vec3 col = hsv2rgb(vec3(h, 0.75, HORIZON_GLOW*exp(-15.0*y)))+HSV2RGB(vec3(0.8, 0.75, 0.5))*ci;
  return col;
}

vec3 neonSky(vec3 ro, vec3 rd, vec3 nrd, out float gl) {
  const vec3 lp       = 500.0*vec3(0.0, 0.25, -1.0);
  const vec3 skyCol   = 0.05*vec3(HAZE_R, HAZE_G, HAZE_B);


  float glare = pow(abs(dot(rd, normalize(lp))), 20.0);

  vec2 sp   = toSpherical(rd.xzy).yz;
  vec2 nsp  = toSpherical(nrd.xzy).yz;
  vec3 grd  = rd;
  grd.xy = mul(ROT(SKY_TURN*TIME), grd.xy);
  vec2 spp = toSpherical(grd).yz;

  float gm = 1.0/abs(rd.y)*mix(0.005, 2.0, glare);
  vec3 col = skyCol*gm;
  float ig = 1.0-glare;
  col += glow(ro, rd, sp, lp);
  if (rd.y > 0.0) {
    if (SUN > 0) col += sunset(sp, nsp);
    col += stars(sp, 0.0)*STARS*ig;
    if (SKY_GRID > 0) col += skyGrid(spp)*ig;
    if (METEORS > 0) col += meteorite(sp)*ig;
  }
  gl = glare;
  return col;
}

vec3 color(vec3 ro, vec3 rd, vec3 nrd) {
  const float off1  = -20.0;
  const vec4 pl1    = vec4(normalize(vec3(0.0, 1.0, ROAD_TILT)), -off1);
  float glare;
  vec3 col = neonSky(ro, rd, nrd, glare);
  if (rd.y < 0.0) {
    float t;
    col += road(ro, rd, nrd, glare, pl1, t);
  }
  return col;
}


//
// PS_OUTPUT ps_main(in PS_INPUT In)
#if defined(WINDOWS_TERMINAL)
float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
#else
float4 ps_main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
#endif
{
  vec2 q = tex;
  vec2 p = -1.0 + 2.0*q;
#if defined(WINDOWS_TERMINAL)
  p.y = -p.y;
#endif
  p.x *= RESOLUTION.x/RESOLUTION.y;
  p = finishView(p);

  float aa = 2.0/RESOLUTION.y;

  vec3 ro = vec3(0.0, 0.0, 10.0);
  vec3 la = vec3(0.0, LOOK_UP, 0.0);
  vec3 up = vec3(0.0, 1.0, 0.0);

  vec3 ww = normalize(la - ro);
  vec3 uu = normalize(cross(up, ww ));
  vec3 vv = (cross(ww,uu));
  const float fov = tan(TAU/6.0);
  vec2 np = p + unit2*(aa);
  vec3 rd = normalize(-p.x*uu + p.y*vv + fov*ww);
  vec3 nrd = normalize(-np.x*uu + np.y*vv + fov*ww);


  vec3 col = unit3*(0.0);
  col = color(ro, rd, nrd);
  col = aces_approx(col);
  col = sRGB(col);

  col = finishGrade(col, q);

  vec4 fg = shaderTexture.Sample(samplerState, q);
  vec4 sh = shaderTexture.Sample(samplerState, q-2.0*unit2/RESOLUTION.xy);
  float2 veilStep = 2.0 * Scale / Resolution;
  float cover = max(fg.w, max(shaderTexture.Sample(samplerState, q + veilStep).w, shaderTexture.Sample(samplerState, q - veilStep).w));
  col *= 1.0 - TEXT_VEIL * cover;                       // darken behind text so it stays readable
  col = mix(col, 0.0*unit3, sh.w*TEXT_SHADOW);
  col = mix(col, fg.xyz, fg.w);

  col += (ditherHash12(q * Resolution) - 0.5) / 255.0;    // dither: hides banding in dark gradients
  return vec4(col, 1.0);
}
