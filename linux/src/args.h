#pragma once
#include <cmath>
#include <cstdio>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>

// The command line after the command word: positional arguments plus --options. Every option a command asks
// for is ticked off, so that finish() can reject the ones nobody asked for.
class Args {
public:
    // flags: the options that take no value.
    Args(int argc, char** argv, const std::set<std::string>& flags) {
        for (int i = 2; i < argc; i++) {
            std::string arg = argv[i];
            if (arg == "-o") arg = "--out";
            if (arg.rfind("--", 0) != 0) positional.push_back(arg);
            else if (flags.count(arg)) options_[arg].push_back("");
            else if (i + 1 < argc) options_[arg].push_back(argv[++i]);
            else throw std::runtime_error(arg + " needs a value");
        }
    }

    std::vector<std::string> positional;

    std::vector<std::string> values(const std::string& name) {
        auto found = options_.find(name);
        if (found == options_.end()) return {};
        std::vector<std::string> values = std::move(found->second);
        options_.erase(found);
        return values;
    }
    bool flag(const std::string& name) { return !values(name).empty(); }
    std::string text(const std::string& name) {
        std::vector<std::string> all = values(name);
        return all.empty() ? "" : all.back();
    }
    double number(const std::string& name, double fallback, double min, double max) {
        return parsed(name, fallback, min, max, false);
    }
    long integer(const std::string& name, long fallback, long min, long max) {
        return std::lround(
            parsed(name, static_cast<double>(fallback), static_cast<double>(min), static_cast<double>(max), true));
    }
    // --size 1280x720; false when the option is absent.
    bool size(int& width, int& height, int maxSide) {
        std::string value = text("--size");
        if (value.empty()) return false;
        char rest = 0;
        if (sscanf(value.c_str(), "%dx%d%c", &width, &height, &rest) != 2 || width < 1 || height < 1 ||
            width > maxSide || height > maxSide)
            throw std::runtime_error("--size must look like 1280x720, with at most " + std::to_string(maxSide) +
                                     " pixels a side");
        return true;
    }
    // --set NAME=VALUE, any number of times.
    std::map<std::string, std::string> overrides() {
        std::map<std::string, std::string> overrides;
        for (const std::string& setting : values("--set")) {
            size_t equals = setting.find('=');
            if (equals == 0 || equals == std::string::npos) throw std::runtime_error("--set must look like NAME=VALUE");
            overrides[setting.substr(0, equals)] = setting.substr(equals + 1);
        }
        return overrides;
    }
    void finish(size_t minPositional, size_t maxPositional) const {
        if (!options_.empty())
            throw std::runtime_error("unknown option " + options_.begin()->first + " (see `wallpepper --help`)");
        if (positional.size() < minPositional || positional.size() > maxPositional)
            throw std::runtime_error("wrong number of arguments (see `wallpepper --help`)");
    }

private:
    double parsed(const std::string& name, double fallback, double min, double max, bool whole) {
        std::string value = text(name);
        if (value.empty()) return fallback;
        size_t used = 0;
        double number = 0;
        try {
            number = std::stod(value, &used);
        } catch (const std::exception&) {
            used = 0;
        }
        if (used != value.size() || !(number >= min && number <= max) || (whole && number != std::floor(number))) {
            char range[80];
            snprintf(range, sizeof range, " must be a %s from %g to %g", whole ? "whole number" : "number", min, max);
            throw std::runtime_error(name + range);
        }
        return number;
    }

    std::map<std::string, std::vector<std::string>> options_;
};
