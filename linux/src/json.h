#pragma once
#include <string>

// A JSON string literal. Control characters become spaces; none of the strings written here need them.
inline std::string jsonString(const std::string& text) {
    std::string out = "\"";
    for (unsigned char c : text) {
        if (c == '"' || c == '\\') out += '\\';
        out += c < 0x20 ? ' ' : static_cast<char>(c);
    }
    return out + "\"";
}
