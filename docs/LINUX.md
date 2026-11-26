# Wallpepper on Linux (experimental)

The Linux build is one small command-line program, `wallpepper`. It reads the same HLSL files as the Windows app,
translates them to GLSL 3.30 when they are loaded and draws them with OpenGL through EGL. It can

- list and check the shader library,
- render single frames to PNG or PPM without a display,
- run a shader in a window or as the wallpaper of an X11 session,
- run the [GPU benchmark](BENCHMARK.md), also on a server without any desktop.

There is no graphical library manager on Linux yet; the command line is the interface. Read
[Status and limits](#status-and-limits) before you rely on it.

## Build

Ubuntu 24.04:

```
sudo apt install build-essential cmake libx11-dev libxrandr-dev
cmake -S linux -B build && cmake --build build
build/wallpepper list
```

`libxrandr-dev` pulls in `libxext-dev`, which is needed as well. No OpenGL development packages are required:
the few EGL and OpenGL functions Wallpepper uses are declared in `linux/src/gl.h` and looked up in `libEGL.so.1` when
the program starts. At run time it needs `libegl1` and a driver: Mesa (`libgl1-mesa-dri`) or the NVIDIA driver.

On a machine without the X11 development packages (a render server, say) the build still succeeds; CMake prints
a warning and `wallpepper run` is left out. Without `libxrandr-dev`, wallpaper mode treats all monitors as one.

Install, optionally:

```
sudo cmake --install build                    # /usr/local/bin/wallpepper, /usr/local/share/wallpepper/shaders
cmake --install build --prefix ~/.local       # or for the current user only
cmake --build build --target package          # or build/wallpepper_<version>_amd64.deb
```

### From the release archive

A release carries `wallpepper-linux-x64.tar.gz`, built on Ubuntu 24.04. Unpack it anywhere and run
`wallpepper/bin/wallpepper`; it finds its shaders in `wallpepper/share/wallpepper/shaders` next to it. It needs
glibc 2.38 or newer (Ubuntu 24.04, Debian 13 or later) and these packages, also on a server that only runs
the benchmark, because the binary is linked against the X11 libraries:

```
sudo apt install libegl1 libx11-6 libxext6 libxrandr2
```

plus a driver, as above. On an older system, build from source instead.

### Where the shaders come from

The first directory that exists wins:

1. `--dir DIR`
2. `$WALLPEPPER_SHADERS`
3. `<directory of wallpepper>/../shaders` (running from `build/` in the repository)
4. `<directory of wallpepper>/../share/wallpepper/shaders` (an installed copy)
5. `~/.local/share/wallpepper/shaders`
6. `/usr/share/wallpepper/shaders`
7. `<directory of wallpepper>/../../shaders` (a build directory inside `linux/`)

## Usage

```
wallpepper list                                    names and tags of the library (--json for scripts)
wallpepper check                                   translate and compile every shader; exit code 1 if one fails
wallpepper check my-shader.hlsl --set LAYERS=8     check single files, with settings
wallpepper render wind out.png --size 1920x1080 --time 6
wallpepper render mm-koi out.png --monitors 2      as if spanning two monitors side by side
wallpepper render crt out.png --text               with a sample text layer, for terminal effects
wallpepper render wind f.png --frames 48 --fps 12  an animation: f-0001.png to f-0048.png
wallpepper run wind                                in a window; Esc closes it
wallpepper run wind --wallpaper                    as wallpaper, one window per monitor, within the default budget
wallpepper run wind --wallpaper --budget balanced  allow it 30 % of the GPU's time
wallpepper run mm-pong --wallpaper                 shaders tagged multimonitor span all monitors (others: --span)
wallpepper translate wind                          print the GLSL the shader is translated to (-o FILE to save it)
wallpepper bench                                   the benchmark, see below
wallpepper devices                                 GPUs that can render without a display server
wallpepper --help
```

A shader is named by its file name without `.hlsl`, or given as a path. Exit codes: 0 for success, 1 when a
shader failed `check`, 2 for everything else.

**Settings.** `--set NAME=VALUE` replaces the value of the shader's `#define NAME ...` before it is compiled, the
same mechanism the Windows app uses for its sliders. It can be repeated. The value has to be a number and the
name a setting of that shader; anything else is an error. `wallpepper run` also takes `--speed X` and
`--frames N` (exit after N frames, for tests).

**Resource budget.** A wallpaper should not keep the graphics card busy, so `wallpepper run` fits itself to a
budget: a share of GPU time, summed over all monitors, and a frame-rate ceiling.

| `--budget` | GPU time | at most |
| --- | --- | --- |
| `minimal` | 8 % | 24 fps |
| `low` (default) | 15 % | 30 fps |
| `balanced` | 30 % | 60 fps |
| `high` | 60 % | 60 fps |
| `unlimited` | no limit | 60 fps, fixed scale 0.75 |

While running, it measures how long the GPU needs per frame (a timer query on a real GPU; on a software
renderer by waiting for every fourth frame) and adjusts, in this order:

1. the render scale, from 1.0 down to 0.5 (the shader draws fewer pixels and the result is stretched),
2. the frame rate, down to 12 fps,
3. the shader's **light settings**, if it has any: the cheaper values its author put in the `// @light:`
   header (see [SHADER-GUIDE.md](SHADER-GUIDE.md#header)),
4. the render scale again, down to 0.3.

So a shader with light settings switches to them before it would drop below half resolution; the Windows app
follows the same rule. The fit starts at scale 0.4, decides after a handful of measurements and prints one line
when it has come to rest:

```
wallpepper: scale 0.50, 26 fps, GPU 13% of budget 15%
```

It re-fits only when the load leaves the band between 60 % and 100 % of the budget, so the picture does not
keep changing size: with measurements that scatter by up to 5 % it stays put once settled, at 10 % it takes
a rare single step. `--scale S` or `--fps N` fixes that value and leaves only the other one to the fitter.

Your own `--set` values always win over the light settings. `--light` uses them from the start, `--full`
never. `wallpepper check` verifies that every `@light` line names real settings, gives them numbers and compiles.

**GLSL ES.** `translate`, `check` and `render` take `--es`: the shader is then translated to GLSL ES 3.00, the
dialect of OpenGL ES 3 and WebGL 2, and `check --es` / `render --es` compile it in an OpenGL ES 3.0 context.
That is how the pages in `web/` get their shaders: `web/build.sh` runs `wallpepper translate --es` over the
library and writes `web/shaders/*.frag` plus a `manifest.json`. A shader that does not compile as GLSL ES makes
it fail, and so does the test script when `web/shaders/` no longer matches the shaders: run `web/build.sh` after
changing one. The site's deployment runs it as well. A shader that sticks to the
[portable subset](SHADER-GUIDE.md#portable-subset) works in both dialects.

## Benchmark on a headless NVIDIA server

`wallpepper bench` needs no X server and no Wayland compositor. It asks EGL for the machine's devices
(`EGL_EXT_device_enumeration`), creates an OpenGL context directly on the first hardware device that works
(`EGL_EXT_platform_device`) and renders into an offscreen image. If no device can be used it falls back to
Mesa's surfaceless platform and then to the default display.

What the server needs:

- the proprietary NVIDIA driver **including its EGL library**, `libEGL_nvidia.so.0`. On Ubuntu that file is in
  the package `libnvidia-gl-<version>`; check whether your driver installation includes it, because the
  `nvidia-headless-<version>` metapackages are meant for compute and may leave it out:
  `ls /usr/lib/x86_64-linux-gnu/libEGL_nvidia.so.0`
- `libegl1` (the vendor-neutral `libEGL.so.1`),
- `build-essential cmake` to build. The X11 packages are not needed for the benchmark.

```
build/wallpepper devices            # the NVIDIA card must be listed as a hardware device, with its name
build/wallpepper bench --quick      # 1280 x 720, about 5 seconds: a first sanity check
build/wallpepper bench --json --out result.json
```

`wallpepper devices` prints one line per device with the OpenGL renderer it gives. If the automatic choice is not the
card you want, pass `--device N` to `bench`. The summary names the renderer and the driver version. If it shows
`llvmpipe` or another software renderer, no GPU was used; Wallpepper prints a warning then, and the score means
nothing.

With `--json` the result object of [BENCHMARK.md](BENCHMARK.md) goes to standard output and the readable
summary to standard error; `--out FILE` writes the same object to a file. `--size WxH` and `--shader NAME`
exist for testing; their results are labelled `custom`.

## Wallpaper at login

Wallpaper mode opens one borderless window per monitor with the type `_NET_WM_WINDOW_TYPE_DESKTOP`, lowers it
below all other windows and makes it transparent to mouse input. That is how a background window is supposed to
announce itself to a window manager on Xorg (EWMH).

To start it with the session, create `~/.config/autostart/wallpepper.desktop`:

```
[Desktop Entry]
Type=Application
Name=Wallpepper
Exec=wallpepper run wind --wallpaper --budget low
```

Stop it with `pkill wallpepper`. This example has not been tried on a real desktop yet, see below.

## Tests

```
linux/tests/run.sh
```

builds into `build/` and then runs, without a GPU (Mesa's llvmpipe renders everything):

- unit tests of the translator and of the budget policy, which need no graphics at all
  (`ctest --test-dir build` runs just these),
- `wallpepper check` over the library, as GLSL 3.30 and as GLSL ES 3.00,
- every shader rendered with OpenGL and with OpenGL ES: the two images must be identical and not blank,
- properties of the shader contract (where `tex` starts, the monitor uniforms, the text layer),
- what the command line has to refuse (unknown options and settings, values that are not numbers),
- that `web/shaders/` is what `web/build.sh` generates from the current shaders,
- a tiny benchmark, with its JSON validated field by field,
- `wallpepper run` under Xvfb, in a window and as wallpaper.

It is what CI runs. Extra packages for it: `python3 xvfb libegl1 libgl1-mesa-dri`, and `node` for a syntax
check of the web pages' scripts.

## Status and limits

What has been run, all of it with Mesa's software renderer (llvmpipe) on Ubuntu 24.04:

- The whole library compiles as GLSL 3.30 and as GLSL ES 3.00, and the two draw identical images. The test
  script checks this for every shader on every run.
- Headless rendering and the benchmark through all three ways of getting a context (EGL device, surfaceless,
  default display).
- Every file in `web/shaders/` compiles in headless Chrome (WebGL 2 through ANGLE).
- Window and wallpaper mode under Xvfb, without a window manager. Two monitors and span mode were tried there
  with a build against a stand-in for the XRandR header, because the test machine had no `libxrandr-dev`.

What has **not** been run yet:

- **Any real GPU.** That includes the NVIDIA path the benchmark was written for. GLSL compilers differ in
  strictness, so a shader that compiles on Mesa may still need a fix on another driver.
- **A real desktop session.** Wallpaper mode has only been smoke-tested under Xvfb. How GNOME, KDE and other
  desktops stack a desktop-type window relative to their own wallpaper and desktop icons is untested.

Known limits:

- **Wayland.** On Ubuntu 24.04's default GNOME Wayland session, a desktop-type X11 window may not be placed
  behind the desktop icons and windows the way it is on an Xorg session (Mutter has no layer-shell protocol).
  Wallpaper mode is designed for Xorg sessions ("Ubuntu on Xorg" on the login screen). `wallpepper run` in a
  normal window should work through XWayland, but that has not been tried either.
- **Terminal-only shaders** are effects on a terminal's text. As a wallpaper they have nothing to work on: the
  text layer is empty (one transparent texel). `wallpepper render --text` gives them a sample layer to show.
- **No library window, tray icon or settings store.** Settings are `--set` arguments; there is no configuration
  file. One shader runs on all monitors; there are no per-monitor shaders, filters or web wallpapers.
- **No pausing.** Rendering does not stop when a fullscreen window covers the desktop, on battery or when the
  session is locked. The budget keeps the load low, but it never reaches zero.
- **The budget on real hardware.** The fitting policy is tested with simulated frame times, and the whole loop
  with llvmpipe, where the time is measured by waiting for the renderer. The timer-query measurement used on
  real GPUs has not produced a single trusted number yet. The budget counts the time for drawing the shader;
  copying the image to the window and the compositor's work come on top. The policy assumes that GPU time
  grows with the number of pixels; where a fixed cost per frame dominates, it settles for a lower scale than
  the budget would allow.
- **Monitor layout** is read once at start. After plugging or rearranging monitors, restart `wallpepper`.
- **Frame pacing** is a timer, not v-sync, so tearing is possible where no compositor is running.
- **Long runs.** The shader contract passes `Time` as a 32-bit float of seconds. After a day its steps are 8 ms
  apart, after three days 31 ms, and motion gets visibly less smooth. Restart `wallpepper` now and then; the
  Windows app has the same limit.
- **X errors.** If the X server reports an error, `wallpepper run` prints it and exits with code 2.
- `Scale` is always 1 and `Background` is always black.
