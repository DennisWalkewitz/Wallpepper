#include "gputimer.h"

#include "renderer.h"

#include <chrono>

namespace {

const int kFinishEvery = 4;   // without timer queries: the frames that are measured by waiting for the GPU

double now() { return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count(); }

}   // namespace

GpuTimer::GpuTimer() {
    // Software rasterizers have timer queries too, but llvmpipe's results are scattered and far too low.
    GLint bits = 0;
    if (glGetQueryObjectui64v && !isSoftwareRenderer(glString(GL_RENDERER)))
        glGetQueryiv(GL_TIME_ELAPSED, GL_QUERY_COUNTER_BITS, &bits);
    if (bits > 0) glGenQueries(1, &query_);
}

GpuTimer::~GpuTimer() {
    if (query_) glDeleteQueries(1, &query_);
}

void GpuTimer::begin() {
    if (state_ != State::Idle) return;   // the previous measurement has not been collected yet
    if (query_) {
        glBeginQuery(GL_TIME_ELAPSED, query_);
    } else {
        if (frame_++ % kFinishEvery != 0) return;
        glFinish();
        started_ = now();
    }
    state_ = State::Running;
}

void GpuTimer::end() {
    if (state_ != State::Running) return;
    if (query_) {
        glEndQuery(GL_TIME_ELAPSED);
    } else {
        glFinish();
        seconds_ = now() - started_;
    }
    state_ = State::Waiting;
}

bool GpuTimer::take(double& seconds) {
    if (state_ != State::Waiting) return false;
    if (query_) {
        GLuint available = 0;
        glGetQueryObjectuiv(query_, GL_QUERY_RESULT_AVAILABLE, &available);
        if (!available) return false;
        GLuint64 nanoseconds = 0;
        glGetQueryObjectui64v(query_, GL_QUERY_RESULT, &nanoseconds);
        seconds_ = static_cast<double>(nanoseconds) / 1e9;
    }
    state_ = State::Idle;
    seconds = seconds_;
    return true;
}
