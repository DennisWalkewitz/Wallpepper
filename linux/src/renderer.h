#pragma once
#include "gl.h"

#include <cstdint>
#include <stdexcept>
#include <string>
#include <vector>

// An offscreen RGBA8 image that shaders are drawn into.
class Target {
public:
    Target(int width, int height);
    Target(Target&& other) noexcept;
    Target(const Target&) = delete;
    Target& operator=(const Target&) = delete;
    ~Target();

    int width() const { return width_; }
    int height() const { return height_; }
    void resize(int width, int height);
    void bind() const;                                // what is drawn next goes into this image
    std::vector<uint8_t> pixels() const;              // RGBA, top row first
    void blitToWindow(int width, int height) const;   // stretched over the current window surface

private:
    GLuint framebuffer_ = 0, texture_ = 0;
    int width_ = 0, height_ = 0;
};

// What a frame is drawn with, besides the target.
struct View {
    float time = 0;
    // Span mode: the target shows a part of a larger image. Zero size means the target is the whole image.
    float imageWidth = 0, imageHeight = 0;        // becomes `Resolution`
    float left = 0, top = 0;                      // the target's top-left corner inside that image, in pixels
    std::vector<float> monitors = {0, 0, 1, 1};   // x, y, w, h per monitor in image UV; at most kMaxMonitors
    int monitorIndex = 0;
};

// A shader that the driver does not accept; what() is the compiler's log.
struct ShaderError : std::runtime_error {
    using std::runtime_error::runtime_error;
};

// A compiled pixel shader.
class Shader {
public:
    explicit Shader(const std::string& glsl);   // output of translateToGlsl(); throws ShaderError
    Shader(const Shader&) = delete;
    Shader& operator=(const Shader&) = delete;
    ~Shader();

    // The terminal's text, which shaders read as `shaderTexture`: premultiplied RGBA, top row first. Without
    // this call there is no text (a wallpaper has none).
    void setText(int width, int height, const std::vector<uint8_t>& rgba);

    // Draws one frame. With tile > 0 the frame is drawn as tile x tile squares with a flush after each, so that a
    // slow GPU never spends long inside a single draw call.
    void draw(const Target& target, const View& view, int tile = 0) const;

private:
    GLuint program_ = 0, vertexArray_ = 0, textTexture_ = 0;
    GLint time_, scale_, resolution_, background_, origin_, monitors_, monitorCount_, monitorIndex_;
};

// "llvmpipe (LLVM 20.1.2, 256 bits)" etc. Needs a current context.
std::string glString(GLenum name);

// Whether a GL_RENDERER string names a CPU rasterizer (llvmpipe, SwiftShader ...) instead of a graphics card.
bool isSoftwareRenderer(std::string renderer);
