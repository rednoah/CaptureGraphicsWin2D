using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// Reads the icon of a window (of any application) via Win32 and converts it to a XAML image.
    /// </summary>
    internal static class WindowIcon
    {
        // Icon size in pixels (i.e. sharp when displayed as 16x16 icon at up to 200% display scaling)
        private const int ICON_SIZE = 32;

        // Do not wait for windows that are busy
        private const uint TIMEOUT_MILLISECONDS = 50;

        /// <summary>
        /// Returns the icon of the given window. Must be called on the UI thread (the shell needs a COM STA thread).
        /// </summary>
        public static ImageSource? Load(IntPtr hwnd)
        {
            try
            {
                byte[] pixels = GetIconPixels(hwnd);
                MakeOpaqueIfNoAlphaChannel(pixels);

                var bitmap = new WriteableBitmap(ICON_SIZE, ICON_SIZE);
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

        // Returns the pixels in the format of WriteableBitmap (i.e. top-down BGRA with premultiplied alpha)
        private static byte[] GetIconPixels(IntPtr hwnd)
        {
            // 1. Ask the window
            IntPtr hIcon = GetWindowIcon(hwnd);
            if (hIcon != IntPtr.Zero)
            {
                return DrawIcon(hIcon);
            }

            // 2. Use the icon of the app (e.g. windows of UWP apps have no icon, because the icon is part of the app package)
            string? appUserModelId = GetAppUserModelId(hwnd);
            byte[]? appIcon = appUserModelId != null ? GetAppIcon(appUserModelId) : null;
            if (appIcon != null)
            {
                return appIcon;
            }

            // 3. Use the icon of the window class, or else the generic application icon
            return DrawIcon(GetClassIcon(hwnd));
        }

        // The icon handle belongs to the other application and must not be destroyed by us
        private static IntPtr GetWindowIcon(IntPtr hwnd)
        {
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
            return IntPtr.Zero;
        }

        // The icon handle belongs to the other application (or to the system) and must not be destroyed by us
        private static IntPtr GetClassIcon(IntPtr hwnd)
        {
            foreach (int iconType in new[] { GCLP_HICON, GCLP_HICONSM })
            {
                IntPtr hIcon = GetClassLongPtr(hwnd, iconType);
                if (hIcon != IntPtr.Zero)
                {
                    return hIcon;
                }
            }

            // e.g. context menus and most windows of the shell have no icon
            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        // Returns the ID that identifies the app of the window for the taskbar (e.g. Microsoft.WindowsStore_8wekyb3d8bbwe!App), or null.
        // The order of the methods of a COM interface must not change, so trimming must not remove the methods that are not called.
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(IPropertyStore))]
        private static string? GetAppUserModelId(IntPtr hwnd)
        {
            // UWP apps are displayed in a frame window of ApplicationFrameHost.exe, and the frame window has the ID of the app
            // (this also works for minimized UWP apps, which is not the case for the child window of the app inside the frame window)
            if (SHGetPropertyStoreForWindow(hwnd, IID_IPropertyStore, out IPropertyStore properties) == 0)
            {
                PROPERTYKEY key = PKEY_AppUserModel_ID;
                if (properties.GetValue(ref key, out PROPVARIANT value) == 0)
                {
                    string? id = value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pwszVal) : null;
                    PropVariantClear(ref value);

                    if (!string.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }
            }

            // Other packaged apps (e.g. WinUI 3 apps from the Microsoft Store) have the ID of their package
            GetWindowThreadProcessId(hwnd, out uint processId);
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                uint length = APPLICATION_USER_MODEL_ID_MAX_LENGTH;
                var id = new StringBuilder((int)length);
                return GetApplicationUserModelId(process, ref length, id) == 0 ? id.ToString() : null; // fails for apps without package
            }
            finally
            {
                CloseHandle(process);
            }
        }

        // Returns the icon of the app as displayed in the Start menu (i.e. the icon of the app in shell:AppsFolder), or null
        private static byte[]? GetAppIcon(string appUserModelId)
        {
            if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appUserModelId, IntPtr.Zero, IID_IShellItemImageFactory, out IShellItemImageFactory app) != 0)
            {
                return null; // e.g. the ID is not the ID of an installed app
            }
            if (app.GetImage(new SIZE { cx = ICON_SIZE, cy = ICON_SIZE }, SIIGBF_ICONONLY, out IntPtr hBitmap) != 0)
            {
                return null;
            }

            // The bitmap belongs to us. Its pixels are BGRA with premultiplied alpha already, i.e. only the row order may need to be changed.
            var pixels = new byte[ICON_SIZE * ICON_SIZE * 4];
            var format = CreateIconFormat();

            IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
            try
            {
                return GetDIBits(hdc, hBitmap, 0, ICON_SIZE, pixels, ref format, DIB_RGB_COLORS) == ICON_SIZE ? pixels : null;
            }
            finally
            {
                DeleteDC(hdc);
                DeleteObject(hBitmap);
            }
        }

        // Draws the icon and returns the pixels
        private static byte[] DrawIcon(IntPtr hIcon)
        {
            var pixels = new byte[ICON_SIZE * ICON_SIZE * 4];
            var format = CreateIconFormat();

            IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr hBitmap = CreateDIBSection(hdc, ref format, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            IntPtr hBitmapDefault = SelectObject(hdc, hBitmap);
            try
            {
                if (hBitmap != IntPtr.Zero && DrawIconEx(hdc, 0, 0, hIcon, ICON_SIZE, ICON_SIZE, 0, IntPtr.Zero, DI_NORMAL))
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
            return pixels;
        }

        // 32-bit bitmap of the icon size (negative height = top-down bitmap)
        private static BITMAPINFOHEADER CreateIconFormat()
        {
            return new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = ICON_SIZE, biHeight = -ICON_SIZE, biPlanes = 1, biBitCount = 32 };
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
