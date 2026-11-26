# Wallpepper GPU benchmark (experimental)

`shaders/rtx-pathtracer.hlsl` is a brute-force path tracer written as an ordinary Wallpepper shader. Its normal settings
are light enough to use as a wallpaper. Compiled with `BENCHMARK 1` it switches to a fixed **reference workload**:
heavy enough that an NVIDIA RTX 5090, the card the score scale is anchored to, does not reach its frame-rate
ceiling at 4K.

Both builds run the same procedure, so scores are comparable between Windows (Direct3D 11) and Linux (OpenGL).
A [browser version](#in-the-browser) covers everything else.

## Procedure (version 0)

1. Compile `rtx-pathtracer.hlsl` with the single override `BENCHMARK 1` (all other settings are ignored in that mode).
2. Render off screen into a 3840 x 2160 RGBA8 target, v-sync off, `Scale` = 1, text texture = 1x1 transparent.
3. `Time` advances by exactly 1/60 s per frame, so every run renders the same frames.
4. Each frame is drawn as 16 x 9 tiles of 240 x 240 pixels (scissor rectangle, flush after every tile). Slow GPUs
   then never spend seconds inside a single draw call, which would trip the driver watchdog.
5. One warm-up frame is rendered and discarded. Then frames are rendered until at least 10 seconds have passed
   and at least 3 frames are done (at most 600 frames). Timing includes waiting for the GPU to finish.
6. Throughput = megapixels rendered per second (frames x 8.2944 / seconds).
7. **Score = round(10000 x throughput / reference throughput).**

The reference throughput is that of an RTX 5090. Until it has been measured on a real card it is the
provisional constant **497.664 MP/s** (60 frames per second at 4K), and every result is labelled
`provisional` (`"calibrated": false` in JSON). After calibration a 5090 scores 10000 by definition.

`--quick` renders 1280 x 720 and stops after 5 seconds instead of 10 (the tiles, the three-frame minimum and
the 600-frame limit stay; a card near the reference speed reaches that limit after about a second). The score formula is the
same (it is based on megapixels per second); the result is labelled `quick`.

## Output

Human-readable summary plus, with `--json`, one JSON object:

```json
{ "benchmark": "wallpepper-pathtracer", "version": 0, "calibrated": false, "mode": "full",
  "gpu": "...", "driver": "...", "api": "d3d11 | opengl | webgl2", "os": "...",
  "width": 3840, "height": 2160, "frames": 37, "seconds": 10.21,
  "frame_ms_avg": 275.9, "frame_ms_min": 270.1, "frame_ms_max": 291.4,
  "mpixels_per_second": 30.06, "score": 604 }
```

## Running it

```
Wallpepper.exe --bench [--quick] [--json] [--out result.json]         (Windows; also in the app: Settings > Benchmark)
wallpepper bench [--quick] [--json] [--out result.json] [--size WxH]  (Linux; works without a display via EGL)
https://denniswalkewitz.github.io/Wallpepper/                         (any browser with WebGL 2; nothing to install)
```

`--size` exists for testing on software renderers; results at other sizes are labelled `custom`.

### In the browser

The page in `web/` runs the same procedure on WebGL 2, for systems without a native build (macOS, phones,
tablets, machines where nothing can be installed). Its shader, `web/shaders/benchmark.frag`, is not a second
implementation: `web/build.sh` generates it from `rtx-pathtracer.hlsl` with
`wallpepper translate --es --set BENCHMARK=1`, and it is regenerated every time the site is published.

What a browser forces to be different:

- The page waits for the graphics card by reading one pixel back (`gl.finish` does not wait in browsers). On
  slow hardware it does so several times per frame and pauses in between, so that the page stays responsive
  and the browser does not drop the graphics context. The pauses are not counted: `seconds` is the sum of the
  measured frame times.
- If the device cannot provide a 3840 x 2160 image, the run uses 2560 x 1440, 1920 x 1080 or 1280 x 720 and is
  labelled `custom`.
- `gpu` is the name the browser reveals (often an ANGLE string, sometimes masked), `driver` is the WebGL
  version string, `api` is `webgl2` and `os` names the browser and the system.

**Comparability.** Between the page and the graphics card sit the browser and, in most browsers, the ANGLE
translation layer, which compiles the shader a second time for Direct3D, Metal or Vulkan. Browser scores are
therefore close to, but not strictly comparable with, the scores of the native builds. Compare browser
scores with browser scores. So far the page has only been run in headless Chrome on a software renderer.

## Workload

**Provisional: chosen by estimate, not yet measured on any GPU.**

With `BENCHMARK 1` the shader ignores every setting and renders its scene (a round hall with a glass sphere, four
other spheres, a plinth, standing slabs, three lights and haze) with constants fixed in a block of bracketed
`#define`s at the top of `rtx-pathtracer.hlsl`:

- 3 paths per pixel, 4 bounces per path, haze lit at every bounce, 7 slabs; everything else as shipped.
- No path ends early (no Russian roulette, no `break`): a path that has run into a light carries on with zero weight.
- Per bounce 7 rays are traced against every object, without early exit: 1 closest-hit ray (15 primitives),
  3 shadow rays for direct light and 3 for the haze, one per light (13 primitives each).
- The random numbers depend only on pixel position and `Time`, so every run traces the same paths.

Estimated cost (counted by hand from the source: add, multiply, compare, divide, square root = 1 operation;
sin, cos, pow, exp, atan, acos = 10):

| | per bounce | per pixel (12 bounces) | per 3840 x 2160 frame |
| --- | --- | --- | --- |
| rays | 7 | 84 | 697 million |
| arithmetic operations | about 6,900 | about 84,000 | about 7.0 x 10^11 |

Of the 6,900: closest hit 960, six shadow rays 3,700, light sampling and surface reflection 1,150, haze sampling
520, random numbers 590. Cross-check: on the CPU preview renderer the workload takes as long per pixel as about
67,000 plain multiply-adds (timed against a calibration loop, +/- 15 %), which is 0.8 of the hand count.

An RTX 5090 has a nominal single-precision throughput of about 105 x 10^12 operations per second (21,760 shader
cores x 2.4 GHz x 2, counting every instruction as a fused multiply-add). Assumption: one third of that, 3.5 x 10^13
per second, arrives as useful arithmetic in a pixel shader like this one (not every instruction is a multiply-add,
neighbouring pixels follow different paths, 144 tile draws per frame). That gives

    3.5 x 10^13 / 7.0 x 10^11  =  50 frames per second (hand count), 63 with the CPU cross-check

so the estimate is "roughly 60 fps on an RTX 5090 at 4K", with an uncertainty of at least a factor of two in either
direction. Not verified: the real GPU cost, the share of nominal throughput a real card reaches, the overhead of
the tiles, and compilation with Microsoft's compiler (only the GLSL path could be tested).

When a real RTX 5090 has been measured: if it lands far from 60 fps, change `SAMPLES` / `BOUNCES` in the benchmark
block and raise the benchmark version; then set the reference throughput from the measurement.

For comparison, the shader's wallpaper defaults (4 paths, 3 bounces, haze two bounces deep, 6 slabs) cost about
69,000 operations per pixel by the same count, roughly 80 % of the reference workload.
