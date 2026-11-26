// @name: CRT
// @tags: retro, terminal-only
// CRT emulation: turns the terminal into an old amber monitor with curved glass, scanlines, a soft glow,
// flickering grain and a band that travels down the screen.

// Define map for PS input
struct PSInput {
  float4 pos : SV_POSITION;
  float2 uv : TEXCOORD0;
};

// The terminal graphics as a texture
Texture2D shaderTexture : register(t0);
SamplerState samplerState : register(s0);

// Terminal settings such as the resolution of the texture
cbuffer PixelShaderSettings : register(b0) {
  // The number of seconds since the pixel shader was enabled
  float  Time;
  // UI Scale
  float  Scale;
  // Resolution of the shaderTexture
  float2 Resolution;
  // Background color as rgba
  float4 Background;
};

// ---- Wallpepper settings: every number below is a control in the app ----
// Shader Options
#define ENABLE_CURVE            1       // 1 = bulging tube glass with a bezel around it, 0 = flat screen
#define ENABLE_OVERSCAN         1       // 1 = the picture is shrunk a little, leaving a dark margin
#define ENABLE_BLOOM            1       // 1 = bright edges glow
#define ENABLE_BLUR             1       // 1 = soft focus
#define ENABLE_GRAYSCALE        1       // 1 = monochrome picture (coloured by the tint), 0 = keep the terminal colours
#define ENABLE_BLACKLEVEL       1       // 1 = dark areas keep a faint phosphor glow
#define ENABLE_REFRESHLINE      1       // 1 = a faint dark band travels down the screen
#define ENABLE_SCANLINES        1       // 1 = dark horizontal scanlines
#define ENABLE_TINT             1       // 1 = colour the picture with the phosphor colour below
#define ENABLE_GRAIN            1       // 1 = flickering noise

// Settings - Curve
#define CURVE_INTENSITY         1.0     // how strongly the glass bulges (0..4)

// Settings - Overscan
#define OVERSCAN_PERCENTAGE     0.02    // width of the dark margin around the picture (0..0.2)

// Settings - Bloom
#define BLOOM_OFFSET            0.0015  // how far the glow reaches sideways (0..0.01)
#define BLOOM_STRENGTH          0.8     // strength of the glow on bright edges (0..3)

// Settings - Blur
#define BLUR_MULTIPLIER         1.05    // brightness of the blurred picture that is mixed in (0.5..2)
#define BLUR_STRENGTH           0.2     // how much of the blurred picture is mixed in: 0 = sharp (0..1)
#define BLUR_OFFSET             0.003   // how wide the blur is (0..0.01)

// Settings - Grayscale
#define GRAYSCALE_METHOD        3       // how colours become grey: 1 = plain average, 2 = gleam, 3 = luminance, 4 = luma; 2 and 4 need bloom switched off (1..4)
#define GRAYSCALE_INTENSITY     (GRAYSCALE_METHOD == 1)
#define GRAYSCALE_GLEAM         (GRAYSCALE_METHOD == 2)
#define GRAYSCALE_LUMINANCE     (GRAYSCALE_METHOD == 3)
#define GRAYSCALE_LUMA          (GRAYSCALE_METHOD == 4)

// Settings - Blacklevel
#define BLACK_LEVEL             0.025   // brightness of the phosphor glow in dark areas (0..0.2)
#define BLACKLEVEL_FLOOR        (TINT_COLOR * BLACK_LEVEL)

// Settings - Tint
#define TINT_R                  1.0     // phosphor colour: red
#define TINT_G                  0.7     // phosphor colour: green
#define TINT_B                  0.0     // phosphor colour: blue
#define TINT_COLOR              float3(TINT_R, TINT_G, TINT_B)

// Classic phosphor colours to copy into the colour above
// Colors variations from https://superuser.com/a/1206781
#define TINT_AMBER              float3(1.0, 0.7, 0.0) // P3 phosphor
#define TINT_LIGHT_AMBER        float3(1.0, 0.8, 0.0)
#define TINT_GREEN_1            float3(0.2, 1.0, 0.0)
#define TINT_APPLE_II           float3(0.2, 1.0, 0.2) // P1 phosphor
#define TINT_GREEN_2            float3(0.0, 1.0, 0.2)
#define TINT_APPLE_IIc          float3(0.4, 1.0, 0.4) // P24 phpsphor
#define TINT_GREEN_3            float3(0.0, 1.0, 0.4)
#define TINT_WARM               float3(1.0, 0.9, 0.8)
#define TINT_COOL               float3(0.8, 0.9, 1.0)

// Settings - Gain
#define GRAIN_INTENSITY         0.02    // strength of the flickering noise (0..0.2)

// Settings - Scanlines
#define SCANLINE_FACTOR         0.3     // how dark the dark scanlines are: 0 = invisible, 1 = black (0..1)
#define SCANLINE_SIZE           1.0     // thickness of each scanline in pixels (1..8)

