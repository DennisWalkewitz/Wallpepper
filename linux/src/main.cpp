// wallpepper: Windows Terminal style HLSL pixel shaders as animated wallpaper, Linux build.
#include "args.h"
#include "bench.h"
#include "context.h"
#include "fit.h"
#include "image.h"
#include "json.h"
#include "library.h"
#include "renderer.h"
#include "translate.h"
#ifdef WALLPEPPER_X11
#include "live.h"
#endif

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <map>
#include <set>
#include <stdexcept>

namespace {

const char* const kUsage = R"(wallpepper )" WALLPEPPER_VERSION R"( - HLSL pixel shaders as animated wallpaper

Usage:
  wallpepper list [SHADER...]             show the shader library, or the named shaders
  wallpepper check [SHADER...]            translate and compile shaders (default: the whole library)
  wallpepper translate SHADER             print the GLSL that a shader is translated to
  wallpepper render SHADER OUT.png|.ppm   render one frame to a file
  wallpepper run SHADER                   render live in a window or as the desktop wallpaper
  wallpepper bench                        GPU benchmark (docs/BENCHMARK.md)
  wallpepper devices                      list the GPUs that can render without a display server
  wallpepper --help | --version

SHADER is a file or the name of a library shader.
Exit codes: 0 success, 1 a shader failed `check`, 2 anything else.

Options:
  --dir DIR          shader library; default: $WALLPEPPER_SHADERS, <exe>/../shaders,
                     <exe>/../share/wallpepper/shaders, ~/.local/share/wallpepper/shaders,
                     /usr/share/wallpepper/shaders
  --set NAME=VALUE   override a `#define NAME value` setting of the shader (repeatable)
  --device N         check, render, bench: use EGL device N instead of choosing automatically
  --es               check, render, translate: GLSL ES 3.00 (OpenGL ES 3, WebGL 2) instead of GLSL 3.30

  list:       --json (id, name and tags as a JSON array)
  render:     --size WxH (1280x720)   --time SECONDS (2)   --monitors N (pretend to span N monitors)
              --frames N --fps F (12): N frames from --time on, written as OUT-0001.png and so on
              --text: a sample terminal text layer, for shaders that show or frame the terminal's text
  run:        --window (default) | --wallpaper     --span (one image across all monitors; default for
              shaders tagged multimonitor)         --size WxH (window, 1280x720)
              --budget minimal|low|balanced|high|unlimited (low): the share of GPU time the wallpaper
              may use, 8, 15, 30 or 60 %. Render scale and frame rate are fitted to it while running.
              --scale S, --fps N: fix that value; only the other one is fitted then
              --light | --full: always | never use the shader's cheaper `@light` settings
              --speed X (1)      --frames N (exit after N frames)
  translate:  -o FILE (write to a file instead of standard output)
  bench:      --quick (1280x720, 5 s)   --json (print the result as JSON)   --out FILE or -o FILE
              (write the JSON)
              --size WxH and --shader SHADER are for testing; such results are labelled custom
)";

const std::set<std::string> kFlags = {"--window", "--wallpaper", "--span", "--quick", "--json",
                                      "--es",     "--light",     "--full", "--text"};
const int kMaxImageSide = 8192;   // of `render` and `bench`; the PNG writer keeps several copies of the image
const int kMaxDevice = 255;

std::string joined(const std::vector<std::string>& parts, const char* separator = ", ") {
    std::string out;
    for (const std::string& part : parts) out += (out.empty() ? "" : separator) + part;
    return out;
}

std::string fileName(const std::string& path) { return std::filesystem::path(path).filename().string(); }

Dialect dialectOf(Args& args) { return args.flag("--es") ? Dialect::GlslEs300 : Dialect::Glsl330; }

// The shader's `@light` settings, with the user's own settings on top: those always win.
std::map<std::string, std::string> withLight(const ShaderInfo& info,
                                             const std::map<std::string, std::string>& overrides) {
    std::map<std::string, std::string> merged(info.light.begin(), info.light.end());
    for (const auto& [name, value] : overrides) merged[name] = value;
    return merged;
}

// Reads a shader, applies the settings and translates it. Errors name the file.
std::string translated(const std::string& path, const std::map<std::string, std::string>& overrides,
                       Dialect dialect = Dialect::Glsl330) {
    try {
        return translateToGlsl(applyOverrides(readFile(path), overrides), dialect);
    } catch (const std::runtime_error& e) {
        throw std::runtime_error(fileName(path) + ": " + e.what());
    }
}

std::runtime_error doesNotCompile(const std::string& path, const ShaderError& error) {
    return std::runtime_error(fileName(path) + " does not compile (line numbers are those of the file):\n" +
                              error.what());
}

