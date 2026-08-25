using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LabelReviewer.Services;

public sealed record LoadedImage(Bitmap Bitmap, int BitDepth, int Channels);

public static class NativeImageLoader
{
    [DllImport("ImageBridge.dll", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int lr_load_image(
        string path, out IntPtr pixels, out int width, out int height,
        out int stride, out int sourceBitDepth, out int sourceChannels);

    [DllImport("ImageBridge.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void lr_free_image(IntPtr pixels);

    [DllImport("ImageBridge.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern IntPtr lr_last_error();

    public static LoadedImage Load(string path)
    {
        if (lr_load_image(path, out var pixels, out var width, out var height,
                out var stride, out var bitDepth, out var channels) == 0)
        {
            var message = Marshal.PtrToStringUTF8(lr_last_error()) ?? "未知图像解码错误";
            throw new InvalidOperationException(message);
        }

        try
        {
            using var wrapped = new Bitmap(width, height, stride,
                PixelFormat.Format32bppArgb, pixels);
            return new LoadedImage(new Bitmap(wrapped), bitDepth, channels);
        }
        finally
        {
            lr_free_image(pixels);
        }
    }
}
