using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// An item of the window selector.
    /// </summary>
    public sealed class WindowItem
    {
        // null = all windows
        internal IntPtr? Hwnd { get; init; }

        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public ImageSource? Icon { get; init; }
        public string? Glyph { get; init; }

        // used for keyboard navigation and by screen readers
        public override string ToString() => Name;
    }

    public sealed partial class MainWindow : Window
    {
        private static readonly WindowItem SELECT_ALL_WINDOWS = new WindowItem { Name = "All Windows", Description = "Capture all visible windows", Glyph = "\uE7C4" };

        // Icons for the windows of the Windows shell (by window name) because these windows have no icon of their own
        private static readonly Dictionary<string, string> SHELL_WINDOW_GLYPHS = new Dictionary<string, string>
        {
            { "Program Manager", "\uE7F8" },        // desktop
            { "Shell_TrayWnd", "\uE90E" },          // taskbar
            { "Shell_SecondaryTrayWnd", "\uE90E" }, // taskbar on additional monitors
            { "Pop-upHost", "\uE9A4" },             // context menus
            { "NarratorHelperWindow", "\uED4D" },   // narrator helper
        };

        private CancellationTokenSource? _cts;

        // the selection to return to if the window picker is cancelled
        private WindowItem _selectionBeforePick = SELECT_ALL_WINDOWS;

        public MainWindow()
        {
            this.InitializeComponent();

            var appWindow = this.AppWindow;

            // Dark title bar to match the dark content.
            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                this.AppWindow.TitleBar.PreferredTheme = Microsoft.UI.Windowing.TitleBarTheme.Dark;
            }

            // Initialize with the expected size (at Scale Factor 1 so the size will increase on Scale Factor 2)
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(430, 420));

            // Shrink window size to fit the simple controls
            // The Window itself doesn't have a 'Loaded' event,
            // so we use the root element of your content
            if (this.Content is FrameworkElement root)
            {
                root.Loaded += (s, e) => Pack();
            }

            // Set default folder
            OutputFolderTextBox.Text = GetDefaultFolder();

            // Set default selected window (the list of windows is loaded when needed)
            WindowSelector.ItemsSource = new List<WindowItem> { SELECT_ALL_WINDOWS };
            WindowSelector.SelectedItem = SELECT_ALL_WINDOWS;

            WindowPicker.PickStarted += WindowPicker_PickStarted;
            WindowPicker.PickChanged += WindowPicker_Picked;
            WindowPicker.PickCompleted += WindowPicker_Picked;

            // Ensure background tasks die when the window is closed
            this.Closed += (s, e) =>
            {
                CancelCapture();
                Environment.Exit(0);
            };

            // Use exe icon as application icon
            SetAppIcon();
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
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            UpdateUiState();

            try
            {
                // The Countdown Loop (the value is NaN if the number box is empty)
                int countdownSeconds = double.IsNaN(CountdownNumberBox.Value) ? 0 : (int)CountdownNumberBox.Value;
                for (int i = countdownSeconds; i > 0; i--)
                {
                    StatusTextBlock.Text = $"Status: Capturing in {i}...";
                    await Task.Delay(1000, token);
                }

                StatusTextBlock.Text = "Status: Capturing windows now...";
                StatusProgressRing.IsActive = true;
                StatusProgressRing.Visibility = Visibility.Visible;

                string outputDir = OutputFolderTextBox.Text;
                bool overwrite = OverwriteCheckBox.IsChecked == true;
                WindowItem? targetWindow = WindowSelector.SelectedItem as WindowItem;

                // Offload actual capture to background to prevent DWM deadlock
                var capturedFiles = await Task.Run(() =>
                {
                    // find the windows now (and not before the countdown) because windows may have been opened, closed or renamed in the meantime
                    List<WindowInfo> visibleWindows = WindowInfo.GetVisibleWindows();
                    List<WindowInfo> selectedWindows = [];

                    // select all windows
                    if (targetWindow == null || targetWindow == SELECT_ALL_WINDOWS)
                    {
                        selectedWindows = visibleWindows;
                    }
                    // select window by handle
                    if (selectedWindows.Count == 0 && targetWindow?.Hwnd != null)
                    {
                        selectedWindows = visibleWindows.Where(win => targetWindow.Hwnd == win.Hwnd).ToList();
                    }
                    // select window by name
                    if (selectedWindows.Count == 0 && !string.IsNullOrWhiteSpace(targetWindow?.Name))
                    {
                        selectedWindows = visibleWindows.Where(win => targetWindow.Name == win.Name).ToList();
                    }

                    return WindowCapture.CaptureWindowsAsync(selectedWindows, outputDir, overwrite, token);
                }, token);

                if (capturedFiles.Count > 0)
                {
                    StatusTextBlock.Text = "Status: Capture complete!";
                    Explorer.OpenFolderAndSelectFiles(outputDir, capturedFiles);
                }
                else
                {
                    StatusTextBlock.Text = "Status: Nothing has been captured!";
                }
            }
            catch (OperationCanceledException)
            {
                StatusTextBlock.Text = "Status: Cancelled.";
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
                StatusTextBlock.Text = $"Status: {e.Message}";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                UpdateUiState();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs evt)
        {
            CancelCapture();
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
            bool isCapturing = _cts != null;

            StartButton.IsEnabled = !isCapturing;
            StopButton.IsEnabled = isCapturing;
            CountdownNumberBox.IsEnabled = !isCapturing;
            BrowseButton.IsEnabled = !isCapturing;

            if (!isCapturing)
            {
                StatusProgressRing.IsActive = false;
                StatusProgressRing.Visibility = Visibility.Collapsed;
            }
        }


        // --- WINDOW SELECTOR ---

        private void WindowSelector_DropDownOpened(object sender, object evt)
        {
            UpdateWindowSelector();
        }

        // Loads the list of windows, and keeps the selected window selected (if it still exists)
        private void UpdateWindowSelector()
        {
            IntPtr? selectedWindow = (WindowSelector.SelectedItem as WindowItem)?.Hwnd;

            var selection = new List<WindowItem> { SELECT_ALL_WINDOWS };
            foreach (var win in WindowInfo.GetVisibleWindows().OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                string? glyph = SHELL_WINDOW_GLYPHS.GetValueOrDefault(win.Name);
                ImageSource? icon = glyph == null ? WindowIcon.Load(win.Hwnd) : null;

                selection.Add(new WindowItem { Hwnd = win.Hwnd, Name = win.Name, Description = win.Description, Glyph = glyph, Icon = icon });
            }

            WindowSelector.ItemsSource = selection;
            WindowSelector.SelectedItem = FindWindowItem(selectedWindow) ?? SELECT_ALL_WINDOWS;
        }

        private WindowItem? FindWindowItem(IntPtr? hwnd)
        {
            return ((List<WindowItem>)WindowSelector.ItemsSource).FirstOrDefault(item => item.Hwnd == hwnd);
        }


        // --- WINDOW PICKER ---

        private void WindowPicker_PickStarted()
        {
            UpdateWindowSelector();
            _selectionBeforePick = (WindowItem)WindowSelector.SelectedItem;
        }

        // Select the window under the cursor already while dragging, so that the user can see the icon and the name of the window.
        // Return to the previous selection if there is no window that can be picked (hwnd is IntPtr.Zero) or if the window is not in the list.
        private void WindowPicker_Picked(IntPtr hwnd)
        {
            WindowSelector.SelectedItem = FindWindowItem(hwnd) ?? _selectionBeforePick;
        }


        public void SetAppIcon()
        {
            try
            {
                // extract the very first icon (index 0) from our exe file
                // (returns 0 if there is no icon, and 1 if the file is not an exe file)
                IntPtr hIcon = NativeMethods.ExtractIcon(IntPtr.Zero, Environment.ProcessPath ?? "", 0);
                if (hIcon > 1)
                {
                    this.AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon));
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }
        }

    }

}