int list(Args& args) {
    std::string dir = args.text("--dir");
    const bool json = args.flag("--json");
    args.finish(0, SIZE_MAX);
    std::vector<ShaderInfo> shaders;
    for (const std::string& name : args.positional) shaders.push_back(describeShader(findShader(name, dir)));
    if (args.positional.empty()) shaders = listShaders(dir);

    if (json) {
        std::vector<std::string> entries;
        for (const ShaderInfo& s : shaders) {
            std::vector<std::string> tags, light;
            for (const std::string& tag : s.tags) tags.push_back(jsonString(tag));
            for (const auto& [name, value] : s.light) light.push_back(jsonString(name) + ": " + jsonString(value));
            const std::string settings = std::to_string(settingNames(readFile(s.path)).size());
            entries.push_back("{ \"id\": " + jsonString(s.id) + ", \"name\": " + jsonString(s.name) + ", \"tags\": [" +
                              joined(tags) + "], \"settings\": " + settings + ", \"light\": {" + joined(light) + "} }");
        }
        printf("[ %s ]\n", joined(entries, ",\n  ").c_str());
        return 0;
    }
    for (const ShaderInfo& s : shaders)
        printf("%-20s %-24s %s\n", s.id.c_str(), s.name.c_str(), joined(s.tags).c_str());
    if (args.positional.empty()) printf("%zu shaders in %s\n", shaders.size(), libraryDir(dir).c_str());
    return 0;
}

int check(Args& args) {
    std::string dir = args.text("--dir");
    auto overrides = args.overrides();
    const int device = static_cast<int>(args.integer("--device", -1, 0, kMaxDevice));
    const Dialect dialect = dialectOf(args);
    args.finish(0, SIZE_MAX);

    std::vector<std::string> paths;
    for (const std::string& name : args.positional) paths.push_back(findShader(name, dir));
    if (paths.empty())
        for (const ShaderInfo& s : listShaders(dir)) paths.push_back(s.path);

    Context context = Context::headless(device, dialect);
    size_t failed = 0;
    const std::string indent = "\n      ";
    for (const std::string& path : paths) {
        const ShaderInfo info = describeShader(path);
        try {
            Shader shader(translateToGlsl(applyOverrides(readFile(path), overrides), dialect));
            if (!info.light.empty()) {   // the `@light` settings must exist, be numbers and compile as well
                try {
                    Shader light(translateToGlsl(applyOverrides(readFile(path), withLight(info, overrides)), dialect));
                } catch (const std::runtime_error& e) {
                    throw std::runtime_error(std::string("@light: ") + e.what());
                }
            }
            printf("ok    %s\n", info.id.c_str());
        } catch (const std::runtime_error& e) {
            failed++;
            std::string message =
                e.what();   // a compiler's log has several lines; its line numbers are those of the file
            for (size_t at = 0; (at = message.find('\n', at)) != std::string::npos; at += indent.size())
                message.replace(at, 1, indent);
            printf("FAIL  %s%s%s\n", info.id.c_str(), indent.c_str(), message.c_str());
        }
    }
    printf("%zu of %zu shaders compile (%s)\n", paths.size() - failed, paths.size(), glString(GL_RENDERER).c_str());
    return failed ? 1 : 0;
}

// "out.png", 3 -> "out-0003.png"
std::string numbered(const std::string& path, int number) {
    char suffix[16];
    snprintf(suffix, sizeof suffix, "-%04d", number);
    const size_t dot = path.rfind('.');
    return path.substr(0, dot) + suffix + path.substr(dot);
}

int render(Args& args) {
    std::string dir = args.text("--dir");
    auto overrides = args.overrides();
    const int device = static_cast<int>(args.integer("--device", -1, 0, kMaxDevice));
    const Dialect dialect = dialectOf(args);
    int width = 1280, height = 720;
    args.size(width, height, kMaxImageSide);
    const double time = args.number("--time", 2, -1e9, 1e9);
    const int frames = static_cast<int>(args.integer("--frames", 1, 1, 9999));
    const double fps = args.number("--fps", 12, 0.01, 1000);
    const bool text = args.flag("--text");
    View view;
    // N monitors side by side, each an equal slice of the image: what a span over N equal monitors looks like.
    if (const int monitors = static_cast<int>(args.integer("--monitors", 0, 1, kMaxMonitors))) {
        view.monitors.clear();
        for (int i = 0; i < monitors; i++)
            for (float v : {float(i) / float(monitors), 0.0f, 1.0f / float(monitors), 1.0f}) view.monitors.push_back(v);
    }
    args.finish(2, 2);
    const std::string path = findShader(args.positional[0], dir), out = args.positional[1];
    checkImagePath(out);

    Context context = Context::headless(device, dialect);
    try {
        Shader shader(translated(path, overrides, dialect));
        if (text) shader.setText(width, height, sampleTextLayer(width, height));
        Target target(width, height);
        for (int frame = 0; frame < frames; frame++) {
            view.time = static_cast<float>(time + frame / fps);
            shader.draw(target, view);
            writeImage(frames > 1 ? numbered(out, frame + 1) : out, target.pixels(), width, height);
        }
    } catch (const ShaderError& e) {
        throw doesNotCompile(path, e);
    }
    if (frames > 1) printf("%s to %s: ", numbered(out, 1).c_str(), numbered(out, frames).c_str());
    else printf("%s: ", out.c_str());
    printf("%d x %d at %g s (%s)\n", width, height, time, glString(GL_RENDERER).c_str());
    return 0;
}

