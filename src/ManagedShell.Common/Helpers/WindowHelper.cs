using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using ManagedShell.Interop;
using Microsoft.Win32;
using static ManagedShell.Interop.NativeMethods;

namespace ManagedShell.Common.Helpers
{
    public static class WindowHelper
    {
        public const string TrayWndClass = "Shell_TrayWnd";

        public static void ShowWindowBottomMost(IntPtr handle)
        {
            SetWindowPos(
                handle,
                (IntPtr)WindowZOrder.HWND_BOTTOM,
                0,
                0,
                0,
                0,
                (int)SetWindowPosFlags.SWP_NOSIZE | (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOACTIVATE/* | SWP_NOZORDER | SWP_NOOWNERZORDER*/);
        }

        public static void ShowWindowTopMost(IntPtr handle)
        {
            SetWindowPos(
                handle,
                (IntPtr)WindowZOrder.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                (int)SetWindowPosFlags.SWP_NOSIZE | (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_SHOWWINDOW/* | (int)SetWindowPosFlags.SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER*/);
        }

        public static void ShowWindowDesktop(IntPtr hwnd)
        {
            IntPtr desktopHwnd = GetLowestDesktopParentHwnd();

            if (desktopHwnd != IntPtr.Zero)
            {
                IntPtr nextHwnd = GetWindow(desktopHwnd, GetWindow_Cmd.GW_HWNDPREV);
                SetWindowPos(
                    hwnd,
                    nextHwnd,
                    0,
                    0,
                    0,
                    0,
                    (int)SetWindowPosFlags.SWP_NOSIZE | (int)SetWindowPosFlags.SWP_NOMOVE | (int)SetWindowPosFlags.SWP_NOACTIVATE);
            }
            else
            {
                ShowWindowBottomMost(hwnd);
            }
        }

        public static IntPtr GetLowestDesktopParentHwnd()
        {
            IntPtr progmanHwnd = FindWindow("Progman", "Program Manager");
            IntPtr desktopHwnd = FindWindowEx(progmanHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (desktopHwnd == IntPtr.Zero)
            {
                IntPtr workerHwnd = IntPtr.Zero;
                IntPtr shellIconsHwnd;
                do
                {
                    workerHwnd = FindWindowEx(IntPtr.Zero, workerHwnd, "WorkerW", null);
                    shellIconsHwnd = FindWindowEx(workerHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                } while (shellIconsHwnd == IntPtr.Zero && workerHwnd != IntPtr.Zero);

                desktopHwnd = workerHwnd;
            }
            else
            {
                desktopHwnd = progmanHwnd;
            }

            return desktopHwnd;
        }

        public static IntPtr GetLowestDesktopChildHwnd()
        {
            IntPtr progmanHwnd = FindWindow("Progman", "Program Manager");
            IntPtr desktopHwnd = FindWindowEx(progmanHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (desktopHwnd == IntPtr.Zero)
            {
                IntPtr workerHwnd = IntPtr.Zero;
                IntPtr shellIconsHwnd;
                do
                {
                    workerHwnd = FindWindowEx(IntPtr.Zero, workerHwnd, "WorkerW", null);
                    shellIconsHwnd = FindWindowEx(workerHwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                } while (shellIconsHwnd == IntPtr.Zero && workerHwnd != IntPtr.Zero);

                desktopHwnd = shellIconsHwnd;
            }

            return desktopHwnd;
        }
        
        public static void HideWindowFromTasks(IntPtr hWnd)
        {
            SetWindowLong(hWnd, GWL_EXSTYLE, GetWindowLong(hWnd, GWL_EXSTYLE) | (int)ExtendedWindowStyles.WS_EX_TOOLWINDOW);

            ExcludeWindowFromPeek(hWnd);
        }

        public static void ExcludeWindowFromPeek(IntPtr hWnd)
        {
            int status = (int)DWMNCRENDERINGPOLICY.DWMNCRP_ENABLED;
            DwmSetWindowAttribute(hWnd,
                DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK,
                ref status,
                sizeof(int));
        }

        public static void PeekWindow(bool show, IntPtr targetHwnd, IntPtr callingHwnd)
        {
            uint enable = 0;
            if (show) enable = 1;

            if (EnvironmentHelper.IsWindows81OrBetter)
            {
                DwmActivateLivePreview(enable, targetHwnd, callingHwnd, AeroPeekType.Window, IntPtr.Zero);
            }
            else
            {
                DwmActivateLivePreview(enable, targetHwnd, callingHwnd, AeroPeekType.Window);
            }
        }

        public static bool SetWindowBlur(IntPtr hWnd, bool enable, Color? color = null)
        {
            if (!EnvironmentHelper.IsWindows10OrBetter || !IsWindowBlurSupportedAndEnabled()) 
                return false;


            // https://github.com/riverar/sample-win32-acrylicblur
            // License: MIT
            var accent = new AccentPolicy();
            var accentStructSize = Marshal.SizeOf(accent);
            if (enable)
            {
                if (EnvironmentHelper.IsWindows10RS4OrBetter)
                {
                    accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                    accent.GradientColor = CalculateGradientColor(color ?? Color.FromRgb(255, 255, 255));
                }
                else
                {
                    accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                }
            }
            else
            {
                accent.AccentState = AccentState.ACCENT_DISABLED;
            }

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(hWnd, ref data);

            Marshal.FreeHGlobal(accentPtr);

            return true;
        }

        // Import the DwmIsCompositionEnabled function from dwmapi.dll
        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmIsCompositionEnabled(out bool pfEnabled);

        // Static method to check if window blur is supported and enabled
        public static bool IsWindowBlurSupportedAndEnabled()
        {
            try
            {
                // Check if DWM composition is enabled
                DwmIsCompositionEnabled(out var isCompositionEnabled);
                return isCompositionEnabled && IsTransparencyEffectsEnabled();
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTransparencyEffectsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("EnableTransparency");
                if (value is int intValue)
                {
                    return intValue == 1;
                }

                return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private static int CalculateGradientColor(Color color)
        {
            // Alpha value (transparency) in the highest byte
            int alpha = color.A;
            // Convert the color to BGR format and shift it to the lower 3 bytes
            int bgr = (color.B << 16) | (color.G << 8) | color.R;
            // Combine alpha and BGR into a single 32-bit integer
            return (alpha << 24) | (bgr & 0xFFFFFF);
        }

        public static bool SetDarkModePreference(PreferredAppMode mode)
        {
            if (EnvironmentHelper.IsWindows10DarkModeSupported)
            {
                return SetPreferredAppMode(mode);
            }

            return false;
        }

        public static IntPtr FindWindowsTray(IntPtr hwndIgnore)
        {
            IntPtr taskbarHwnd = FindWindow(TrayWndClass, "");

            if (hwndIgnore != IntPtr.Zero)
            {
                while (taskbarHwnd == hwndIgnore)
                {
                    taskbarHwnd = FindWindowEx(IntPtr.Zero, taskbarHwnd, TrayWndClass, "");
                }
            }

            return taskbarHwnd;
        }
    }
}
