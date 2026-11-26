#pragma once
#include "dialect.h"
#include "gl.h"

#include <string>
#include <vector>

// A GPU (or software rasterizer) that EGL can render on without a display server.
struct Device {
    EGLDeviceEXT handle;
    bool software;
    std::string drmNode;   // e.g. /dev/dri/card0; empty when unknown
};

// Devices from EGL_EXT_device_enumeration; empty when the EGL library cannot enumerate them.
std::vector<Device> listDevices();

// An OpenGL 3.3 core or OpenGL ES 3.0 context. Failures throw std::runtime_error.
class Context {
public:
    // A current context without any display server. device < 0 picks automatically: EGL devices (hardware ones
    // only, if there are any), then Mesa's surfaceless platform, then the default display.
    static Context headless(int device = -1, Dialect dialect = Dialect::Glsl330);

    // A context for windows of an X11 display (a Display*); it becomes current with the first window surface.
    static Context forX11(void* display, long preferredVisual);

    Context(const Context&) = delete;
    Context& operator=(const Context&) = delete;
    ~Context();

    const std::string& origin() const { return origin_; }   // how the context was obtained, for messages
    long visualId() const;                                  // the X11 visual that windows must use
    EGLSurface createWindowSurface(unsigned long window);   // also makes it current
    void makeCurrent(EGLSurface surface);
    void swapBuffers(EGLSurface surface);

private:
    // windowVisual: null for a headless context, else the X11 visual its windows should preferably have.
    Context(EGLDisplay display, Dialect dialect, const long* windowVisual, std::string origin);
    void release();

    EGLDisplay display_;
    EGLConfig config_ = nullptr;
    EGLContext context_ = nullptr;
    std::string origin_;
};
