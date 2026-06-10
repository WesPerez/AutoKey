using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AutoKey
{
    public static class NativeInterop
    {
        #region Constants
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const long WS_EX_TOOLWINDOW = 0x00000080L;

        public const int GWL_EXSTYLE = -20;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;

        public const int WM_HOTKEY = 0x0312;
        public const int WM_SETICON = 0x0080;
        public const uint WM_AUTOKEY_RESTORE = 0x8020;

        public const int INPUT_KEYBOARD = 1;
        public const ushort KEYEVENTF_KEYUP = 0x0002;

        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;
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
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        public static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetWindowLong64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        #endregion

        #region Input Functions
        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public static bool IsKeyDown(int vKey)
            => (GetAsyncKeyState(vKey) & unchecked((short)0x8000)) != 0;
        #endregion

        #region Hotkey Functions
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        #endregion

        #region Hook Functions
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        #endregion

        #region Ancestor Functions
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        #endregion

        #region Helper Methods
        /// <summary>
        /// Send a key press to a specific window handle using PostMessage (background).
        /// Uses randomized press duration to mimic human typing.
        /// </summary>
        public static async Task SendKeyToWindowAsync(IntPtr hWnd, int vkCode)
        {
            uint scanCode = MapVirtualKey((uint)vkCode, 0);
            int lParamDown = 1 | ((int)scanCode << 16);
            bool isExtended = IsExtendedKey(vkCode);
            if (isExtended)
                lParamDown |= (1 << 24);

            int lParamUp = lParamDown | (1 << 30) | (1 << 31);

            int pressDuration = Humanizer.NextPressDuration();

            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vkCode, (IntPtr)lParamDown);
            await Task.Delay(pressDuration);
            PostMessage(hWnd, WM_KEYUP, (IntPtr)vkCode, (IntPtr)lParamUp);
        }

        /// <summary>
        /// Send a key press to the foreground using SendInput.
        /// Uses randomized press duration to mimic human typing.
        /// </summary>
        public static async Task SendKeyForegroundAsync(int vkCode)
        {
            int pressDuration = Humanizer.NextPressDuration();

            SendInput(1, new[] { CreateKeyboardInput(vkCode, false) }, Marshal.SizeOf(typeof(INPUT)));
            await Task.Delay(pressDuration);
            SendInput(1, new[] { CreateKeyboardInput(vkCode, true) }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendKeyForeground(int vkCode)
            => SendKeyForegroundAsync(vkCode).GetAwaiter().GetResult();

        private static INPUT CreateKeyboardInput(int vkCode, bool keyUp)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)vkCode,
                        wScan = (ushort)MapVirtualKey((uint)vkCode, 0),
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
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
                long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
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

        #endregion
    }

    public class WindowInfo
    {
        public IntPtr HWnd { get; set; }
        public string Title { get; set; } = "";

        public override string ToString() => Title;
    }
}
