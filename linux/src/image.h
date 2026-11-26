#pragma once
#include <cstdint>
#include <string>
#include <vector>

// Throws std::runtime_error unless the path ends in .png or .ppm (in any letter case), the two formats
// writeImage() knows.
void checkImagePath(const std::string& path);

// Writes RGBA pixels (top row first) as an RGB image: PNG or binary PPM, by the extension of the path.
// Throws std::runtime_error when the file cannot be written.
void writeImage(const std::string& path, const std::vector<uint8_t>& rgba, int width, int height);

// A stand-in for a terminal's text layer, for previews: rows of coloured blocks where a terminal would have
// words, on a transparent background. Premultiplied RGBA, top row first.
std::vector<uint8_t> sampleTextLayer(int width, int height);
