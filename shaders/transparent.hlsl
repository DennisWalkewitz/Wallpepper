// @name: Transparent
// @tags: terminal-only
// This shader helps to add transparency for vim themes that use xterm colors: one chosen colour of the
// terminal picture (by default the dark grey 8/255) is made see-through.

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
#define KEY_R 0.031373        // the colour that becomes transparent (xterm colour 8/255 grey by default): red
#define KEY_G 0.031373        // the colour that becomes transparent: green
#define KEY_B 0.031373        // the colour that becomes transparent: blue
#define KEY_TOLERANCE 0.002   // how close a colour must be to count; 0.002 = only exactly that colour (0.002..0.5)
#define SOFT_EDGE 0.0         // colours a little further away become partly transparent: 0 = hard cut (0..0.5)
#define KEY_OPACITY 0.0       // how much of the keyed colour stays visible: 0 = fully transparent (0..1)
#define INVERT 0              // 1 = keep only the key colour and make everything else transparent
#define TEXT_OPACITY 1.0      // opacity of everything that is kept (0.1..1)

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

// Set the color that is supposed to be transparent
static const float3 chromaKey = float3(KEY_R, KEY_G, KEY_B);

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
	
  // Filter by chroma key. The original compared for exact equality with 8/255; a tolerance of half an
  // 8-bit step selects the same pixels and still works when the colour comes from a colour picker.
  float3 away = abs(color.rgb - chromaKey);
  float dist = max(away.r, max(away.g, away.b));
  float keep = dist <= KEY_TOLERANCE ? 0.0 : 1.0;
  if (SOFT_EDGE > 0.0) keep = saturate((dist - KEY_TOLERANCE) / max(SOFT_EDGE, 0.0001));
  if (INVERT > 0) keep = 1.0 - keep;

  return color * ((keep + (1.0 - keep) * KEY_OPACITY) * TEXT_OPACITY);
//-- Shader goes here --//
}

#if SHADERed
#include "SHADERed/PS-DebugPatch.hlsl"
#include "SHADERed/PS-CoordinatesPatch.hlsl"
#endif