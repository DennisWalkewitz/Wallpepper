// @name: Star Tunnel
// @tags: space, calming
// A calm flight through a tunnel of stars: layer upon layer of coloured points streams past while the
// view slowly rolls and sways around a soft glow far ahead.
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
#define FLY_SPEED 0.666       // how fast you fly through the stars, negative = backwards (-4..4)
#define ROLL_SPEED 0.125      // how fast the view rolls around the flight direction (-1.5..1.5)
#define SWAY 0.2              // how far the flight direction wanders off course (0..1)
#define FISHEYE 0.5           // lens distortion towards the screen edges (-1..3)
#define STAR_LAYERS 5         // layers of stars: more = more and finer stars (2..8)
#define STAR_DENSITY 1.0      // number of stars in every layer (0.25..3)
#define STAR_SIZE 1.0         // size of the stars (0.5..4)
#define STAR_BRIGHTNESS 3.0   // brightness of the stars (0..9)
#define DEPTH_FADE 0.05       // how quickly the stars fade with distance (0..0.3)
#define TUNNEL_MIN 0.25       // radius of the innermost stars: lower = stars pass closer to you (0.05..1.5)
#define TUNNEL_MAX 2.0        // radius of the outermost stars (0.5..4)
#define STAR_HUE 0.0          // base colour of the stars, as a position on the colour wheel (0..1)
#define HUE_SPREAD 0.4        // how far the star colours stray from the base colour (0..1)
#define STAR_COLOUR 0.4       // how colourful the stars are, 0 = all white (0..1)
#define CORE_GLOW 1.0         // brightness of the glow far ahead (0..6)
#define CORE_R 0.5            // colour of the glow far ahead: red
#define CORE_G 0.66           // colour of the glow far ahead: green
#define CORE_B 1.0            // colour of the glow far ahead: blue
#define CORNER_SHADE 0.5      // the shader's own darkening of the screen edges (0..1)
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

// License CC0: Another star tunnel
//  Inspired by: https://www.shadertoy.com/view/MdlXWr
#define FASTATAN

#define PI              3.141592654
#define PI_2            (0.5*PI)
#define TAU             (2.0*PI)
#define ROT(a)          mat2(cos(a), sin(a), -sin(a), cos(a))

#if defined(FASTATAN)
#define ATAN atan_approx
#else
#define ATAN atan2
#endif

// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
static const vec4 hsv2rgb_K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
vec3 hsv2rgb(vec3 c) {
  vec3 p = abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www);
  return c.z * mix(hsv2rgb_K.xxx, clamp(p - hsv2rgb_K.xxx, 0.0, 1.0), c.y);
}
// License: WTFPL, author: sam hocevar, found: https://stackoverflow.com/a/17897228/418488
//  Macro version of above to enable compile-time constants
#define HSV2RGB(c)  (c.z * mix(hsv2rgb_K.xxx, clamp(abs(fract(c.xxx + hsv2rgb_K.xyz) * 6.0 - hsv2rgb_K.www) - hsv2rgb_K.xxx, 0.0, 1.0), c.y))

// License: Unknown, author: Unknown, found: don't remember
float hash(float co) {
  co += 123.4;
  return fract(sin(co*12.9898) * 13758.5453);
}

// License: MIT, author: Pascal Gilcher, found: https://www.shadertoy.com/view/flSXRV
float atan_approx(float y, float x) {
  float cosatan2 = x / (abs(x) + abs(y));
  float t = PI_2 - cosatan2 * PI_2;
  return y < 0.0 ? -t : t;
}

vec3 alphaBlend(vec3 back, vec4 front) {
  return mix(back, front.xyz, front.w);
}

