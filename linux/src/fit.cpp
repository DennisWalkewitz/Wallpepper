#include "fit.h"

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace {

const Budget kBudgets[] = {
    {"minimal", 0.08, 24}, {"low", 0.15, 30}, {"balanced", 0.30, 60}, {"high", 0.60, 60}, {"unlimited", 0, 60},
};

const double kMinScale = 0.3, kMinFps = 12;
const double kComfortScale = 0.5;      // below this the picture gets visibly soft: lower the frame rate first
const double kLightBelowScale = 0.5;   // and below this a shader's light settings are the better trade
const double kStartScale = 0.4;        // until the first measurements are in
const double kScaleStep = 0.05;
const int kWindow = 5;   // measurements per decision; their median counts
// Hysteresis: a fit aims below the budget, and is only redone when the load leaves the band around that aim.
const double kAim = 0.85, kRaiseBelow = 0.6;
const int kPatience = 2;   // windows to wait before raising quality or giving up on full settings

}   // namespace

const char* const kDefaultBudget = "low";

const Budget* findBudget(const std::string& name) {
    for (const Budget& budget : kBudgets)
        if (name == budget.name) return &budget;
    return nullptr;
}

Fitter::Fitter(const FitOptions& options)
    : options_(options),
      scale_(options.pinnedScale > 0 ? options.pinnedScale : kStartScale),
      fps_(options.pinnedFps > 0 ? options.pinnedFps : options.budget.maxFps),
      light_(options.light) {}

// Chooses scale and frame rate for a GPU that can afford `affordable` = scale x scale x frames per second
// (GPU time grows with the number of pixels). Returns whether anything changed.
bool Fitter::fitTo(double affordable) {
    const double maxFps = options_.budget.maxFps;
    double scale = options_.pinnedScale, fps = options_.pinnedFps;
    if (scale > 0 && fps > 0) return false;
    if (scale > 0) {
        fps = affordable / (scale * scale);
    } else if (fps > 0) {
        scale = std::sqrt(affordable / fps);
    } else {
        fps = maxFps;
        scale = std::sqrt(affordable / fps);
        if (scale < kComfortScale) {
            scale = kComfortScale;
            fps = affordable / (scale * scale);
            if (fps < kMinFps) {
                fps = kMinFps;
                scale = std::sqrt(affordable / fps);
            }
        }
    }
    // Round down, so that the result stays within what is affordable.
    if (options_.pinnedScale <= 0)
        scale = std::clamp(std::floor(scale / kScaleStep + 1e-6) * kScaleStep, kMinScale, 1.0);
    if (options_.pinnedFps <= 0) fps = std::clamp(std::floor(fps + 1e-6), std::min(kMinFps, maxFps), maxFps);
    const bool changed = std::abs(scale - scale_) > 1e-9 || std::abs(fps - fps_) > 1e-9;
    scale_ = scale;
    fps_ = fps;
    return changed;
}

Fitter::Result Fitter::report(double gpuSeconds) {
    if (skip_ > 0) {   // the first frame after a change pays for new render targets or a new shader
        skip_--;
        return Result::Same;
    }
    window_.push_back(gpuSeconds);
    if (static_cast<int>(window_.size()) < kWindow) return Result::Same;
    std::nth_element(window_.begin(), window_.begin() + kWindow / 2, window_.end());
    const double seconds = std::max(window_[kWindow / 2], 1e-7);
    window_.clear();

    const double share = options_.budget.share;
    load_ = seconds * fps_;
    const bool over = load_ > share;
    calm_ = load_ < kRaiseBelow * share ? calm_ + 1 : 0;
    if (over || calm_ >= kPatience || !fitted_) {
        fitted_ = true;
        calm_ = 0;
        const bool changed = fitTo(kAim * share / (seconds / (scale_ * scale_)));
        // The shader's light settings, if it has any, are the better trade in two cases: when the fit asks for
        // less than half resolution, and when it is over budget with nothing left to lower (which takes a
        // pinned scale). The second case waits for a second window, the first is the fit's own verdict.
        starved_ = over && !changed ? starved_ + 1 : 0;
        const bool tooSmall = options_.pinnedScale <= 0 && scale_ < kLightBelowScale;
        if (options_.lightAvailable && !light_ && (tooSmall || starved_ >= kPatience)) {
            light_ = true;
            starved_ = 0;
            skip_ = 1;
            fitted_ = announced_ = false;   // the cost is a different one now: fit afresh
            return Result::Changed;
        }
        if (changed) {
            skip_ = 1;
            announced_ = false;
            return Result::Changed;
        }
        if (over && options_.lightAvailable && !light_) return Result::Same;   // not at rest: light settings are next
    }
    if (announced_ || calm_ > 0) return Result::Same;   // calm: quality may be about to go up, so not at rest
    announced_ = true;
    return Result::Settled;
}

std::string Fitter::describe() const {
    // Above 100 % the figure only says "far too slow": it is GPU time per frame times the frame rate asked for.
    char load[32];
    snprintf(load, sizeof load, load_ > 1 ? "more than 100%%" : "%.0f%%", 100 * load_);
    char text[200];
    snprintf(text, sizeof text, "scale %.2f, %.0f fps, GPU %s of budget %.0f%%%s%s", scale_, fps_, load,
             100 * options_.budget.share, light_ ? ", light settings on" : "",
             load_ > options_.budget.share ? " (over budget: nothing left to lower)" : "");
    return text;
}
