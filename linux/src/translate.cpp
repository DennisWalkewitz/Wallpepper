#include "translate.h"

#include <algorithm>
#include <cctype>
#include <cstring>
#include <set>
#include <stdexcept>

namespace {

const char* const kHeader = "#version 330 core\n";
// OpenGL ES has no default precision in fragment shaders; everything here needs the full 32 bits.
// It also forbids redefining built-in functions. HLSL has no `mod`, so a shader that uses one brings its own.
const char* const kHeaderEs = R"(#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
#define mod hlsl_mod
)";

// HLSL names as macros and helpers.
const char* const kPrelude = R"(#define float2 vec2
#define float3 vec3
#define float4 vec4
#define float2x2 mat2
#define float3x3 mat3
#define float4x4 mat4
#define int2 ivec2
#define int3 ivec3
#define int4 ivec4
#define uint2 uvec2
#define uint3 uvec3
#define uint4 uvec4
#define bool2 bvec2
#define bool3 bvec3
#define bool4 bvec4
#define lerp mix
#define frac fract
#define atan2(y, x) atan(y, x)
#define rsqrt inversesqrt
#define mul(a, b) ((b) * (a))   /* HLSL row-major constructor == transposed GLSL matrix */
#define fmod(x, y) ((x) - (y) * trunc((x) / (y)))
#define ddx dFdx
#define ddy dFdy
float saturate(float x) { return clamp(x, 0.0, 1.0); }
vec2  saturate(vec2 x)  { return clamp(x, 0.0, 1.0); }
vec3  saturate(vec3 x)  { return clamp(x, 0.0, 1.0); }
vec4  saturate(vec4 x)  { return clamp(x, 0.0, 1.0); }
)";

// The Windows Terminal constant buffer and the multi-monitor extension, as uniforms. `#line 1` makes the
// compiler count the shader's own lines from here on.
const char* const kUniforms = R"(uniform float Time;
uniform float Scale;
uniform vec2  Resolution;
uniform vec4  Background;
uniform sampler2D shaderTexture;
uniform vec4  Monitors[MAX_MONITORS];   // x, y, w, h of each monitor in span UV
uniform float MonitorCount;
uniform float MonitorIndex;
uniform vec2  MonitorPad;
uniform vec2  WallpepperOrigin;   // this target's bottom-left corner in the Resolution-sized image
out vec4 fragColor;
#line 1
)";

// HLSL counts pixels from the top-left corner, OpenGL from the bottom-left one.
const char* const kEpilogue = R"(
void main() {
    vec2 p = vec2(WallpepperOrigin.x + gl_FragCoord.x, WallpepperOrigin.y - gl_FragCoord.y);
    fragColor = ps_entry(ENTRY_ARGUMENTS);
}
)";

const std::set<std::string> kAttributes = {"loop", "unroll", "branch", "flatten", "fastopt"};
const std::set<std::string> kGlslNames = {"vec2",  "vec3",  "vec4",  "mat2",  "mat3", "mat4",
                                          "ivec2", "ivec3", "ivec4", "fract", "mix"};

// The source as a list of tokens. Rules below match and edit the code tokens; whitespace and comments (trivia)
// pass through untouched, which keeps every line of the shader on its line.
struct Token {
    enum Kind { Trivia, Word, Number, Punctuation } kind;
    std::string text;
    int line;
};

std::vector<Token> tokenize(const std::string& source) {
    const auto isWordChar = [](char c) { return std::isalnum(static_cast<unsigned char>(c)) || c == '_'; };
    std::vector<Token> tokens;
    int line = 1;
    for (size_t at = 0, end; at < source.size(); at = end) {
        const char c = source[at], next = at + 1 < source.size() ? source[at + 1] : '\0';
        Token::Kind kind = Token::Punctuation;
        end = at + 1;
        if (std::isspace(static_cast<unsigned char>(c))) {
            kind = Token::Trivia;
            while (end < source.size() && std::isspace(static_cast<unsigned char>(source[end]))) end++;
        } else if (c == '/' && next == '/') {
            kind = Token::Trivia;
            end = std::min(source.find('\n', at), source.size());
        } else if (c == '/' && next == '*') {
            kind = Token::Trivia;
            end = source.find("*/", at + 2);
            end = end == std::string::npos ? source.size() : end + 2;
        } else if (std::isdigit(static_cast<unsigned char>(c)) ||
                   (c == '.' && std::isdigit(static_cast<unsigned char>(next)))) {
            kind = Token::Number;   // 2, 0.5, .5, 1e-3, 1.0f
            while (end < source.size() &&
                   (isWordChar(source[end]) || source[end] == '.' ||
                    ((source[end] == '-' || source[end] == '+') && std::tolower(source[end - 1]) == 'e')))
                end++;
        } else if (isWordChar(c)) {
            kind = Token::Word;
            while (end < source.size() && isWordChar(source[end])) end++;
        }
        tokens.push_back({kind, source.substr(at, end - at), line});
        line += static_cast<int>(std::count(source.begin() + at, source.begin() + end, '\n'));
    }
    return tokens;
}

