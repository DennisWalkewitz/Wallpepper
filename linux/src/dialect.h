#pragma once

// Which OpenGL a shader is translated for and drawn with.
enum class Dialect {
    Glsl330,     // OpenGL 3.3 core with GLSL 3.30
    GlslEs300,   // OpenGL ES 3.0 with GLSL ES 3.00, which is also what WebGL 2 takes
};