int translate(Args& args) {
    std::string dir = args.text("--dir"), outFile = args.text("--out");
    auto overrides = args.overrides();
    const Dialect dialect = dialectOf(args);
    args.finish(1, 1);
    std::string glsl = translated(findShader(args.positional[0], dir), overrides, dialect);
    if (outFile.empty()) {
        fputs(glsl.c_str(), stdout);
    } else {
        std::ofstream out(outFile, std::ios::binary);
        if (!(out << glsl).flush()) throw std::runtime_error("cannot write " + outFile);
    }
    return 0;
}

int run(Args& args) {
#ifdef WALLPEPPER_X11
    std::string dir = args.text("--dir");
    auto overrides = args.overrides();
    LiveOptions options;
    options.wallpaper = args.flag("--wallpaper");
    if (args.flag("--window") && options.wallpaper)
        throw std::runtime_error("--window and --wallpaper exclude each other");
    options.span = args.flag("--span");
    args.size(options.width, options.height, kMaxImageSide);
    const std::string budgetName = args.text("--budget");
    const Budget* budget = findBudget(budgetName.empty() ? kDefaultBudget : budgetName);
    if (!budget) throw std::runtime_error("--budget must be minimal, low, balanced, high or unlimited");
    options.budget = *budget;
    options.fps = args.number("--fps", 0, 1, 1000);
    options.scale = args.number("--scale", 0, 0.1, 2);
    const bool forceLight = args.flag("--light"), forbidLight = args.flag("--full");
    if (forceLight && forbidLight) throw std::runtime_error("--light and --full exclude each other");
    options.speed = args.number("--speed", 1, -100, 100);
    options.frames = args.integer("--frames", 0, 0, 1000000000);
    args.finish(1, 1);

    const ShaderInfo info = describeShader(findShader(args.positional[0], dir));
    options.span |= std::count(info.tags.begin(), info.tags.end(), "multimonitor") > 0;
    options.title = "Wallpepper - " + info.name;
    options.light = forceLight && !info.light.empty();
    options.glsl = translated(info.path, options.light ? withLight(info, overrides) : overrides);
    if (!forceLight && !forbidLight && !info.light.empty())
        options.lightGlsl = translated(info.path, withLight(info, overrides));
    try {
        return runLive(options);
    } catch (const ShaderError& e) {
        throw doesNotCompile(info.path, e);
    }
#else
    (void)args;
    throw std::runtime_error("this build has no `run`: it was compiled without the X11 development packages");
#endif
}

int bench(Args& args) {
    std::string dir = args.text("--dir"), standIn = args.text("--shader");
    BenchOptions options;
    options.reference = standIn.empty();
    options.quick = args.flag("--quick");
    options.json = args.flag("--json");
    options.outFile = args.text("--out");
    options.device = static_cast<int>(args.integer("--device", -1, 0, kMaxDevice));
    args.size(options.width, options.height, kMaxImageSide);
    args.finish(0, 0);
    options.shaderPath = findShader(options.reference ? "rtx-pathtracer" : standIn, dir);
    try {
        return runBenchmark(options);
    } catch (const ShaderError& e) {
        throw doesNotCompile(options.shaderPath, e);
    }
}

int devices(Args& args) {
    args.finish(0, 0);
    std::vector<Device> all = listDevices();
    if (all.empty()) printf("This EGL library cannot enumerate devices (EGL_EXT_device_enumeration is missing).\n");
    for (size_t i = 0; i < all.size(); i++) {
        std::string renderer;
        try {
            Context context = Context::headless(static_cast<int>(i));
            renderer = glString(GL_RENDERER) + ", OpenGL " + glString(GL_VERSION);
        } catch (const std::exception& e) {
            renderer = std::string("not usable: ") + e.what();
        }
        printf("%zu  %s  %-14s %s\n", i, all[i].software ? "software" : "hardware",
               all[i].drmNode.empty() ? "-" : all[i].drmNode.c_str(), renderer.c_str());
    }
    Context context = Context::headless();
    printf("Default: %s (%s)\n", context.origin().c_str(), glString(GL_RENDERER).c_str());
    return 0;
}

}   // namespace

int main(int argc, char** argv) {
    const std::string command = argc > 1 ? argv[1] : "help";
    const auto isHelp = [](const std::string& arg) { return arg == "--help" || arg == "-h"; };
    if (command == "help" || std::any_of(argv + 1, argv + argc, isHelp)) {
        fputs(kUsage, stdout);
        return 0;
    }
    if (command == "--version") {
        puts("wallpepper " WALLPEPPER_VERSION);
        return 0;
    }
    try {
        Args args(argc, argv, kFlags);
        if (command == "list") return list(args);
        if (command == "check") return check(args);
        if (command == "render") return render(args);
        if (command == "translate") return translate(args);
        if (command == "run") return run(args);
        if (command == "bench") return bench(args);
        if (command == "devices") return devices(args);
        throw std::runtime_error("unknown command '" + command + "' (see `wallpepper --help`)");
    } catch (const std::exception& e) {
        fprintf(stderr, "wallpepper: %s\n", e.what());
        return 2;
    }
}
