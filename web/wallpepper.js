// WebGL 2 plumbing shared by the benchmark and the gallery. The fragment shaders in shaders/ are generated from
// the HLSL sources by `wallpepper translate --es` (see build.sh); this file supplies everything around them.
"use strict";

const Wallpepper = {
    // One triangle that covers the whole target, as in the native builds.
    vertexShader: `#version 300 es
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`,

    // A context without anything the shaders do not need, prepared for drawing them: no terminal text (one
    // transparent texel on texture unit 0, where their sampler looks by default) and the vertex array that
    // WebGL 2 wants bound even for a triangle without attributes. null when the browser has no WebGL 2.
    createContext(canvas) {
        const gl = canvas.getContext("webgl2", {
            alpha: false, antialias: false, depth: false, stencil: false,
            powerPreference: "high-performance",   // on laptops with two GPUs: the fast one
        });
        if (!gl) return null;
        gl.bindTexture(gl.TEXTURE_2D, gl.createTexture());
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(4));
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.bindVertexArray(gl.createVertexArray());
        return gl;
    },

    // Name of the graphics card and of the WebGL implementation. Browsers may hide the real name.
    describe(gl) {
        const info = gl.getExtension("WEBGL_debug_renderer_info");
        return {
            gpu: String((info && gl.getParameter(info.UNMASKED_RENDERER_WEBGL)) || gl.getParameter(gl.RENDERER)),
            driver: String(gl.getParameter(gl.VERSION)),
        };
    },

    isSoftwareRenderer(gpu) {
        return /swiftshader|llvmpipe|softpipe|software|basic render/i.test(gpu);
    },

    // "Firefox 133 on Windows": browser family and operating system, as far as the browser tells.
    platform() {
        const ua = navigator.userAgent;
        const data = navigator.userAgentData;
        let browser = "";
        if (data && data.brands) {
            const brands = data.brands.filter(b => !/not.?a.?brand/i.test(b.brand));
            const brand = brands.find(b => b.brand !== "Chromium") || brands[0];
            if (brand) browser = brand.brand + " " + brand.version;
        }
        if (!browser) {
            const match = /(Firefox|FxiOS|Edg|EdgA|OPR|SamsungBrowser|CriOS|Chrome)\/(\d+)/.exec(ua) ||
                          /Version\/(\d+)[\d.]* (?:Mobile\/\S+ )?(Safari)/.exec(ua);
            const names = {FxiOS: "Firefox", Edg: "Edge", EdgA: "Edge", OPR: "Opera", SamsungBrowser: "Samsung Internet", CriOS: "Chrome"};
            if (match && match[2] === "Safari") browser = "Safari " + match[1];
            else if (match) browser = (names[match[1]] || match[1]) + " " + match[2];
            else browser = "Unknown browser";
        }
        let os = data && data.platform;
        if (!os) {
            os = /Android/.test(ua) ? "Android" : /iPhone|iPad|iPod/.test(ua) ? "iOS" : /Windows/.test(ua) ? "Windows" :
                 /CrOS/.test(ua) ? "ChromeOS" : /Mac OS X/.test(ua) ? "macOS" : /Linux/.test(ua) ? "Linux" : "unknown system";
        }
        return browser + " on " + os;
    },

    async fetchText(url) {
        let response;
        try {
            response = await fetch(url);
        } catch (error) {
            const hint = location.protocol === "file:" ? " Browsers do not load files next to a page opened from disk: " +
                "serve the folder instead, for example with `python3 -m http.server -d web`." : "";
            throw new Error("Cannot load " + url + "." + hint);
        }
        if (!response.ok) throw new Error("Cannot load " + url + " (HTTP " + response.status + ").");
        return response.text();
    },

    // Compiles and links a translated fragment shader. Where the browser supports it, the page stays responsive
    // while the driver compiles. Throws an Error that carries the compiler's messages.
    async createProgram(gl, fragmentSource) {
        const compile = (type, source) => {
            const shader = gl.createShader(type);
            gl.shaderSource(shader, source);
            gl.compileShader(shader);
            return shader;
        };
        const vertex = compile(gl.VERTEX_SHADER, Wallpepper.vertexShader);
        const fragment = compile(gl.FRAGMENT_SHADER, fragmentSource);
        const program = gl.createProgram();
        gl.attachShader(program, vertex);
        gl.attachShader(program, fragment);
        gl.linkProgram(program);

        const parallel = gl.getExtension("KHR_parallel_shader_compile");
        while (parallel && !gl.isContextLost() && !gl.getProgramParameter(program, parallel.COMPLETION_STATUS_KHR)) {
            await new Promise(resolve => setTimeout(resolve, 30));
        }
        const linked = gl.getProgramParameter(program, gl.LINK_STATUS) || gl.isContextLost();
        const messages = linked ? [] : [gl.getShaderInfoLog(fragment), gl.getShaderInfoLog(vertex), gl.getProgramInfoLog(program)];
        gl.deleteShader(vertex);
        gl.deleteShader(fragment);
        if (!linked) {
            gl.deleteProgram(program);
            throw new Error("The shader does not compile in this browser:\n" + messages.filter(Boolean).join("\n").trim());
        }

        // What the shader reads besides the uniforms set per frame: a single monitor.
        gl.useProgram(program);
        gl.uniform1f(gl.getUniformLocation(program, "Scale"), 1);
        gl.uniform4f(gl.getUniformLocation(program, "Background"), 0, 0, 0, 1);
        gl.uniform4fv(gl.getUniformLocation(program, "Monitors"), [0, 0, 1, 1]);
        gl.uniform1f(gl.getUniformLocation(program, "MonitorCount"), 1);
        gl.uniform1f(gl.getUniformLocation(program, "MonitorIndex"), 0);
        const time = gl.getUniformLocation(program, "Time");
        const resolution = gl.getUniformLocation(program, "Resolution");
        const origin = gl.getUniformLocation(program, "WallpepperOrigin");
        return {
            // Selects the shader for an image of the given size (`Resolution`) at the given `Time`.
            use(seconds, width, height) {
                gl.useProgram(program);
                gl.uniform1f(time, seconds);
                gl.uniform2f(resolution, width, height);
                gl.uniform2f(origin, 0, height);
            },
            dispose() { gl.deleteProgram(program); },
        };
    },
};
