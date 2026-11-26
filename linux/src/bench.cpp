#include "bench.h"

#include "context.h"
#include "json.h"
#include "library.h"
#include "renderer.h"
#include "translate.h"

#include <sys/utsname.h>
#include <unistd.h>
#include <algorithm>
#include <cctype>
#include <chrono>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <fstream>
#include <limits>
#include <stdexcept>

namespace {

// docs/BENCHMARK.md, procedure version 0.
const int kVersion = 0;
const bool kCalibrated = false;
const double kReferenceMpixelsPerSecond = 497.664;   // provisional: 60 frames per second at 4K
const int kFullWidth = 3840, kFullHeight = 2160, kQuickWidth = 1280, kQuickHeight = 720;
const double kFullSeconds = 10, kQuickSeconds = 5;
const int kTile = 240;
const int kMinFrames = 3, kMaxFrames = 600;
const float kTimeStep = 1.0f / 60.0f;

std::string osName() {
    std::string name = "Linux";
    std::ifstream release("/etc/os-release");
    for (std::string line; std::getline(release, line);)
        if (line.rfind("PRETTY_NAME=", 0) == 0) name = line.substr(12);
    name.erase(std::remove(name.begin(), name.end(), '"'), name.end());
    utsname kernel{};
    uname(&kernel);
    return name + " (Linux " + kernel.release + ")";
}

double secondsSince(std::chrono::steady_clock::time_point start) {
    return std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
}

// printf into a string of whatever length it takes.
std::string formatted(const char* format, ...) {
    va_list arguments, again;
    va_start(arguments, format);
    va_copy(again, arguments);
    std::string text(vsnprintf(nullptr, 0, format, arguments), '\0');
    vsnprintf(text.data(), text.size() + 1, format, again);
    va_end(again);
    va_end(arguments);
    return text;
}

struct Result {
    std::string mode;   // full, quick or custom
    std::string gpu, driver, origin;
    int width, height;
    int frames;
    double seconds, fastestMs, slowestMs;

