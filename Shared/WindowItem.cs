using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;

namespace CaptureGraphicsWin2D.Shared
{
    /// <summary>
    /// An entry in the window drop-down menu: the name displayed to the user and the
    /// window it represents. An entry with a handle value of zero represents 'All Windows'.
    ///
    /// If an icon is available, it is displayed next to the name. For "All windows," the
    /// stacked windows icon from the 'Segoe Fluent Icons' set is used, while for windows
    /// without an icon, the corresponding window icon from that set is used.
    /// </summary>
    public sealed record WindowItem(IntPtr Hwnd, string Name)
    {
        public bool IsAllWindows => Hwnd == IntPtr.Zero;

        public ImageSource? Icon { get; init; }

        public Visibility IconVisibility => Icon != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;
        public string Glyph => IsAllWindows ? "\uE7C4" : "\uE737"; // Segoe Fluent Icons: stacked windows / window

        public override string ToString() => Name;
    }
}
