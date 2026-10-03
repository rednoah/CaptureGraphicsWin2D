using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Runtime.InteropServices;

namespace CaptureGraphicsWin2D.Shared
{
    /// <summary>
    /// Search tool in the style of Spy++: Left-click the crosshair, drag it onto any window, and release
    /// the mouse button. This triggers the 'WindowPicked' event, passing the topmost window located beneath
    /// the mouse pointer. Built in code only (no XAML) so the file can be shared between projects.
    /// </summary>
    public sealed partial class CrosshairWindowPicker : UserControl
    {
        public event EventHandler<IntPtr>? WindowPicked;

        // shared by the icon and the drag cursor so both look the same
        private const double ICON_SIZE = 20;
        private const double ICON_RADIUS = ICON_SIZE * 0.35;
        private const double ICON_STROKE = 1.5;

        private readonly Canvas _icon;
        private readonly InputCursor _hoverCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        private bool _isDragging;

        // the drag cursor (native handle + WinUI wrapper) and the scale factor it was created for (0 = none yet)
        private IntPtr _cursorHandle = IntPtr.Zero;
        private InputCursor? _dragCursor;
        private double _cursorScale;

        public CrosshairWindowPicker()
        {
            Width = 32;
            Height = 32;

            _icon = CreateCrosshairIcon();

            // transparent (not null) background so the whole area receives pointer input
            Content = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                Child = _icon,
            };

            ToolTipService.SetToolTip(this, "Grab and drag onto a window to select it");
            AutomationProperties.SetName(this, "Window picker");
            ProtectedCursor = _hoverCursor;

            IsEnabledChanged += (_, _) => UpdateIcon();

            // free the native cursor
            Unloaded += (_, _) => DestroyDragCursor();
        }

        private Canvas CreateCrosshairIcon()
        {
            var canvas = new Canvas
            {
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            const double center = ICON_SIZE / 2;

            var circle = new Ellipse { Width = ICON_RADIUS * 2, Height = ICON_RADIUS * 2 };
            Canvas.SetLeft(circle, center - ICON_RADIUS);
            Canvas.SetTop(circle, center - ICON_RADIUS);

            var horizontal = new Line { X1 = 0, Y1 = center, X2 = ICON_SIZE, Y2 = center };
            var vertical = new Line { X1 = center, Y1 = 0, X2 = center, Y2 = ICON_SIZE };

            foreach (Shape shape in new Shape[] { circle, horizontal, vertical })
            {
                // draw in the (theme dependent) text color of this control
                shape.SetBinding(Shape.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
                shape.StrokeThickness = ICON_STROKE;
                canvas.Children.Add(shape);
            }

            return canvas;
        }

        private void UpdateIcon()
        {
            // while dragging the crosshair is "in hand" (i.e. it is the mouse cursor), so the icon is hidden
            _icon.Opacity = _isDragging ? 0 : (IsEnabled ? 1 : 0.4);
        }

        protected override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (!CapturePointer(e.Pointer))
            {
                return;
            }

            // continue receiving pointer inputs while the mouse is outside our window
            _isDragging = true;
            UpdateIcon();

            // WinUI shows the cursor itself (Win32 SetCursor / SetCapture don't work for a drag that WinUI
            // started), so the drag cursor has to be the ProtectedCursor of the control holding the capture
            ProtectedCursor = GetDragCursor() ?? _hoverCursor;

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerRoutedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (!_isDragging)
            {
                return;
            }

            EndDrag();
            ReleasePointerCapture(e.Pointer);
            e.Handled = true;

            // released on the crosshair itself (i.e. not dragged anywhere) = nothing picked
            var position = e.GetCurrentPoint(this).Position;
            if (position is { X: >= 0, Y: >= 0 } && position.X < ActualWidth && position.Y < ActualHeight)
            {
                return;
            }

            IntPtr hwnd = GetTopLevelWindowUnderCursor();
            if (hwnd != IntPtr.Zero)
            {
                WindowPicked?.Invoke(this, hwnd);
            }
        }

        protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            EndDrag();
        }

        private void EndDrag()
        {
            _isDragging = false;
            UpdateIcon();
            ProtectedCursor = _hoverCursor;
        }

        private InputCursor? GetDragCursor()
        {
            // match the DPI of the monitor our window is on (recreate if that changed since last time)
            double scale = XamlRoot?.RasterizationScale ?? 1.0;
            if (_dragCursor == null || Math.Abs(_cursorScale - scale) > 0.001)
            {
                DestroyDragCursor();
                _cursorHandle = CrosshairCursor.Create(ICON_SIZE, ICON_RADIUS, ICON_STROKE, scale);
                _dragCursor = CrosshairCursor.ToInputCursor(_cursorHandle);
                _cursorScale = scale;
            }
            return _dragCursor;
        }

        private void DestroyDragCursor()
        {
            if (ProtectedCursor == _dragCursor)
            {
                ProtectedCursor = _hoverCursor;
            }

            _dragCursor?.Dispose();
            _dragCursor = null;

            CrosshairCursor.Destroy(_cursorHandle);
            _cursorHandle = IntPtr.Zero;
            _cursorScale = 0;
        }

        private static IntPtr GetTopLevelWindowUnderCursor()
        {
            if (!GetCursorPos(out POINT point))
            {
                return IntPtr.Zero;
            }

            // WindowFromPoint returns the deepest child window (e.g. a button), we want the top-level window
            IntPtr hwnd = WindowFromPoint(point);
            if (hwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            return root != IntPtr.Zero ? root : hwnd;
        }

        // --- WIN32 INTEROP ---

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private const uint GA_ROOT = 2;

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("user32.dll")]
        private static partial IntPtr WindowFromPoint(POINT point);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    }
}
