using System;
using System.Collections.Generic;
using System.Linq;
using static CaptureGraphicsWin2D.NativeMethods;

namespace CaptureGraphicsWin2D
{
    internal static class Explorer
    {
        /// <summary>
        /// Opens the folder in File Explorer and selects the given files.
        /// </summary>
        public static void OpenFolderAndSelectFiles(string folderPath, List<string> filePaths)
        {
            IntPtr dirPidl = IntPtr.Zero;
            List<IntPtr> absoluteFilePidls = new List<IntPtr>();

            try
            {
                // 1. Get the native shell pointer for the parent directory
                if (SHParseDisplayName(folderPath, IntPtr.Zero, out dirPidl, 0, out _) != 0)
                {
                    return;
                }

                // 2. Get the native shell pointers for every captured file
                foreach (string filePath in filePaths)
                {
                    if (SHParseDisplayName(filePath, IntPtr.Zero, out IntPtr absPidl, 0, out _) == 0)
                    {
                        absoluteFilePidls.Add(absPidl);
                    }
                }

                // The API requires pointers relative to the parent folder, so we extract the last ID
                IntPtr[] relativeFilePidls = absoluteFilePidls.Select(ILFindLastID).ToArray();

                // 3. Command Windows Explorer to open the folder and highlight the array of items
                SHOpenFolderAndSelectItems(dirPidl, (uint)relativeFilePidls.Length, relativeFilePidls, 0);
            }
            catch (Exception e)
            {
                // not being able to show the files is not worth bothering the user
                System.Diagnostics.Trace.WriteLine(e);
            }
            finally
            {
                // 4. Free all native shell memory allocations
                if (dirPidl != IntPtr.Zero) ILFree(dirPidl);
                foreach (var pidl in absoluteFilePidls)
                {
                    ILFree(pidl);
                }
            }
        }
    }
}