// The code tokens of a token list, for matching a few tokens ahead without bounds checks.
class Code {
public:
    explicit Code(std::vector<Token>& tokens) {
        for (Token& t : tokens)
            if (t.kind != Token::Trivia && !t.text.empty()) code_.push_back(&t);
    }
    size_t size() const { return code_.size(); }
    std::string& operator[](size_t i) { return code_[i]->text; }
    bool is(size_t i, const char* text) const { return i < code_.size() && code_[i]->text == text; }
    bool is(size_t i, Token::Kind kind) const {
        return i < code_.size() && code_[i]->kind == kind && !code_[i]->text.empty();
    }
    bool isWord(size_t i) const { return is(i, Token::Word); }
    bool isNumber(size_t i) const { return is(i, Token::Number); }
    bool endsLine(size_t i) const { return i + 1 >= code_.size() || code_[i + 1]->line > code_[i]->line; }
    bool sameLine(size_t first, size_t last) const {
        return last < code_.size() && code_[first]->line == code_[last]->line;
    }
    void drop(size_t first, size_t count) {
        for (size_t i = first; i < first + count; i++) code_[i]->text.clear();
    }

private:
    std::vector<Token*> code_;
};

std::string joined(const std::vector<Token>& tokens) {
    std::string text;
    for (const Token& t : tokens) text += t.text;
    return text;
}

bool startsWith(const std::string& text, const char* prefix) { return text.rfind(prefix, 0) == 0; }

// The template with its placeholder replaced.
std::string filled(std::string text, const char* placeholder, const std::string& value) {
    return text.replace(text.find(placeholder), strlen(placeholder), value);
}

// A setting is a line `#define NAME number`, optionally with a sign and a trailing comment.
struct Setting {
    size_t name, value;   // positions in the code
};

std::vector<Setting> findSettings(const Code& code) {
    std::vector<Setting> settings;
    for (size_t i = 0; i + 3 < code.size(); i++) {
        if (!code.is(i, "#") || !code.is(i + 1, "define") || !code.isWord(i + 2)) continue;
        const size_t value = code.is(i + 3, "-") ? i + 4 : i + 3;
        if (code.isNumber(value) && code.sameLine(i, value) && code.endsLine(value)) settings.push_back({i + 2, value});
    }
    return settings;
}

// HLSL syntax without a GLSL counterpart: `: register(b0)`, `static`, `[loop]`, `[unroll(8)]`.
void dropHlslSyntax(std::vector<Token>& tokens) {
    Code code(tokens);
    for (size_t i = 0; i < code.size(); i++) {
        if (code.is(i, ":") && code.is(i + 1, "register") && code.is(i + 2, "(") && code.is(i + 4, ")")) {
            code.drop(i, 5);
        } else if (code.is(i, "static")) {
            code.drop(i, 1);
        } else if (code.is(i, "[") && code.isWord(i + 1) && kAttributes.count(code[i + 1])) {
            const bool withCount = code.is(i + 2, "(") && code.isNumber(i + 3) && code.is(i + 4, ")");
            if (code.is(i + 2, "]")) code.drop(i, 3);
            else if (withCount && code.is(i + 5, "]")) code.drop(i, 6);
        }
    }
}

