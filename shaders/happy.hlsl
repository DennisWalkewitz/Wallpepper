// @name: Happy Tunnel
// @tags: abstract
// @light: RAY_STEPS=40
// A view down a twisting tunnel with mirror-polished walls: the far opening, a ceiling light and two
// coloured lamps are reflected again and again while the tunnel slowly turns and breathes wider and narrower.
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
#define TWIST 0.2             // how much the tunnel is twisted along its length, negative = the other way (-0.8..0.8)
#define TURN_SPEED 0.1        // how fast the tunnel turns around you, negative = the other way (-1..1)
#define TUNNEL_HEIGHT 1.25    // height of the tunnel (0.6..3)
#define WIDTH_MIN 1.25        // width of the tunnel at its narrowest (0.6..4)
#define WIDTH_MAX 2.5         // width of the tunnel at its widest (0.6..4)
#define BREATHE_SPEED 1.0     // how fast the tunnel goes from narrow to wide and back; 1 = once a minute (0..20)
#define REFLECTION 0.75       // how strongly the walls mirror each other (0..1.5)
#define AMBIENT 0.1           // base light on the walls (0..0.5)
#define SUN_GLOW 1.0          // glow at the far end of the tunnel (0..20)
#define CEILING_LIGHT 1.0     // brightness of the light panel reflected in the walls (0..4)
#define RAY_STEPS 60          // rendering quality of the walls: more steps = cleaner but slower (20..120)
#define SKY_R 0.1             // colour of the light panel and the haze: red
#define SKY_G 1.0             // colour of the light panel and the haze: green
#define SKY_B 1.0             // colour of the light panel and the haze: blue
#define HORIZON_R 0.24        // colour of the bright line across the far end: red
#define HORIZON_G 0.05        // colour of the bright line across the far end: green
#define HORIZON_B 1.0         // colour of the bright line across the far end: blue
#define SUN_R 0.82            // colour of the glow at the far end: red
#define SUN_G 0.1             // colour of the glow at the far end: green
#define SUN_B 1.0             // colour of the glow at the far end: blue
#define LAMP_1_R 0.55         // first lamp on the walls: red
#define LAMP_1_G 0.1          // first lamp on the walls: green
#define LAMP_1_B 1.0          // first lamp on the walls: blue
#define LAMP_2_R 1.0          // second lamp on the walls: red
#define LAMP_2_G 0.1          // second lamp on the walls: green
#define LAMP_2_B 0.37         // second lamp on the walls: blue
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
// --------------------

#define PI          3.141592654
#define TAU         (2.0*PI)
#define ROT(a)      mat2(cos(a), sin(a), -sin(a), cos(a))

#define TOLERANCE       (0.0005)
#define MAX_RAY_LENGTH  (10.0)
#define MAX_RAY_MARCHES RAY_STEPS
#define NORM_OFF        (0.005)

//float g_mod = 2.5;

// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
static const vec4 hsv2rgb_K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
vec3 hsv2rgb(vec3 c) {
  vec3 p = abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www);
  return c.z * mix(hsv2rgb_K.xxx, clamp(p - hsv2rgb_K.xxx, 0.0, 1.0), c.y);
}
// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
//  Macro version of above to enable compile-time constants
#define HSV2RGB(c)  (c.z * mix(hsv2rgb_K.xxx, clamp(abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www) - hsv2rgb_K.xxx, 0.0, 1.0), c.y))

static const float hoff = 0.;

static const vec3 skyCol     = 0.25*vec3(SKY_R, SKY_G, SKY_B);
static const vec3 skylineCol = 0.5*vec3(HORIZON_R, HORIZON_G, HORIZON_B);
static const vec3 sunCol     = 0.5*vec3(SUN_R, SUN_G, SUN_B);
static const vec3 diffCol1   = 0.5*vec3(LAMP_1_R, LAMP_1_G, LAMP_1_B);
static const vec3 diffCol2   = 0.5*vec3(LAMP_2_R, LAMP_2_G, LAMP_2_B);

static const vec3 sunDir1    = normalize(vec3(0., 0.05, -1.0));

static const float lpf = 5.0;
static const vec3 lightPos1  = lpf*vec3(+1.0, 2.0, 3.0);
static const vec3 lightPos2  = lpf*vec3(-1.0, 2.0, 3.0);

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
  //  Found this somewhere on the interwebs
  //  return tanh(x);
  float x2 = x*x;
  return clamp(x*(27.0 + x2)/(27.0+9.0*x2), -1.0, 1.0);
}

// License: MIT, author: Inigo Quilez, found: https://iquilezles.org/articles/distfunctions/
float rayPlane(vec3 ro, vec3 rd, vec4 p) {
  return -(dot(ro,p.xyz)+p.w)/dot(rd,p.xyz);
}

// License: MIT, author: Inigo Quilez, found: https://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
float box(vec2 p, vec2 b) {
  vec2 d = abs(p)-b;
  return length(max(d,0.0)) + min(max(d.x,d.y),0.0);
}

