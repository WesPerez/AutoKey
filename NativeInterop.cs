using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoKey
{
    public static class NativeInterop
    {
        #region Constants
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_CHAR = 0x0102;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;

        public const uint WS_VISIBLE = 0x10000000;
        public const long WS_EX_APPWINDOW = 0x00040000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_NOREPEAT = 0x4000;

        public const int WM_HOTKEY = 0x0312;

        public const int INPUT_KEYBOARD = 1;
        public const ushort KEYEVENTF_KEYUP = 0x0002;
        public const ushort KEYEVENTF_SCANCODE = 0x0008;

        public const int WH_MOUSE_LL = 14;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        #endregion

        #region Structures
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        #endregion

        #region Window Functions
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern long GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        public static int GetSystemMetrics_SM_CXSCREEN => 0;
        public static int GetSystemMetrics_SM_CYSCREEN => 1;
        #endregion

        #region Input Functions
        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);
        #endregion

        #region Hotkey Functions
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        #endregion

        #region Process Functions
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        #endregion

        #region Mouse Hook Functions
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        #endregion

        #region Ancestor Functions
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        #endregion

        #region Helper Methods
        /// <summary>
        /// Send a key press to a specific window handle using PostMessage (background).
        /// </summary>
        public static void SendKeyToWindow(IntPtr hWnd, int vkCode)
        {
            uint scanCode = MapVirtualKey((uint)vkCode, 0);
            // lParam for WM_KEYDOWN: repeat=1, scancode, extended, context=0, previous=0, transition=0
            int lParamDown = 1 | ((int)scanCode << 16);
            // Check if extended key
            bool isExtended = IsExtendedKey(vkCode);
            if (isExtended)
                lParamDown |= (1 << 24);

            int lParamUp = lParamDown | (1 << 30) | (1 << 31);

            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vkCode, (IntPtr)lParamDown);
            // Small delay between down and up for some apps to register
            System.Threading.Thread.Sleep(15);
            PostMessage(hWnd, WM_KEYUP, (IntPtr)vkCode, (IntPtr)lParamUp);
        }

        /// <summary>
        /// Send a key press to the foreground using SendInput.
        /// </summary>
        public static void SendKeyForeground(int vkCode)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki = new KEYBDINPUT
            {
                wVk = (ushort)vkCode,
                wScan = (ushort)MapVirtualKey((uint)vkCode, 0),
                dwFlags = 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki = new KEYBDINPUT
            {
                wVk = (ushort)vkCode,
                wScan = (ushort)MapVirtualKey((uint)vkCode, 0),
                dwFlags = KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Determine if a virtual key code represents an extended key.
        /// </summary>
        private static bool IsExtendedKey(int vk)
        {
            return vk == 0x21 || vk == 0x22 || // VK_PRIOR, VK_NEXT
                   vk == 0x23 || vk == 0x24 || // VK_END, VK_HOME
                   vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 || // Arrow keys
                   vk == 0x2D || vk == 0x2E || // VK_INSERT, VK_DELETE
                   vk == 0x5B || vk == 0x5C || // VK_LWIN, VK_RWIN
                   vk == 0x5D || // VK_APPS
                   vk == 0x90 || // VK_NUMLOCK
                   vk == 0xA0 || vk == 0xA1 || // VK_LSHIFT, VK_RSHIFT
                   vk == 0xA2 || vk == 0xA3 || // VK_LCONTROL, VK_RCONTROL
                   vk == 0xA4 || vk == 0xA5;   // VK_LMENU, VK_RMENU
        }

        /// <summary>
        /// Enumerate all visible top-level windows.
        /// </summary>
        public static System.Collections.Generic.List<WindowInfo> EnumerateWindows()
        {
            var windows = new System.Collections.Generic.List<WindowInfo>();
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                long exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                windows.Add(new WindowInfo { HWnd = hWnd, Title = title });
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        /// <summary>
        /// Get window title from handle.
        /// </summary>
        public static string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            return sb.ToString();
        }

        /// <summary>
        /// Make lParam for PostMessage key events.
        /// </summary>
        public static IntPtr MakeLParam(int loWord, int hiWord)
        {
            return (IntPtr)((hiWord << 16) | (loWord & 0xFFFF));
        }
        #endregion
    }

    public class WindowInfo
    {
        public IntPtr HWnd { get; set; }
        public string Title { get; set; } = "";

        public override string ToString() => Title;
    }
}