// What the prelude declares itself: the texture, its sampler and the constant buffers. Also the GLSL-to-HLSL
// adapter some shaders carry (`#define vec3 float3`): with the prelude's macros it would form a cycle.
void dropDeclarations(std::vector<Token>& tokens) {
    Code code(tokens);
    for (size_t i = 0; i < code.size(); i++) {
        if ((code.is(i, "Texture2D") || code.is(i, "SamplerState")) && code.isWord(i + 1) && code.is(i + 2, ";")) {
            code.drop(i, 3);
        } else if (code.is(i, "cbuffer") && code.isWord(i + 1) && code.is(i + 2, "{")) {
            size_t close = i + 3;
            while (close < code.size() && !code.is(close, "}")) close++;
            if (close < code.size()) code.drop(i, close - i + (code.is(close + 1, ";") ? 2 : 1));
        } else if (code.is(i, "#") && code.is(i + 1, "define") && code.isWord(i + 2) && kGlslNames.count(code[i + 2]) &&
                   code.isWord(i + 3) && code.sameLine(i, i + 3) && code.endsLine(i + 3)) {
            code.drop(i, 4);
        }
    }
}

// Expressions that are spelled differently in GLSL.
void rewriteExpressions(std::vector<Token>& tokens) {
    Code code(tokens);
    for (size_t i = 0; i < code.size(); i++) {
        if (code.isWord(i) && code.isWord(i + 1) && code.is(i + 2, "[") && code.is(i + 4, "]") && code.is(i + 5, "=") &&
            code.is(i + 6, "{")) {
            // float a[3] = { 1.0, 2.0, 3.0 }  ->  float a[3] = float[3](1.0, 2.0, 3.0)
            size_t close = i + 7;
            while (close < code.size() && !code.is(close, "}") && !code.is(close, "{")) close++;
            if (!code.is(close, "}")) continue;   // nested braces: not a flat array
            code[i + 6] = code[i] + "[" + code[i + 3] + "](";
            code[close] = ")";
        } else if (code.isWord(i) && code.is(i + 1, ".") && code.is(i + 2, "Sample") && code.is(i + 3, "(") &&
                   code.isWord(i + 4) && code.is(i + 5, ",")) {
            // tex.Sample(sampler, uv)  ->  texture(tex, uv)
            code[i + 4] = code[i];
            code[i] = "texture";
            code.drop(i + 1, 2);
        }
    }
}

// Renames the entry point to ps_entry and returns the arguments main() has to call it with. Two forms exist:
//   float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET
//   float4 main(PSInput pin) : SV_TARGET        (a struct that holds the two, position first)
std::string renameEntryPoint(std::vector<Token>& tokens) {
    Code code(tokens);
    for (size_t i = 0; i < code.size(); i++) {
        if (!code.is(i, "float4") || !code.is(i + 1, "main") || !code.is(i + 2, "(")) continue;
        const bool position =
            code.is(i + 3, "float4") && code.isWord(i + 4) && code.is(i + 5, ":") && code.is(i + 6, "SV_POSITION");
        const bool texcoord = code.is(i + 8, "float2") && code.isWord(i + 9) && code.is(i + 10, ":") &&
                              code.isWord(i + 11) && startsWith(code[i + 11], "TEXCOORD");
        const bool plain = position && code.is(i + 7, ",") && texcoord && code.is(i + 12, ")");
        const bool viaStruct = code.isWord(i + 3) && code.isWord(i + 4) && code.is(i + 5, ")");
        const size_t close = plain ? i + 12 : i + 5;
        const bool returnsColour =
            code.is(close + 1, ":") && code.isWord(close + 2) && startsWith(code[close + 2], "SV_TARGET");
        if ((!plain && !viaStruct) || !returnsColour) continue;
        const std::string arguments = "vec4(p, 0.0, 1.0), p / Resolution";
        const std::string structName = code[i + 3];
        code[i + 1] = "ps_entry";
        return viaStruct ? structName + "(" + arguments + ")" : arguments;
    }
    throw std::runtime_error(
        "no entry point `float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET`");
}

// SV_POSITION, TEXCOORD0, COLOR ...
bool isSemantic(const std::string& word) {
    if (startsWith(word, "SV_")) return true;
    for (const char* name : {"TEXCOORD", "POSITION", "COLOR"})
        if (startsWith(word, name))
            return std::all_of(word.begin() + strlen(name), word.end(),
                               [](unsigned char c) { return std::isdigit(c); });
    return false;
}

