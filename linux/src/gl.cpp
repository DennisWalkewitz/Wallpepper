#include "gl.h"

#include <dlfcn.h>
#include <stdexcept>
#include <string>

#define X(ret, name, params) ret(*name) params = nullptr;
WALLPEPPER_EGL_FUNCTIONS(X)
WALLPEPPER_EGL_EXTENSION_FUNCTIONS(X)
WALLPEPPER_GL_FUNCTIONS(X)
WALLPEPPER_GL_TIMER_FUNCTIONS(X)
#undef X

void loadGl() {
    static bool loaded = false;
    if (loaded) return;
    void* egl = dlopen("libEGL.so.1", RTLD_NOW | RTLD_GLOBAL);
    if (!egl)
        throw std::runtime_error(std::string("cannot load libEGL.so.1 (is a graphics driver installed?): ") +
                                 dlerror());

    const char* missing = nullptr;
#define X(ret, name, params) \
    name = reinterpret_cast<ret(*) params>(dlsym(egl, #name)); \
    if (!name) missing = #name;
    WALLPEPPER_EGL_FUNCTIONS(X)
#undef X
    if (missing) throw std::runtime_error(std::string("libEGL.so.1 has no ") + missing);

        // OpenGL entry points come from EGL as well, so libGL is not needed. They work once a context is current.
#define X(ret, name, params) name = reinterpret_cast<ret(*) params>(eglGetProcAddress(#name));
    WALLPEPPER_EGL_EXTENSION_FUNCTIONS(X)
    WALLPEPPER_GL_FUNCTIONS(X)
    WALLPEPPER_GL_TIMER_FUNCTIONS(X)
#undef X
#define X(ret, name, params) \
    if (!name) missing = #name;
    WALLPEPPER_GL_FUNCTIONS(X)
#undef X
    if (missing) throw std::runtime_error(std::string("the EGL library does not provide OpenGL function ") + missing);
    loaded = true;
}