vec3 render0(vec3 ro, vec3 rd) {
  vec3 col = unit3*(0.0);
  float sf = 1.0001-max(dot(sunDir1, rd), 0.0);
  col += skyCol*pow((1.0-abs(rd.y)), 8.0);
  col += (mix(0.0025, 0.125, tanh_approx(.005/sf))/abs(rd.y))*skylineCol;
  sf *= sf;
  col += sunCol*0.00005*SUN_GLOW/sf;

  float tp1  = rayPlane(ro, rd, vec4(vec3(0.0, -1.0, 0.0), 6.0));

  if (tp1 > 0.0) {
    vec3 pos  = ro + tp1*rd;
    vec2 pp = pos.xz;
    float db = box(pp, vec2(5.0, 9.0))-3.0;

    col += unit3*(4.0)*skyCol*rd.y*rd.y*smoothstep(0.25, 0.0, db)*CEILING_LIGHT;
    col += unit3*(0.8)*skyCol*exp(-0.5*max(db, 0.0))*CEILING_LIGHT;
  }

  return clamp(col, 0.0, 10.0);;
}

float df(vec3 p, vec2 dim) {
  vec3 p0 = p;
  p0.xy = mul(ROT(TWIST*p0.z-(TURN_SPEED)*TIME), p0.xy);
  return -box(p0.xy, dim);
}

vec3 normal(vec3 pos, vec2 dim) {
  vec2  eps = vec2(NORM_OFF,0.0);
  vec3 nor;
  nor.x = df(pos+eps.xyy, dim) - df(pos-eps.xyy, dim);
  nor.y = df(pos+eps.yxy, dim) - df(pos-eps.yxy, dim);
  nor.z = df(pos+eps.yyx, dim) - df(pos-eps.yyx, dim);
  return normalize(nor);
}

float rayMarch(vec3 ro, vec3 rd, float initt, vec2 dim) {
  float t = initt;
  float tol = TOLERANCE;
  for (int i = 0; i < MAX_RAY_MARCHES; ++i) {
    if (t > MAX_RAY_LENGTH) {
      t = MAX_RAY_LENGTH;
      break;
    }
    float d = df(ro + rd*t, dim);
    if (d < TOLERANCE) {
      break;
    }
    t += d;
  }
  return t;
}

vec3 boxCol(vec3 col, vec3 nsp, vec3 ro, vec3 rd, vec3 nnor, vec3 nrcol) {
  float nfre  = 1.0+dot(rd, nnor);
  nfre        *= nfre;

  vec3 nld1   = normalize(lightPos1-nsp);
  vec3 nld2   = normalize(lightPos2-nsp);

  float ndif1 = max(dot(nld1, nnor), 0.0);
  ndif1       *= ndif1;

  float ndif2 = max(dot(nld2, nnor), 0.0);
  ndif2       *= ndif2;

  vec3 scol = unit3*(0.0);
  scol += diffCol1*ndif1;
  scol += diffCol2*ndif2;
  scol += AMBIENT*(skyCol+skylineCol);
  scol += nrcol*REFLECTION*mix(unit3*(0.25), vec3(0.5, 0.5, 1.0), nfre);

  vec3 pp = nsp-ro;

  col = mix(col, scol, smoothstep(100.0, 20.0, dot(pp, pp)));

  return col;
}

vec3 render1(vec3 ro, vec3 rd) {
  vec3 col = 0.1*sunCol;

  vec2 dim = vec2(mix(WIDTH_MIN, WIDTH_MAX, 0.5+0.5*sin(TAU*TIME*BREATHE_SPEED/60.0)), TUNNEL_HEIGHT);

  float nt    = rayMarch(ro, rd, .0, dim);
  if (nt < MAX_RAY_LENGTH) {
    vec3 nsp    = ro + rd*nt;
    vec3 nnor   = normal(nsp, dim);
    vec3 nref   = reflect(rd, nnor);
    float nrt   = rayMarch(nsp, nref, 0.2, dim);
    vec3 nrcol  = render0(nsp, nref);

    if (nrt < MAX_RAY_LENGTH) {
      vec3 nrsp   = nsp + nref*nrt;
      vec3 nrnor  = normal(nrsp, dim);
      vec3 nrref  = reflect(nref, nrnor);
      nrcol = boxCol(nrcol, nrsp, ro, nref, nrnor, render0(nrsp, nrref));
    }

    col = boxCol(col, nsp, ro, rd, nnor, nrcol);
  }

  return col;
}

vec3 effect(vec2 p) {
  const float fov = tan(TAU/(6.-0.6));
  const vec3 up = vec3(0.0, 1.0, 0.0);
  const vec3 ro = vec3(0.0, 0.0, 5.0);
  const vec3 la = vec3(0.0, 0.0, 0.);

  vec3 ww = normalize(la - ro);
  vec3 uu = normalize(cross(up, ww));
  vec3 vv = cross(ww,uu);
  vec3 rd = normalize(-p.x*uu + p.y*vv + fov*ww);

  vec3 col = render1(ro, rd);

  col -= 0.0333*vec3(1.0, 2.0, 2.0);

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

#if defined(WINDOWS_TERMINAL)
  col = finishGrade(col, q);

  vec4 fg = shaderTexture.Sample(samplerState, q);
  vec4 sh = shaderTexture.Sample(samplerState, q+2.0*vec2(-1.0, 1.0)/RESOLUTION.xy);
  float2 veilStep = 2.0 * Scale / Resolution;
  float cover = max(fg.w, max(shaderTexture.Sample(samplerState, q + veilStep).w, shaderTexture.Sample(samplerState, q - veilStep).w));
  col *= 1.0 - TEXT_VEIL * cover;                       // darken behind text so it stays readable
  col = mix(col, 0.0*unit3, sh.w*TEXT_SHADOW);
  col = mix(col, fg.xyz, fg.w);
#endif

  col += (ditherHash12(q * Resolution) - 0.5) / 255.0;    // dither: hides banding in dark gradients
  return vec4(col, 1.0);
}