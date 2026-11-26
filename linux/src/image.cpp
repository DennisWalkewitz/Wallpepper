#include "image.h"

#include <algorithm>
#include <cctype>
#include <fstream>
#include <stdexcept>

namespace {

void put32(std::string& out, uint32_t v) {
    for (int shift = 24; shift >= 0; shift -= 8) out += static_cast<char>(v >> shift);
}

uint32_t crc32(const std::string& data) {
    uint32_t crc = 0xFFFFFFFFu;
    for (unsigned char byte : data) {
        crc ^= byte;
        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1)));
    }
    return ~crc;
}

std::string chunk(const char* type, const std::string& data) {
    std::string body = type + data, out;
    put32(out, static_cast<uint32_t>(data.size()));
    out += body;
    put32(out, crc32(body));
    return out;
}

// A valid PNG without a compressor: the zlib stream consists of "stored" deflate blocks.
std::string png(const std::string& rgb, int width, int height) {
    std::string header;
    put32(header, width);
    put32(header, height);
    header += std::string("\x08\x02\x00\x00\x00", 5);   // 8 bits per channel, RGB, no interlacing

    std::string raw;   // every row starts with its filter type (0 = none)
    const size_t row = static_cast<size_t>(width) * 3;
    for (int y = 0; y < height; y++) raw.append(1, '\0').append(rgb, row * y, row);

    std::string zlib = "\x78\x01";
    uint32_t a = 1, b = 0;   // Adler-32
    for (unsigned char byte : raw) {
        a = (a + byte) % 65521;
        b = (b + a) % 65521;
    }
    for (size_t at = 0; at < raw.size(); at += 65535) {
        size_t length = std::min<size_t>(65535, raw.size() - at);
        zlib += at + length == raw.size() ? '\x01' : '\x00';
        zlib += static_cast<char>(length);
        zlib += static_cast<char>(length >> 8);
        zlib += static_cast<char>(~length);
        zlib += static_cast<char>(~length >> 8);
        zlib.append(raw, at, length);
    }
    put32(zlib, b << 16 | a);

    return "\x89PNG\r\n\x1a\n" + chunk("IHDR", header) + chunk("IDAT", zlib) + chunk("IEND", "");
}

std::string lowerExtension(const std::string& path) {
    std::string extension = path.size() >= 4 ? path.substr(path.size() - 4) : "";
    std::transform(extension.begin(), extension.end(), extension.begin(),
                   [](unsigned char c) { return std::tolower(c); });
    return extension;
}

}   // namespace

void checkImagePath(const std::string& path) {
    const std::string extension = lowerExtension(path);
    if (extension != ".png" && extension != ".ppm")
        throw std::runtime_error("images are written as .png or .ppm, not as '" + path + "'");
}

void writeImage(const std::string& path, const std::vector<uint8_t>& rgba, int width, int height) {
    checkImagePath(path);
    std::string rgb;
    rgb.reserve(rgba.size() / 4 * 3);
    for (size_t i = 0; i + 3 < rgba.size(); i += 4) rgb.append(reinterpret_cast<const char*>(&rgba[i]), 3);

    std::ofstream out(path, std::ios::binary);
    if (lowerExtension(path) == ".png") out << png(rgb, width, height);
    else out << "P6\n" << width << " " << height << "\n255\n" << rgb;
    if (!out.flush()) throw std::runtime_error("cannot write " + path);
}

std::vector<uint8_t> sampleTextLayer(int width, int height) {
    // Each row: its colour and the lengths of its words, in character cells; 0 ends the row.
    struct Row {
        uint8_t colour[3];
        int words[6];
    };
    static const Row rows[] = {
        {{204, 204, 204}, {2, 3, 6}},
        {{204, 204, 204}, {2, 6, 4}},
        {{231, 72, 86}, {9, 14}},
        {{204, 204, 204}, {}},
        {{204, 204, 204}, {2, 5, 5, 9}},
        {{22, 198, 12}, {9, 12, 6}},
        {{22, 198, 12}, {8, 9, 7, 11, 2, 6}},
        {{249, 241, 165}, {7, 4, 10, 3}},
        {{204, 204, 204}, {2, 1}},
    };
    const int cellHeight = std::max(10, height / 34), cellWidth = std::max(5, cellHeight / 2);
    std::vector<uint8_t> layer(static_cast<size_t>(width) * height * 4, 0);
    int top = cellHeight / 2;
    for (const Row& row : rows) {
        int left = cellWidth;
        for (int word : row.words) {
            for (int y = top + cellHeight / 5; word > 0 && y < std::min(top + cellHeight * 4 / 5, height); y++)
                for (int x = left; x < std::min(left + word * cellWidth, width); x++) {
                    uint8_t* pixel = &layer[(static_cast<size_t>(y) * width + x) * 4];
                    std::copy_n(row.colour, 3, pixel);
                    pixel[3] = 255;
                }
            left += (word + 1) * cellWidth;
        }
        top += cellHeight * 4 / 3;
    }
    return layer;
}