// Removes `: SEMANTIC` after a declaration: `float4 pos : SV_POSITION` or `float4 main(...) : SV_TARGET`. The
// colon of `a ? b : COLOR` is none of its business.
void stripSemantics(std::vector<Token>& tokens) {
    Code code(tokens);
    for (size_t i = 2; i < code.size(); i++) {
        if (!code.is(i, ":") || !code.isWord(i + 1) || !isSemantic(code[i + 1])) continue;
        size_t name = i - 1;   // the declared variable, or the function in front of its parameter list
        if (code.is(name, ")")) {
            int depth = 1;
            while (name > 0 && depth > 0) {
                name--;
                depth += code.is(name, ")") ? 1 : code.is(name, "(") ? -1 : 0;
            }
            if (name == 0) continue;
            name--;
        }
        if (name >= 1 && code.isWord(name) && code.isWord(name - 1)) code.drop(i, 2);
    }
}

}   // namespace

const char* const kVertexShader = R"(
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}
)";

bool isNumberLiteral(const std::string& text) {
    size_t at = 0;
    const auto digits = [&] {
        const size_t start = at;
        while (at < text.size() && std::isdigit(static_cast<unsigned char>(text[at]))) at++;
        return at - start;
    };
    if (at < text.size() && (text[at] == '-' || text[at] == '+')) at++;
    size_t mantissa = digits();
    if (at < text.size() && text[at] == '.') {
        at++;
        mantissa += digits();
    }
    if (mantissa == 0) return false;
    if (at < text.size() && (text[at] == 'e' || text[at] == 'E')) {
        at++;
        if (at < text.size() && (text[at] == '-' || text[at] == '+')) at++;
        if (digits() == 0) return false;
    }
    if (at < text.size() && (text[at] == 'f' || text[at] == 'F')) at++;
    return at == text.size();
}

std::string applyOverrides(const std::string& hlsl, const std::map<std::string, std::string>& overrides) {
    if (overrides.empty()) return hlsl;
    for (const auto& [name, value] : overrides)
        if (!isNumberLiteral(value))
            throw std::runtime_error("the value of " + name + " must be a number, not '" + value + "'");

    std::vector<Token> tokens = tokenize(hlsl);
    Code code(tokens);
    std::set<std::string> done;
    for (const Setting& setting : findSettings(code)) {
        auto found = overrides.find(code[setting.name]);
        if (found == overrides.end() || !done.insert(found->first).second) continue;
        // A float setting stays a float when it is given a whole number: GLSL ES would not convert `0` itself.
        const std::string& old = code[setting.value];
        const bool isFloat = old.find_first_of(".eEfF") != std::string::npos && !startsWith(old, "0x");
        const bool wholeNumber = found->second.find_first_not_of("-+0123456789") == std::string::npos;
        code.drop(setting.name + 1, setting.value - (setting.name + 1));   // a minus sign, if there is one
        code[setting.value] = found->second + (isFloat && wholeNumber ? ".0" : "");
    }
    for (const auto& [name, value] : overrides)
        if (!done.count(name)) throw std::runtime_error("there is no setting called " + name);
    return joined(tokens);
}

std::vector<std::string> settingNames(const std::string& hlsl) {
    std::vector<Token> tokens = tokenize(hlsl);
    Code code(tokens);
    std::vector<std::string> names;
    for (const Setting& setting : findSettings(code)) names.push_back(code[setting.name]);
    return names;
}

std::string translateToGlsl(const std::string& hlsl, Dialect dialect) {
    std::string source = hlsl;
    source.erase(std::remove(source.begin(), source.end(), '\r'), source.end());
    std::vector<Token> tokens = tokenize(source);

    // Every pass edits tokens in place and never touches whitespace or comments, so no line moves.
    dropHlslSyntax(tokens);
    dropDeclarations(tokens);   // after dropHlslSyntax: `Texture2D t : register(t0);` is `Texture2D t;` by now
    rewriteExpressions(tokens);
    const std::string arguments = renameEntryPoint(tokens);
    stripSemantics(tokens);

    const char* header = dialect == Dialect::GlslEs300 ? kHeaderEs : kHeader;
    return header + (kPrelude + filled(kUniforms, "MAX_MONITORS", std::to_string(kMaxMonitors))) + joined(tokens) +
           filled(kEpilogue, "ENTRY_ARGUMENTS", arguments);
}
