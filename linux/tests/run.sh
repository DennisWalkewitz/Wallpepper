#!/usr/bin/env bash
# Builds Wallpepper and tests it without a GPU or a desktop: everything renders on Mesa's software rasterizer
# (llvmpipe), the window test runs under Xvfb. This is what CI runs on ubuntu-24.04.
#
#   linux/tests/run.sh [BUILD_DIR]        default build directory: <repository>/build
#
# Needs: build-essential cmake libx11-dev libxrandr-dev (build), libegl1 libgl1-mesa-dri python3 (tests),
# xvfb (window tests) and node (syntax check of web/*.js); the last two are skipped when missing.
set -u

root=$(cd "$(dirname "$0")/../.." && pwd)
build=${1:-$root/build}
wallpepper=$build/wallpepper
export WALLPEPPER_SHADERS=$root/shaders
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

passed=0 failed=0 skipped=0
pass() { passed=$((passed + 1)); echo "PASS  $1"; }
skip() { skipped=$((skipped + 1)); echo "SKIP  $1"; }
fail() {
    failed=$((failed + 1))
    echo "FAIL  $1"
    [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/      /'
}
# outcome STATUS NAME [DETAIL]: passes NAME when STATUS, the exit status of a test just made, is 0.
outcome() {
    if [ "$1" -eq 0 ]; then pass "$2"; else fail "$2" "${3:-}"; fi
}
# expect NAME COMMAND...: passes when the command succeeds; its output is shown when it does not.
expect() {
    local name=$1 output
    shift
    if output=$("$@" 2>&1); then pass "$name"; else fail "$name" "$output"; fi
}

# ---- build ----------------------------------------------------------------------------------------------------
log=$work/build.log
if cmake -S "$root/linux" -B "$build" >"$log" 2>&1 && cmake --build "$build" -j "$(nproc)" >>"$log" 2>&1; then
    pass "build"
    ! grep -q "warning:" "$log"
    outcome $? "build without compiler warnings" "$(grep -A3 "warning:" "$log")"
else
    fail "build" "$(tail -n 30 "$log")"
    echo; echo "RESULT: build failed, nothing else was tested"
    exit 1
fi

# Small shaders for the tests below.
main='float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET'
printf '%s { return float4(tex, 0.0, 1.0) }\n' "$main" >"$work/broken.hlsl"
printf '%s { return float4(tex.x, tex.y, pos.y / Resolution.y, 1.0); }\n' "$main" >"$work/corners.hlsl"
printf '%s { return float4(MonitorCount / 8.0, Monitors[1].x * 0.5, shaderTexture.Sample(samplerState, tex).a, 1.0); }\n' \
    "$main" >"$work/uniforms.hlsl"
printf '// @name: Crlf Name\r\n// @tags: fun, Retro\r\n// @namespace: not a name\r\n%s { return float4(tex, 0.0, 1.0); }\r\n' \
    "$main" >"$work/crlf.hlsl"
samples="#define SAMPLES 4\n$main { return float4(float(SAMPLES) * 0.1, 0.0, 0.0, 1.0); }\n"
printf "// @light: SAMPLES=1\n$samples" >"$work/light-good.hlsl"
printf "// @light: SAMPLES=1, BOUNCES=2\n$samples" >"$work/light-bad.hlsl"

# ---- unit tests: no GPU involved ----------------------------------------------------------------------------------
expect "translate-test: the translator and the settings code" "$build/translate-test"
expect "fit-test: the budget policy with simulated frame times" "$build/fit-test"

# ---- command line -------------------------------------------------------------------------------------------------
# refuses MESSAGE COMMAND...: passes when the command exits with code 2 and prints MESSAGE.
refuses() {
    local message=$1 output code=0
    shift
    output=$("$@" 2>&1) || code=$?
    if [ "$code" -eq 2 ] && printf '%s' "$output" | grep -q -- "$message"; then pass "refused: $message"
    else fail "refused: $message" "exit code $code: $output"; fi
}
refuses "unknown option --bogus" "$wallpepper" render wind "$work/x.ppm" --bogus 1
refuses "--time needs a value" "$wallpepper" render wind "$work/x.ppm" --time
refuses "must be a whole number" "$wallpepper" render wind "$work/x.ppm" --monitors 2.5
refuses "written as .png or .ppm" "$wallpepper" render wind "$work/x.jpg"
refuses "no setting called TYPO" "$wallpepper" render wind "$work/x.ppm" --set TYPO=1
refuses "must be a number" "$wallpepper" render wind "$work/x.ppm" --set "GRASS_HEIGHT=1;"
refuses "must be a number" "$wallpepper" translate wind --set "GRASS_HEIGHT=0.3
uniform float injected;"
refuses "does not compile" "$wallpepper" render "$work/broken.hlsl" "$work/x.ppm"

# ---- library ----------------------------------------------------------------------------------------------------
count=$(find "$WALLPEPPER_SHADERS" -maxdepth 1 -name '*.hlsl' | wc -l)
listed=$("$wallpepper" list | grep -c -v ' shaders in ')
[ "$listed" -eq "$count" ] && [ "$count" -gt 0 ]
outcome $? "list: $listed of $count shaders"
expect "devices" "$wallpepper" devices
header=$("$wallpepper" list --json "$work/crlf.hlsl")
[ "$header" = '[ { "id": "crlf", "name": "Crlf Name", "tags": ["fun", "retro"], "settings": 0, "light": {} } ]' ]
outcome $? "header of a file with CRLF line endings" "$header"

# ---- every shader translates and compiles ---------------------------------------------------------------------
if "$wallpepper" check >"$work/check.txt" 2>&1; then
    pass "check: $(tail -n 1 "$work/check.txt")"
else
    fail "check" "$(grep -v '^ok ' "$work/check.txt")"
fi
if "$wallpepper" check --es >"$work/check-es.txt" 2>&1; then
    pass "check --es: $(tail -n 1 "$work/check-es.txt")"
else
    fail "check --es" "$(grep -v '^ok ' "$work/check-es.txt")"
fi
if [ -f "$WALLPEPPER_SHADERS/rtx-pathtracer.hlsl" ]; then
    expect "check: benchmark workload (BENCHMARK=1)" "$wallpepper" check rtx-pathtracer --set BENCHMARK=1
    expect "check --es: benchmark workload (BENCHMARK=1)" "$wallpepper" check --es rtx-pathtracer --set BENCHMARK=1
fi
expect "check accepts a valid @light line" "$wallpepper" check "$work/light-good.hlsl"
"$wallpepper" check "$work/light-bad.hlsl" 2>&1 | grep -q "@light: there is no setting called BOUNCES"
outcome $? "check rejects @light with an unknown setting"

# ---- rendering ------------------------------------------------------------------------------------------------
# image_stats FILE: prints "WIDTH HEIGHT DISTINCT_BYTE_VALUES" of a binary PPM or of a PNG as wallpepper writes it.
image_stats() {
    python3 - "$1" <<'PY'
import struct, sys, zlib
data = open(sys.argv[1], "rb").read()
if data[:8] == b"\x89PNG\r\n\x1a\n":
    chunks, at = {}, 8
    while at < len(data):
        size, kind = struct.unpack(">I4s", data[at:at + 8])
        body = data[at + 8:at + 8 + size]
        assert zlib.crc32(kind + body) == struct.unpack(">I", data[at + 8 + size:at + 12 + size])[0], "bad CRC"
        chunks[kind] = body
        at += 12 + size
    width, height, depth, colour = struct.unpack(">IIBB", chunks[b"IHDR"][:10])
    assert (depth, colour) == (8, 2), "not 8-bit RGB"
    rows = zlib.decompress(chunks[b"IDAT"])
    assert len(rows) == height * (1 + 3 * width), "wrong image size"
    pixels = b"".join(rows[y * (1 + 3 * width) + 1:(y + 1) * (1 + 3 * width)] for y in range(height))
else:
    magic, width, height, maximum = data.split(None, 4)[:4]
    assert magic == b"P6" and maximum == b"255", "not a binary PPM"
    pixels = data[-3 * int(width) * int(height):]
print(int(width), int(height), len(set(pixels)))
PY
}
# A frame counts as rendered when it has the requested size and is clearly not one flat colour.
for spec in blackhole:ppm wind:png mm-koi:ppm; do
    id=${spec%%:*} file=$work/$id.${spec##*:}
    if output=$("$wallpepper" render "$id" "$file" --size 160x90 --time 3 2>&1) && stats=$(image_stats "$file" 2>&1); then
        read -r width height distinct <<<"$stats"
        if [ "$width $height" = "160 90" ] && [ "$distinct" -ge 16 ]; then pass "render $id (${spec##*:}, $distinct distinct values)"
        else fail "render $id" "blank or wrong size: $stats"; fi
    else
        fail "render $id" "$output ${stats:-}"
    fi
done

"$wallpepper" render wind "$work/a.ppm" --size 96x54 --time 3 >/dev/null 2>&1
"$wallpepper" render wind "$work/b.ppm" --size 96x54 --time 3 >/dev/null 2>&1
"$wallpepper" render wind "$work/c.ppm" --size 96x54 --time 3 --set GRASS_HEIGHT=0.3 >/dev/null 2>&1
"$wallpepper" render wind "$work/d.ppm" --size 96x54 --time 7 >/dev/null 2>&1
cmp -s "$work/a.ppm" "$work/b.ppm"
outcome $? "render is deterministic"
[ -s "$work/c.ppm" ] && ! cmp -s "$work/a.ppm" "$work/c.ppm"
outcome $? "--set changes the image"
[ -s "$work/d.ppm" ] && ! cmp -s "$work/a.ppm" "$work/d.ppm"
outcome $? "--time changes the image"

# pixel FILE X Y: prints "R G B" of one pixel of a binary PPM.
pixel() {
    python3 - "$@" <<'PY'
import sys
data = open(sys.argv[1], "rb").read()
width, height = (int(v) for v in data.split(None, 3)[1:3])
pixels = data[-3 * width * height:]
at = 3 * (int(sys.argv[3]) * width + int(sys.argv[2]))
print(*pixels[at:at + 3])
PY
}
# The shader contract: tex runs from (0, 0) at the top left to (1, 1) at the bottom right, and so does pos.
"$wallpepper" render "$work/corners.hlsl" "$work/corners.ppm" --size 16x16 >/dev/null 2>&1
corners="$(pixel "$work/corners.ppm" 0 0) / $(pixel "$work/corners.ppm" 15 15)"
[ "$corners" = "8 8 8 / 247 247 247" ]
outcome $? "tex and pos start at the top left" "$corners"
# Uniforms: one monitor and no text by default; --monitors and --text change that.
"$wallpepper" render "$work/uniforms.hlsl" "$work/u1.ppm" --size 64x36 >/dev/null 2>&1
"$wallpepper" render "$work/uniforms.hlsl" "$work/u2.ppm" --size 64x36 --monitors 2 --text >/dev/null 2>&1
# Pixel (6, 8) lies in the first word of the sample text, pixel (60, 30) in no word.
uniforms="$(pixel "$work/u1.ppm" 6 8) / $(pixel "$work/u2.ppm" 6 8) / $(pixel "$work/u2.ppm" 60 30)"
[ "$uniforms" = "32 0 0 / 64 64 255 / 64 64 0" ]
outcome $? "monitor uniforms and the text layer" "$uniforms"
"$wallpepper" render wind "$work/anim.png" --size 32x18 --frames 3 --fps 2 >/dev/null 2>&1
[ -s "$work/anim-0001.png" ] && [ -s "$work/anim-0003.png" ] && ! cmp -s "$work/anim-0001.png" "$work/anim-0003.png"
outcome $? "render --frames writes numbered images" "$(ls "$work")"

# ---- the whole library, OpenGL against OpenGL ES ---------------------------------------------------------------
# Two independent GLSL compilers (the GLSL 3.30 and the GLSL ES 3.00 front end) must draw every shader alike,
# and nothing may come out blank. The text layer is on so that terminal effects have something to show.
different="" blank=""
for path in "$WALLPEPPER_SHADERS"/*.hlsl; do
    id=$(basename "$path" .hlsl)
    "$wallpepper" render "$id" "$work/gl.ppm" --size 64x36 --time 3 --text >/dev/null 2>&1
    "$wallpepper" render "$id" "$work/es.ppm" --size 64x36 --time 3 --text --es >/dev/null 2>&1
    cmp -s "$work/gl.ppm" "$work/es.ppm" || different="$different $id"
    read -r _ _ distinct <<<"$(image_stats "$work/gl.ppm" 2>/dev/null || echo 0 0 0)"
    [ "$distinct" -ge 4 ] || blank="$blank $id"
done
[ -z "$different" ]
outcome $? "all $count shaders: OpenGL and OpenGL ES draw the same image" "different:$different"
[ -z "$blank" ]
outcome $? "all $count shaders: no blank image" "blank:$blank"

# ---- the web site ---------------------------------------------------------------------------------------------
# web/shaders is generated and committed: it must be what web/build.sh makes of the current shaders.
if [ -d "$root/web/shaders" ]; then
    output=$(bash "$root/web/build.sh" "$wallpepper" "$work/web-shaders" 2>&1) &&
        output=$(diff -rq "$work/web-shaders" "$root/web/shaders" 2>&1)
    outcome $? "web/shaders is up to date ($(find "$root/web/shaders" -name '*.frag' | wc -l) shaders)" \
        "a shader or the translator changed, run web/build.sh. First of $(printf '%s\n' "$output" | wc -l) differences:
$(printf '%s\n' "$output" | head -n 3)"
fi
if command -v node >/dev/null && [ -d "$root/web" ]; then
    expect "web: JavaScript syntax" sh -c 'for file in "$1"/web/*.js; do node --check "$file" || exit 1; done' sh "$root"
else
    skip "web: JavaScript syntax (node is not installed)"
fi

# ---- benchmark ------------------------------------------------------------------------------------------------
# Tiny and on a software renderer: this validates the procedure and the JSON, not the score.
workload=()
[ -f "$WALLPEPPER_SHADERS/rtx-pathtracer.hlsl" ] || workload=(--shader blackhole)
if "$wallpepper" bench --size 96x54 --json --out "$work/bench-file.json" "${workload[@]}" >"$work/bench.json" 2>"$work/bench.txt"; then
    if output=$(python3 - "$work/bench.json" "$work/bench-file.json" <<'PY' 2>&1
import json, sys
result = json.load(open(sys.argv[1]))
assert result == json.load(open(sys.argv[2])), "--out and --json differ"
types = {"benchmark": str, "version": int, "calibrated": bool, "mode": str, "gpu": str, "driver": str, "api": str,
         "os": str, "width": int, "height": int, "frames": int, "seconds": float, "frame_ms_avg": float,
         "frame_ms_min": float, "frame_ms_max": float, "mpixels_per_second": float, "score": int}
assert list(result) == list(types), "fields: %s" % list(result)
for key, kind in types.items():
    assert type(result[key]) is kind, "%s is %s" % (key, type(result[key]).__name__)
assert (result["benchmark"], result["version"], result["calibrated"]) == ("wallpepper-pathtracer", 0, False)
assert (result["mode"], result["api"]) == ("custom", "opengl")
assert (result["width"], result["height"]) == (96, 54) and 3 <= result["frames"] <= 600
assert result["seconds"] >= 10 or result["frames"] == 600, "stopped early"
assert result["frame_ms_min"] <= result["frame_ms_avg"] <= result["frame_ms_max"]
megapixels = result["frames"] * 96 * 54 / 1e6 / result["seconds"]   # the JSON numbers are rounded
assert abs(megapixels - result["mpixels_per_second"]) < 0.01 * megapixels + 0.006, "throughput"
assert abs(result["score"] - 10000 * megapixels / 497.664) <= 1, "score"
print("%d frames in %.1f s, %.2f MP/s, score %d on %s" % (result["frames"], result["seconds"],
      result["mpixels_per_second"], result["score"], result["gpu"]))
PY
    ); then pass "bench: $output"; else fail "bench: JSON" "$output"$'\n'"$(cat "$work/bench.json")"; fi
    if grep -q "llvmpipe" "$work/bench.json" && ! grep -q "WARNING" "$work/bench.txt"; then
        fail "bench: warns about software rendering" "$(cat "$work/bench.txt")"
    fi
else
    fail "bench" "$(cat "$work/bench.txt")"
fi

# ---- live rendering under Xvfb --------------------------------------------------------------------------------
if command -v xvfb-run >/dev/null; then
    live() { timeout 120 xvfb-run -a -s "-screen 0 1280x720x24" "$wallpepper" run "$@"; }
    expect "run --window under Xvfb (5 frames)" live wind --window --frames 5
    expect "run --wallpaper under Xvfb (5 frames)" live mm-koi --wallpaper --frames 5
    # The policy is tested by fit-test. Here: the measuring loop runs, reports once it has settled, and a tiny
    # window does not end over budget. (Exact scale and frame rate depend on how busy this machine is.)
    fit=$(live breathe --window --size 160x90 --frames 150 2>&1 | grep "of budget" | tail -n 1)
    [ -n "$fit" ] && [ "${fit%over budget*}" = "$fit" ]
    outcome $? "run with the default budget: ${fit#*: }"
else
    skip "run under Xvfb (xvfb-run is not installed)"
fi

echo
if [ "$failed" -eq 0 ]; then
    echo "RESULT: all $passed tests passed, $skipped skipped"
else
    echo "RESULT: $failed of $((passed + failed)) tests FAILED"
fi
[ "$failed" -eq 0 ]
