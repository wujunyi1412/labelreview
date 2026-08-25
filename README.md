# 图像复判与矩形标注工具

基于 WPF 的 Windows 桌面复判软件。递归读取输入目录中的 JPEG、PNG、TIF 和 TIFF，支持
OpenCV 可解码的 8/16/32/64 位图像。非 8 位图会基于有效强度范围自动拉伸到
8 位，仅用于屏幕显示；矩形坐标始终对应原图像素。

## 构建环境

- Windows 10/11 x64
- Visual Studio 2022（C++ 桌面开发组件）
- CMake 3.21+
- .NET 9 Windows Desktop Runtime/SDK
- OpenCV 4.5.3 Release，默认路径：
  `D:\opencv-4.5.3\opencv-4.5.3\build\install`

在“x64 Native Tools Command Prompt for VS 2022”中运行（此命令已经过验证）：

```powershell
cmake -S . -B build -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release `
  -DOPENCV_ROOT="D:/opencv-4.5.3/opencv-4.5.3/build/install"
cmake --build build
```

程序输出位置：

```text
src/app/bin/Release/net9.0-windows/LabelReviewer.exe
```

也可以直接双击根目录下的 `build_and_publish.bat`。脚本会自动编译 C++ 桥接库和
C# 程序，生成包含 .NET、OpenCV、VC++ 运行库的 Windows x64 自包含版本，并输出：

```text
publish/LabelReviewer-win-x64/
publish/LabelReviewer-win-x64.zip
```

如果 OpenCV 不在默认位置，请先设置 `OPENCV_ROOT` 环境变量再运行脚本。

若 Visual Studio 已正常注册到系统，也可以使用 `Visual Studio 17 2022` 生成器，
并指定 `-A x64`，然后构建 `LabelReviewer` 目标。

## 使用

1. 选择输入根文件夹，然后在“选择扫描范围”窗口中选择目录层级，并勾选一个或多个需要递归扫描的目录。
2. 输出文件夹无需选择；软件会自动在输入文件夹同级创建“输入文件夹名_review”。之后新增、删除标注及切换图片时会自动保存。
3. 按 `W`、点击“创建矩形框”或使用右键菜单进入画框模式，再用两次左键单击确定矩形的两个角。
4. 在弹窗中先选择“误检”或“漏检”，再输入新类别或选择已有类别。类别会保存在输出目录的
   `categories.json`，下次自动恢复。
5. 选中画布中的框或右侧列表项，按 `E` 重新调整框的位置和大小，按 `R` 修改问题类型和类别名称，按 `Delete` 删除。

常用操作：滚轮缩放，按住左键拖动画布，`F` 适应窗口，`A/D` 或左右方向键切图，
`Ctrl+S` 手动保存，`Esc` 取消正在创建的框。

## 输出结构

输出目录完整保留输入图片的相对文件夹层级。无标注图片按原名复制；有标注图片会按原名输出带有误检/漏检框和标签的可视化结果，标注文件使用
`原图片文件名.json`，例如：

```text
输入/批次1/camera/a.tif
输出/批次1/camera/a.tif
输出/批次1/camera/a.tif.json
```

JSON 包含图片名、相对路径、宽高、原始位深、通道数，以及每个矩形的问题类型、类别、
唯一 ID、`x/y/width/height` 和 `left/top/right/bottom`。软件会读取已有 JSON，
因此可以关闭后继续复判。

## 代码结构

```text
src/native/               OpenCV 解码与显示拉伸桥接层
src/app/MainWindow.*      WPF 主窗口与自适应界面
src/app/WpfControls/      WPF 可缩放、平移、标注图像画布
src/app/WpfDialogs/       WPF 标签和类别弹窗
src/app/Models/           图片与标注领域模型
src/app/Services/         扫描、OpenCV 调用、类别和 JSON 持久化
```
