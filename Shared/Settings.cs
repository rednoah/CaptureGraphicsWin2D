using Microsoft.Win32;
using System;
using System.IO;

namespace CaptureGraphicsWin2D
{
    /// <summary>
    /// Settings that are remembered between sessions, stored in the registry (HKEY_CURRENT_USER\Software\CaptureGraphicsWin2D).
    /// The registry works for both the classic (unpackaged) and the Microsoft Store (packaged) app.
    /// </summary>
    internal static class Settings
    {
        private const string KEY = @"Software\CaptureGraphicsWin2D";

        public const string OUTPUT_FOLDER = "OutputFolder";
        public const string COUNTDOWN = "Countdown";

        /// <summary>
        /// Calls apply with the stored folder, but only if there is one and it still exists.
        /// </summary>
        public static void RestoreDirectory(string name, Action<string> apply, string defaultValue)
        {
            if (Load(name) is string path && Directory.Exists(path))
            {
                apply(path);
            } else
            {
                apply(defaultValue);
            }
        }

        /// <summary>
        /// Calls apply with the stored number, but only if there is one and it is between min and max.
        /// </summary>
        public static void RestoreInteger(string name, Action<int> apply, int defaultValue)
        {
            if (Load(name) is int value)
            {
                apply(value);
            } else
            {
                apply(defaultValue);
            }
        }

        public static void Save(string name, string value)
        {
            Write(name, value);
        }

        public static void Save(string name, int value)
        {
            Write(name, value);
        }

        // Returns the stored value, or null if there is none
        private static object? Load(string name)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KEY);
                return key?.GetValue(name);
            }
            catch (Exception e)
            {
                // not being able to remember settings is not worth bothering the user
                System.Diagnostics.Trace.WriteLine(e);
                return null;
            }
        }

        private static void Write(string name, object value)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(KEY);
                key.SetValue(name, value);
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }
        }
    }
}
