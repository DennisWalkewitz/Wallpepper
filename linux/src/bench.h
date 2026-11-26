#pragma once
#include <string>

struct BenchOptions {
    std::string shaderPath;   // the workload
    bool reference = true;    // false: a stand-in shader chosen with --shader
    bool quick = false, json = false;
    int width = 0, height = 0;   // 0: the size of the mode
    int device = -1;
    std::string outFile;   // also write the JSON here
};

// The procedure of docs/BENCHMARK.md. Returns the process exit code.
int runBenchmark(const BenchOptions& options);
