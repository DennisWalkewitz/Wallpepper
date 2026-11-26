// @name: Grid
// @tags: retro, terminal-only
// A grid of thin lines laid over the terminal, like graph paper: spacing, thickness, colour, main lines and a
// slow drift can all be set.

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
#define GRID_SIZE 25.0        // distance between two grid lines, in pixels (4..200)
#define LINE_WIDTH 1.0        // thickness of the lines, in pixels (1..12)
#define VERTICAL_LINES 1      // 1 = draw the vertical lines
#define HORIZONTAL_LINES 1    // 1 = draw the horizontal lines
#define GRID_STRENGTH 1.0     // how strongly the lines light up the picture (0..5)
#define GRID_R 0.1            // colour added on the lines: red
#define GRID_G 0.2            // colour added on the lines: green
#define GRID_B 0.15           // colour added on the lines: blue
#define MAJOR_EVERY 4         // every so many lines one is a main line (2..10)
#define MAJOR_BOOST 0.0       // extra brightness of the main lines, 0 = all lines alike (0..4)
#define SCROLL_X 0.0          // the grid drifts sideways: pixels per second (-200..200)
#define SCROLL_Y 0.0          // the grid drifts up or down: pixels per second (-200..200)

// Settings - Debug (in brackets so that the app does not list it as a setting)
#define DEBUG (0)

// Brightness of the grid at one coordinate (in pixels): 0 = between lines, 1 = on a line, more on a main line.
float gridLine(float x, float drift)
{
  float big = GRID_SIZE * float(MAJOR_EVERY);
  float shifted = x + fmod(drift, big) + big;          // always positive, so that fmod never sees a negative number
  if (fmod(shifted, GRID_SIZE) >= LINE_WIDTH) return 0.0;
  return fmod(shifted, big) < LINE_WIDTH ? 1.0 + MAJOR_BOOST : 1.0;
}

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
  float lineX = VERTICAL_LINES > 0 ? gridLine(uv.x * Resolution.x, Time * SCROLL_X) : 0.0;
  float lineY = HORIZONTAL_LINES > 0 ? gridLine(uv.y * Resolution.y, Time * SCROLL_Y) : 0.0;
  float onLine = max(lineX, lineY);
  if (onLine > 0.0)
  {
    color.rgb += float3(GRID_R, GRID_G, GRID_B) * (GRID_STRENGTH * onLine);
  }
  return color;
//-- Shader goes here --//
}

#if SHADERed
#include "SHADERed/PS-DebugPatch.hlsl"
#include "SHADERed/PS-CoordinatesPatch.hlsl"
#endif