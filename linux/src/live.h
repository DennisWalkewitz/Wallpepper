#pragma once
#include "fit.h"

#include <string>

struct LiveOptions {
    std::string glsl;        // translated shader
    std::string lightGlsl;   // the same with the shader's light settings; empty: it has none, or --full
    bool light = false;      // glsl already is the light version (--light)
    std::string title;
    bool wallpaper = false;           // one desktop window per monitor instead of a normal window
    bool span = false;                // one image across all monitors
    int width = 1280, height = 720;   // size of the normal window
    double speed = 1;
    long frames = 0;             // stop after this many frames; 0 = run until closed
    Budget budget{};             // a share of 0 means fixed scale and frame rate
    double scale = 0, fps = 0;   // given by the user and therefore not fitted; 0: not given
};

// Renders the shader live on the X11 display. Returns the process exit code. Throws ShaderError when the
// driver does not accept the shader.
int runLive(const LiveOptions& options);
