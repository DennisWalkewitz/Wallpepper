#pragma once
#include "gl.h"

// Measures the GPU time of what is drawn between begin() and end() without slowing the drawing down: with a
// timer query on a real GPU whose driver has one, else by waiting for the GPU before and after one frame in
// four. Not every frame yields a measurement.
class GpuTimer {
public:
    GpuTimer();
    GpuTimer(const GpuTimer&) = delete;
    GpuTimer& operator=(const GpuTimer&) = delete;
    ~GpuTimer();

    void begin();
    void end();
    bool take(double& seconds);   // a finished measurement, if there is one

private:
    enum class State { Idle, Running, Waiting } state_ = State::Idle;
    GLuint query_ = 0;   // 0: no timer queries, measure with glFinish
    long frame_ = 0;
    double started_ = 0, seconds_ = 0;
};
