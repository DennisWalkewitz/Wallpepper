// Wallpepper's bundled shaders, live. shaders/manifest.json lists what build.sh translated to GLSL ES.
"use strict";

const $ = id => document.getElementById(id);
const canvas = $("scene");
const gl = Wallpepper.createContext(canvas);
let shader = null;      // the running shader
let loads = 0;          // counts selections, so that a slow load cannot replace a newer one
let loading = false;    // the status line belongs to the load, not to the frame counter
let paused = matchMedia("(prefers-reduced-motion: reduce)").matches;
let time = 0;           // the shader's `Time`: seconds it has been running, pauses excluded
let lastFrame = 0;
let frames = 0, countedSince = 0;

async function select(entry) {
    const load = ++loads;
    loading = true;
    $("tags").textContent = entry.tags.join(", ");
    $("status").textContent = "Compiling " + entry.name + "…";
    history.replaceState(null, "", "?shader=" + encodeURIComponent(entry.id));
    let compiled = null;
    try {
        compiled = await Wallpepper.createProgram(gl, await Wallpepper.fetchText("shaders/" + entry.id + ".frag"));
    } catch (error) {
        if (load !== loads) return;
        $("status").textContent = error.message;
        console.error("[wallpepper] " + error.message);
    }
    if (load !== loads) {
        if (compiled) compiled.dispose();
        return;
    }
    if (shader) shader.dispose();
    shader = compiled;
    time = 0;
    loading = false;
    if (shader && paused) $("status").textContent = "Paused.";
}

function draw(now) {
    requestAnimationFrame(draw);
    const elapsed = now - lastFrame;
    lastFrame = now;
    if (!shader || gl.isContextLost()) return;
    if (!paused) time += elapsed / 1000;
    else if (frames > 0) return;   // paused: one still frame is enough

    // Draw at a fraction of the canvas's place on the page; CSS stretches the result.
    const scale = Number($("scale").value) * (window.devicePixelRatio || 1);
    const width = Math.max(1, Math.round(canvas.clientWidth * scale));
    const height = Math.max(1, Math.round(canvas.clientHeight * scale));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
    gl.viewport(0, 0, width, height);
    shader.use(time, width, height);
    gl.drawArrays(gl.TRIANGLES, 0, 3);

    frames++;
    if (now - countedSince >= 1000 && !paused) {
        if (!loading) $("status").textContent = `${width} × ${height}, ${Math.round(frames * 1000 / (now - countedSince))} frames per second`;
        frames = 0;
        countedSince = now;
    }
}

function setPaused(value) {
    paused = value;
    frames = 0;   // so that a paused gallery still draws the frame it stopped at, or a new shader's first one
    $("pause").textContent = paused ? "Play" : "Pause";
    if (paused && shader && !loading) $("status").textContent = "Paused.";
}

async function start() {
    if (!gl) {
        $("status").textContent = "This browser has no WebGL 2, which the gallery needs.";
        return;
    }
    canvas.addEventListener("webglcontextlost", () => {
        $("status").textContent = "The browser lost its connection to the graphics card. Reload the page and lower the detail.";
    });
    let manifest;
    try {
        manifest = JSON.parse(await Wallpepper.fetchText("shaders/manifest.json"));
    } catch (error) {
        $("status").textContent = error.message;
        return;
    }
    if (!Array.isArray(manifest) || manifest.length === 0) {
        $("status").textContent = "The shader list is empty. Run web/build.sh to generate it.";
        return;
    }
    manifest.sort((a, b) => a.name.localeCompare(b.name));
    const picker = $("shader");
    picker.replaceChildren(...manifest.map(entry => new Option(entry.name, entry.id)));
    picker.disabled = false;
    const byId = id => manifest.find(entry => entry.id === id);
    const first = byId(new URLSearchParams(location.search).get("shader")) || byId("wind") || manifest[0];
    picker.value = first.id;
    picker.addEventListener("change", () => {
        frames = 0;
        select(byId(picker.value));
    });
    $("pause").addEventListener("click", () => setPaused(!paused));
    $("scale").addEventListener("input", () => { frames = 0; });
    setPaused(paused);
    console.log(`[wallpepper] ${manifest.length} shaders, renderer: ${Wallpepper.describe(gl).gpu}`);
    select(first);
    requestAnimationFrame(draw);
}

start();