// License: Unknown, author: Unknown, found: don't remember
float tanh_approx(float x) {
//  return tanh(x);
  float x2 = x*x;
  return clamp(x*(27.0 + x2)/(27.0+9.0*x2), -1.0, 1.0);
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

// License: MIT, author: Inigo Quilez, found: https://www.iquilezles.org/www/articles/spherefunctions/spherefunctions.htm
vec2 rayCylinder(vec3 ro, vec3 rd, vec3 cb, vec3 ca, float cr) {
    vec3  oc = ro - cb;
    float card = dot(ca,rd);
    float caoc = dot(ca,oc);
    float a = 1.0 - card*card;
    float b = dot( oc, rd) - caoc*card;
    float c = dot( oc, oc) - caoc*caoc - cr*cr;
    float h = b*b - a*c;
    if( h<0.0 ) return -1.0*unit2; //no intersection
    h = sqrt(h);
    return vec2(-b-h,-b+h)/a;
}

// License: MIT, author: Inigo Quilez, found: https://www.iquilezles.org/www/index.htm
vec3 postProcess(vec3 col, vec2 q) {
  col = clamp(col, 0.0, 1.0);
  col = sqrt(col);
  col = col*0.6+0.4*col*col*(3.0-2.0*col);
  col = mix(col, unit3*(dot(col, 0.33*unit3)), -0.4);
  col *=(1.0-CORNER_SHADE)+CORNER_SHADE*pow(19.0*q.x*q.y*(1.0-q.x)*(1.0-q.y),0.7);
  return col;
}

vec3 skyColor(vec3 ro, vec3 rd) {
  const vec3 l = normalize(vec3(0.0, 0.0, 1));
  const vec3 baseCol = vec3(CORE_R, CORE_G, CORE_B);
  return CORE_GLOW*baseCol*pow(max(dot(l, normalize(rd*vec3(1.0, 0.33, 1.0))), 0.0), 250.0);
}

vec3 color(vec3 ww, vec3 uu, vec3 vv, vec3 ro, vec2 p) {
  float lp = length(p);
  vec2 np = p + 1.0/RESOLUTION.xy;
  float rdd = (2.0+(FISHEYE)*tanh_approx(lp));  // Playing around with rdd can give interesting distortions
  vec3 rd = normalize(-p.x*uu + p.y*vv + rdd*ww);

  vec3 skyCol = skyColor(ro, rd);

  float aa = TIME*ROLL_SPEED;
  rd.yx = mul(ROT(aa), rd.yx);
  float a = ATAN(rd.y, rd.x);

  vec3 col = skyCol;
  const float mm = float(STAR_LAYERS);
  for(float i = 0.0; i < mm; ++i) {
    float ma = a;
    float ii = i/(mm-1.0);
    float sz = floor((31.0+i*64.0)*STAR_DENSITY+0.5);
    float slices = TAU/sz;
    float na = mod1(ma, slices);

    float hh = hash(na+113.0*i);
    float h1 = hh;
    float h2 = fract(hh*113.0);
    float h3 = fract(hh*127.0);

    float tr = mix(TUNNEL_MIN, TUNNEL_MAX, h1);
    vec2 tc = rayCylinder(ro, rd, ro, vec3(0.0, 0.0, 1.0), tr);
    vec3 tcp = ro + tc.y*rd;
    vec2 tcp2 = vec2(tcp.z+h2*2.0, ATAN(tcp.y, tcp.x));

    float sx = mix(0.75, 1.5, h3);
    vec2 tnp2 = mod2(tcp2, vec2(sx, slices));
    tcp2.y *= tr*PI;
    float h4 = hash(tnp2.x+hh);
    float h5 = fract(113.0*h4);
    tcp2.x += 0.4*sx*h4;
    float d = length(tcp2)-0.001;

    float si = exp(-(100.0+1.4*sz)/STAR_SIZE*max(d, 0.0));

    vec3 hsv = vec3(STAR_HUE-HUE_SPREAD*h4, mix(STAR_COLOUR, 0.00, ii), 1.0);
    vec3 bcol = hsv2rgb(hsv)*STAR_BRIGHTNESS;
    vec4 scol = vec4(bcol*sqrt(si), sqrt(si)*exp(-DEPTH_FADE*tc.y*tc.y));

    col = alphaBlend(col, scol);
  }

  return col;
}

vec3 effect(vec2 p) {
  float tm  = TIME*FLY_SPEED;
  vec3 ro   = vec3(0.0, 0, tm);
  vec3 dro  = normalize(vec3(0.20, 0.2, 1.0));
  dro.xz = mul(ROT(SWAY*sin(0.05*tm)), dro.xz);
  dro.yz = mul(ROT(SWAY*sin(0.05*tm*sqrt(0.5))), dro.yz);
  vec3 up = vec3(0.0,1.0,0.0);

  vec3 ww = normalize(dro);
  vec3 uu = normalize(cross(up, ww));
  vec3 vv = (cross(ww, uu));

  vec3 col = color(ww, uu, vv, ro, p);
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
  col = postProcess(col, q);

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



