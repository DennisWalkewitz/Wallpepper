// @name: Demonstration
// @tags: terminal-only
// A small demonstration of what a terminal effect can do: the terminal colours are inverted and washed with
// a colour gradient whose brightness pulses slowly.

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
#define DEBUG                   (1)
//#define DEBUG_ROTATION          0.25
#define DEBUG_ROTATION          frac(Time/10)
#define DEBUG_SEGMENTS          (1)
#define DEBUG_OFFSET            (0.375)
#define DEBUG_WIDTH             (0.25)
#define SHOW_UV                 (0)
#define SHOW_POS                (0)

// ---- Wallpepper settings: every number below is a control in the app ----
#define INVERT 1              // 1 = invert the terminal colours first (light text becomes dark), 0 = leave them
#define GRADIENT 1.0          // strength of the colour gradient across the window, 0 = none (0..3)
#define PULSE_SPEED 1.0       // how fast the brightness pulses (0..10)
#define PULSE_DEPTH 0.5       // how much the brightness goes up and down, 0 = steady (0..1)
#define PULSE_LEVEL 0.5       // brightness around which it pulses (0..1.5)
#define TINT_R 1.0            // colour multiplied over the result, white = unchanged: red
#define TINT_G 1.0            // colour multiplied over the result, white = unchanged: green
#define TINT_B 1.0            // colour multiplied over the result, white = unchanged: blue
#define BRIGHTNESS 1.0        // overall brightness (0.2..3)

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
  float4 color = shaderTexture.Sample(samplerState, uv);
  
  // Invert the colors
  if (INVERT > 0) color.rgb = float3(1.0, 1.0, 1.0) - color.rgb;
  // Do something to demonstrate using uv, pos, Resolution, and Time
  color.rgb *= saturate(float3(uv.x, (uv.y + (pos.x / Resolution.x)) / 2.0,
    pos.x / Resolution.x) * GRADIENT + (sin(Time * PULSE_SPEED) * PULSE_DEPTH + PULSE_LEVEL)) * (float3(TINT_R, TINT_G, TINT_B) * BRIGHTNESS);
  
  return color;
//-- Shader goes here --//
}

#if SHADERed
#include "SHADERed/PS-DebugPatch.hlsl"
#include "SHADERed/PS-CoordinatesPatch.hlsl"
#endif