using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// Window picker that works like the Finder Tool of Spy++: press the mouse button on this button,
    /// drag the crosshair onto a window, and release the mouse button to pick that window.
    /// </summary>
    /// <remarks>
    /// The pointer is captured while dragging, i.e. all mouse input is sent to this button, even if the cursor is
    /// over another window. Thus the other window is not affected in any way (e.g. it is not clicked or activated)
    /// and cannot change the cursor, so the cursor remains a crosshair wherever it is on screen.
    /// </remarks>
    public sealed partial class WindowPickerButton : Button
    {
        private readonly WindowHighlight _highlight = new WindowHighlight();

        private bool _isPicking = false;
        private IntPtr _window = IntPtr.Zero;

        /// <summary>
        /// The user has started dragging.
        /// </summary>
        public event Action? PickStarted;

        /// <summary>
        /// The user has dragged the cursor onto another window (IntPtr.Zero if the cursor is not over a window that can be picked).
        /// </summary>
        public event Action<IntPtr>? PickChanged;

        /// <summary>
        /// The user has picked a window (IntPtr.Zero if cancelled, i.e. released over our own window, or pressed Escape).
        /// </summary>
        public event Action<IntPtr>? PickCompleted;

        protected override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!_isPicking && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isPicking = true;
                CapturePointer(e.Pointer);
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
                PickStarted?.Invoke();
            }
        }

        protected override void OnPointerMoved(PointerRoutedEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_isPicking)
            {
                IntPtr window = WindowInfo.GetWindowUnderCursor();
                if (window != _window)
                {
                    _window = window;
                    if (window != IntPtr.Zero)
                    {
                        _highlight.Show(window);
                    }
                    else
                    {
                        _highlight.Hide();
                    }
                    PickChanged?.Invoke(window);
                }
            }
        }

        protected override void OnPointerReleased(PointerRoutedEventArgs e)
        {
            CompletePick(_window);

            // releases the pointer capture
            base.OnPointerReleased(e);
        }

        protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
        {
            // e.g. the user has switched to another application via keyboard while dragging
            CompletePick(IntPtr.Zero);

            base.OnPointerCaptureLost(e);
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (_isPicking && e.Key == VirtualKey.Escape)
            {
                CompletePick(IntPtr.Zero);
                ReleasePointerCaptures();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        private void CompletePick(IntPtr window)
        {
            if (_isPicking)
            {
                _isPicking = false;
                _window = IntPtr.Zero;
                _highlight.Hide();
                ProtectedCursor = null;
                PickCompleted?.Invoke(window);
            }
        }
    }
}