// Settings - Refresh line
#define REFRESHLINE_SPEED       1.0     // how fast the band travels down the screen (0.1..6)
#define REFRESHLINE_WIDTH       0.03    // height of the band, as a part of the screen (0.005..0.3)
#define REFRESHLINE_STRENGTH    2.0     // how dark the band is (0..6)

// Settings - Finish (not in the original: both do nothing at their defaults)
#define VIGNETTE                0.0     // darkening towards the corners of the tube (0..1.5)
#define BRIGHTNESS              1.0     // overall brightness (0.2..3)

// If you have Bloom enabled, it doesn't play well
// with the way Gleam and Luma calculate grayscale
// so fall back to Luminance
#if ENABLE_BLOOM && (GRAYSCALE_GLEAM || GRAYSCALE_LUMA)
#undef GRAYSCALE_INTENSITY
#undef GRAYSCALE_GLEAM
#undef GRAYSCALE_LUMINANCE
#undef GRAYSCALE_LUMA
#define GRAYSCALE_INTENSITY (0)
#define GRAYSCALE_GLEAM (0)
#define GRAYSCALE_LUMINANCE (1)
#define GRAYSCALE_LUMA (0)
#endif

// Provide a reasonable Blacklevel even if Tint isn't enabled
#if ENABLE_BLACKLEVEL && !ENABLE_TINT
#undef BLACKLEVEL_FLOOR
#define BLACKLEVEL_FLOOR (float3(2.0, 2.0, 2.0) * BLACK_LEVEL)
#endif

// All the DEBUG settings are optional
// At a minimum, #define DEBUG 1 will pass debug visualizations
// through, but that can be refined with the additional settings
// #define SHOW_UV and SHOW_POS can be useful for seeing the
// coordinates but this is more valuable when trying to see these
// coordinates when applied to the Windows Terminal, a capability
// disabled by default. SHOW_UV and SHOW_POS are independant of
// DEBUG and effectively replace the shader code being written. This
// can be useful to temporarily disable the shader code with a
// minumum output which renders during development.

// Settings - Debug
#define DEBUG                   (1)
//#define DEBUG_ROTATION          0.25
//#define DEBUG_SEGMENTS          1
//#define DEBUG_OFFSET            0.425
//#define DEBUG_WIDTH             0.15
#define SHOW_UV                 (0)
#define SHOW_POS                (0)

// SHADERed (the shader editor this effect was written in) defines this; everywhere else it is off.
// Without the definition, GLSL ES rejects the #if lines below.
#ifndef SHADERed
#define SHADERed (0)
#endif

#if SHADERed
// Must be inlined to the shader or it breaks single-step debugging
PSInput patchCoordinates(PSInput pin);

struct DebugOut {
  bool show;
  float4 color;
};
DebugOut debug(float4 pos, float2 uv);
#endif

#if ENABLE_CURVE
float2 transformCurve(float2 uv) {
  uv -= 0.5;				// offcenter screen
  float r = (uv.x * uv.x + uv.y * uv.y) * CURVE_INTENSITY; 	// get ratio
  uv *= 4.2 + r;				// apply ratio
  uv *= 0.25;				// zoom
  uv += 0.5;				// move back to center
  return uv;
}
#endif

#if ENABLE_OVERSCAN
// Modifies uv
float4 overscan(float4 color, in float2 screenuv, out float2 uv)
{
  // Overscan Region
  uv = screenuv;
  uv -= 0.5;
  uv *= 1.0/(1.0-OVERSCAN_PERCENTAGE);
  uv += 0.5;

  // The scaler was found through trial and error, but the result seems to work
  // for all the color schemes I tried. I think that Background should be
  // providing the same values as if the texture was sampled, but it doesn't seem
  // to do so. On the otherhand, I can also see a need for the actual Background
  // to be passed into the shader. Maybe this just needs more documentation.
  // SHADERed seems to have a problem with this in the preview window, but the
  // Pixel Inspect shows the right thing.
  if (uv.x < 0.000 || uv.y < 0.000) { return saturate(float4(Background.rgb, 0)*0.1); }
  if (uv.y > 1.000 || uv.y > 1.000) { return saturate(float4(Background.rgb, 0)*0.1); }
  
  return float4(color);
}
#endif

#if ENABLE_BLOOM
float3 bloom(float3 color, float2 uv)
{
  float3 bloom = color - shaderTexture.Sample(samplerState, uv + float2(-BLOOM_OFFSET, 0.0) * Scale).rgb;
  float3 bloom_mask = bloom * BLOOM_STRENGTH;
  //return bloom_mask;
  return saturate(color + bloom_mask);
}
#endif

