// The Wallpepper GPU benchmark in a browser: docs/BENCHMARK.md, procedure version 0, on WebGL 2.
// The constants below must stay in step with linux/src/bench.cpp and windows/Bench.cs.
"use strict";

const VERSION = 0;
const CALIBRATED = false;
const REFERENCE_MPIXELS_PER_SECOND = 497.664;   // provisional: 60 frames per second at 3840 x 2160
const TILE = 240;
const MIN_FRAMES = 3, MAX_FRAMES = 600;
const TIME_STEP = 1 / 60;
// Sizes to try in turn. Only the first one of a mode is the real thing; a smaller one is labelled custom.
const MODES = {
    full: {seconds: 10, sizes: [[3840, 2160], [2560, 1440], [1920, 1080], [1280, 720]]},
    quick: {seconds: 5, sizes: [[1280, 720]]},
};
// Slow graphics cards draw a frame in several slices with a pause in between, so that the page stays alive
// and the image builds up on screen. A slice aims at this many milliseconds.
const SLICE_MS = 100;

const $ = id => document.getElementById(id);
const canvas = $("scene");
const gl = Wallpepper.createContext(canvas);
const parameters = new URLSearchParams(location.search);   // ?run=quick|full  ?size=WIDTHxHEIGHT (for testing)
let shader = null;
let running = false, stopRequested = false, tabWasHidden = false;

function log(text) {
    const seconds = (performance.now() / 1000).toFixed(2).padStart(7);
    $("log").textContent += seconds + "  " + text + "\n";
    console.log("[wallpepper] " + text);
}

function status(text) {
    $("status").textContent = text;
}

const pause = () => new Promise(resolve => setTimeout(resolve, 0));

class Stopped extends Error {}

function checkContext() {
    if (gl.isContextLost()) {
        throw new Error("The browser lost its connection to the graphics card. Usually the graphics driver was " +
                        "reset because drawing took too long or memory ran out. Reload the page and try the quick run.");
    }
}

// An offscreen image of the first size the device can provide.
function createTarget(sizes) {
    const limit = Math.min(gl.getParameter(gl.MAX_RENDERBUFFER_SIZE), ...gl.getParameter(gl.MAX_VIEWPORT_DIMS));
    for (const [width, height] of sizes) {
        if (width > limit || height > limit) {
            log(`${width} x ${height} is beyond this device's limit of ${limit} pixels`);
            continue;
        }
        while (gl.getError() !== gl.NO_ERROR) { /* forget old errors */ }
        const renderbuffer = gl.createRenderbuffer();
        const framebuffer = gl.createFramebuffer();
        gl.bindRenderbuffer(gl.RENDERBUFFER, renderbuffer);
        gl.renderbufferStorage(gl.RENDERBUFFER, gl.RGBA8, width, height);
        gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
        gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.RENDERBUFFER, renderbuffer);
        const complete = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
        const error = gl.getError();
        const dispose = () => { gl.deleteFramebuffer(framebuffer); gl.deleteRenderbuffer(renderbuffer); };
        if (complete && error === gl.NO_ERROR) return {width, height, framebuffer, dispose};
        log(`no ${width} x ${height} image: ${complete ? "error 0x" + error.toString(16) : "framebuffer incomplete"}`);
        dispose();
        checkContext();
    }
    throw new Error("This device cannot provide an offscreen image for the benchmark.");
}

// Shows the offscreen image on the page, scaled down.
function present(target) {
    gl.disable(gl.SCISSOR_TEST);
    gl.bindFramebuffer(gl.READ_FRAMEBUFFER, target.framebuffer);
    gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
    gl.blitFramebuffer(0, 0, target.width, target.height, 0, 0, canvas.width, canvas.height, gl.COLOR_BUFFER_BIT, gl.LINEAR);
}

