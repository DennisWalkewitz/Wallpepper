// Tests the budget policy (src/fit.cpp) without a GPU: the fitter is fed the GPU times of a simulated shader
// whose frame takes a given time at full resolution.
#include "../src/fit.h"

#include <algorithm>
#include <cmath>
#include <cstdio>

namespace {

struct Outcome {
    double scale, fps, load;
    bool light;
    int settledAfter;   // measurements until the first status line; -1: never
    int lateChanges;    // changes in the second half of the run: any of them means the fit does not hold still
    std::string status;
};

// lightFactor: what the light settings do to the frame time. noise: random variation of each measurement.
// overheadMs: the part of the GPU time that does not depend on the number of pixels.
Outcome simulate(FitOptions options, double fullFrameMs, double lightFactor = 1, double noise = 0,
                 double overheadMs = 0.05) {
    const int measurements = 2000;
    Fitter fitter(options);
    Outcome outcome{};
    outcome.settledAfter = -1;
    unsigned random = 12345;
    for (int i = 0; i < measurements; i++) {
        random = random * 1664525u + 1013904223u;
        const double jitter = 1 + noise * ((random >> 8) / double(1 << 24) * 2 - 1);
        const double ms =
            fullFrameMs * fitter.scale() * fitter.scale() * (fitter.light() ? lightFactor : 1) + overheadMs;
        outcome.load = ms / 1000 * fitter.fps();
        const Fitter::Result result = fitter.report(ms / 1000 * jitter);
        if (result == Fitter::Result::Settled && outcome.settledAfter < 0) outcome.settledAfter = i + 1;
        if (result == Fitter::Result::Changed && i >= measurements / 2) outcome.lateChanges++;
    }
    outcome.scale = fitter.scale();
    outcome.fps = fitter.fps();
    outcome.light = fitter.light();
    outcome.status = fitter.describe();
    return outcome;
}

int failures = 0;

void expect(const char* what, const Outcome& o, bool ok) {
    printf("%s  %-56s %s (settled after %d measurements)\n", ok ? "ok  " : "FAIL", what, o.status.c_str(),
           o.settledAfter);
    if (!ok) failures++;
}

bool is(double value, double wanted) { return std::abs(value - wanted) < 1e-6; }

// Runs every frame time from 0.1 to 400 ms (in steps of 3 %) under every budget. A case is restless when its
// fit still changes in the second half of the run, and over budget when it ends above the budget (by more
// than the noise) although scale or frame rate could still go down.
void sweep(double noise, double overheadMs, double restlessAllowed) {
    int cases = 0, restless = 0, worst = 0, overBudget = 0;
    for (const char* name : {"minimal", "low", "balanced", "high"}) {
        FitOptions options;
        options.budget = *findBudget(name);
        for (double ms = 0.1; ms <= 400; ms *= 1.03) {
            const Outcome o = simulate(options, ms, 1, noise, overheadMs);
            cases++;
            restless += o.lateChanges > 0;
            worst = std::max(worst, o.lateChanges);
            const bool atMinimum = is(o.scale, 0.3) && o.fps <= 12;
            overBudget += o.load > options.budget.share * (1 + noise) && !atMinimum;
        }
    }
    const bool ok = restless <= restlessAllowed * cases && overBudget == 0;
    printf("%s  %d cases, %2.0f %% noise, %.2f ms fixed cost: %d restless (most changes: %d), %d over budget\n",
           ok ? "ok  " : "FAIL", cases, 100 * noise, overheadMs, restless, worst, overBudget);
    if (!ok) failures++;
}

}   // namespace

int main() {
    FitOptions low;
    low.budget = *findBudget("low");   // 15 % of GPU time, at most 30 fps
    FitOptions lowWithLight = low;
    lowWithLight.lightAvailable = true;
    FitOptions balanced;
    balanced.budget = *findBudget("balanced");

    Outcome o = simulate(low, 0.2);
    expect("0.2 ms shader, low: full quality", o, is(o.scale, 1) && is(o.fps, 30) && !o.light && o.settledAfter <= 20);

    o = simulate(lowWithLight, 0.2);
    expect("0.2 ms shader with light settings: unused", o, is(o.scale, 1) && is(o.fps, 30) && !o.light);

    o = simulate(low, 20);
    expect("20 ms shader, low: scale first, then fps", o,
           is(o.scale, 0.5) && o.fps >= 20 && o.fps < 30 && o.load <= 0.15 && o.settledAfter <= 20);

    o = simulate(balanced, 5);
    expect("5 ms shader, balanced: only scale", o, o.scale >= 0.85 && o.scale < 1 && is(o.fps, 60) && o.load <= 0.30);

    o = simulate(low, 100);
    expect("100 ms shader, low: minimum, just within", o,
           is(o.scale, 0.3) && is(o.fps, 12) && !o.light && o.load <= 0.15);

    o = simulate(lowWithLight, 20);
    expect("20 ms shader with light settings: half scale is enough", o, is(o.scale, 0.5) && !o.light && o.load <= 0.15);

    o = simulate(lowWithLight, 100, 0.4);
    expect("100 ms shader with light settings: light, not below half", o,
           is(o.scale, 0.5) && is(o.fps, 12) && o.light && o.load <= 0.15);

    o = simulate(lowWithLight, 250, 0.4);
    expect("250 ms shader, low: light and minimum", o, is(o.scale, 0.3) && is(o.fps, 12) && o.light && o.load <= 0.15);

    o = simulate(lowWithLight, 250, 0.02);
    expect("250 ms shader, cheap light: recovers", o, o.light && o.scale >= 0.5 && o.load <= 0.15 && o.load >= 0.08);

    o = simulate(low, 250);
    expect("250 ms shader without light: over budget", o,
           is(o.scale, 0.3) && is(o.fps, 12) && !o.light && o.load > 0.15 && o.settledAfter > 0);

    FitOptions pinnedFps = low;
    pinnedFps.pinnedFps = 30;
    o = simulate(pinnedFps, 20);
    expect("20 ms shader, --fps 30: only scale", o, is(o.fps, 30) && o.scale < 0.5 && o.scale >= 0.3 && o.load <= 0.15);

    FitOptions pinnedScale = low;
    pinnedScale.pinnedScale = 0.75;
    o = simulate(pinnedScale, 10);
    expect("10 ms shader, --scale 0.75: only fps", o, is(o.scale, 0.75) && o.fps < 30 && o.fps >= 12 && o.load <= 0.15);

    FitOptions smallWithLight = lowWithLight;
    smallWithLight.pinnedScale = 0.4;
    o = simulate(smallWithLight, 20);
    expect("--scale 0.4 by choice: no light while within budget", o, is(o.scale, 0.4) && !o.light && o.load <= 0.15);
    o = simulate(smallWithLight, 200, 0.4);
    expect("--scale 0.4, over budget at 12 fps: light", o, is(o.scale, 0.4) && is(o.fps, 12) && o.light);

    // What the policy promises. With GPU time proportional to the pixel count: no change once settled at up
    // to 5 % measurement noise, and rare single steps at 10 %. A large fixed cost per frame, which the policy
    // does not model, makes it more restless. In no case does it stay over budget with room to go down.
    sweep(0.00, 0.05, 0);
    sweep(0.05, 0.05, 0);
    sweep(0.10, 0.05, 0.01);
    sweep(0.05, 2.00, 0.05);
    sweep(0.20, 0.05, 1);

    printf("%s\n", failures ? "fit: FAILED" : "fit: all cases pass");
    return failures ? 1 : 0;
}
