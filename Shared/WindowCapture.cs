using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage.Streams;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// Captures windows with alpha channel and saves them as PNG files.
    /// </summary>
    internal static class WindowCapture
    {
        // Ignore fake windows (e.g. a window that is tiny, or a capture that is just a plain black bitmap and thus compresses well)
        private const int MIN_WINDOW_SIZE = 32;
        private const int MIN_FILE_SIZE = 1024;

        private static readonly TimeSpan CAPTURE_TIMEOUT = TimeSpan.FromSeconds(1);

        private const string HWND_CLS_DESKTOP = "Progman";
        private const string HWND_CLS_TASKBAR = "Shell_TrayWnd";

        /// <summary>
        /// Captures the given windows and returns the files that have been saved.
        /// </summary>
        /// <param name="overwrite">Replace existing files, as opposed to saving to a new file name (e.g. Name-2.png)</param>
        public static async Task<List<string>> CaptureWindowsAsync(IEnumerable<WindowInfo> windows, string outputDir, bool overwrite, CancellationToken cancellationToken = default)
        {
            var capturedFiles = new List<string>();

            // create output folder if needed
            Directory.CreateDirectory(outputDir);

            using var device = new CanvasDevice();

            foreach (var win in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[]? png = null;
                try
                {
                    png = await CaptureWindowAlphaAsync(device, win.Hwnd);
                }
                catch (Exception e)
                {
                    // windows that cannot be captured are expected (e.g. window has been closed in the meantime, or never draws anything)
                    System.Diagnostics.Trace.WriteLine($"Failed to capture {win}: {e}");
                }

                if (png == null || png.Length <= MIN_FILE_SIZE)
                {
                    continue;
                }

                // failing to write the file is not expected, and thus not ignored
                string path = GetOutputPath(outputDir, win.Name, capturedFiles, overwrite);
                await File.WriteAllBytesAsync(path, png);
                capturedFiles.Add(path); // Track successful captures
            }

            return capturedFiles;
        }

        private static string GetOutputPath(string outputDir, string windowName, List<string> capturedFiles, bool overwrite)
        {
            // strip invalid characters
            string name = string.Join(" ", windowName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (name.Length == 0)
            {
                name = "Window";
            }

            // generate a new unique file name if the file has already been captured just now (or if the file already exists and must not be overwritten)
            string path = Path.Combine(outputDir, $"{name}.png");
            for (int i = 2; capturedFiles.Contains(path, StringComparer.OrdinalIgnoreCase) || (!overwrite && File.Exists(path)); i++)
            {
                path = Path.Combine(outputDir, $"{name}-{i}.png");
            }
            return path;
        }

        // Returns the PNG image, or null if there is nothing to capture
        private static async Task<byte[]?> CaptureWindowAlphaAsync(CanvasDevice device, IntPtr hwnd)
        {
            GraphicsCaptureItem item = CreateItemForWindow(hwnd);

            if (item.Size.Width <= MIN_WINDOW_SIZE || item.Size.Height <= MIN_WINDOW_SIZE)
            {
                return null;
            }

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);

            using var session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;

            var frameArrivedCompletion = new TaskCompletionSource<Direct3D11CaptureFrame>();

            framePool.FrameArrived += (s, e) =>
            {
                var frame = s.TryGetNextFrame();
                if (frame != null && !frameArrivedCompletion.TrySetResult(frame))
                {
                    frame.Dispose(); // we only need the first frame
                }
            };

            session.StartCapture();

            // throws TimeoutException if the window does not draw anything
            using Direct3D11CaptureFrame capturedFrame = await frameArrivedCompletion.Task.WaitAsync(CAPTURE_TIMEOUT);

            using var renderTarget = new CanvasRenderTarget(device, item.Size.Width, item.Size.Height, WindowInfo.SCALE_FACTOR_1);

            using (var ds = renderTarget.CreateDrawingSession())
            {
                ds.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));

                using var capturedBitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, capturedFrame.Surface);

                // Create a clipping mask with rounded corners
                float radius = GetWindowCornerRadius(hwnd);

                if (radius > 0)
                {
                    // Apply rounded clipping
                    Windows.Foundation.Rect bounds = new Windows.Foundation.Rect(0, 0, item.Size.Width, item.Size.Height);
                    using var geometry = CanvasGeometry.CreateRoundedRectangle(device, bounds, radius, radius);

                    using (ds.CreateLayer(1.0f, geometry))
                    {
                        ds.DrawImage(capturedBitmap);
                    }
                }
                else
                {
                    // Draw normally for square / maximized windows
                    ds.DrawImage(capturedBitmap);
                }
            }

            // Encode in memory, so that the caller can decide if (and where) to save the image
            using var stream = new InMemoryRandomAccessStream();
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);

            var png = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)png.Length);
            reader.ReadBytes(png);
            return png;
        }

        private static float GetWindowCornerRadius(IntPtr hwnd)
        {
            // If the window is maximized, it never has rounded corners
            if (IsZoomed(hwnd))
            {
                return 0f;
            }

            var cls = WindowInfo.GetClassName(hwnd);
            if (cls == HWND_CLS_DESKTOP || cls == HWND_CLS_TASKBAR)
            {
                return 0f;
            }

            // Ask the DWM what the window prefers
            int result = DwmGetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, out int windowCornerPreference, sizeof(int));

            if (result == 0) // S_OK
            {
                switch (windowCornerPreference)
                {
                    case DWMWCP_DONOTROUND:
                        return 0f; // E.g., older Win32 apps that opted out, or specific tool windows
                    case DWMWCP_ROUNDSMALL:
                        return WindowInfo.GetWindowScaleFactor(hwnd) * 4f; // E.g., Context menus or small popups
                    case DWMWCP_ROUND:
                        return WindowInfo.GetWindowScaleFactor(hwnd) * 8f; // Explicitly requested standard rounding
                    case DWMWCP_DEFAULT:
                        return WindowInfo.GetWindowScaleFactor(hwnd) * 8f; // On Windows 11, the default for top-level windows is 8px
                }
            }

            // Fallback if the API call fails (e.g. Windows 10 has no rounded corners)
            return 0f;
        }


        // --- UNMANAGED COM CREATION (Bypasses CLR Marshalling Crash) ---

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop { [PreserveSig] int CreateForWindow([In] IntPtr window, [In] ref Guid iid, out IntPtr result); }

        [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
        private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        private static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            IntPtr hstring = IntPtr.Zero;
            IntPtr factoryPtr = IntPtr.Zero;

            try
            {
                string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                int hr = WindowsCreateString(className, className.Length, out hstring);
                if (hr != 0) throw new Exception($"WindowsCreateString failed: {hr}");

                Guid interopGuid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                hr = RoGetActivationFactory(hstring, ref interopGuid, out factoryPtr);
                if (hr != 0) throw new Exception($"RoGetActivationFactory failed: {hr}");

                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);

                Guid itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                hr = interop.CreateForWindow(hwnd, ref itemGuid, out IntPtr itemPtr);
                if (hr != 0) throw new Exception($"CreateForWindow failed: {hr}");

                return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
                if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
            }
        }
    }
}
