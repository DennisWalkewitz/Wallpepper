// @name: KodeLife
// @tags: terminal-only
// Inspired by the KodeLife default shader: soft bands of red, green and blue that bend and fold into each
// other. It replaces the whole terminal picture unless SHOW_TEXT is switched on.

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
// (in brackets so that the app does not list these SHADERed debugging switches as settings)
#define DEBUG                   (0)
#define DEBUG_ROTATION          (0.25)
#define DEBUG_SEGMENTS          (1)
#define DEBUG_OFFSET            (0.425)
#define DEBUG_WIDTH             (0.15)
#define SHOW_UV                 (0)
#define SHOW_POS                (0)

// ---- Wallpepper settings: every number below is a control in the app ----
#define SPEED 1.0             // time scale: 0 = frozen, 2 = twice as fast (0..4)
#define ZOOM 1.0              // magnify the pattern: below 1 = more waves on screen (0.2..4)
#define WAVES_X 2.0           // how busy the pattern is from left to right (0..12)
#define WAVES_Y 3.0           // how busy the pattern is from top to bottom (0..12)
#define BLUE_SPEED 1.0        // how fast the blue part of the colours fades in and out (0..6)
#define TINT_R 1.0            // colour multiplied over the pattern, white = unchanged: red
#define TINT_G 1.0            // colour multiplied over the pattern, white = unchanged: green
#define TINT_B 1.0            // colour multiplied over the pattern, white = unchanged: blue
#define BRIGHTNESS 1.0        // overall brightness (0.2..2)
#define SHOW_TEXT 0           // 1 = draw the terminal text on top of the pattern (the original hides it), 0 = pattern only
#define TEXT_VEIL 0.5         // with SHOW_TEXT on: darkening behind the text so it stays readable (0..1)

// SHADERed (the shader editor these effects were written in) defines this; everywhere else it is off.
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

float4 main(PSInput pin) : SV_TARGET
{
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
  float4 fg = shaderTexture.Sample(samplerState, uv);   // terminal text, only used with SHOW_TEXT
  float t = Time * SPEED;
  uv = (-1.0 + 2.0 * uv) / max(ZOOM, 0.01);
  float4 color = float4(
    abs(sin(cos(t + WAVES_Y * uv.y) * WAVES_X * uv.x + t)),
    abs(cos(sin(t + WAVES_X * uv.x) * WAVES_Y * uv.y + t)),
    cos(t * BLUE_SPEED) * 0.5 + 0.5,
    1.0f);
  color.rgb *= float3(TINT_R, TINT_G, TINT_B) * BRIGHTNESS;
  if (SHOW_TEXT > 0)
  {
    color.rgb *= 1.0 - TEXT_VEIL * fg.a;
    color.rgb = color.rgb * (1.0 - fg.a) + fg.rgb;      // premultiplied text on top
  }
return color;
//-- Shader goes here --//
}

#if SHADERed
#include "SHADERed/PS-DebugPatch.hlsl"
#include "SHADERed/PS-CoordinatesPatch.hlsl"
#endif