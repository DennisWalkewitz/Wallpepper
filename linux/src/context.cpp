#include "context.h"

#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <utility>

namespace {

bool hasExtension(const char* list, const char* name) {
    size_t len = strlen(name);
    for (const char* p = list ? strstr(list, name) : nullptr; p; p = strstr(p + len, name))
        if ((p == list || p[-1] == ' ') && (p[len] == ' ' || p[len] == '\0')) return true;
    return false;
}

const char* clientExtensions() {
    loadGl();
    return eglQueryString(nullptr, EGL_EXTENSIONS);
}

std::runtime_error eglError(const char* what) {
    char text[64];
    snprintf(text, sizeof text, "%s failed (EGL error 0x%04X)", what, static_cast<unsigned>(eglGetError()));
    return std::runtime_error(text);
}

}   // namespace

std::vector<Device> listDevices() {
    std::vector<Device> devices;
    const char* client = clientExtensions();
    bool enumeration =
        hasExtension(client, "EGL_EXT_device_enumeration") || hasExtension(client, "EGL_EXT_device_base");
    if (!enumeration || !hasExtension(client, "EGL_EXT_platform_device")) return devices;
    if (!eglQueryDevicesEXT || !eglQueryDeviceStringEXT || !eglGetPlatformDisplayEXT) return devices;

    EGLint count = 0;
    if (!eglQueryDevicesEXT(0, nullptr, &count) || count <= 0) return devices;
    std::vector<EGLDeviceEXT> handles(count);
    if (!eglQueryDevicesEXT(count, handles.data(), &count)) return devices;
    handles.resize(count);

    for (EGLDeviceEXT handle : handles) {
        const char* extensions = eglQueryDeviceStringEXT(handle, EGL_EXTENSIONS);
        const char* node = hasExtension(extensions, "EGL_EXT_device_drm")
                               ? eglQueryDeviceStringEXT(handle, EGL_DRM_DEVICE_FILE_EXT)
                               : nullptr;
        devices.push_back({handle, hasExtension(extensions, "EGL_MESA_device_software"), node ? node : ""});
    }
    return devices;
}

Context Context::headless(int device, Dialect dialect) {
    const char* client = clientExtensions();
    std::vector<Device> devices = listDevices();
    std::vector<std::pair<EGLDisplay, std::string>> candidates;

    const auto addDevice = [&](size_t index) {
        candidates.emplace_back(eglGetPlatformDisplayEXT(EGL_PLATFORM_DEVICE_EXT, devices[index].handle, nullptr),
                                "EGL device " + std::to_string(index));
    };

    if (device >= 0) {
        if (device >= static_cast<int>(devices.size()))
            throw std::runtime_error("there is no EGL device " + std::to_string(device) +
                                     " (see `wallpepper devices`)");
        addDevice(device);
    } else {
        bool anyHardware = false;
        for (const Device& d : devices) anyHardware |= !d.software;
        for (size_t i = 0; i < devices.size(); i++)
            if (!devices[i].software || !anyHardware) addDevice(i);
        if (hasExtension(client, "EGL_MESA_platform_surfaceless") && eglGetPlatformDisplayEXT)
            candidates.emplace_back(eglGetPlatformDisplayEXT(EGL_PLATFORM_SURFACELESS_MESA, nullptr, nullptr),
                                    "EGL surfaceless");
        candidates.emplace_back(eglGetDisplay(nullptr), "EGL default display");
    }

    std::string errors;
    for (auto& [display, origin] : candidates) {
        try {
            return Context(display, dialect, nullptr, origin);
        } catch (const std::exception& e) {
            errors += "\n  " + origin + ": " + e.what();
        }
    }
    const char* wanted = dialect == Dialect::GlslEs300 ? "no OpenGL ES 3.0" : "no OpenGL 3.3";
    throw std::runtime_error(wanted + (" context available:" + errors));
}

Context Context::forX11(void* display, long preferredVisual) {
    const char* client = clientExtensions();
    bool platform = eglGetPlatformDisplayEXT &&
                    (hasExtension(client, "EGL_EXT_platform_x11") || hasExtension(client, "EGL_KHR_platform_x11"));
    return Context(platform ? eglGetPlatformDisplayEXT(EGL_PLATFORM_X11_KHR, display, nullptr) : eglGetDisplay(display),
                   Dialect::Glsl330, &preferredVisual, "EGL on X11");
}

