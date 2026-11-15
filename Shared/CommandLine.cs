using System;
using System.Linq;
using System.Threading.Tasks;
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
                    var windows = WindowInfo.GetVisibleWindows().Where(win => targetWindows.Length == 0 || targetWindows.Any(win.Matches));
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
    }
}
