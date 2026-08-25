@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

for %%I in ("%~dp0.") do set "PROJECT_ROOT=%%~fI"
set "BUILD_DIR=%PROJECT_ROOT%\build-publish"
set "PUBLISH_DIR=%PROJECT_ROOT%\publish\LabelReviewer-win-x64"
set "PACKAGE_ZIP=%PROJECT_ROOT%\publish\LabelReviewer-win-x64.zip"
set "NUGET_PACKAGES=%USERPROFILE%\.nuget\packages"

if not defined OPENCV_ROOT set "OPENCV_ROOT=D:\opencv-4.5.3\opencv-4.5.3\build\install"

echo [1/8] Checking build tools...
where cmake >nul 2>nul || (
    echo ERROR: CMake was not found in PATH.
    goto :failed
)
where dotnet >nul 2>nul || (
    echo ERROR: dotnet SDK was not found in PATH.
    goto :failed
)
if not exist "%OPENCV_ROOT%\include\opencv2\core.hpp" (
    echo ERROR: OpenCV was not found at "%OPENCV_ROOT%".
    echo Set OPENCV_ROOT before running this script if OpenCV is elsewhere.
    goto :failed
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: Visual Studio Installer vswhere.exe was not found.
    goto :failed
)
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS_INSTALL=%%I"
if not defined VS_INSTALL (
    echo ERROR: Visual Studio C++ x64 build tools were not found.
    goto :failed
)
if not exist "%VS_INSTALL%\VC\Auxiliary\Build\vcvars64.bat" (
    echo ERROR: vcvars64.bat was not found.
    goto :failed
)
call "%VS_INSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 goto :failed

for /f "delims=" %%D in ('dir /b /ad /o-n "%VS_INSTALL%\VC\Redist\MSVC" 2^>nul') do (
    if not defined VC_REDIST_VERSION if exist "%VS_INSTALL%\VC\Redist\MSVC\%%D\x64\Microsoft.VC143.CRT\msvcp140.dll" set "VC_REDIST_VERSION=%%D"
)
if not defined VC_REDIST_VERSION (
    echo ERROR: Visual C++ redistributable files were not found.
    goto :failed
)
set "VC_REDIST_DIR=%VS_INSTALL%\VC\Redist\MSVC\%VC_REDIST_VERSION%\x64\Microsoft.VC143.CRT"
if not exist "%VC_REDIST_DIR%\msvcp140.dll" (
    echo ERROR: x64 Visual C++ runtime was not found at "%VC_REDIST_DIR%".
    goto :failed
)

echo [2/8] Configuring native build...
if exist "%BUILD_DIR%\CMakeCache.txt" rmdir /s /q "%BUILD_DIR%"
cmake -S "%PROJECT_ROOT%" -B "%BUILD_DIR%" -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release "-DOPENCV_ROOT=%OPENCV_ROOT%"
if errorlevel 1 goto :failed

echo [3/8] Building ImageBridge...
cmake --build "%BUILD_DIR%" --target ImageBridge
if errorlevel 1 goto :failed
set "NATIVE_DIR=%BUILD_DIR%"
if not exist "%NATIVE_DIR%\ImageBridge.dll" (
    echo ERROR: ImageBridge.dll was not generated.
    goto :failed
)

echo [4/8] Cleaning previous package...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%PACKAGE_ZIP%" del /q "%PACKAGE_ZIP%"
if not exist "%PROJECT_ROOT%\publish" mkdir "%PROJECT_ROOT%\publish"

echo [5/8] Restoring Windows x64 self-contained runtime...
dotnet restore "%PROJECT_ROOT%\src\app\LabelReviewer.csproj" --runtime win-x64
if errorlevel 1 goto :failed

echo [6/8] Publishing self-contained Windows x64 application...
dotnet publish "%PROJECT_ROOT%\src\app\LabelReviewer.csproj" --configuration Release --runtime win-x64 --self-contained true --no-restore --output "%PUBLISH_DIR%" "-p:NativeBridgeDir=%NATIVE_DIR%" "-p:OpenCvBinDir=%OPENCV_ROOT%\bin" "-p:VCRedistDir=%VC_REDIST_DIR%"
if errorlevel 1 goto :failed
copy /y "%PROJECT_ROOT%\README.md" "%PUBLISH_DIR%\README.md" >nul

echo [7/8] Verifying required runtime files...
for %%F in (LabelReviewer.exe ImageBridge.dll opencv_core453.dll opencv_imgproc453.dll opencv_imgcodecs453.dll coreclr.dll hostfxr.dll msvcp140.dll vcruntime140.dll vcruntime140_1.dll concrt140.dll) do (
    if not exist "%PUBLISH_DIR%\%%F" (
        echo ERROR: Missing published file %%F.
        goto :failed
    )
)

echo [8/8] Creating ZIP package...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -LiteralPath $env:PUBLISH_DIR -DestinationPath $env:PACKAGE_ZIP -CompressionLevel Optimal -Force"
if errorlevel 1 goto :failed
for /f "usebackq tokens=*" %%H in (`powershell -NoProfile -Command "(Get-FileHash -LiteralPath $env:PACKAGE_ZIP -Algorithm SHA256).Hash"`) do set "PACKAGE_HASH=%%H"

echo.
echo Build and publish completed successfully.
echo Folder: "%PUBLISH_DIR%"
echo ZIP:    "%PACKAGE_ZIP%"
echo SHA256: !PACKAGE_HASH!
echo.
pause
exit /b 0

:failed
echo.
echo Build or publish failed. Review the error above.
echo.
pause
exit /b 1
