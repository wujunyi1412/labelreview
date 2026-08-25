# 图像复判与矩形标注工具

Windows 桌面复判软件。递归读取输入目录中的 JPEG、PNG、TIF 和 TIFF，支持
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

若 Visual Studio 已正常注册到系统，也可以使用 `Visual Studio 17 2022` 生成器，
并指定 `-A x64`，然后构建 `LabelReviewer` 目标。

## 使用

1. 选择输入根文件夹，软件会递归列出所有支持的图片。
2. 输出文件夹可以不手动选择；默认会在输入文件夹同级创建“输入文件夹名_review”。之后新增、删除标注及切换图片时会自动保存。
3. 把鼠标放在图片目标位置按 `W`，该位置成为矩形第一角；移动鼠标后单击完成。
   也可点“创建矩形框”或右键菜单，再用两次单击确定矩形。
4. 在弹窗中先选择“误检”或“漏检”，再输入新类别或选择已有类别。类别会保存在输出目录的
   `categories.json`，下次自动恢复。
5. 选中画布中的框或右侧列表项，按 `Delete` 删除。

常用操作：滚轮缩放，中键拖动画布，`F` 适应窗口，`A/D` 或左右方向键切图，
`Ctrl+S` 手动保存，`Esc` 取消正在创建的框。

## 输出结构

输出目录完整保留输入图片的相对文件夹层级。原图片按原名复制，标注文件使用
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
src/app/Controls/         可缩放、平移、标注的图像画布
src/app/Forms/            主窗口和类别选择窗口
src/app/Models/           图片与标注领域模型
src/app/Services/         扫描、OpenCV 调用、类别和 JSON 持久化
```