Context::Context(EGLDisplay display, Dialect dialect, const long* windowVisual, std::string origin)
    : display_(display), origin_(std::move(origin)) {
    if (!display_) throw eglError("eglGetDisplay");
    if (!eglInitialize(display_, nullptr, nullptr)) throw eglError("eglInitialize");
    const bool es = dialect == Dialect::GlslEs300, forWindows = windowVisual != nullptr;
    try {
        if (!eglBindAPI(es ? EGL_OPENGL_ES_API : EGL_OPENGL_API)) throw eglError("eglBindAPI");

        const EGLint configAttributes[] = {EGL_SURFACE_TYPE,
                                           forWindows ? EGL_WINDOW_BIT : EGL_PBUFFER_BIT,
                                           EGL_RENDERABLE_TYPE,
                                           es ? EGL_OPENGL_ES3_BIT : EGL_OPENGL_BIT,
                                           EGL_RED_SIZE,
                                           8,
                                           EGL_GREEN_SIZE,
                                           8,
                                           EGL_BLUE_SIZE,
                                           8,
                                           EGL_NONE};
        EGLConfig configs[128];
        EGLint count = 0;
        eglChooseConfig(display_, configAttributes, configs, 128, &count);
        if (count > 0) config_ = configs[0];
        // A window that uses the screen's default visual needs no colormap tricks and is never translucent.
        for (EGLint i = 0; forWindows && i < count; i++) {
            EGLint visual = 0;
            if (eglGetConfigAttrib(display_, configs[i], EGL_NATIVE_VISUAL_ID, &visual) && visual == *windowVisual) {
                config_ = configs[i];
                break;
            }
        }
        if (forWindows && !config_) throw std::runtime_error("no EGL config for OpenGL windows");

        // Without a matching config, EGL_KHR_no_config_context still allows a context that renders to framebuffers.
        const EGLint coreAttributes[] = {
            EGL_CONTEXT_MAJOR_VERSION,           3,       EGL_CONTEXT_MINOR_VERSION, 3, EGL_CONTEXT_OPENGL_PROFILE_MASK,
            EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT, EGL_NONE};
        const EGLint esAttributes[] = {EGL_CONTEXT_MAJOR_VERSION, 3, EGL_CONTEXT_MINOR_VERSION, 0, EGL_NONE};
        context_ = eglCreateContext(display_, config_, nullptr, es ? esAttributes : coreAttributes);
        if (!context_) throw eglError(es ? "eglCreateContext(OpenGL ES 3.0)" : "eglCreateContext(OpenGL 3.3 core)");

        if (!forWindows && !eglMakeCurrent(display_, nullptr, nullptr, context_)) {
            // No EGL_KHR_surfaceless_context: a 1x1 pbuffer serves as the surface that is never drawn to.
            const EGLint size[] = {EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE};
            EGLSurface pbuffer = config_ ? eglCreatePbufferSurface(display_, config_, size) : nullptr;
            if (!pbuffer || !eglMakeCurrent(display_, pbuffer, pbuffer, context_)) throw eglError("eglMakeCurrent");
        }
    } catch (...) {
        release();
        throw;
    }
}

Context::~Context() { release(); }

void Context::release() {
    eglMakeCurrent(display_, nullptr, nullptr, nullptr);
    if (context_) eglDestroyContext(display_, context_);
    eglTerminate(display_);   // also frees the surfaces
}

long Context::visualId() const {
    EGLint visual = 0;
    eglGetConfigAttrib(display_, config_, EGL_NATIVE_VISUAL_ID, &visual);
    return visual;
}

EGLSurface Context::createWindowSurface(unsigned long window) {
    EGLSurface surface = eglCreateWindowSurface(display_, config_, window, nullptr);
    if (!surface) throw eglError("eglCreateWindowSurface");
    makeCurrent(surface);
    eglSwapInterval(display_, 0);   // frames are paced by a timer, not by the monitor
    return surface;
}

void Context::makeCurrent(EGLSurface surface) {
    if (!eglMakeCurrent(display_, surface, surface, context_)) throw eglError("eglMakeCurrent");
}

void Context::swapBuffers(EGLSurface surface) { eglSwapBuffers(display_, surface); }
