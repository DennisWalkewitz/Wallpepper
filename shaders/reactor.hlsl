// @name: Reactor
// @tags: abstract
// A wall of hexagonal tiles of different heights scrolls past under a low lamp, the taller tiles
// casting long shadows; in front of it floats a softly glowing blue hexagonal ring.
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
#define SCROLL_SPEED 0.2      // how fast the wall of tiles scrolls, negative = the other way (-2..2)
#define TILE_SIZE 0.327       // size of the hexagonal tiles (0.1..1)
#define TILE_FILL 0.4         // how much of its cell each tile fills: lower = wider gaps (0.15..0.42)
#define TILE_ROUNDING 0.075   // rounding of the tile corners (0..0.09)
#define TILE_HEIGHT 0.05      // height differences between the tiles: higher = longer shadows (0..0.2)
#define BEVEL 0.0075          // width of the lit edge around each tile (0.002..0.03)
#define SHADOWS 1.0           // darkness of the shadows the tiles cast on each other (0..1)
#define LIGHT_POSITION 3.0    // position of the lamp along the scrolling direction (-6..6)
#define LIGHT_SIDE 0.0        // position of the lamp across the scrolling direction (-4..4)
#define LIGHT_HEIGHT 1.0      // height of the lamp above the tiles: lower = longer shadows (0.3..4)
#define LIGHT_FALLOFF 0.025   // how quickly the wall gets dark away from the lamp (0..0.2)
#define TILE_R 0.1463         // colour of the tiles: red
#define TILE_G 0.1463         // colour of the tiles: green
#define TILE_B 0.19           // colour of the tiles: blue
#define FLOOR_R 0.1078        // colour of the wall between the tiles: red
#define FLOOR_G 0.1078        // colour of the wall between the tiles: green
#define FLOOR_B 0.14          // colour of the wall between the tiles: blue
#define RING 1                // 1 = glowing hexagonal ring in front, 0 = tiles only
#define RING_SIZE 0.6         // size of the ring (0.2..1.2)
#define RING_WIDTH 0.08       // thickness of the ring (0.01..0.3)
#define RING_HUE 0.6          // colour of the ring, as a position on the colour wheel (0..1)
#define RING_OPACITY 0.4      // how solid the body of the ring is (0..1)
#define RING_GLOW 0.005       // strength of the glow around the ring (0..0.03)
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
#define ROT(a)      mat2(cos(a), sin(a), -sin(a), cos(a))
#define PI          3.141592654
#define TAU         (2.0*PI)

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
float hash(vec2 co) {
  return fract(sin(dot(co.xy ,vec2(12.9898,58.233))) * 13758.5453);
}

// License: Unknown, author: Martijn Steinrucken, found: https://www.youtube.com/watch?v=VmrIDyYiJBA
vec2 hextile(inout vec2 p) {
  // See Art of Code: Hexagonal Tiling Explained!
  // https://www.youtube.com/watch?v=VmrIDyYiJBA
  const vec2 sz       = vec2(1.0, sqrt(3.0));
  const vec2 hsz      = 0.5*sz;

  vec2 p1 = mod(p, sz)-hsz;
  vec2 p2 = mod(p - hsz, sz)-hsz;
  vec2 p3 = dot(p1, p1) < dot(p2, p2) ? p1 : p2;
  vec2 n = ((p3 - p + hsz)/sz);
  p = p3;

  n -= (0.5);
  // Rounding to make hextile 0,0 well behaved
  return round(n*2.0)*0.5;
}

// License: MIT, author: Inigo Quilez, found: https://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm
float hexagon(vec2 p, float r) {
  const vec3 k = vec3(-0.866025404,0.5,0.577350269);
  p = abs(p);
  p -= 2.0*min(dot(k.xy,p),0.0)*k.xy;
  p -= vec2(clamp(p.x, -k.z*r, k.z*r), r);
  return length(p)*sign(p.y);
}

float shape(vec2 p) {
  return hexagon(p.yx, TILE_FILL)-TILE_ROUNDING;
}

float cellHeight(float h) {
  return TILE_HEIGHT*2.0*(-h);
}

vec3 cell(vec2 p, float h) {
  float hd = shape(p);

  const float he = BEVEL*2.0;
  float aa = he;
  float hh = -he*smoothstep(aa, -aa, hd);

  return vec3(hd, hh, cellHeight(h));
}

float height(vec2 p, float h) {
  return cell(p, h).y;
}

vec3 normal(vec2 p, float h) {
  vec2 e = vec2(4.0/RESOLUTION.y, 0);

  vec3 n;
  n.x = height(p + e.xy, h) - height(p - e.xy, h);
  n.y = height(p + e.yx, h) - height(p - e.yx, h);
  n.z = 2.0*e.x;

  return normalize(n);
}

vec3 planeColor(vec3 ro, vec3 rd, vec3 lp, vec3 pp, vec3 pnor, vec3 bcol, vec3 pcol) {
  vec3  ld = normalize(lp-pp);
  float dif  = pow(max(dot(ld, pnor), 0.0), 1.0);
  vec3 col = pcol;
  col = mix(bcol, col, dif);
  return col;
}

