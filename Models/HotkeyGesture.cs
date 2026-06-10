using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace AutoKey
{
    public static class HotkeyGesture
    {
        public const int VkShift = 0x10;
        public const int VkControl = 0x11;
        public const int VkAlt = 0x12;
        public const int VkWin = 0x5B;

        public static int NormalizeVirtualKey(int vk)
        {
            return vk switch
            {
                0xA0 or 0xA1 => VkShift,
                0xA2 or 0xA3 => VkControl,
                0xA4 or 0xA5 => VkAlt,
                0x5C => VkWin,
                _ => vk
            };
        }

        public static bool IsModifier(int vk)
        {
            vk = NormalizeVirtualKey(vk);
            return vk is VkShift or VkControl or VkAlt or VkWin;
        }

        public static string FromVirtualKeys(IEnumerable<int> virtualKeys)
        {
            var keys = virtualKeys
                .Select(NormalizeVirtualKey)
                .Where(vk => vk > 0)
                .Distinct()
                .ToList();

            if (keys.Count == 0)
                return "";

            var ordered = new List<int>();
            foreach (int modifier in new[] { VkControl, VkShift, VkAlt, VkWin })
            {
                if (keys.Remove(modifier))
                    ordered.Add(modifier);
            }

            ordered.AddRange(keys.OrderBy(v => v));
            return string.Join("+", ordered.Select(GetDisplayName));
        }

        public static bool Matches(string hotkeyText, ISet<int> pressedKeys)
        {
            if (!TryParse(hotkeyText, out var hotkeyKeys) || hotkeyKeys.Count == 0)
                return false;

            var pressed = pressedKeys
                .Select(NormalizeVirtualKey)
                .Where(vk => vk > 0)
                .ToHashSet();

            return pressed.SetEquals(hotkeyKeys);
        }

        public static bool IsRegisterableHotkey(string? text)
            => TryGetRegistration(text, out _, out _);

        public static bool IsRegisterableHotkey(IEnumerable<int> virtualKeys)
        {
            var keys = virtualKeys
                .Select(NormalizeVirtualKey)
                .Where(vk => vk > 0)
                .Distinct()
                .ToHashSet();

            return IsRegisterableKeySet(keys);
        }

        public static bool TryGetRegistration(string? text, out int modifiers, out int key)
        {
            modifiers = 0;
            key = 0;
            if (!TryParse(text, out var keys) || !IsRegisterableKeySet(keys))
                return false;

            foreach (int vk in keys)
            {
                if (IsModifier(vk))
                {
                    modifiers |= vk switch
                    {
                        VkControl => NativeInterop.MOD_CONTROL,
                        VkShift => NativeInterop.MOD_SHIFT,
                        VkAlt => NativeInterop.MOD_ALT,
                        VkWin => NativeInterop.MOD_WIN,
                        _ => 0
                    };
                }
                else
                {
                    key = vk;
                }
            }

            modifiers |= NativeInterop.MOD_NOREPEAT;
            return key > 0;
        }

        public static bool TryParse(string? text, out HashSet<int> virtualKeys)
        {
            virtualKeys = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParsePart(rawPart, out int vk))
                    return false;

                virtualKeys.Add(NormalizeVirtualKey(vk));
            }

            return virtualKeys.Count > 0;
        }

        private static bool IsRegisterableKeySet(ISet<int> virtualKeys)
        {
            int nonModifierCount = virtualKeys.Count(vk => !IsModifier(vk));
            return nonModifierCount == 1;
        }

        private static bool TryParsePart(string part, out int vk)
        {
            vk = 0;
            string token = part.Trim();
            if (token.Length == 0)
                return false;

            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    vk = VkControl;
                    return true;
                case "SHIFT":
                    vk = VkShift;
                    return true;
                case "ALT":
                    vk = VkAlt;
                    return true;
                case "WIN":
                case "WINDOWS":
                    vk = VkWin;
                    return true;
                case "ESC":
                case "ESCAPE":
                    vk = 0x1B;
                    return true;
                case "SPACE":
                    vk = 0x20;
                    return true;
            }

            if (token.Length == 1)
            {
                char c = char.ToUpperInvariant(token[0]);
                if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    vk = c;
                    return true;
                }
            }

            if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(token[1..], out int fn) &&
                fn is >= 1 and <= 24)
            {
                vk = 0x70 + fn - 1;
                return true;
            }

            if (Enum.TryParse<Key>(token, true, out var key))
            {
                vk = KeyInterop.VirtualKeyFromKey(key);
                return vk > 0;
            }

            return false;
        }

        private static string GetDisplayName(int vk)
        {
            return vk switch
            {
                VkControl => "Ctrl",
                VkShift => "Shift",
                VkAlt => "Alt",
                VkWin => "Win",
                >= 0x30 and <= 0x39 => ((char)vk).ToString(),
                >= 0x41 and <= 0x5A => ((char)vk).ToString(),
                >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
                0x20 => "Space",
                0x1B => "Esc",
                _ => KeyInterop.KeyFromVirtualKey(vk).ToString()
            };
        }
    }
}