async function measure(target, mode, minSeconds) {
    const {width, height} = target;
    const tiles = [];   // from the top row down, so that the picture builds up the way one reads
    for (let y = Math.ceil(height / TILE) * TILE - TILE; y >= 0; y -= TILE) {
        for (let x = 0; x < width; x += TILE) tiles.push([x, y]);
    }
    const pixel = new Uint8Array(4);
    let lastPause = performance.now(), lastPresent = 0;

    // Draws tiles [first, end) of a frame and returns the milliseconds until the graphics card has finished them.
    const drawSlice = (frame, first, end) => {
        const start = performance.now();
        gl.bindFramebuffer(gl.FRAMEBUFFER, target.framebuffer);
        gl.viewport(0, 0, width, height);
        gl.enable(gl.SCISSOR_TEST);
        shader.use(frame * TIME_STEP, width, height);
        for (let i = first; i < end; i++) {
            gl.scissor(tiles[i][0], tiles[i][1], TILE, TILE);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
            gl.flush();
        }
        // Reading a pixel back is the one call that reliably waits for the graphics card in a browser.
        gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
        return performance.now() - start;
    };

    // Draws one frame and returns its milliseconds. Pauses between slices are not counted: the graphics card is
    // idle by then. `report` is told how many tiles are done whenever there is time to update the page.
    const drawFrame = async (frame, tilesPerSlice, report) => {
        let milliseconds = 0;
        for (let first = 0; first < tiles.length; first += tilesPerSlice) {
            const end = Math.min(first + tilesPerSlice, tiles.length);
            milliseconds += drawSlice(frame, first, end);
            checkContext();
            const now = performance.now();
            if (now - lastPause < SLICE_MS) continue;
            if (now - lastPresent >= 250) {
                present(target);
                lastPresent = now;
            }
            report(end);
            await pause();
            lastPause = performance.now();
            if (stopRequested) throw new Stopped();
        }
        return milliseconds;
    };

    // Warm-up, not measured: one tile to get shader compilation out of the way, then a whole frame that waits
    // after every tile, which tells how fast this graphics card is.
    status("Warming up…");
    await pause();
    const firstTile = drawSlice(0, 0, 1);
    const warmUp = await drawFrame(0, 1, done => status(`Warm-up frame, tile ${done} of ${tiles.length}`));
    const tileMs = warmUp / tiles.length;
    const tilesPerSlice = Math.max(1, Math.min(tiles.length, Math.round(SLICE_MS / Math.max(tileMs, 0.01))));
    log(`warm-up: first tile ${firstTile.toFixed(1)} ms, then ${tileMs.toFixed(2)} ms per tile; ` +
        `${tiles.length} tiles per frame, waiting for the graphics card every ${tilesPerSlice} tiles`);

    let frames = 0, seconds = 0, fastest = Infinity, slowest = 0;
    while (frames < MAX_FRAMES && (seconds < minSeconds || frames < MIN_FRAMES)) {
        const number = frames + 1;
        const milliseconds = await drawFrame(number, tilesPerSlice, done => {
            const tile = done < tiles.length ? `, tile ${done} of ${tiles.length}` : "";
            status(`Frame ${number}${tile}. ${seconds.toFixed(1)} of ${minSeconds} seconds measured.`);
            $("progress").value = Math.min(seconds / minSeconds, frames / MIN_FRAMES, 1);
        });
        frames = number;
        seconds += milliseconds / 1000;
        fastest = Math.min(fastest, milliseconds);
        slowest = Math.max(slowest, milliseconds);
        if (frames <= 5 || frames % 50 === 0) log(`frame ${frames}: ${milliseconds.toFixed(1)} ms`);
    }
    present(target);
    if (!(seconds > 0)) throw new Error("This browser's clock is too coarse to time the run. Nothing was measured.");

    const mpixelsPerSecond = frames * (width * height / 1e6) / seconds;
    return {
        mode, width, height, frames, seconds,
        averageMs: 1000 * seconds / frames, fastestMs: fastest, slowestMs: slowest,
        mpixelsPerSecond,
        score: Math.round(10000 * mpixelsPerSecond / REFERENCE_MPIXELS_PER_SECOND),
    };
}

// The same object, field for field, as the native builds write.
function toJson(result, info) {
    const text = JSON.stringify;
    return `{ "benchmark": "wallpepper-pathtracer", "version": ${VERSION}, "calibrated": ${CALIBRATED}, "mode": ${text(result.mode)},
  "gpu": ${text(info.gpu)}, "driver": ${text(info.driver)}, "api": "webgl2", "os": ${text(Wallpepper.platform())},
  "width": ${result.width}, "height": ${result.height}, "frames": ${result.frames}, "seconds": ${result.seconds.toFixed(2)},
  "frame_ms_avg": ${result.averageMs.toFixed(1)}, "frame_ms_min": ${result.fastestMs.toFixed(1)}, "frame_ms_max": ${result.slowestMs.toFixed(1)},
  "mpixels_per_second": ${result.mpixelsPerSecond.toFixed(2)}, "score": ${result.score} }`;
}

function show(result, wanted) {
    const info = Wallpepper.describe(gl);
    const labels = [CALIBRATED ? "" : "provisional", result.mode === "full" ? "" : result.mode].filter(Boolean);
    $("score").textContent = result.score;
    $("label").textContent = labels.length ? "(" + labels.join(", ") + ")" : "";
    $("speed").textContent = `${result.mpixelsPerSecond.toFixed(2)} megapixels per second`;
    $("frames").textContent = `${result.frames} at ${result.width} × ${result.height} in ${result.seconds.toFixed(2)} s`;
    $("frame-time").textContent = `${result.averageMs.toFixed(1)} ms on average, ` +
        `${result.fastestMs.toFixed(1)} ms fastest, ${result.slowestMs.toFixed(1)} ms slowest`;
    $("gpu").textContent = info.gpu;
    $("api").textContent = info.driver;
    $("browser").textContent = Wallpepper.platform();
    $("json").textContent = toJson(result, info);

    const warnings = [];
    if (Wallpepper.isSoftwareRenderer(info.gpu)) {
        warnings.push("This browser draws with a software renderer, not with a graphics card. The score says nothing " +
                      "about your hardware. Check that hardware acceleration is switched on in the browser's settings.");
    }
    if (result.width !== wanted[0] || result.height !== wanted[1]) {
        warnings.push(`This device could not provide a ${wanted[0]} × ${wanted[1]} image, so the benchmark ran at ` +
                      `${result.width} × ${result.height}. The score is still megapixels per second, but it was not measured at the reference size.`);
    }
    if (tabWasHidden) warnings.push("The tab was in the background for part of the run. Browsers slow hidden tabs down, so run it again in front.");
    $("warnings").replaceChildren(...warnings.map(text => Object.assign(document.createElement("li"), {textContent: text})));
    $("result").hidden = false;
    document.title = `${result.score} – Wallpepper GPU benchmark`;
    log("result:\n" + $("json").textContent);
}