static const mat2 rots[6] = {
    ROT(0.0*TAU/6.0)
  , ROT(1.0*TAU/6.0)
  , ROT(2.0*TAU/6.0)
  , ROT(3.0*TAU/6.0)
  , ROT(4.0*TAU/6.0)
  , ROT(5.0*TAU/6.0)
};

static const vec2 off = vec2(1.0, 0.0);

static const vec2 offs[6] = {
    mul(rots[0], off)
  , mul(rots[1], off)
  , mul(rots[2], off)
  , mul(rots[3], off)
  , mul(rots[4], off)
  , mul(rots[5], off)
  };

float cutSlice(vec2 p, vec2 off) {
  // A bit like this but unbounded
  // https://www.shadertoy.com/view/MlycD3
  p.x = abs(p.x);
  off.x *= 0.5;

  vec2 nn = normalize(vec2(off));
  vec2 n  = vec2(nn.y, -nn.x);

  float d0 = length(p-off);
  float d1 = -(p.y-off.y);
  float d2 = dot(n, p);

  bool b = p.x > off.x && (dot(nn, p)-dot(nn, off)) < 0.0;

  return b ? d0 : max(d1, d2);
}

float hexSlice(vec2 p, int n) {
  n = 6-n;
  n = n%6;
  p = mul(rots[n], p);
  p = p.yx;
  const vec2 dim  = vec2((0.5)*2.0/sqrt(3.0), (0.5));
  return cutSlice(p, dim);
}

vec3 backdrop(vec2 p, vec2 q) {
  const float z = TILE_SIZE;
  float aa = 2.0/(z*RESOLUTION.y);

  p.yx = p;

  vec3 lp = vec3(LIGHT_POSITION, LIGHT_SIDE, LIGHT_HEIGHT);

  p -= vec2(0.195, 0.);
  p /= z;

  float toff = SCROLL_SPEED*TIME;
  p.x += toff;
  lp.x += toff;

  vec2 hp  = p;
  vec2 hn  = hextile(hp);
  float hh = hash(hn);
  vec3 c   = cell(hp, hh);
  float cd = c.x;
  float ch = c.z;

  vec3 fpp = vec3(p, ch);
  vec3 bpp = vec3(p, 0.0);

  vec3 ro = vec3(0.0, 0.0, 1.0);
  vec3 rd = normalize(fpp-ro);

  vec3  bnor = vec3(0.0, 0.0, 1.0);
  vec3  bdif = lp-bpp;
  float bl2  = dot(bdif, bdif);

  vec3  fnor = normal(hp, hh);
  vec3  fld  = normalize(lp-fpp);

  float sf = 0.0;

  for (int i = 0; i < 6; ++i) {
    vec2  ioff= offs[i];
    vec2  ip  = p+ioff;
    vec2  ihn = hextile(ip);
    float ihh = hash(ihn);
    float ich = cellHeight(ihh);
    float iii = (ich-ch)/fld.z;
    vec3  ipp = vec3(hp, ch)+iii*fld;

    float hsd = hexSlice(ipp.xy, i);
    if (ich > ch) {
      sf += exp(-20.0*tanh_approx(1.0/(10.0*iii))*max(hsd+0., 0.0));
    }
  }

  const float sat = 0.23;
  const vec3 bcol0 = vec3(FLOOR_R, FLOOR_G, FLOOR_B);
  const vec3 bcol1 = vec3(TILE_R, TILE_G, TILE_B);
  vec3 bpcol = planeColor(ro, rd, lp, bpp, bnor, unit3*(0.0), bcol0);
  vec3 fpcol = planeColor(ro, rd, lp, fpp, fnor, bpcol, bcol1);

  vec3 col = bpcol;
  col = mix(col, fpcol, smoothstep(aa, -aa, cd));
  col *= 1.0-SHADOWS*tanh_approx(sf);

  float fo = exp(-LIGHT_FALLOFF*max(bl2-0., 0.0));
  col *= fo;
  col = mix(bpcol, col, fo);

  return col;
}

vec3 effect(vec2 p, vec2 q) {
  float aa = 2.0/RESOLUTION.y;

  float dh = hexagon(p, RING_SIZE);
  float odh = abs(dh+0.016) - RING_WIDTH;

  vec3 col = backdrop(p, q);

  vec3 bcol = hsv2rgb(vec3(RING_HUE, mix(0.95, 0.8, p.x+p.y),0.5));
  if (RING > 0) col = mix(col, bcol, RING_OPACITY*smoothstep(aa, -aa, odh));

  const vec3 gcol0 = HSV2RGB(vec3(RING_HUE, 0.95, RING_GLOW));
  const vec3 gcol1 = HSV2RGB(vec3(0.65, 0.85, 0.005));
  if (RING > 0) col += gcol0/(abs(odh));
//  col += gcol1/(abs(odh)+0.05*p.y*p.y);
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

  vec3 col = effect(p, q);
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



