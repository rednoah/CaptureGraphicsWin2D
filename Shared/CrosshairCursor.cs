using Microsoft.UI.Input;
using System;
using System.Runtime.InteropServices;
using WinRT;

namespace CaptureGraphicsWin2D.Shared
{
    /// <summary>
    /// Creates a Win32 mouse cursor that looks like the crosshair icon (circle + cross, no outline). The
    /// cursor is created at runtime via CreateIconIndirect and handed to WinUI via IInputCursorStaticsInterop.
    /// </summary>
    internal static partial class CrosshairCursor
    {
        /// <summary>
        /// Create the cursor in physical pixels for the specified scaling factor (e.g., 1.5 for 150%); the hotspot
        /// is located at the center of the crosshair. All other sizes are specified in device-independent pixels,
        /// just like the icon.
        /// </summary>
        public static IntPtr Create(double iconSize, double radius, double strokeThickness, double scale)
        {
            double halfLength = iconSize / 2 * scale;
            double circleRadius = radius * scale;
            // slightly thinner than the icon, otherwise the cursor looks too thick (like the system cursors)
            double halfStroke = Math.Max(0.5, strokeThickness / 2 * scale - 0.5);

            // odd size so that the cross runs through the center of a pixel
            int size = 2 * (int)Math.Ceiling(halfLength) + 1;
            int center = size / 2;

            // A monochrome cursor consists of a double-height bitmap: the AND mask followed by the XOR mask (1 bit per pixel,
            // most significant bit first, row-aligned to WORD boundaries). AND 1 / XOR 0 = transparent, AND 1 / XOR 1 = inverted.
            int stride = (size + 15) / 16 * 2;
            byte[] masks = new byte[stride * size * 2];
            Array.Fill(masks, (byte)0xFF, 0, stride * size);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x - center;
                    double dy = y - center;

                    // Distance to the circle, the horizontal line, and the vertical line (whichever is closest)
                    double distance = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - circleRadius);
                    distance = Math.Min(distance, Math.Sqrt(Math.Pow(Math.Max(Math.Abs(dx) - halfLength, 0), 2) + dy * dy));
                    distance = Math.Min(distance, Math.Sqrt(Math.Pow(Math.Max(Math.Abs(dy) - halfLength, 0), 2) + dx * dx));

                    if (distance <= halfStroke)
                    {
                        masks[(size + y) * stride + x / 8] |= (byte)(0x80 >> (x % 8)); // XOR mask
                    }
                }
            }

            IntPtr hbmMask = CreateBitmap(size, size * 2, 1, 1, masks);
            if (hbmMask == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                var info = new ICONINFO
                {
                    fIcon = 0, // FALSE = cursor
                    xHotspot = (uint)center,
                    yHotspot = (uint)center,
                    hbmMask = hbmMask,
                    hbmColor = IntPtr.Zero, // monochrome
                };

                // CreateIconIndirect copies the bitmap, we can delete it below in any case
                return CreateIconIndirect(ref info);
            }
            finally
            {
                _ = DeleteObject(hbmMask);
            }
        }

        /// <summary>
        /// Encapsulate a native cursor in a WinUI InputCursor (which can be used as a ProtectedCursor).
        /// The native cursor must remain valid for as long as the InputCursor is in use.
        /// </summary>
        public static unsafe InputCursor? ToInputCursor(IntPtr cursor)
        {
            if (cursor == IntPtr.Zero)
            {
                return null;
            }

            // Microsoft.UI.Input.InputCursor.Interop.h: IInputCursorStaticsInterop::CreateFromHCursor
            var iid = new Guid("ac6f5065-90c4-46ce-beb7-05e138e54117");
            using var factory = ActivationFactory.Get("Microsoft.UI.Input.InputCursor", iid);

            // vtable: 3 x IUnknown, 3 x IInspectable, then CreateFromHCursor
            IntPtr thisPtr = factory.ThisPtr;
            var createFromHCursor = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)(*(*(void***)thisPtr + 6));

            IntPtr result = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(createFromHCursor(thisPtr, cursor, &result));
            try
            {
                return InputCursor.FromAbi(result);
            }
            finally
            {
                Marshal.Release(result);
            }
        }

        public static void Destroy(IntPtr cursor)
        {
            if (cursor != IntPtr.Zero)
            {
                _ = DestroyCursor(cursor);
            }
        }

        // --- WIN32 INTEROP ---

        // blittable (BOOL as int) so it can be passed to a LibraryImport method
        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int fIcon;
            public uint xHotspot;
            public uint yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, [In] byte[] lpBits);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr hObject);

        [LibraryImport("user32.dll")]
        private static partial IntPtr CreateIconIndirect(ref ICONINFO pIconInfo);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyCursor(IntPtr hCursor);
    }
}
