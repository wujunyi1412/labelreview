cmake_minimum_required(VERSION 3.21)

project(LabelReviewer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(OPENCV_ROOT "D:/opencv-4.5.3/opencv-4.5.3/build/install" CACHE PATH
    "OpenCV 4.5.3 installation directory")

set(OPENCV_INCLUDE_DIR "${OPENCV_ROOT}/include")
set(OPENCV_LIB_DIR "${OPENCV_ROOT}/lib")
foreach(required_file
        "${OPENCV_INCLUDE_DIR}/opencv2/core.hpp"
        "${OPENCV_LIB_DIR}/opencv_core453.lib"
        "${OPENCV_LIB_DIR}/opencv_imgproc453.lib"
        "${OPENCV_LIB_DIR}/opencv_imgcodecs453.lib")
    if(NOT EXISTS "${required_file}")
        message(FATAL_ERROR "Required OpenCV 4.5.3 file not found: ${required_file}")
    endif()
endforeach()

add_library(ImageBridge SHARED
    src/native/ImageBridge.cpp
)
target_include_directories(ImageBridge PRIVATE "${OPENCV_INCLUDE_DIR}")
target_link_libraries(ImageBridge PRIVATE
    "${OPENCV_LIB_DIR}/opencv_core453.lib"
    "${OPENCV_LIB_DIR}/opencv_imgproc453.lib"
    "${OPENCV_LIB_DIR}/opencv_imgcodecs453.lib")
target_compile_definitions(ImageBridge PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)

if(MSVC)
    target_compile_options(ImageBridge PRIVATE /W4 /utf-8 /EHsc)
endif()

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
set(APP_PROJECT "${CMAKE_CURRENT_SOURCE_DIR}/src/app/LabelReviewer.csproj")

# The WinForms application stays an SDK-style C# project.  CMake owns the
# complete build: it first builds the OpenCV bridge, then invokes dotnet and
# passes the exact native output directories to the C# project.
add_custom_target(LabelReviewer ALL
    COMMAND "${DOTNET_EXECUTABLE}" build "${APP_PROJECT}"
            --configuration "$<CONFIG>"
            -p:NativeBridgeDir="$<TARGET_FILE_DIR:ImageBridge>"
            -p:OpenCvBinDir="${OPENCV_ROOT}/bin"
    DEPENDS ImageBridge
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
    COMMENT "Building LabelReviewer WinForms application"
    VERBATIM
)

set_property(DIRECTORY PROPERTY VS_STARTUP_PROJECT LabelReviewer)
