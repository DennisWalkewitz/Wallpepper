// The slice of EGL 1.5 and OpenGL 3.3 that Wallpepper uses, declared by hand and resolved at run time (gl.cpp).
// Building therefore needs no EGL/OpenGL development packages, and the binary only depends on libEGL.so.1.
// Everything here is in OpenGL ES 3.0 as well, except the timer queries.
#pragma once
#include <cstdint>

typedef unsigned int GLenum, GLuint, GLbitfield;
typedef int GLint, GLsizei;
typedef float GLfloat;
typedef char GLchar;
typedef unsigned char GLubyte;
typedef uint64_t GLuint64;

typedef void *EGLDisplay, *EGLConfig, *EGLContext, *EGLSurface, *EGLDeviceEXT;
typedef int32_t EGLint;
typedef unsigned int EGLBoolean, EGLenum;

enum : GLenum {
    GL_TRIANGLES = 0x0004,
    GL_SCISSOR_TEST = 0x0C11,
    GL_TEXTURE_2D = 0x0DE1,
    GL_UNSIGNED_BYTE = 0x1401,
    GL_RGBA = 0x1908,
    GL_RENDERER = 0x1F01,
    GL_VERSION = 0x1F02,
    GL_LINEAR = 0x2601,
    GL_TEXTURE_MAG_FILTER = 0x2800,
    GL_TEXTURE_MIN_FILTER = 0x2801,
    GL_TEXTURE_WRAP_S = 0x2802,
    GL_TEXTURE_WRAP_T = 0x2803,
    GL_COLOR_BUFFER_BIT = 0x4000,
    GL_RGBA8 = 0x8058,
    GL_CLAMP_TO_EDGE = 0x812F,
    GL_QUERY_COUNTER_BITS = 0x8864,
    GL_QUERY_RESULT = 0x8866,
    GL_QUERY_RESULT_AVAILABLE = 0x8867,
    GL_TIME_ELAPSED = 0x88BF,
    GL_FRAGMENT_SHADER = 0x8B30,
    GL_VERTEX_SHADER = 0x8B31,
    GL_COMPILE_STATUS = 0x8B81,
    GL_LINK_STATUS = 0x8B82,
    GL_INFO_LOG_LENGTH = 0x8B84,
    GL_READ_FRAMEBUFFER = 0x8CA8,
    GL_DRAW_FRAMEBUFFER = 0x8CA9,
    GL_FRAMEBUFFER_COMPLETE = 0x8CD5,
    GL_COLOR_ATTACHMENT0 = 0x8CE0,
    GL_FRAMEBUFFER = 0x8D40,
};

enum : EGLint {
    EGL_PBUFFER_BIT = 0x0001,
    EGL_WINDOW_BIT = 0x0004,
    EGL_OPENGL_BIT = 0x0008,
    EGL_OPENGL_ES3_BIT = 0x0040,
    EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT = 0x0001,
    EGL_BLUE_SIZE = 0x3022,
    EGL_GREEN_SIZE = 0x3023,
    EGL_RED_SIZE = 0x3024,
    EGL_NATIVE_VISUAL_ID = 0x302E,
    EGL_SURFACE_TYPE = 0x3033,
    EGL_NONE = 0x3038,
    EGL_RENDERABLE_TYPE = 0x3040,
    EGL_EXTENSIONS = 0x3055,
    EGL_HEIGHT = 0x3056,
    EGL_WIDTH = 0x3057,
    EGL_CONTEXT_MAJOR_VERSION = 0x3098,
    EGL_OPENGL_ES_API = 0x30A0,
    EGL_OPENGL_API = 0x30A2,
    EGL_CONTEXT_MINOR_VERSION = 0x30FB,
    EGL_CONTEXT_OPENGL_PROFILE_MASK = 0x30FD,
    EGL_PLATFORM_DEVICE_EXT = 0x313F,
    EGL_PLATFORM_X11_KHR = 0x31D5,
    EGL_PLATFORM_SURFACELESS_MESA = 0x31DD,
    EGL_DRM_DEVICE_FILE_EXT = 0x3233,
};

// X(return type, name, parameters)
#define WALLPEPPER_EGL_FUNCTIONS(X) \
    X(void*, eglGetProcAddress, (const char*)) \
    X(EGLint, eglGetError, ()) \
    X(const char*, eglQueryString, (EGLDisplay, EGLint)) \
    X(EGLDisplay, eglGetDisplay, (void*)) \
    X(EGLBoolean, eglInitialize, (EGLDisplay, EGLint*, EGLint*)) \
    X(EGLBoolean, eglTerminate, (EGLDisplay)) \
    X(EGLBoolean, eglBindAPI, (EGLenum)) \
    X(EGLBoolean, eglChooseConfig, (EGLDisplay, const EGLint*, EGLConfig*, EGLint, EGLint*)) \
    X(EGLBoolean, eglGetConfigAttrib, (EGLDisplay, EGLConfig, EGLint, EGLint*)) \
    X(EGLContext, eglCreateContext, (EGLDisplay, EGLConfig, EGLContext, const EGLint*)) \
    X(EGLBoolean, eglDestroyContext, (EGLDisplay, EGLContext)) \
    X(EGLSurface, eglCreatePbufferSurface, (EGLDisplay, EGLConfig, const EGLint*)) \
    X(EGLSurface, eglCreateWindowSurface, (EGLDisplay, EGLConfig, unsigned long, const EGLint*)) \
    X(EGLBoolean, eglMakeCurrent, (EGLDisplay, EGLSurface, EGLSurface, EGLContext)) \
    X(EGLBoolean, eglSwapInterval, (EGLDisplay, EGLint)) \
    X(EGLBoolean, eglSwapBuffers, (EGLDisplay, EGLSurface))

