#pragma once
#include <string>
#include <utility>
#include <vector>

struct ShaderInfo {
    std::string path;
    std::string id;                  // file name without .hlsl; what the command line accepts
    std::string name;                // "// @name:" header, else derived from the file name
    std::vector<std::string> tags;   // "// @tags:" header
    // "// @light: NAME=VALUE, ..." header: cheaper settings for machines the shader is too heavy for.
    std::vector<std::pair<std::string, std::string>> light;
};

// The shader library: `dir` if given, else the first existing directory of $WALLPEPPER_SHADERS, <exe>/../shaders,
// <exe>/../share/wallpepper/shaders, ~/.local/share/wallpepper/shaders, /usr/share/wallpepper/shaders and, for
// builds inside the source tree, <exe>/../../shaders. Throws std::runtime_error when there is none.
std::string libraryDir(const std::string& dir);

std::vector<ShaderInfo> listShaders(const std::string& dir);   // sorted by id
ShaderInfo describeShader(const std::string& path);

// A shader given on the command line: a file path, or the id of a library shader.
std::string findShader(const std::string& nameOrPath, const std::string& dir);

std::string readFile(const std::string& path);
