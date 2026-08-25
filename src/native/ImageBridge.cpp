#include <windows.h>

#include <opencv2/core.hpp>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

thread_local std::string g_lastError;

void setError(const std::string& message) {
    g_lastError = message;
}

cv::Mat readUnicodeImage(const wchar_t* path) {
    std::ifstream stream(std::wstring(path), std::ios::binary | std::ios::ate);
    if (!stream) {
        throw std::runtime_error("Cannot open the image file.");
    }

    const auto end = stream.tellg();
    if (end <= 0) {
        throw std::runtime_error("The image file is empty.");
    }

    std::vector<std::uint8_t> bytes(static_cast<std::size_t>(end));
    stream.seekg(0, std::ios::beg);
    stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (!stream) {
        throw std::runtime_error("Cannot read the complete image file.");
    }

    cv::Mat encoded(1, static_cast<int>(bytes.size()), CV_8U, bytes.data());
    cv::Mat image = cv::imdecode(encoded, cv::IMREAD_UNCHANGED);
    if (image.empty()) {
        throw std::runtime_error("OpenCV cannot decode this image.");
    }
    return image;
}

int bitDepthFor(int depth) {
    switch (depth) {
        case CV_8U:
        case CV_8S: return 8;
        case CV_16U:
        case CV_16S: return 16;
        case CV_32S:
        case CV_32F: return 32;
        case CV_64F: return 64;
        default: return 0;
    }
}

cv::Mat stretchTo8Bit(const cv::Mat& source) {
    if (source.depth() == CV_8U) {
        return source;
    }

    cv::Mat finiteSource;
    if (source.depth() == CV_64F) {
        source.convertTo(finiteSource, CV_MAKETYPE(CV_32F, source.channels()));
    } else {
        source.convertTo(finiteSource, source.type());
    }
    if (finiteSource.depth() == CV_32F) {
        cv::patchNaNs(finiteSource, 0.0);
    }

    std::vector<cv::Mat> planes;
    cv::split(finiteSource, planes);
    const std::size_t visualPlaneCount = std::min<std::size_t>(planes.size(), 3);
    double minimum = std::numeric_limits<double>::max();
    double maximum = std::numeric_limits<double>::lowest();

    for (std::size_t i = 0; i < visualPlaneCount; ++i) {
        double localMinimum = 0.0;
        double localMaximum = 0.0;
        cv::minMaxLoc(planes[i], &localMinimum, &localMaximum);
        minimum = std::min(minimum, localMinimum);
        maximum = std::max(maximum, localMaximum);
    }

    std::vector<cv::Mat> converted;
    converted.reserve(planes.size());
    if (!std::isfinite(minimum) || !std::isfinite(maximum) || maximum <= minimum) {
        const double fill = maximum > 0.0 ? 127.0 : 0.0;
        for (std::size_t i = 0; i < planes.size(); ++i) {
            cv::Mat output(planes[i].size(), CV_8U,
                           cv::Scalar(i == 3 ? 255.0 : fill));
            converted.push_back(output);
        }
    } else {
        const double scale = 255.0 / (maximum - minimum);
        const double offset = -minimum * scale;
        for (std::size_t i = 0; i < planes.size(); ++i) {
            cv::Mat output;
            if (i == 3) {
                // Alpha is display metadata rather than image intensity.
                output = cv::Mat(planes[i].size(), CV_8U, cv::Scalar(255));
            } else {
                planes[i].convertTo(output, CV_8U, scale, offset);
            }
            converted.push_back(output);
        }
    }

    cv::Mat result;
    cv::merge(converted, result);
    return result;
}

cv::Mat toBgra(const cv::Mat& source) {
    cv::Mat display = stretchTo8Bit(source);
    cv::Mat bgra;
    switch (display.channels()) {
        case 1:
            cv::cvtColor(display, bgra, cv::COLOR_GRAY2BGRA);
            break;
        case 3:
            cv::cvtColor(display, bgra, cv::COLOR_BGR2BGRA);
            break;
        case 4:
            bgra = display;
            break;
        default: {
            std::vector<cv::Mat> planes;
            cv::split(display, planes);
            cv::cvtColor(planes.front(), bgra, cv::COLOR_GRAY2BGRA);
            break;
        }
    }
    return bgra.isContinuous() ? bgra : bgra.clone();
}

}  // namespace

extern "C" {

__declspec(dllexport) int __cdecl lr_load_image(
    const wchar_t* path,
    unsigned char** pixels,
    int* width,
    int* height,
    int* stride,
    int* sourceBitDepth,
    int* sourceChannels) {
    if (!path || !pixels || !width || !height || !stride ||
        !sourceBitDepth || !sourceChannels) {
        setError("Invalid argument passed to the image bridge.");
        return 0;
    }

    *pixels = nullptr;
    try {
        const cv::Mat source = readUnicodeImage(path);
        const cv::Mat bgra = toBgra(source);
        const std::size_t byteCount = bgra.total() * bgra.elemSize();
        auto* buffer = static_cast<unsigned char*>(std::malloc(byteCount));
        if (!buffer) {
            throw std::bad_alloc();
        }
        std::memcpy(buffer, bgra.data, byteCount);

        *pixels = buffer;
        *width = bgra.cols;
        *height = bgra.rows;
        *stride = static_cast<int>(bgra.step);
        *sourceBitDepth = bitDepthFor(source.depth());
        *sourceChannels = source.channels();
        g_lastError.clear();
        return 1;
    } catch (const cv::Exception& error) {
        setError(error.what());
    } catch (const std::exception& error) {
        setError(error.what());
    } catch (...) {
        setError("Unknown error while decoding the image.");
    }
    return 0;
}

__declspec(dllexport) void __cdecl lr_free_image(unsigned char* pixels) {
    std::free(pixels);
}

__declspec(dllexport) const char* __cdecl lr_last_error() {
    return g_lastError.c_str();
}

}  // extern "C"
