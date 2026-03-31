using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage.Pickers;

namespace UWPCaptureGraphicsWin2D
{
    public sealed partial class MainWindow : Window
    {
        private bool _isCapturing = false;
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            this.InitializeComponent();

            var appWindow = this.AppWindow;

            // Force Title Bar to match Dark Theme Background perfectly
            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                // #202020 is the exact background color of WinUI 3 Dark Mode
                var darkBg = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                titleBar.BackgroundColor = darkBg;
                titleBar.ButtonBackgroundColor = darkBg;
                titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                titleBar.InactiveBackgroundColor = darkBg;
                titleBar.ButtonInactiveBackgroundColor = darkBg;
                titleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;

                // Explicitly hide the icon and system menu from the title bar
                titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
            }

            // Shrink window size to fit the simple controls
            // The Window itself doesn't have a 'Loaded' event, 
            // so we use the root element of your content
            if (this.Content is FrameworkElement root)
            {
                root.Loaded += (s, e) => Pack();
            }

            // Set default folder
            OutputFolderTextBox.Text = GetDefaultFolder();

            // Set default selected window
            WindowSelector.ItemsSource = new List<string> { SELECT_ALL_WINDOWS };
            WindowSelector.SelectedIndex = 0;

            // Ensure background tasks die when the window is closed
            this.Closed += (s, e) =>
            {
                CancelCapture();
                Environment.Exit(0);
            };
        }


        public async void ProcessCommandLine(string[] args)
        {
            try
            {
                AttachConsole(ATTACH_PARENT_PROCESS);

                List<string> targetWindow = new List<string> { SELECT_ALL_WINDOWS };
                string outputDir = GetDefaultFolder();

                // args[1] = output folder
                if (args.Length > 1)
                {
                    outputDir = args[1];
                }

                // args[2..n] = window names
                if (args.Length > 2)
                {
                    targetWindow = args.Skip(2).ToList();
                }

                foreach (var win in targetWindow)
                {
                    var capturedFiles = await CaptureAllVisibleWindowsAsync(win, outputDir);
                    capturedFiles.ForEach(Console.WriteLine);
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }
            finally
            {
                Environment.Exit(0);
            }
        }


        private string GetDefaultFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Captures");
        }


        private void Pack()
        {
            // 1. Get the root element and its XamlRoot
            var root = this.Content as FrameworkElement;
            if (root == null || root.XamlRoot == null) return;

            // 2. Measure the content with infinite constraints
            root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

            // 3. Get the scaling factor (e.g. 2.0 for 200% scaling)
            double scale = root.XamlRoot.RasterizationScale;

            // 4. Scale the logical DesiredSize to physical pixels
            int physicalWidth = (int)Math.Ceiling(root.DesiredSize.Width * scale * 1.25);
            int physicalHeight = (int)Math.Ceiling(root.DesiredSize.Height * scale * 1.25);

            // 5. Apply the physical size to the AppWindow
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs evt)
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                OutputFolderTextBox.Text = folder.Path;
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs evt)
        {
            _isCapturing = true;
            UpdateUiState();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            string outputDir = OutputFolderTextBox.Text;
            int countdownSeconds = (int)CountdownNumberBox.Value;

            try
            {
                // The Countdown Loop
                for (int i = countdownSeconds; i > 0; i--)
                {
                    if (token.IsCancellationRequested) break;
                    StatusTextBlock.Text = $"Status: Capturing in {i}...";
                    await Task.Delay(1000, token);
                }

                if (!token.IsCancellationRequested)
                {
                    StatusTextBlock.Text = "Status: Capturing windows now...";
                    StatusProgressRing.IsActive = true;
                    StatusProgressRing.Visibility = Visibility.Visible;

                    string targetWindow = WindowSelector.SelectedItem as String ?? SELECT_ALL_WINDOWS;

                    // Offload actual capture to background to prevent DWM deadlock
                    await Task.Run(async () =>
                    {
                        try
                        {
                            // Grab the list of all successful captures
                            var capturedFiles = await CaptureAllVisibleWindowsAsync(targetWindow, outputDir);

                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (capturedFiles.Count > 0)
                                {
                                    StatusTextBlock.Text = "Status: Capture complete!";
                                    OpenFolderAndSelectFiles(outputDir, capturedFiles);
                                }
                                else
                                {
                                    StatusTextBlock.Text = "Status: Nothing has been captured!";
                                }
                            });
                        }
                        catch (Exception e)
                        {
                            System.Diagnostics.Trace.WriteLine(e);
                        }
                    });
                }
            }
            catch (TaskCanceledException)
            {
                StatusTextBlock.Text = "Status: Cancelled.";
            }
            finally
            {
                _isCapturing = false;
                UpdateUiState();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs evt)
        {
            CancelCapture();
            StatusTextBlock.Text = "Status: Cancelled.";
        }

        private void CancelCapture()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }

        private void UpdateUiState()
        {
            StartButton.IsEnabled = !_isCapturing;
            StopButton.IsEnabled = _isCapturing;
            CountdownNumberBox.IsEnabled = !_isCapturing;
            BrowseButton.IsEnabled = !_isCapturing;

            if (!_isCapturing)
            {
                StatusProgressRing.IsActive = false;
                StatusProgressRing.Visibility = Visibility.Collapsed;
            }
        }

        private const string SELECT_ALL_WINDOWS = "All Windows";

        private string GetWindowName(WindowInfo win)
        {
            return string.IsNullOrWhiteSpace(win.Title) ? win.ClassName : win.Title;
        }

        private void WindowSelector_DropDownOpened(object sender, object evt)
        {
            var selection = new List<string> { SELECT_ALL_WINDOWS };
            selection.AddRange(GetVisibleWindows().Select(GetWindowName).Order().Distinct().ToList());

            WindowSelector.ItemsSource = selection;
            WindowSelector.SelectedIndex = 0;
        }

        private async Task<List<string>> CaptureAllVisibleWindowsAsync(string targetWindow, string outputDir)
        {
            var windows = GetVisibleWindows();
            var capturedFiles = new List<string>();

            foreach (var win in windows)
            {
                string name = GetWindowName(win);
                if (targetWindow != name && targetWindow != SELECT_ALL_WINDOWS)
                {
                    continue;
                }

                // strip invalid characters
                name = string.Join(" ", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

                string path = Path.Combine(outputDir, $"{name}.png");
                FileInfo file = new FileInfo(path);

                // generate new unique file name if a file already exists
                for (int i = 2; capturedFiles.Contains(path); i++)
                {
                    path = Path.Combine(outputDir, $"{name}-{i}.png");
                    file = new FileInfo(path);
                }

                // create parent folder if needed
                if (file.Directory != null && !file.Directory.Exists)
                {
                    file.Directory.Create();
                }

                try
                {
                    await CaptureWindowAlphaAsync(win.Hwnd, path);

                    if (file.Exists)
                    {
                        // ignore fake windows (e.g. capture that is so small because it's just a plain black bitmap and thus compresses well)
                        if (file.Length > 1024)
                        {
                            capturedFiles.Add(path); // Track successful captures
                        }
                        else
                        {
                            file.Delete();
                        }
                    }
                }
                catch (Exception e)
                {
                    {
                        System.Diagnostics.Trace.WriteLine(e);
                    }
                }
            }
            return capturedFiles;
        }

        private async Task CaptureWindowAlphaAsync(IntPtr hwnd, string outputPath)
        {
            GraphicsCaptureItem item = CreateItemForWindow(hwnd);
            CanvasDevice device = new CanvasDevice();

            if (item.Size.Width <= 32 || item.Size.Height <= 32)
            {
                return;
            }

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);

            using var session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;

            var frameArrivedCompletion = new TaskCompletionSource<Direct3D11CaptureFrame>();

            framePool.FrameArrived += (s, e) =>
            {
                var frame = s.TryGetNextFrame();
                if (frame != null) frameArrivedCompletion.TrySetResult(frame);
            };

            session.StartCapture();

            var timeoutTask = Task.Delay(1000);
            var completedTask = await Task.WhenAny(frameArrivedCompletion.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                session.Dispose();
                throw new Exception("Capture timed out.");
            }

            using Direct3D11CaptureFrame capturedFrame = await frameArrivedCompletion.Task;

            using var renderTarget = new CanvasRenderTarget(device, item.Size.Width, item.Size.Height, SCALE_FACTOR_1);

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

            await renderTarget.SaveAsync(outputPath, CanvasBitmapFileFormat.Png);

            session.Dispose();
        }

        // --- WIN32 INTEROP ---

        struct WindowInfo { public IntPtr Hwnd; public string ClassName; public string Title; }

        private List<WindowInfo> GetVisibleWindows()
        {
            var windows = new List<WindowInfo>();
            EnumWindows((hwnd, lParam) =>
            {
                if (IsWindowVisible(hwnd) && !IsWindowCloaked(hwnd))
                {
                    windows.Add(new WindowInfo { Hwnd = hwnd, ClassName = GetClassName(hwnd), Title = GetWindowText(hwnd) });
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private bool IsWindowCloaked(IntPtr hwnd)
        {
            int cloaked = 0;
            DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
            return cloaked != 0;
        }

        private string GetClassName(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private string GetWindowText(IntPtr hwnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private float GetWindowCornerRadius(IntPtr hwnd)
        {
            // If the window is maximized, it never has rounded corners
            if (IsZoomed(hwnd))
            {
                return 0f;
            }

            var cls = GetClassName(hwnd);
            if (cls == "Progman" || cls == "Shell_TrayWnd")
            {
                return 0f;
            }

            // Ask the DWM what the window prefers
            int windowCornerPreference;
            int result = DwmGetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, out windowCornerPreference, Marshal.SizeOf(typeof(int)));

            if (result == 0) // S_OK
            {
                switch (windowCornerPreference)
                {
                    case DWMWCP_DONOTROUND:
                        return 0f; // E.g., older Win32 apps that opted out, or specific tool windows
                    case DWMWCP_ROUNDSMALL:
                        return GetWindowScaleFactor(hwnd) * 4f; // E.g., Context menus or small popups
                    case DWMWCP_ROUND:
                        return GetWindowScaleFactor(hwnd) * 8f; // Explicitly requested standard rounding
                    case DWMWCP_DEFAULT:
                        return GetWindowScaleFactor(hwnd) * 8f; // On Windows 11, the default for top-level windows is 8px
                }
            }

            // Fallback if the API call fails (safe bet for Windows 11)
            return 0f;
        }

        private const float SCALE_FACTOR_1 = 96.0f;

        private float GetWindowScaleFactor(IntPtr hwnd)
        {
            // e.g. DPI 144 / 96.0 = 1.5 multiplier
            return GetDpiForWindow(hwnd) / SCALE_FACTOR_1;
        }

        private void OpenFolderAndSelectFiles(string folderPath, List<string> filePaths)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                return;
            }

            IntPtr dirPidl = IntPtr.Zero;
            List<IntPtr> absoluteFilePidls = new List<IntPtr>();
            IntPtr[] relativeFilePidls = new IntPtr[filePaths.Count];
            uint sfgao;

            try
            {
                // 1. Get the native shell pointer for the parent directory
                int hr = SHParseDisplayName(folderPath, IntPtr.Zero, out dirPidl, 0, out sfgao);
                if (hr != 0) throw new Exception();

                // 2. Get the native shell pointers for every captured file
                for (int i = 0; i < filePaths.Count; i++)
                {
                    hr = SHParseDisplayName(filePaths[i], IntPtr.Zero, out IntPtr absPidl, 0, out sfgao);
                    if (hr == 0)
                    {
                        absoluteFilePidls.Add(absPidl);
                        // The API requires pointers relative to the parent folder, so we extract the last ID
                        relativeFilePidls[i] = ILFindLastID(absPidl);
                    }
                }

                // 3. Command Windows Explorer to open the folder and highlight the array of items
                SHOpenFolderAndSelectItems(dirPidl, (uint)relativeFilePidls.Length, relativeFilePidls, 0);
            }
            catch
            {
                // no Fallback to simply opening the folder if the COM interop fails
            }
            finally
            {
                // 4. Free all native shell memory allocations
                if (dirPidl != IntPtr.Zero) ILFree(dirPidl);
                foreach (var pidl in absoluteFilePidls)
                {
                    if (pidl != IntPtr.Zero) ILFree(pidl);
                }
            }
        }




        public const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        static extern bool FreeConsole();




        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, int dwFlags);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern void ILFree(IntPtr pidl);




        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);




        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_CLOAKED = 14;

        public const int DWMWCP_DEFAULT = 0;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND = 2;
        public const int DWMWCP_ROUNDSMALL = 3;

        // Interop methods
        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd); // "Zoomed" is the Win32 term for Maximized

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);




        // --- UNMANAGED COM CREATION (Bypasses CLR Marshalling Crash) ---

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IGraphicsCaptureItemInterop { [PreserveSig] int CreateForWindow([In] IntPtr window, [In] ref Guid iid, out IntPtr result); }

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