    double averageMs() const { return 1000 * seconds / frames; }
    double mpixelsPerSecond() const { return frames * (width * static_cast<double>(height) / 1e6) / seconds; }
    long score() const { return std::lround(10000.0 * mpixelsPerSecond() / kReferenceMpixelsPerSecond); }
};

Result measure(const BenchOptions& options) {
    const bool customSize = options.width > 0;
    Result result;
    result.width = customSize ? options.width : options.quick ? kQuickWidth : kFullWidth;
    result.height = customSize ? options.height : options.quick ? kQuickHeight : kFullHeight;
    result.mode = customSize || !options.reference ? "custom" : options.quick ? "quick" : "full";
    const double minSeconds = options.quick ? kQuickSeconds : kFullSeconds;

    Context context = Context::headless(options.device);
    result.gpu = glString(GL_RENDERER);
    result.driver = glString(GL_VERSION);
    result.origin = context.origin();
    fprintf(stderr, "Benchmarking %s at %d x %d on %s ...\n", options.shaderPath.c_str(), result.width, result.height,
            result.gpu.c_str());

    // The workload is the shader with its BENCHMARK switch on. A stand-in shader need not have one.
    std::string source = readFile(options.shaderPath);
    const std::vector<std::string> settings = settingNames(source);
    const bool hasSwitch = std::count(settings.begin(), settings.end(), "BENCHMARK") > 0;
    if (options.reference && !hasSwitch)
        throw std::runtime_error(options.shaderPath + " has no `#define BENCHMARK` switch");
    if (hasSwitch) source = applyOverrides(source, {{"BENCHMARK", "1"}});

    Shader shader(translateToGlsl(source));
    Target target(result.width, result.height);
    View view;
    auto drawFrame = [&](int number) {
        view.time = static_cast<float>(number) * kTimeStep;
        shader.draw(target, view, kTile);
        glFinish();   // the time of a frame includes waiting for the GPU
    };

    drawFrame(0);   // warm-up, not measured
    result.frames = 0;
    result.seconds = result.slowestMs = 0;
    result.fastestMs = std::numeric_limits<double>::infinity();
    const bool progress = isatty(STDERR_FILENO);
    const auto start = std::chrono::steady_clock::now();
    while (result.frames < kMaxFrames && (result.seconds < minSeconds || result.frames < kMinFrames)) {
        drawFrame(++result.frames);
        const double frameSeconds = secondsSince(start) - result.seconds;
        result.seconds += frameSeconds;
        result.slowestMs = std::max(result.slowestMs, 1000 * frameSeconds);
        result.fastestMs = std::min(result.fastestMs, 1000 * frameSeconds);
        if (progress) fprintf(stderr, "\r  frame %d, %.1f s", result.frames, result.seconds);
    }
    if (progress) fprintf(stderr, "\n");
    return result;
}

// Same fields, order and number formats as the Windows build and the web page write.
std::string toJson(const Result& r) {
    return formatted("{ \"benchmark\": \"wallpepper-pathtracer\", \"version\": %d, \"calibrated\": %s, \"mode\": %s,\n",
                     kVersion, kCalibrated ? "true" : "false", jsonString(r.mode).c_str()) +
           formatted("  \"gpu\": %s, \"driver\": %s, \"api\": \"opengl\", \"os\": %s,\n", jsonString(r.gpu).c_str(),
                     jsonString(r.driver).c_str(), jsonString(osName()).c_str()) +
           formatted("  \"width\": %d, \"height\": %d, \"frames\": %d, \"seconds\": %.2f,\n", r.width, r.height,
                     r.frames, r.seconds) +
           formatted("  \"frame_ms_avg\": %.1f, \"frame_ms_min\": %.1f, \"frame_ms_max\": %.1f,\n", r.averageMs(),
                     r.fastestMs, r.slowestMs) +
           formatted("  \"mpixels_per_second\": %.2f, \"score\": %ld }\n", r.mpixelsPerSecond(), r.score());
}

void printSummary(FILE* out, const Result& r) {
    std::string label = kCalibrated ? "" : "provisional";
    if (r.mode != "full") label += (label.empty() ? "" : ", ") + r.mode;
    fprintf(out, "Wallpepper GPU benchmark v%d%s\n", kVersion, label.empty() ? "" : (" (" + label + ")").c_str());
    fprintf(out, "GPU     %s, driver %s, OpenGL (%s)\n", r.gpu.c_str(), r.driver.c_str(), r.origin.c_str());
    fprintf(out, "Size    %d x %d\n", r.width, r.height);
    fprintf(out, "Frames  %d in %.2f s  (%.1f ms average, %.1f min, %.1f max)\n", r.frames, r.seconds, r.averageMs(),
            r.fastestMs, r.slowestMs);
    fprintf(out, "Speed   %.2f megapixels per second\n", r.mpixelsPerSecond());
    fprintf(out, "Score   %ld%s\n", r.score(),
            kCalibrated ? "" : "  (provisional: 10000 = 60 frames per second in 4K)");
    if (isSoftwareRenderer(r.gpu))
        fprintf(out,
                "\nWARNING: %s is a software renderer. No GPU was used, so this score is meaningless.\n"
                "         `wallpepper devices` shows what EGL offers on this machine.\n",
                r.gpu.c_str());
}

}   // namespace

int runBenchmark(const BenchOptions& options) {
    // A run takes a while: find out now whether the result can be written (without touching an old result).
    if (!options.outFile.empty() && !std::ofstream(options.outFile, std::ios::app))
        throw std::runtime_error("cannot write " + options.outFile);

    const Result result = measure(options);
    const std::string json = toJson(result);
    printSummary(options.json ? stderr : stdout, result);   // with --json, standard output carries only the JSON
    if (options.json) fputs(json.c_str(), stdout);
    if (!options.outFile.empty() && !(std::ofstream(options.outFile) << json).flush())
        throw std::runtime_error("cannot write " + options.outFile);
    return 0;
}
