# Writing shaders for Wallpepper

Every wallpaper is a single HLSL pixel shader that follows the **Windows Terminal pixel shader contract**
(`experimental.pixelShaderPath`). The same file runs unchanged in Windows Terminal, in Wallpepper on Windows
(Direct3D 11, `ps_4_0`) and in Wallpepper on Linux (translated to GLSL 3.30 at load time).

## The contract

```hlsl
Texture2D shaderTexture;      // terminal text; fully transparent in Wallpepper
SamplerState samplerState;
cbuffer PixelShaderSettings {
    float  Time;              // seconds
    float  Scale;             // UI scale
    float2 Resolution;        // pixels
    float4 Background;        // terminal background colour
};

float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
```

`tex` runs from (0,0) top-left to (1,1) bottom-right. The texture is **transparent where there is no text**,
so composite the text over your image instead of reading its colour as a background:

```hlsl
float4 fg = shaderTexture.Sample(samplerState, tex);
float2 o = 2.0 * Scale / Resolution;
float cover = max(fg.a, max(shaderTexture.Sample(samplerState, tex + o).a, shaderTexture.Sample(samplerState, tex - o).a));
col *= 1.0 - TEXT_VEIL * cover;                     // darken behind text so it stays readable
col = col * (1.0 - fg.a) + fg.rgb;                  // premultiplied text on top
col += (hash12(tex * Resolution) - 0.5) / 255.0;    // dither against banding
return float4(saturate(col), 1.0);
```

Sample `shaderTexture` only at the top level of `main` (never inside a loop or a branch).

## Header

```hlsl
// @name: Pretty Name
// @tags: nature, calming
// One or two sentences describing what you see.
```

Tags in use: `featured`, `space`, `water`, `rain`, `nature`, `calming`, `meditative`, `abstract`, `retro`,
`math`, `fun`, `tech`, `3d`, `benchmark`, `multimonitor`, `terminal-only`.

An optional third line names cheaper settings for weak machines:

```hlsl
// @light: SAMPLES=1, BOUNCES=2, FLOOR_REFLECTION=0
```

Wallpepper fits every wallpaper to a resource budget by lowering render scale and frame rate. When that is not
enough for a heavy shader, it applies the `@light` values automatically (settings the user changed by hand always
win). Name the settings that cost the most and still leave the picture recognisable.

## Settings

Every numeric `#define NAME value // description` becomes a control in the app:

| Form | Control |
| --- | --- |
| `#define SPEED 1.0 // how fast it moves` | slider |
| `#define STARS 1 // 1 = on, 0 = off` | toggle (integer 0/1) |
| `#define LAYERS 4 // number of layers (1..8)` | stepper (other integers) |
| `#define SKY_R 0.2` / `SKY_G` / `SKY_B` | colour picker (a `_R`, `_G`, `_B` triplet) |

Give every shader plenty of them, each with a plain-language description and a sensible default. Do not use
numeric `#define`s for internal constants (they would show up as controls); write the literal instead.

## Multi-monitor extension (optional)

```hlsl
cbuffer MonitorLayout : register(b1) {
    float4 Monitors[8];   // x, y, w, h of each monitor in span UV
    float  MonitorCount;  // 0 in Windows Terminal, >= 1 in Wallpepper
    float  MonitorIndex;
    float2 MonitorPad;
};
```

In span mode `Resolution` is the size of the whole virtual desktop and `tex` covers all monitors.

## Portable subset

Windows compiles a shader with the real HLSL compiler. The Linux build and the web pages translate it to GLSL
with a small source-level translator (`linux/src/translate.cpp`). It knows nothing about types: it rewrites a
fixed list of constructs and passes everything else through unchanged. GLSL ES, which the browser uses, is the
strictest of the three compilers, so a shader that passes `wallpepper check --es` is usually fine everywhere.

What the translator takes care of:

- the contract's declarations: `Texture2D`, `SamplerState`, every `cbuffer` (whatever its name), `: register(...)`
- semantics after declarations (`: SV_POSITION`, `: TEXCOORD0`, `: SV_TARGET`), and both forms of the entry
  point: the two parameters shown above, or one struct that holds them with the position first
- `tex.Sample(sampler, uv)`
- `static`, and the attributes `[loop]`, `[unroll(n)]`, `[branch]`, `[flatten]`, `[fastopt]`
- flat array initialisers: `static const float w[3] = { 0.2, 0.5, 0.3 };`
- names, through macros: `float2..4`, `int2..4`, `uint2..4`, `bool2..4`, `float2x2`, `float3x3`, `float4x4`,
  `lerp`, `frac`, `fmod`, `atan2`, `rsqrt`, `ddx`, `ddy`, `saturate`, and `mul` for matrices
- a shader's own `#define vec3 float3` style adapter, and its own `mod` function

What it does not do, so keep to these rules:

- **No implicit conversions.** Write float literals with a decimal point (`1.0 - x`, not `1 - x`), build
  vectors with all their components (`float3(0.0, 0.0, 0.0)`, never `float3(0.0)`, which HLSL rejects, or
  `(float3)0`), write `float(i)` instead of `(float)i`, and never pass or assign a vector of the wrong size.
- `%` only on integers; `fmod` for floats. `|` and `&` only on integers; `||` and `&&` for conditions.
- Matrices only through their full constructors and `mul`. No non-square matrices.
- No intrinsics without a GLSL namesake: `clip`, `mad`, `rcp`, `sincos`, `SampleLevel`, `Load`.
- No nested array or struct initialisers, no global variables that are written to.
- Loops with constant bounds; unique loop variable names within a function; assign every `out` parameter on
  all paths.
- Avoid names reserved in either language: `input`, `output`, `sample`, `filter`, `mix`, `smooth`, `flat`,
  `point`, `line`, `half`, `fixed`, `active`, `common`, `partition`, `noise`, `patch`, `texture`. Do not
  give a function of your own the name of a GLSL built-in (`mod` is the one exception).
- One pass, no feedback buffers, no extra textures.

## Preview and check without Windows

```
wallpepper check my.hlsl                     # does it translate and compile? Errors carry the file's line numbers
wallpepper check my.hlsl --es                # the same as GLSL ES, as the browser gallery needs it
wallpepper render my.hlsl out.png --size 1280x720 --time 6
wallpepper render my.hlsl out.png --monitors 2          # as if spanning two monitors
wallpepper render my.hlsl out.png --text                # with a sample terminal text layer
wallpepper render my.hlsl f.png --frames 48 --fps 12    # f-0001.png ... f-0048.png
wallpepper run my.hlsl --window                         # live
```

See [LINUX.md](LINUX.md) for building `wallpepper`. Without a GPU it renders on the CPU (Mesa llvmpipe), which is
enough to check the look and catch most errors. The sample text layer is rows of coloured blocks where a
terminal would have words. To turn numbered frames into a GIF, use a tool such as ffmpeg:
`ffmpeg -framerate 12 -i f-%04d.png out.gif`. The real HLSL compiler is stricter in places;
`windows\check-shaders.cmd` compiles every shader with it and measures the GPU time per 1080p frame.
