#pragma once
#include "dialect.h"

#include <map>
#include <string>
#include <vector>

// The size of the `Monitors` array in the multi-monitor extension of the shader contract.
constexpr int kMaxMonitors = 8;

// Whether text is a decimal number as shaders write them: 2, -0.5, .5, 1e-3, 1.0f.
bool isNumberLiteral(const std::string& text);

// The names of a shader's settings: every `#define NAME number`.
std::vector<std::string> settingNames(const std::string& hlsl);

// Settings: replaces the value of each `#define NAME number` that has an override (first occurrence only),
// like the Windows app does, so no macro is ever defined twice. A whole number given to a float setting is
// written as a float. Throws std::runtime_error for a value that is not a number and for a name that is not
// a setting of the shader.
std::string applyOverrides(const std::string& hlsl, const std::map<std::string, std::string>& overrides);

// Translates a Windows Terminal style HLSL pixel shader (the portable subset of docs/SHADER-GUIDE.md) into a
// fragment shader. Line numbers are preserved, so compiler messages point into the HLSL file.
// Throws std::runtime_error when the source has no recognisable entry point.
std::string translateToGlsl(const std::string& hlsl, Dialect dialect = Dialect::Glsl330);

// The matching vertex shader, without its #version line (use the one of the fragment shader): one triangle
// that covers the whole target.
extern const char* const kVertexShader;
