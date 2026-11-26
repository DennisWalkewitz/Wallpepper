// Tests the HLSL-to-GLSL translator and the settings code (src/translate.cpp). No GPU involved: these are
// checks on text. That the translated text compiles and draws the right picture is tested by run.sh.
#include "../src/translate.h"

#include <algorithm>
#include <cstdio>
#include <stdexcept>

namespace {

int failures = 0;

void expect(const char* what, bool ok) {
    printf("%s  %s\n", ok ? "ok  " : "FAIL", what);
    if (!ok) failures++;
}

const std::string kEntry =
    "float4 main(float4 pos : SV_POSITION, float2 tex : TEXCOORD) : SV_TARGET { return float4(tex, 0.0, 1.0); }\n";

// The shader's own part of a translation: what lies between the prelude and the generated main().
std::string body(const std::string& hlsl, Dialect dialect = Dialect::Glsl330) {
    const std::string glsl = translateToGlsl(hlsl, dialect);
    const size_t start = glsl.find("#line 1\n") + 8, end = glsl.rfind("\nvoid main() {");
    return glsl.substr(start, end - start);
}

// Dropped tokens leave their surrounding blanks behind; for comparisons, runs of blanks count as one.
std::string squeezed(const std::string& text) {
    std::string out;
    for (char c : text)
        if (c != ' ' || out.empty() || out.back() != ' ') out += c;
    return out;
}

bool has(const std::string& text, const std::string& part) { return squeezed(text).find(part) != std::string::npos; }
long lines(const std::string& text) { return std::count(text.begin(), text.end(), '\n'); }

// What the call throws, or "" when it does not.
template <typename Call>
std::string errorOf(Call call) {
    try {
        call();
    } catch (const std::runtime_error& e) {
        return e.what();
    }
    return "";
}

void testRewrites() {
    std::string out =
        body("Texture2D shaderTexture : register(t0);\nSamplerState samplerState : register(s0);\n" + kEntry);
    expect("texture and sampler declarations go, with their registers",
           !has(out, "Texture2D") && !has(out, "SamplerState") && !has(out, "register"));

    out = body(
        "cbuffer PixelShaderSettings : register(b0) {\n  float Time; // seconds\n  float2 Resolution;\n};\ncbuffer "
        "Other { float4 Monitors[8]; }\n" +
        kEntry);
    expect("constant buffers go, whatever their name",
           !has(out, "cbuffer") && !has(out, "Resolution;") && !has(out, "Monitors"));

    out = body("static const float k = 2.0;\nstatic float helper() { return k; }\n" + kEntry);
    expect("static is dropped", !has(out, "static") && has(out, "const float k = 2.0;") && has(out, "float helper()"));

    out = body(
        "float f() { float s = 0.0; [loop] for (int i = 0; i < 4; i++) s += 1.0; [unroll(8)] for (int j = 0; j < 8; "
        "j++) s += 1.0; return s; }\n" +
        kEntry);
    expect("loop attributes are dropped",
           !has(out, "[loop]") && !has(out, "unroll") && has(out, "for (int i = 0;") && has(out, "for (int j = 0;"));

    out = body("#define vec3  float3\n#define mix lerp // adapter\n#define SPEED 1.5\n" + kEntry);
    expect("GLSL adapter macros go, settings stay",
           !has(out, "#define vec3") && !has(out, "#define mix") && has(out, "#define SPEED 1.5"));

    out = body("static const float3 cols[2] = { float3(1.0, 0.0, 0.0), float3(0.0, 1.0, 0.0) };\n" + kEntry);
    expect("a flat array initialiser becomes a constructor",
           has(out, "float3 cols[2] = float3[2]( float3(1.0, 0.0, 0.0), float3(0.0, 1.0, 0.0) );"));

    out = body("float4 f(float2 uv) { return shaderTexture.Sample(samplerState, uv + float2(0.1, 0.0)); }\n" + kEntry);
    expect("Sample becomes texture", has(out, "return texture(shaderTexture, uv + float2(0.1, 0.0));"));

    out =
        body("float f(bool c, float COLOR, float2 uv) { float a = c ? 1.0 : COLOR; return c ? length(uv) : COLOR; }\n" +
             kEntry);
    expect("a ternary is not mistaken for a semantic", has(out, "? 1.0 : COLOR;") && has(out, "? length(uv) : COLOR;"));
}

void testEntryPoint() {
    std::string glsl = translateToGlsl(kEntry);
    expect("the usual entry point", has(glsl, "float4 ps_entry(float4 pos , float2 tex ) {") &&
                                        has(glsl, "ps_entry(vec4(p, 0.0, 1.0), p / Resolution);"));

    glsl = translateToGlsl(
        "struct PSInput {\n  float4 pos : SV_POSITION;\n  float2 uv : TEXCOORD0;\n};\n"
        "float4 main(PSInput pin) : SV_TARGET { return float4(pin.uv, 0.0, 1.0); }\n");
    expect("an entry point that takes a struct",
           has(glsl, "float4 pos ;") && has(glsl, "float2 uv ;") && has(glsl, "float4 ps_entry(PSInput pin) {") &&
               has(glsl, "ps_entry(PSInput(vec4(p, 0.0, 1.0), p / Resolution));"));

    expect("no entry point is an error",
           has(errorOf([] { translateToGlsl("float4 other() { return float4(0.0, 0.0, 0.0, 1.0); }\n"); }),
               "no entry point"));
}

void testWholeOutput() {
    const std::string hlsl =
        "// header\nTexture2D shaderTexture;\nSamplerState samplerState;\ncbuffer PixelShaderSettings {\n  float "
        "Time;\n"
        "  float Scale;\n  float2 Resolution;\n  float4 Background;\n};\n\n#define vec2 float2\n/* a comment\n   over "
        "two lines */\n"
        "static const float a[2] = {\n  1.0,\n  2.0\n};\n" +
        kEntry;
    expect("every line stays on its line", lines(body(hlsl)) == lines(hlsl));

    std::string crlf = hlsl;
    for (size_t at = 0; (at = crlf.find('\n', at)) != std::string::npos; at += 2) crlf.replace(at, 1, "\r\n");
    expect("CRLF input gives the same translation", translateToGlsl(crlf) == translateToGlsl(hlsl));

    const std::string desktop = translateToGlsl(hlsl), es = translateToGlsl(hlsl, Dialect::GlslEs300);
    expect("GLSL 3.30 header, no extensions",
           desktop.rfind("#version 330 core\n", 0) == 0 && !has(desktop, "#extension"));
    expect("GLSL ES 3.00 header with precision", es.rfind("#version 300 es\nprecision highp float;\n", 0) == 0);
    expect("the Monitors array has kMaxMonitors entries",
           has(desktop, "Monitors[" + std::to_string(kMaxMonitors) + "];"));
}

void testSettings() {
    const std::string hlsl =
        "#define A 0.25   // a comment\n  #define B -0.5\n#define C 1\n#define D 2 + 1\n#define E 1.0f\n#define C 7\n" +
        kEntry;
    const std::vector<std::string> names = settingNames(hlsl);
    expect("settings are the #defines with a plain number",
           names == std::vector<std::string>({"A", "B", "C", "E", "C"}));

    std::string out = applyOverrides(hlsl, {{"A", "0.5"}, {"B", "2"}, {"C", "0"}, {"E", "3"}});
    expect("a value is replaced and the comment kept", has(out, "#define A 0.5 // a comment\n"));
    expect("a whole number given to a float setting stays a float, sign and all",
           has(out, "#define B 2.0\n") && has(out, "#define E 3.0\n"));
    expect("an integer setting stays an integer; only its first definition changes",
           has(out, "#define C 0\n") && has(out, "#define C 7\n"));
    expect("nothing else changes", lines(out) == lines(hlsl) && has(out, "#define D 2 + 1\n"));

    expect("an unknown name is an error",
           has(errorOf([&] { applyOverrides(hlsl, {{"NOPE", "1"}}); }), "no setting called NOPE"));
    expect("a #define that is not a plain number is not a setting",
           has(errorOf([&] { applyOverrides(hlsl, {{"D", "1"}}); }), "no setting called D"));
    bool rejected = true, accepted = true;
    for (const char* bad : {"", "abc", "1;", "1 2", "1\nuniform float injected;", "0x10", "--1", "1e", "."})
        rejected = rejected && !isNumberLiteral(bad) &&
                   has(errorOf([&] { applyOverrides(hlsl, {{"A", bad}}); }), "must be a number");
    for (const char* good : {"2", "-0.5", "+2", ".5", "5.", "1e-3", "1.5E+2", "1.0f", "3F"})
        accepted = accepted && isNumberLiteral(good) && errorOf([&] { applyOverrides(hlsl, {{"A", good}}); }).empty();
    expect("values that are not numbers are errors, among them one with a line break", rejected);
    expect("numbers in every spelling are accepted", accepted);
}

}   // namespace

int main() {
    testRewrites();
    testEntryPoint();
    testWholeOutput();
    testSettings();
    printf("%s\n", failures ? "translate: FAILED" : "translate: all cases pass");
    return failures ? 1 : 0;
}
