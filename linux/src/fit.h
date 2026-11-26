#pragma once
#include <string>
#include <vector>

// How much of the graphics card a wallpaper may use: a share of GPU time, summed over all monitors, and a
// frame-rate ceiling. A share of 0 means no limit.
struct Budget {
    const char* name;
    double share;
    double maxFps;
};

// minimal, low, balanced, high, unlimited; null for any other name.
const Budget* findBudget(const std::string& name);
extern const char* const kDefaultBudget;

struct FitOptions {
    Budget budget;                           // must have a share above 0
    double pinnedScale = 0, pinnedFps = 0;   // > 0: chosen by the user, not fitted
    bool lightAvailable = false;             // the shader has cheaper settings to fall back on
    bool light = false;                      // they are on from the start
};

// Fits render scale and frame rate to a budget. Pure policy: it is told how long the GPU took for each measured
// frame and answers with the settings to render with. Scale goes down first, to half; then the frame rate; a
// shader that would need less than half resolution switches to its light settings, if it has any; only then
// does the scale go below half. The Windows app follows the same rules.
class Fitter {
public:
    explicit Fitter(const FitOptions& options);

    double scale() const { return scale_; }
    double fps() const { return fps_; }
    bool light() const { return light_; }

    enum class Result {
        Same,      // nothing to do
        Changed,   // scale(), fps() or light() changed: apply them
        Settled,   // the fit has come to rest after a change and is not about to raise quality: worth one line
                   // of describe()
    };
    // gpuSeconds: GPU time of one whole frame (all monitors) drawn with the current settings.
    Result report(double gpuSeconds);

    // "scale 0.45, 24 fps, GPU 14% of budget 15%, light settings on"
    std::string describe() const;

private:
    bool fitTo(double affordable);

    FitOptions options_;
    double scale_, fps_;
    bool light_;
    double load_ = 0;              // measured share of GPU time
    std::vector<double> window_;   // the measurements being collected
    int skip_ = 0;                 // measurements to ignore after a change
    int calm_ = 0, starved_ = 0;   // consecutive windows far below / above the budget
    bool fitted_ = false, announced_ = false;
};
