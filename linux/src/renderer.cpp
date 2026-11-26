#include "renderer.h"

#include "translate.h"

#include <algorithm>
#include <cctype>
#include <stdexcept>
#include <utility>

namespace {

GLuint compile(GLenum type, const char* source) {
    GLuint shader = glCreateShader(type);
    glShaderSource(shader, 1, &source, nullptr);
    glCompileShader(shader);
    GLint ok = 0;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
    if (ok) return shader;

    GLint length = 0;
    glGetShaderiv(shader, GL_INFO_LOG_LENGTH, &length);
    std::string log(std::max(length, 1), '\0');
    glGetShaderInfoLog(shader, length, nullptr, log.data());
    glDeleteShader(shader);
    log.erase(log.find_last_not_of(std::string("\n\0 ", 3)) + 1);
    throw ShaderError(log);
}

}   // namespace

std::string glString(GLenum name) {
    const GLubyte* text = glGetString(name);
    return text ? reinterpret_cast<const char*>(text) : "unknown";
}

bool isSoftwareRenderer(std::string renderer) {
    std::transform(renderer.begin(), renderer.end(), renderer.begin(), [](unsigned char c) { return std::tolower(c); });
    for (const char* name : {"llvmpipe", "softpipe", "swiftshader", "software"})
        if (renderer.find(name) != std::string::npos) return true;
    return false;
}

Target::Target(int width, int height) {
    glGenTextures(1, &texture_);
    glGenFramebuffers(1, &framebuffer_);
    resize(width, height);
}

Target::Target(Target&& other) noexcept
    : framebuffer_(std::exchange(other.framebuffer_, 0)),
      texture_(std::exchange(other.texture_, 0)),
      width_(other.width_),
      height_(other.height_) {}

Target::~Target() {
    glDeleteFramebuffers(1, &framebuffer_);
    glDeleteTextures(1, &texture_);
}

void Target::resize(int width, int height) {
    width_ = std::max(width, 1);
    height_ = std::max(height, 1);
    glBindTexture(GL_TEXTURE_2D, texture_);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width_, height_, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glBindFramebuffer(GL_FRAMEBUFFER, framebuffer_);
    glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture_, 0);
    if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE)
        throw std::runtime_error("cannot create a " + std::to_string(width_) + "x" + std::to_string(height_) +
                                 " render target");
}

void Target::bind() const {
    glBindFramebuffer(GL_FRAMEBUFFER, framebuffer_);
    glViewport(0, 0, width_, height_);
}

std::vector<uint8_t> Target::pixels() const {
    const size_t row = static_cast<size_t>(width_) * 4;
    std::vector<uint8_t> bottomUp(row * height_), image(row * height_);
    glBindFramebuffer(GL_FRAMEBUFFER, framebuffer_);
    glReadPixels(0, 0, width_, height_, GL_RGBA, GL_UNSIGNED_BYTE, bottomUp.data());
    for (int y = 0; y < height_; y++)
        std::copy_n(bottomUp.begin() + row * (height_ - 1 - y), row, image.begin() + row * y);
    return image;
}

void Target::blitToWindow(int width, int height) const {
    glBindFramebuffer(GL_READ_FRAMEBUFFER, framebuffer_);
    glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
    glBlitFramebuffer(0, 0, width_, height_, 0, 0, width, height, GL_COLOR_BUFFER_BIT, GL_LINEAR);
}

Shader::Shader(const std::string& glsl) {
    GLuint fragment = compile(GL_FRAGMENT_SHADER, glsl.c_str());
    const std::string versionLine = glsl.substr(0, glsl.find('\n'));   // OpenGL or OpenGL ES, as translated
    GLuint vertex = compile(GL_VERTEX_SHADER, (versionLine + kVertexShader).c_str());
    program_ = glCreateProgram();
    glAttachShader(program_, vertex);
    glAttachShader(program_, fragment);
    glLinkProgram(program_);
    glDeleteShader(vertex);
    glDeleteShader(fragment);
    GLint ok = 0;
    glGetProgramiv(program_, GL_LINK_STATUS, &ok);
    if (!ok) {
        char log[4096] = "";
        glGetProgramInfoLog(program_, sizeof log, nullptr, log);
        glDeleteProgram(program_);
        throw ShaderError(std::string("link: ") + log);
    }

    time_ = glGetUniformLocation(program_, "Time");
    scale_ = glGetUniformLocation(program_, "Scale");
    resolution_ = glGetUniformLocation(program_, "Resolution");
    background_ = glGetUniformLocation(program_, "Background");
    origin_ = glGetUniformLocation(program_, "WallpepperOrigin");
    monitors_ = glGetUniformLocation(program_, "Monitors");
    monitorCount_ = glGetUniformLocation(program_, "MonitorCount");
    monitorIndex_ = glGetUniformLocation(program_, "MonitorIndex");

    // `shaderTexture` is the terminal's text in Windows Terminal. A wallpaper has no text: one transparent texel.
    const uint8_t transparent[4] = {0, 0, 0, 0};
    glGenTextures(1, &textTexture_);
    glBindTexture(GL_TEXTURE_2D, textTexture_);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, 1, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, transparent);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    glGenVertexArrays(1, &vertexArray_);   // core profile: drawing needs one, even without vertex attributes
}

Shader::~Shader() {
    glDeleteVertexArrays(1, &vertexArray_);
    glDeleteTextures(1, &textTexture_);
    glDeleteProgram(program_);
}

void Shader::setText(int width, int height, const std::vector<uint8_t>& rgba) {
    // No flip: the first row uploaded is the one at texture coordinate 0, and `tex` is 0 at the top.
    glBindTexture(GL_TEXTURE_2D, textTexture_);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, rgba.data());
}

void Shader::draw(const Target& target, const View& view, int tile) const {
    const int width = target.width(), height = target.height();
    const float imageWidth = view.imageWidth > 0 ? view.imageWidth : width;
    const float imageHeight = view.imageHeight > 0 ? view.imageHeight : height;
    const int monitorCount = std::min(static_cast<int>(view.monitors.size() / 4), kMaxMonitors);

    target.bind();
    glUseProgram(program_);
    glBindVertexArray(vertexArray_);
    glBindTexture(GL_TEXTURE_2D, textTexture_);   // texture unit 0, which is what the sampler uniform defaults to
    glUniform1f(time_, view.time);
    glUniform1f(scale_, 1.0f);
    glUniform2f(resolution_, imageWidth, imageHeight);
    glUniform4f(background_, 0.0f, 0.0f, 0.0f, 1.0f);
    glUniform2f(origin_, view.left, view.top + height);
    glUniform4fv(monitors_, monitorCount, view.monitors.data());
    glUniform1f(monitorCount_, monitorCount);
    glUniform1f(monitorIndex_, view.monitorIndex);

    if (tile <= 0) {
        glDrawArrays(GL_TRIANGLES, 0, 3);
        return;
    }
    glEnable(GL_SCISSOR_TEST);
    for (int y = 0; y < height; y += tile) {
        for (int x = 0; x < width; x += tile) {
            glScissor(x, y, tile, tile);
            glDrawArrays(GL_TRIANGLES, 0, 3);
            glFlush();
        }
    }
    glDisable(GL_SCISSOR_TEST);
}
