#include "library.h"

#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <stdexcept>

namespace fs = std::filesystem;

namespace {

const int kHeaderLines = 30;   // where `@name`, `@tags` and `@light` are looked for

std::string trim(const std::string& s, const char* chars = " \t\r") {
    size_t a = s.find_first_not_of(chars), b = s.find_last_not_of(chars);
    return a == std::string::npos ? "" : s.substr(a, b - a + 1);
}

// The value of a header line `// @key: value`. The key must open the comment and end there: `@name` does not
// match `@namespace`.
bool headerValue(const std::string& line, const std::string& key, std::string& value) {
    const std::string text = trim(line);
    if (text.rfind("//", 0) != 0) return false;
    const std::string comment = trim(text.substr(2));
    if (comment.rfind(key, 0) != 0) return false;
    const std::string rest = comment.substr(key.size());
    if (!rest.empty() && std::string(" \t:=").find(rest[0]) == std::string::npos) return false;
    value = trim(rest, " \t\r:=");
    return true;
}

}   // namespace

std::string readFile(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("cannot read " + path);
    std::ostringstream text;
    text << in.rdbuf();
    return text.str();
}

std::string libraryDir(const std::string& dir) {
    std::vector<fs::path> candidates;
    if (!dir.empty()) {
        candidates.push_back(dir);
    } else {
        std::error_code ignored;
        fs::path exeDir = fs::read_symlink("/proc/self/exe", ignored).parent_path();
        const char* env = std::getenv("WALLPEPPER_SHADERS");
        const char* home = std::getenv("HOME");
        if (env && *env) candidates.push_back(env);
        candidates.push_back(exeDir / "../shaders");
        candidates.push_back(exeDir / "../share/wallpepper/shaders");
        if (home && *home) candidates.push_back(fs::path(home) / ".local/share/wallpepper/shaders");
        candidates.push_back("/usr/share/wallpepper/shaders");
        candidates.push_back(exeDir / "../../shaders");   // last resort: a build directory inside linux/
    }
    std::string tried;
    for (const fs::path& c : candidates) {
        std::error_code ignored;
        if (fs::is_directory(c, ignored)) return fs::weakly_canonical(c, ignored).string();
        tried += "\n  " + c.lexically_normal().string();
    }
    throw std::runtime_error("no shader library found; use --dir or $WALLPEPPER_SHADERS. Looked in:" + tried);
}

ShaderInfo describeShader(const std::string& path) {
    ShaderInfo info;
    info.path = path;
    info.id = fs::path(path).stem().string();

    // Like the Windows app: only the first 30 lines count.
    std::ifstream in(path);
    std::string line, tags, light;
    bool haveTags = false;
    for (int i = 0; i < kHeaderLines && std::getline(in, line); i++) {
        if (info.name.empty() && headerValue(line, "@name", info.name)) continue;
        if (!haveTags && (haveTags = headerValue(line, "@tags", tags))) continue;
        if (light.empty()) headerValue(line, "@light", light);
    }
    std::istringstream settings(light);
    for (std::string setting; std::getline(settings, setting, ',');) {
        size_t equals = setting.find('=');   // without one, the value stays empty and `check` complains
        info.light.emplace_back(trim(setting.substr(0, equals)),
                                equals == std::string::npos ? "" : trim(setting.substr(equals + 1)));
    }
    std::replace(tags.begin(), tags.end(), ';', ',');
    std::istringstream list(tags);
    for (std::string tag; std::getline(list, tag, ',');) {
        tag = trim(tag);
        std::transform(tag.begin(), tag.end(), tag.begin(), [](unsigned char c) { return std::tolower(c); });
        if (!tag.empty()) info.tags.push_back(tag);
    }

    if (info.name.empty()) {
        info.name = info.id;
        std::replace(info.name.begin(), info.name.end(), '-', ' ');
        std::replace(info.name.begin(), info.name.end(), '_', ' ');
        info.name[0] = std::toupper(static_cast<unsigned char>(info.name[0]));
    }
    return info;
}

std::vector<ShaderInfo> listShaders(const std::string& dir) {
    std::vector<ShaderInfo> shaders;
    for (const fs::directory_entry& entry : fs::directory_iterator(libraryDir(dir)))
        if (entry.is_regular_file() && entry.path().extension() == ".hlsl")
            shaders.push_back(describeShader(entry.path().string()));
    std::sort(shaders.begin(), shaders.end(), [](const ShaderInfo& a, const ShaderInfo& b) { return a.id < b.id; });
    return shaders;
}

std::string findShader(const std::string& nameOrPath, const std::string& dir) {
    std::error_code ignored;
    if (fs::is_regular_file(nameOrPath, ignored)) return nameOrPath;
    fs::path inLibrary = fs::path(libraryDir(dir)) / nameOrPath;
    if (inLibrary.extension() != ".hlsl") inLibrary += ".hlsl";
    if (fs::is_regular_file(inLibrary, ignored)) return inLibrary.string();
    throw std::runtime_error("no shader file or library shader called '" + nameOrPath + "' (see `wallpepper list`)");
}