// Extensions: these stay null when the EGL library does not have them.
#define WALLPEPPER_EGL_EXTENSION_FUNCTIONS(X) \
    X(EGLDisplay, eglGetPlatformDisplayEXT, (EGLenum, void*, const EGLint*)) \
    X(EGLBoolean, eglQueryDevicesEXT, (EGLint, EGLDeviceEXT*, EGLint*)) \
    X(const char*, eglQueryDeviceStringEXT, (EGLDeviceEXT, EGLint))

#define WALLPEPPER_GL_FUNCTIONS(X) \
    X(const GLubyte*, glGetString, (GLenum)) \
    X(GLuint, glCreateShader, (GLenum)) \
    X(void, glShaderSource, (GLuint, GLsizei, const GLchar* const*, const GLint*)) \
    X(void, glCompileShader, (GLuint)) \
    X(void, glGetShaderiv, (GLuint, GLenum, GLint*)) \
    X(void, glGetShaderInfoLog, (GLuint, GLsizei, GLsizei*, GLchar*)) \
    X(void, glDeleteShader, (GLuint)) \
    X(GLuint, glCreateProgram, ()) \
    X(void, glAttachShader, (GLuint, GLuint)) \
    X(void, glLinkProgram, (GLuint)) \
    X(void, glGetProgramiv, (GLuint, GLenum, GLint*)) \
    X(void, glGetProgramInfoLog, (GLuint, GLsizei, GLsizei*, GLchar*)) \
    X(void, glDeleteProgram, (GLuint)) \
    X(void, glUseProgram, (GLuint)) \
    X(GLint, glGetUniformLocation, (GLuint, const GLchar*)) \
    X(void, glUniform1i, (GLint, GLint)) \
    X(void, glUniform1f, (GLint, GLfloat)) \
    X(void, glUniform2f, (GLint, GLfloat, GLfloat)) \
    X(void, glUniform4f, (GLint, GLfloat, GLfloat, GLfloat, GLfloat)) \
    X(void, glUniform4fv, (GLint, GLsizei, const GLfloat*)) \
    X(void, glGenVertexArrays, (GLsizei, GLuint*)) \
    X(void, glBindVertexArray, (GLuint)) \
    X(void, glDeleteVertexArrays, (GLsizei, const GLuint*)) \
    X(void, glGenTextures, (GLsizei, GLuint*)) \
    X(void, glBindTexture, (GLenum, GLuint)) \
    X(void, glTexImage2D, (GLenum, GLint, GLint, GLsizei, GLsizei, GLint, GLenum, GLenum, const void*)) \
    X(void, glTexParameteri, (GLenum, GLenum, GLint)) \
    X(void, glDeleteTextures, (GLsizei, const GLuint*)) \
    X(void, glGenFramebuffers, (GLsizei, GLuint*)) \
    X(void, glBindFramebuffer, (GLenum, GLuint)) \
    X(void, glFramebufferTexture2D, (GLenum, GLenum, GLenum, GLuint, GLint)) \
    X(GLenum, glCheckFramebufferStatus, (GLenum)) \
    X(void, glBlitFramebuffer, (GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLint, GLbitfield, GLenum)) \
    X(void, glDeleteFramebuffers, (GLsizei, const GLuint*)) \
    X(void, glViewport, (GLint, GLint, GLsizei, GLsizei)) \
    X(void, glScissor, (GLint, GLint, GLsizei, GLsizei)) \
    X(void, glEnable, (GLenum)) \
    X(void, glDisable, (GLenum)) \
    X(void, glDrawArrays, (GLenum, GLint, GLsizei)) \
    X(void, glReadPixels, (GLint, GLint, GLsizei, GLsizei, GLenum, GLenum, void*)) \
    X(void, glFlush, ()) \
    X(void, glFinish, ())

// Timer queries (OpenGL 3.3, not OpenGL ES 3.0): these stay null where the library does not have them.
#define WALLPEPPER_GL_TIMER_FUNCTIONS(X) \
    X(void, glGenQueries, (GLsizei, GLuint*)) \
    X(void, glDeleteQueries, (GLsizei, const GLuint*)) \
    X(void, glGetQueryiv, (GLenum, GLenum, GLint*)) \
    X(void, glBeginQuery, (GLenum, GLuint)) \
    X(void, glEndQuery, (GLenum)) \
    X(void, glGetQueryObjectuiv, (GLuint, GLenum, GLuint*)) \
    X(void, glGetQueryObjectui64v, (GLuint, GLenum, GLuint64*))

#define X(ret, name, params) extern ret(*name) params;
WALLPEPPER_EGL_FUNCTIONS(X)
WALLPEPPER_EGL_EXTENSION_FUNCTIONS(X)
WALLPEPPER_GL_FUNCTIONS(X)
WALLPEPPER_GL_TIMER_FUNCTIONS(X)
#undef X

// Loads libEGL and resolves everything above. Throws std::runtime_error; safe to call more than once.
void loadGl();
