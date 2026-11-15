using System;
using System.Runtime.InteropServices;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// A colored frame that is displayed on top of a window to mark it on screen.
    /// </summary>
    /// <remarks>
    /// Spy++ inverts the border of the window by drawing directly onto the screen. This does not work reliably anymore
    /// since windows are composed by the DWM, so the frame is a window of its own: always on top, not activatable
    /// and transparent for mouse input (i.e. it is never the window under the cursor).
    /// </remarks>
    internal sealed class WindowHighlight
    {
        private const uint COLOR = 0x0000FF4C; // bright green (0x00BBGGRR)
        private const float THICKNESS = 4f;

        private static readonly string WindowClass = RegisterWindowClass();

        private readonly IntPtr _hwnd;

        public WindowHighlight()
        {
            _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WindowClass, null, WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

            // layered windows are invisible until their opacity is set
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
        }

        public void Show(IntPtr target)
        {
            RECT bounds = WindowInfo.GetWindowBounds(target);
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            int thickness = (int)Math.Ceiling(THICKNESS * WindowInfo.GetWindowScaleFactor(target));

            // Cut out the inside, so that only the frame remains
            IntPtr frame = CreateRectRgn(0, 0, width, height);
            IntPtr inside = CreateRectRgn(thickness, thickness, width - thickness, height - thickness);
            CombineRgn(frame, frame, inside, RGN_DIFF);
            DeleteObject(inside);

            // the window takes ownership of the region
            SetWindowRgn(_hwnd, frame, true);
            SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public void Hide()
        {
            ShowWindow(_hwnd, SW_HIDE);
        }

        // The window needs no code of its own: the default window procedure fills it with the background color of the window class
        private static string RegisterWindowClass()
        {
            var windowClass = new WNDCLASS
            {
                lpfnWndProc = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), "DefWindowProcW"),
                hInstance = GetModuleHandle(null),
                hbrBackground = CreateSolidBrush(COLOR),
                lpszClassName = "CaptureGraphicsWin2D.WindowHighlight"
            };
            RegisterClass(ref windowClass);
            return windowClass.lpszClassName;
        }
    }
}