async function run(modeName) {
    if (running || !gl) return;
    running = true;
    stopRequested = tabWasHidden = false;
    setButtons();
    $("result").hidden = true;
    $("progress").value = 0;
    document.title = "Running… Wallpepper GPU benchmark";
    let target = null;
    try {
        if (!shader) {
            status("Loading the shader…");
            const source = await Wallpepper.fetchText("shaders/benchmark.frag");
            status("Compiling the shader. With some graphics drivers this takes a minute…");
            await pause();
            const start = performance.now();
            shader = await Wallpepper.createProgram(gl, source);
            checkContext();
            log(`shader: ${source.length} characters, compiled and linked in ${Math.round(performance.now() - start)} ms`);
        }

        const mode = MODES[modeName];
        const custom = /^(\d+)x(\d+)$/.exec(parameters.get("size") || "");
        const sizes = custom ? [[Number(custom[1]), Number(custom[2])]] : mode.sizes;
        target = createTarget(sizes);
        const exact = !custom && target.width === mode.sizes[0][0] && target.height === mode.sizes[0][1];
        log(`${modeName} run at ${target.width} x ${target.height}: ${mode.seconds} s and ${MIN_FRAMES} frames, or ${MAX_FRAMES} frames`);

        // The visible canvas only needs the pixels of its place on the page.
        canvas.width = Math.max(1, Math.min(1920, Math.round(canvas.clientWidth * (window.devicePixelRatio || 1))));
        canvas.height = Math.max(1, Math.round(canvas.width * target.height / target.width));
        canvas.style.aspectRatio = `${target.width} / ${target.height}`;
        $("placeholder").hidden = true;

        const result = await measure(target, exact ? modeName : "custom", mode.seconds);
        $("progress").value = 1;
        status("Done.");
        show(result, sizes[0]);
    } catch (error) {
        document.title = "Wallpepper GPU benchmark";
        if (error instanceof Stopped) {
            status("Stopped.");
            log("stopped");
        } else {
            status(error.message);
            log("error: " + error.message);
            $("details").open = true;
        }
    } finally {
        if (target) target.dispose();
        running = false;
        setButtons();
    }
}

function setButtons() {
    const usable = gl && !gl.isContextLost();
    $("run-full").disabled = $("run-quick").disabled = running || !usable;
    $("stop").hidden = !running;
}

$("run-full").addEventListener("click", () => run("full"));
$("run-quick").addEventListener("click", () => run("quick"));
$("stop").addEventListener("click", () => { stopRequested = true; });
$("copy").addEventListener("click", async () => {
    const button = $("copy");
    try {
        await navigator.clipboard.writeText($("json").textContent);
        button.textContent = "Copied";
    } catch (error) {
        // No clipboard access (old browser, or the page is not served over HTTPS): select the text instead.
        getSelection().selectAllChildren($("json"));
        button.textContent = "Text selected: copy it by hand";
    }
    setTimeout(() => { button.textContent = "Copy result (JSON)"; }, 2500);
});
document.addEventListener("visibilitychange", () => {
    if (document.hidden && running) tabWasHidden = true;
});
canvas.addEventListener("webglcontextlost", () => {
    log("webglcontextlost");
    if (!running) status("The browser lost its connection to the graphics card. Reload the page.");
    setButtons();
});

if (!gl) {
    status("This browser has no WebGL 2, which the benchmark needs. Try a current Chrome, Edge, Firefox or Safari, " +
           "and check that hardware acceleration is switched on.");
    log("getContext('webgl2') returned null");
} else {
    const info = Wallpepper.describe(gl);
    status("Ready. Graphics card: " + info.gpu);
    log("user agent: " + navigator.userAgent);
    log("platform: " + Wallpepper.platform());
    log("renderer: " + info.gpu);
    log("version: " + info.driver + ", " + gl.getParameter(gl.SHADING_LANGUAGE_VERSION));
    log(`limits: texture ${gl.getParameter(gl.MAX_TEXTURE_SIZE)}, renderbuffer ${gl.getParameter(gl.MAX_RENDERBUFFER_SIZE)}, ` +
        `viewport ${Array.from(gl.getParameter(gl.MAX_VIEWPORT_DIMS)).join(" x ")}`);
    log(`screen: ${screen.width} x ${screen.height} at device pixel ratio ${window.devicePixelRatio}`);
    log("parallel shader compile: " + (gl.getExtension("KHR_parallel_shader_compile") ? "yes" : "no"));
    setButtons();
    if (Object.hasOwn(MODES, parameters.get("run") || "")) run(parameters.get("run"));
}
