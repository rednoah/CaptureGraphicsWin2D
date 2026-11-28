using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// A top-level window, identified by its window handle.
    /// </summary>
    internal sealed record WindowInfo(IntPtr Hwnd, string ClassName, string Title)
    {
        /// <summary>
        /// The window title, or the window class for windows without title (e.g. Shell_TrayWnd).
        /// </summary>
        public string Name => string.IsNullOrWhiteSpace(Title) ? ClassName : Title;

        /// <summary>
        /// The window handle in the notation of Spy++ (e.g. 0x000A0B2C).
        /// </summary>
        public string Id => "0x" + Hwnd.ToString("X8");

        /// <summary>
        /// The current size of the window on screen in pixels (e.g. 600x400).
        /// </summary>
        public string Size
        {
            get
            {
                RECT bounds = GetWindowBounds(Hwnd);
                return $"{bounds.Right - bounds.Left}x{bounds.Bottom - bounds.Top}";
            }
        }

        public string Description => $"{Name}\nClass: {ClassName}\nHandle: {Id}\nSize: {Size}";

        /// <summary>
        /// All visible top-level windows, topmost window first.
        /// </summary>
        public static List<WindowInfo> GetVisibleWindows()
        {
            var windows = new List<WindowInfo>();

            // EnumWindows skips the windows of apps that are not desktop apps (e.g. toast notifications of the shell), but FindWindowEx does not
            for (IntPtr hwnd = FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, null); hwnd != IntPtr.Zero; hwnd = FindWindowEx(IntPtr.Zero, hwnd, null, null))
            {
                if (IsWindowVisible(hwnd) && !IsWindowCloaked(hwnd) && HasWindowBounds(hwnd))
                {
                    windows.Add(new WindowInfo(hwnd, GetClassName(hwnd), GetWindowText(hwnd)));
                }
            }
            return windows;
        }

        /// <summary>
        /// Internal windows of the shell may have zero size (all bounds 0)
        /// </summary>
        private static bool HasWindowBounds(IntPtr hwnd)
        {
            RECT bounds = GetWindowBounds(hwnd);
            return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }

        /// <summary>
        /// The top-level window of another application at the current cursor position, or IntPtr.Zero.
        /// </summary>
        public static IntPtr GetWindowUnderCursor()
        {
            if (!GetCursorPos(out POINT cursor))
            {
                return IntPtr.Zero;
            }

            // the cursor may be over a child window (e.g. a button), but we want the window that contains it
            IntPtr hwnd = GetAncestor(WindowFromPoint(cursor), GA_ROOT);

            GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == Environment.ProcessId ? IntPtr.Zero : hwnd;
        }

        /// <summary>
        /// The window bounds as seen on screen (in pixels).
        /// </summary>
        public static RECT GetWindowBounds(IntPtr hwnd)
        {
            // GetWindowRect includes the invisible resize borders, so prefer the bounds that the DWM actually draws
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds, Marshal.SizeOf<RECT>()) != 0)
            {
                GetWindowRect(hwnd, out bounds);
            }
            return bounds;
        }

        public const float SCALE_FACTOR_1 = 96.0f;

        public static float GetWindowScaleFactor(IntPtr hwnd)
        {
            // e.g. DPI 144 / 96.0 = 1.5 multiplier
            return GetDpiForWindow(hwnd) / SCALE_FACTOR_1;
        }

        public static string GetClassName(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetWindowText(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static bool IsWindowCloaked(IntPtr hwnd)
        {
            DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
            return cloaked != 0;
        }
    }
}