#if ENABLE_BLUR
static const float blurWeights[9]={0.0, 0.092, 0.081, 0.071, 0.061, 0.051, 0.041, 0.031, 0.021};

float3 blurH(float3 c, float2 uv)
{
  float3 screen =
    shaderTexture.Sample(samplerState, uv).rgb * 0.102;
  for (int i = 1; i < 9; i++) screen +=
    shaderTexture.Sample(samplerState, uv + float2( float(i) * BLUR_OFFSET, 0.0)).rgb * blurWeights[i];
  for (int i = 1; i < 9; i++) screen +=
    shaderTexture.Sample(samplerState, uv + float2(-float(i) * BLUR_OFFSET, 0.0)).rgb * blurWeights[i];
  return screen * BLUR_MULTIPLIER;
}

float3 blurV(float3 c, float2 uv)
{
  float3 screen =
    shaderTexture.Sample(samplerState, uv).rgb * 0.102;
  for (int i = 1; i < 9; i++) screen +=
    shaderTexture.Sample(samplerState, uv + float2(0.0,  float(i) * BLUR_OFFSET)).rgb * blurWeights[i];
  for (int i = 1; i < 9; i++) screen +=
    shaderTexture.Sample(samplerState, uv + float2(0.0, -float(i) * BLUR_OFFSET)).rgb * blurWeights[i];
  return screen * BLUR_MULTIPLIER;
}

float3 blur(float3 color, float2 uv)
{
  float3 blur = (blurH(color, uv) + blurV(color, uv)) / 2.0 - color;
  float3 blur_mask = blur * BLUR_STRENGTH;
  //return blur_mask;
  return saturate(color + blur_mask);
}
#endif

#if ENABLE_GRAYSCALE
// https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0029740
float3 rgb2intensity(float3 c)
{
  float i = (c.r + c.g + c.b) / 3.0;
  return float3(i, i, i);
}

#define GAMMA (2.2)
float3 gamma(float3 c)
{
  return pow(c, float3(GAMMA, GAMMA, GAMMA));
}

float3 invGamma(float3 c)
{
  return pow(c, float3(1.0 / GAMMA, 1.0 / GAMMA, 1.0 / GAMMA));
}

float3 rgb2gleam(float3 c)
{
  c = invGamma(c);
  c= rgb2intensity(c);
  return gamma(c);
}

float3 rgb2luminance(float3 c)
{
  float l = 0.2989 * c.r + 0.5866 * c.g + 0.1145 * c.b;
  return float3(l, l, l);
}

float3 rgb2luma(float3 c)
{
  c = invGamma(c);
  float l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
  return gamma(float3(l, l, l));
}

float3 grayscale(float3 color)
{
  #if GRAYSCALE_INTENSITY
  color.rgb = saturate(rgb2intensity(color.rgb));
  #elif GRAYSCALE_GLEAM
  color.rgb = saturate(rgb2gleam(color.rgb));
  #elif GRAYSCALE_LUMINANCE
  color.rgb = saturate(rgb2luminance(color.rgb));
  #elif GRAYSCALE_LUMA
  color.rgb = saturate(rgb2luma(color.rgb));
  #else // Error, strategy not defined
  color.rgb = float3(1.0, 0.0, 1.0) - color.rgb;
  #endif

  return color;
}
#endif

#if ENABLE_BLACKLEVEL
float3 blacklevel(float3 color)
{
	color.rgb -= BLACKLEVEL_FLOOR;
	color.rgb = saturate(color.rgb);
	color.rgb += BLACKLEVEL_FLOOR;
	return saturate(color);
}
#endif

#if ENABLE_REFRESHLINE
float3 refreshLines(float3 color, float2 uv)
{
  float timeOver = fmod(Time * REFRESHLINE_SPEED / 5.0, 1.5) - 0.5;
  float refreshLineColorTint = timeOver - uv.y;
  if(uv.y > timeOver && uv.y - REFRESHLINE_WIDTH < timeOver ) color.rgb += (refreshLineColorTint * REFRESHLINE_STRENGTH);
  return saturate(color);
}
#endif

#if ENABLE_SCANLINES
// retro.hlsl
#define SCALED_SCANLINE_PERIOD (Scale * SCANLINE_SIZE)

float squareWave(float y)
{
  return 1.0 - fmod(floor(y / SCALED_SCANLINE_PERIOD), 2.0) * SCANLINE_FACTOR;
}

float3 scanlines(float3 color, float2 pos)
{
  float wave = squareWave(pos.y);

  // TODO:GH#3929 make this configurable.
  // Remove the && false to draw scanlines everywhere.
  if (length(color.rgb) < 0.2 && false)
  {
    return saturate(color + wave * 0.1);
  }
  else
  {
    return saturate(color * wave);
  }
}
// end - retro.hlsl
#endif

