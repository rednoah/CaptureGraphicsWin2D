using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;

namespace CaptureGraphicsWin2D.Shared
{
    /// <summary>
    /// Reads the icon of an application window and converts it into a WinUI image, which is cached per window handle.
    /// </summary>
    internal static partial class WindowIcons
    {
        // a hung window must not block our drop-down for long (WM_GETICON is answered by the other process)
        private const uint TIMEOUT_MS = 100;

        private static readonly Dictionary<IntPtr, ImageSource?> s_cache = new();

        /// <summary> The icon of the window, or null if it has none or could not be read. </summary>
        public static ImageSource? Get(IntPtr hwnd)
        {
            if (s_cache.TryGetValue(hwnd, out ImageSource? image))
            {
                return image;
            }

            IntPtr icon = GetIconHandle(hwnd);
            image = icon != IntPtr.Zero ? ToImageSource(icon) : null;
            s_cache[hwnd] = image;
            return image;
        }

        /// <summary> Discard the icons of all windows not included in the specified list (closed windows). </summary>
        public static void Retain(IEnumerable<IntPtr> hwnds)
        {
            var alive = hwnds.ToHashSet();
            foreach (IntPtr hwnd in s_cache.Keys.Where(h => !alive.Contains(h)).ToList())
            {
                s_cache.Remove(hwnd);
            }
        }

        private static IntPtr GetIconHandle(IntPtr hwnd)
        {
            // window icon first (small icons fit the drop-down best), the icons belong to the window, do not destroyed
            foreach (IntPtr type in new[] { ICON_SMALL2, ICON_SMALL, ICON_BIG })
            {
                if (SendMessageTimeout(hwnd, WM_GETICON, type, IntPtr.Zero, SMTO_ABORTIFHUNG, TIMEOUT_MS, out IntPtr icon) == IntPtr.Zero)
                {
                    break; // hung or timed out, don't wait for the other icon types as well
                }
                if (icon != IntPtr.Zero)
                {
                    return icon;
                }
            }

            // fall back to the icon of the window class
            IntPtr classIcon = GetClassLong(hwnd, GCLP_HICONSM);
            return classIcon != IntPtr.Zero ? classIcon : GetClassLong(hwnd, GCLP_HICON);
        }

        private static ImageSource? ToImageSource(IntPtr icon)
        {
            if (!GetIconInfo(icon, out ICONINFO info))
            {
                return null;
            }

            try
            {
                // monochrome icons, no color bitmap, ignore
                if (info.hbmColor == IntPtr.Zero || GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0)
                {
                    return null;
                }

                int width = bitmap.bmWidth;
                int height = bitmap.bmHeight;
                int[]? pixels = ReadPixels(info.hbmColor, width, height);
                if (pixels == null)
                {
                    return null;
                }

                // old icons without alpha channel: take the transparency from the mask (black = opaque)
                if (pixels.All(p => (p & unchecked((int)0xFF000000)) == 0))
                {
                    int[]? mask = info.hbmMask != IntPtr.Zero ? ReadPixels(info.hbmMask, width, height) : null;
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        bool opaque = mask == null || (mask[i] & 0x00FFFFFF) == 0;
                        pixels[i] = opaque ? pixels[i] | unchecked((int)0xFF000000) : 0;
                    }
                }

                // WriteableBitmap expects premultiplied BGRA, icons have straight alpha
                byte[] bytes = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                {
                    int p = pixels[i];
                    int a = (p >> 24) & 0xFF;
                    bytes[i * 4 + 0] = (byte)((p & 0xFF) * a / 255);
                    bytes[i * 4 + 1] = (byte)(((p >> 8) & 0xFF) * a / 255);
                    bytes[i * 4 + 2] = (byte)(((p >> 16) & 0xFF) * a / 255);
                    bytes[i * 4 + 3] = (byte)a;
                }

                var image = new WriteableBitmap(width, height);
                using (var stream = image.PixelBuffer.AsStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
                image.Invalidate();
                return image;
            }
            finally
            {
                // GetIconInfo creates copies of the bitmaps, they have to be deleted by the caller
                if (info.hbmColor != IntPtr.Zero)
                {
                    _ = DeleteObject(info.hbmColor);
                }
                if (info.hbmMask != IntPtr.Zero)
                {
                    _ = DeleteObject(info.hbmMask);
                }
            }
        }

        // the bitmap as 32 bit BGRA pixels, rows top-down
        private static int[]? ReadPixels(IntPtr hBitmap, int width, int height)
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative = top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };

            int[] pixels = new int[width * height];
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                return GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS) == height ? pixels : null;
            }
            finally
            {
                _ = ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        private static IntPtr GetClassLong(IntPtr hwnd, int index)
        {
            // GetClassLongPtr only exists in 64-bit user32
            return IntPtr.Size == 8 ? GetClassLongPtr64(hwnd, index) : (IntPtr)GetClassLong32(hwnd, index);
        }

        // --- WIN32 INTEROP ---

        private const uint WM_GETICON = 0x007F;
        private static readonly IntPtr ICON_SMALL = (IntPtr)0;
        private static readonly IntPtr ICON_BIG = (IntPtr)1;
        private static readonly IntPtr ICON_SMALL2 = (IntPtr)2;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;
        private const uint BI_RGB = 0;
        private const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int fIcon;
            public uint xHotspot;
            public uint yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        private static partial IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpDwResult);

        [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static partial IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll", EntryPoint = "GetClassLongW")]
        private static partial uint GetClassLong32(IntPtr hWnd, int nIndex);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetIconInfo(IntPtr hIcon, out ICONINFO pIconInfo);

        [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
        private static partial int GetObject(IntPtr h, int c, out BITMAP pv);

        [LibraryImport("gdi32.dll")]
        private static partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] int[] lpvBits, ref BITMAPINFOHEADER lpBmi, uint usage);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr hObject);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetDC(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    }
}
