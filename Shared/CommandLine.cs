using System;
using System.Globalization;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    internal static class CommandLine
    {
        /// <summary>
        /// Usage: CaptureGraphicsWin2D OUTPUT_FOLDER [WINDOW]...
        /// </summary>
        /// <param name="args">
        /// args[0] = executable,
        /// args[1] = output folder,
        /// args[2..n] = windows to capture, given by window title, window class or window handle (default: all windows)
        /// </param>
        /// <returns>Exit code, i.e. 0 if at least one window has been captured</returns>
        public static async Task<int> RunAsync(string[] args)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);

            try
            {
                string outputDir = args[1];
                string[] targetWindows = args.Skip(2).ToArray();

                // Offload actual capture to background (same as the GUI)
                var capturedFiles = await Task.Run(() =>
                {
                    var windows = WindowInfo.GetVisibleWindows().Where(window => targetWindows.Length == 0 || targetWindows.Any(target => Matches(target, window)));
                    return WindowCapture.CaptureWindowsAsync(windows, outputDir, overwrite: true);
                });

                capturedFiles.ForEach(Console.WriteLine);
                return capturedFiles.Count > 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }


        /// <summary>
        /// True if the given text is the name or the handle of this window.
        /// </summary>
        private static bool Matches(string target, WindowInfo window)
        {
            // check for an exact match first, so that a name with a backslash (escape character of the pattern) still matches itself
            return target == window.Name || FileSystemName.MatchesSimpleExpression(target, window.Name) || (TryParseId(target, out IntPtr hwnd) && hwnd == window.Hwnd);
        }

        /// <summary>
        /// Parses a window handle in hexadecimal (e.g. 0x000A0B2C) or decimal (e.g. 658220) notation.
        /// </summary>
        private static bool TryParseId(string text, out IntPtr hwnd)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hwnd);
            }
            return IntPtr.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out hwnd);
        }


    }
}
