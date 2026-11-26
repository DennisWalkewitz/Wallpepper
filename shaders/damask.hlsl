// @name: Damask
// @tags: abstract, calming
// An embossed ornament like woven damask, lit by two coloured lamps: rings of petals drift outwards
// from the centre while the whole pattern slowly turns and twists.
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
#define TURN_SPEED 0.05       // how fast the whole ornament turns, negative = the other way (-0.5..0.5)
#define FLOW_SPEED 1.0        // how fast the petals drift outwards, negative = inwards (-20..20)
#define SWIRL 2.0             // how much the ornament is twisted around its centre (-6..6)
#define PETALS 10             // petals around the centre (3..24)
#define PATTERN_LAYERS 3      // overlapping layers of petals (1..6)
#define ORNAMENT 80.0         // how deep the petals are pressed into the surface (20..200)
#define RELIEF 0.015          // height of the embossing: higher = stronger light and shadow (0.006..0.04)
#define SHININESS 16.0        // lower = wide soft sheen, higher = small sharp highlights (6..64)
#define LIGHT_HEIGHT 1.95     // height of the two lamps above the surface (1.2..3.5)
#define LIGHT_SPREAD 1.25     // how far apart the two lamps stand (0.6..2.2)
#define LIGHT_1_R 0.72        // first lamp: red
#define LIGHT_1_G 1.0         // first lamp: green
#define LIGHT_1_B 0.65        // first lamp: blue
#define LIGHT_2_R 0.4         // second lamp: red
#define LIGHT_2_G 0.748       // second lamp: green
#define LIGHT_2_B 1.0         // second lamp: blue
#define FABRIC_R 0.1          // colour of the surface itself: red
#define FABRIC_G 0.73         // colour of the surface itself: green
#define FABRIC_B 1.0          // colour of the surface itself: blue
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

#define PI          3.141592654
#define PI_2        (0.5*PI)
#define TAU         (2.0*PI)
#define ROT(a)      mat2(cos(a), sin(a), -sin(a), cos(a))

static const float hf = RELIEF;

// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
static const vec4 hsv2rgb_K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
vec3 hsv2rgb(vec3 c) {
  vec3 p = abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www);
  return c.z * mix(hsv2rgb_K.xxx, clamp(p - hsv2rgb_K.xxx, 0.0, 1.0), c.y);
}
// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
//  Macro version of above to enable compile-time constants
#define HSV2RGB(c)  (c.z * mix(hsv2rgb_K.xxx, clamp(abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www) - hsv2rgb_K.xxx, 0.0, 1.0), c.y))

// License: Unknown, author: nmz (twitter: @stormoid), found: https://www.shadertoy.com/view/NdfyRM
vec3 sRGB(vec3 t) {
  return mix(1.055*pow(t, unit3*(1./2.4)) - 0.055, 12.92*t, step(t, unit3*(0.0031308)));
}

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
float tanh_approx(float x) {
//  return tanh(x);
  float x2 = x*x;
  return clamp(x*(27.0 + x2)/(27.0+9.0*x2), -1.0, 1.0);
}

// License: MIT, author: Inigo Quilez, found: https://www.iquilezles.org/www/articles/smin/smin.htm
float pmin(float a, float b, float k) {
  float h = clamp(0.5+0.5*(b-a)/k, 0.0, 1.0);
  return mix(b, a, h) - k*h*(1.0-h);
}

float pabs(float a, float k) {
  return -pmin(a, -a, k);
}

float height(vec2 p) {
  float tm = TIME;
  const float xm = 0.5*0.005123*FLOW_SPEED;
  float ym = mix(0.125, 0.25, 0.5-0.5*cos(TAU*TIME/600.0));

  p *= 0.4;

  float d = length(p);
  p = mul(ROT(TURN_SPEED*tm-(SWIRL)*d), p);
  float c = 1E6;
  float x = pow(d, 0.1);
  float y = atan2(p.x, p.y) / TAU;

  for (float i = 0.; i < float(PATTERN_LAYERS); ++i) {
    float v = length(fract(vec2(x - tm*i*xm, fract(y + i*ym)*.5)*float(2*PETALS))*2.-1.);
    c = pmin(c, v, 0.125);
  }

  float h =  (-hf+hf*(pabs(tanh_approx(5.5*d-ORNAMENT*c*c*d*d*(.55-d))-0.25*d, 0.25)));
  return h;
}

vec3 normal(vec2 p) {
  vec2 v;
  vec2 w;
  vec2 e = vec2(4.0/RESOLUTION.y, 0);

  vec3 n;
  n.x = height(p + e.xy) - height(p - e.xy);
  n.y = -2.0*e.x;
  n.z = height(p + e.yx) - height(p - e.yx);

  return normalize(n);
}

vec3 effect(vec2 p) {
  const float ss = LIGHT_SPREAD;
  const float hh = LIGHT_HEIGHT;

  const vec3 lp1 = -vec3(1.0 , hh, -1.0)*vec3(ss, 1.0, ss);
  const vec3 lp2 = -vec3(-1.0, hh, -1.0)*vec3(ss, 1.0, ss);

  const vec3 lcol1 = 2.0*vec3(LIGHT_1_R, LIGHT_1_G, LIGHT_1_B);
  const vec3 lcol2 = 2.0*vec3(LIGHT_2_R, LIGHT_2_G, LIGHT_2_B);
  const vec3 mat   = 0.05*vec3(FABRIC_R, FABRIC_G, FABRIC_B);
  const float spe  = SHININESS;

  float h = height(p);
  vec3  n = normal(p);

  vec3 ro = vec3(0.0, 8.0, 0.0);
  vec3 pp = vec3(p.x, 0.0, p.y);

  vec3 po = vec3(p.x, 0.0, p.y);
  vec3 rd = normalize(ro - po);

  vec3 ld1 = normalize(lp1 - po);
  vec3 ld2 = normalize(lp2 - po);

  float diff1 = max(dot(n, ld1), 0.0);
  float diff2 = max(dot(n, ld2), 0.0);

  vec3  rn    = n;
  vec3  ref   = reflect(rd, rn);
  float ref1  = max(dot(ref, ld1), 0.0);
  float ref2  = max(dot(ref, ld2), 0.0);
  float fre   = 1.0+dot(n, rd);

  float dm = tanh_approx(abs(h)*120.0);
  float rm = dm;
  dm *= dm;

  vec3 lpow1 = dm*mat*lcol1;
  vec3 lpow2 = dm*mat*lcol2;

  vec3 col = unit3*(0.0);
  col += diff1*diff1*lpow1;
  col += diff2*diff2*lpow2;

  col += rm*pow(ref1, spe)*lcol1;
  col += rm*pow(ref2, spe)*lcol2;

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

  vec3 col = effect(p);
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