#if ENABLE_TINT
float3 tint(float3 color)
{
	color.rgb *= TINT_COLOR;
	return saturate(color);
}
#endif

#if ENABLE_GRAIN
// Grain Lookup Table
#define a0  (0.151015505647689)
#define a1 (-0.5303572634357367)
#define a2  (1.365020122861334)
#define b0  (0.132089632343748)
#define b1 (-0.7607324991323768)

float permute(float x)
{
  x *= (34.0 * x + 1.0);
  return 289.0 * frac(x * 1.0 / 289.0);
}

float rand(inout float state)
{
  state = permute(state);
  return frac(state / 41.0);
}

float3 grain(float3 color, float2 uv)
{
  float3 m = float3(uv, fmod(Time, 5.0) / 5.0) + 1.0;
  float state = permute(permute(m.x) + m.y) + m.z;
  
  float p = 0.95 * rand(state) + 0.025;
  float q = p - 0.5;
  float r2 = q * q;
  
  float grain = q * (a2 + (a1 * r2 + a0) / (r2 * r2 + b1 * r2 + b0));
  color.rgb += GRAIN_INTENSITY * grain;

  return saturate(color);
}
#endif

float4 main(PSInput pin) : SV_TARGET {
  // Use pos and uv in the shader the same as we might use
  // Time, Scale, Resolution, and Background. Unlike those,
  // they are local variables in this implementation and should
  // be passed to any functions using them.
  
  float4 pos = pin.pos;
  float2 uv = pin.uv;
  
  #if SHADERed
  // Must be inlined to the shader or it breaks single-step debugging
  // Patches the pin pos and uv
  PSInput patchedPin = patchCoordinates(pin);
  pos = patchedPin.pos;
  uv = patchedPin.uv;

  // Patches in the UV Debug output
  DebugOut debugOut = debug(pos, uv);
  if (debugOut.show) { return debugOut.color; }
  #endif

//-- Shader goes here --//
  #if ENABLE_CURVE
  uv = transformCurve(uv);

  // TODO: add monitor visuals and make colors static consts
  // Outer Box
  if(uv.x <  -0.025 || uv.y <  -0.025) return float4(0.00, 0.00, 0.00, 1.0); 
  if(uv.x >   1.025 || uv.y >   1.025) return float4(0.00, 0.00, 0.00, 1.0); 
  // Bezel
  if(uv.x <  -0.015 || uv.y <  -0.015) return float4(0.03, 0.03, 0.03, 1.0);
  if(uv.x >   1.015 || uv.y >   1.015) return float4(0.03, 0.03, 0.03, 1.0);
  // Screen Border
  if(uv.x <  -0.001 || uv.y <  -0.001) return float4(0.00, 0.00, 0.00, 1.0);
  if(uv.x >   1.001 || uv.y >   1.001) return float4(0.00, 0.00, 0.00, 1.0);
  #endif
  
  // Temporary color to be substituted
  float4 color = float4(1,0,1,-1);

  // We need to track two different uv's. The screen uv is effectively
  // the CRT glass. We also want to track uv for when we sample from the
  // texture.
  float2 screenuv = uv;
  #if ENABLE_OVERSCAN
  // Modifies uv while screenuv remains the same.
  color = overscan(color, screenuv, uv);
  #endif

  // If no options are selected, this will just display as normal.
  // This must come after we've adjusted the uv for OVERSCAN.
  if (color.a < 0.0) {
    color = shaderTexture.Sample(samplerState, uv);
  }
   
  #if ENABLE_BLOOM
  color.rgb = bloom(color.rgb, uv);
  #endif
  
  #if ENABLE_BLUR
  color.rgb = blur(color.rgb, uv);
  #endif
  
  #if ENABLE_GRAYSCALE
  color.rgb = grayscale(color.rgb);
  #endif
  
  #if ENABLE_BLACKLEVEL
  color.rgb = blacklevel(color.rgb);
  #endif
  
  #if ENABLE_REFRESHLINE
  color.rgb = refreshLines(color.rgb, screenuv);
  #endif
  
  #if ENABLE_SCANLINES
  color.rgb = scanlines(color.rgb, pos.xy);
  #endif
  
  #if ENABLE_TINT
  color.rgb = tint(color.rgb);
  #endif
  
  #if ENABLE_GRAIN
  color.rgb = grain(color.rgb, screenuv);
  #endif

  // finish: brightness and vignette (both do nothing at their defaults)
  float2 fromCentre = screenuv - 0.5;
  color.rgb *= BRIGHTNESS * saturate(1.0 - dot(fromCentre, fromCentre) * VIGNETTE * 2.0);
  
  return color;
//-- Shader goes here --//
}

#if SHADERed
#include "SHADERed/PS-DebugPatch.hlsl"
#include "SHADERed/PS-CoordinatesPatch.hlsl"
#endif