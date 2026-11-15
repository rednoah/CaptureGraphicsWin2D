using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// Reads the icon of a window (of any application) via Win32 and converts it to a XAML image.
    /// </summary>
    internal static class WindowIcon
    {
        // Icon size in pixels (i.e. sharp when displayed as 16x16 icon at up to 200% display scaling)
        private const int SIZE = 32;

        // Do not wait for windows that are busy
        private const uint TIMEOUT_MILLISECONDS = 50;

        /// <summary>
        /// Returns the icon of the given window. Must be called on the UI thread.
        /// </summary>
        public static ImageSource? Load(IntPtr hwnd)
        {
            try
            {
                byte[] pixels = GetIconPixels(GetIconHandle(hwnd));

                var bitmap = new WriteableBitmap(SIZE, SIZE);
                using (Stream stream = bitmap.PixelBuffer.AsStream())
                {
                    stream.Write(pixels, 0, pixels.Length);
                }
                bitmap.Invalidate();
                return bitmap;
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
                return null;
            }
        }

        // The icon handle belongs to the other application (or to the system) and must not be destroyed by us
        private static IntPtr GetIconHandle(IntPtr hwnd)
        {
            // 1. Ask the window
            foreach (int iconType in new[] { ICON_BIG, ICON_SMALL2, ICON_SMALL })
            {
                if (SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)iconType, IntPtr.Zero, SMTO_ABORTIFHUNG, TIMEOUT_MILLISECONDS, out IntPtr hIcon) == IntPtr.Zero)
                {
                    break; // the window is not responding
                }
                if (hIcon != IntPtr.Zero)
                {
                    return hIcon;
                }
            }

            // 2. Use the icon of the window class
            foreach (int iconType in new[] { GCLP_HICON, GCLP_HICONSM })
            {
                IntPtr hIcon = GetClassLongPtr(hwnd, iconType);
                if (hIcon != IntPtr.Zero)
                {
                    return hIcon;
                }
            }

            // 3. Use the generic application icon (e.g. context menus and most windows of the shell have no icon)
            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        // Draws the icon and returns the pixels in the format of WriteableBitmap (i.e. top-down BGRA with premultiplied alpha)
        private static byte[] GetIconPixels(IntPtr hIcon)
        {
            var pixels = new byte[SIZE * SIZE * 4];

            // negative height = top-down bitmap
            var format = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = SIZE, biHeight = -SIZE, biPlanes = 1, biBitCount = 32 };

            IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr hBitmap = CreateDIBSection(hdc, ref format, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr hBitmapDefault = SelectObject(hdc, hBitmap);
            try
            {
                if (hBitmap != IntPtr.Zero && DrawIconEx(hdc, 0, 0, hIcon, SIZE, SIZE, 0, IntPtr.Zero, DI_NORMAL))
                {
                    Marshal.Copy(bits, pixels, 0, pixels.Length);
                }
            }
            finally
            {
                SelectObject(hdc, hBitmapDefault);
                DeleteObject(hBitmap);
                DeleteDC(hdc);
            }

            MakeOpaqueIfNoAlphaChannel(pixels);
            return pixels;
        }

        // Icons of very old applications have no alpha channel, i.e. alpha is 0 for all pixels and the icon would be invisible.
        // In this case, treat black as transparent (the bitmap is black where nothing was drawn) and all other colors as opaque.
        private static void MakeOpaqueIfNoAlphaChannel(byte[] pixels)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    return; // icon has an alpha channel
                }
            }

            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                {
                    pixels[i + 3] = 255;
                }
            }
        }
    }
}
