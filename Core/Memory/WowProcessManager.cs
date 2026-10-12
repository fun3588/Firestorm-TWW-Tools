using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using FirestormTwwMonitor.Core.Native;

namespace FirestormTwwMonitor.Core.Memory
{
    public class WowProcessItem
    {
        public int Pid { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public IntPtr MainWindowHandle { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public uint Level { get; set; }
        public bool IsInWorld { get; set; }

        public override string ToString()
        {
            string charStr = string.IsNullOrWhiteSpace(CharacterName) ? "未登录/未知" : CharacterName;
            return $"PID {Pid} | 角色: {charStr} | 窗口: {WindowTitle}";
        }
    }

    public static class WowProcessManager
    {
        /// <summary>
        /// 严格精确匹配窗口标题：一字不差地等于 "魔兽世界" 或 "world of warcraft"（英文忽略大小写）
        /// </summary>
        public static bool IsExactWowWindowTitle(string? title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            string t = title.Trim();
            return string.Equals(t, "魔兽世界", StringComparison.Ordinal) ||
                   string.Equals(t, "world of warcraft", StringComparison.OrdinalIgnoreCase);
        }

        public static IntPtr FindWindowForProcess(int pid)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                Win32Api.EnumWindows((hWnd, lParam) =>
                {
                    Win32Api.GetWindowThreadProcessId(hWnd, out uint wPid);
                    if (wPid == (uint)pid)
                    {
                        var sbClass = new StringBuilder(256);
                        Win32Api.GetClassName(hWnd, sbClass, 256);
                        if (sbClass.ToString().Equals("GxWindowClass", StringComparison.OrdinalIgnoreCase))
                        {
                            found = hWnd;
                            return false;
                        }

                        var sbTitle = new StringBuilder(256);
                        Win32Api.GetWindowText(hWnd, sbTitle, 256);
                        if (IsExactWowWindowTitle(sbTitle.ToString()))
                        {
                            found = hWnd;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }
            return found;
        }

        public static List<WowProcessItem> FindWowProcesses()
        {
            var result = new List<WowProcessItem>();
            int currentPid = Environment.ProcessId;

            // 1. 先通过 Win32 EnumWindows 枚举所有顶层窗口，精准捕获标题一字不差为 "魔兽世界" 或 "world of warcraft" 的窗口句柄与 PID
            var matchedWindows = new Dictionary<int, (IntPtr Hwnd, string Title)>();
            Win32Api.EnumWindows((hWnd, lParam) =>
            {
                var sb = new StringBuilder(256);
                int len = Win32Api.GetWindowText(hWnd, sb, sb.Capacity);
                if (len <= 0) return true;

                string wTitle = sb.ToString();
                if (IsExactWowWindowTitle(wTitle))
                {
                    Win32Api.GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid > 0 && pid != currentPid)
                    {
                        var sbClass = new StringBuilder(128);
                        Win32Api.GetClassName(hWnd, sbClass, sbClass.Capacity);
                        bool isGx = sbClass.ToString().Equals("GxWindowClass", StringComparison.OrdinalIgnoreCase);

                        if (!matchedWindows.ContainsKey((int)pid) || isGx)
                        {
                            matchedWindows[(int)pid] = (hWnd, wTitle.Trim());
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            // 2. 遍历系统进程，仅保留窗口标题一字不差为 "魔兽世界" 或 "world of warcraft" 的客户端
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid) continue;

                    IntPtr hWnd = IntPtr.Zero;
                    string title = string.Empty;

                    if (matchedWindows.TryGetValue(p.Id, out var winInfo))
                    {
                        hWnd = winInfo.Hwnd;
                        title = winInfo.Title;
                    }
                    else if (IsExactWowWindowTitle(p.MainWindowTitle))
                    {
                        hWnd = p.MainWindowHandle != IntPtr.Zero ? p.MainWindowHandle : FindWindowForProcess(p.Id);
                        title = p.MainWindowTitle.Trim();
                    }
                    else
                    {
                        continue;
                    }

                    string charName = TryReadCharacterName(p);

                    result.Add(new WowProcessItem
                    {
                        Pid = p.Id,
                        ProcessName = p.ProcessName,
                        WindowTitle = title,
                        MainWindowHandle = hWnd,
                        CharacterName = charName
                    });
                }
                catch
                {
                    // 进程权限受限或已被关闭
                }
            }

            return result;
        }

        private static string TryReadCharacterName(Process process)
        {
            IntPtr hProc = IntPtr.Zero;
            try
            {
                hProc = Win32Api.OpenProcess(Win32Api.PROCESS_VM_READ | Win32Api.PROCESS_QUERY_INFORMATION, false, process.Id);
                if (hProc == IntPtr.Zero) return string.Empty;

                long baseAddr = process.MainModule?.BaseAddress.ToInt64() ?? 0;
                if (baseAddr == 0) return string.Empty;

                byte[] buffer = new byte[32];
                if (Win32Api.ReadProcessMemory(hProc, new IntPtr(baseAddr + Offsets.OFF_NAME_GLOBAL), buffer, new IntPtr(32), out _))
                {
                    int zeroIdx = Array.IndexOf(buffer, (byte)0);
                    if (zeroIdx >= 0)
                    {
                        return Encoding.UTF8.GetString(buffer, 0, zeroIdx).Trim();
                    }
                    return Encoding.UTF8.GetString(buffer).Trim();
                }
            }
            catch
            {
            }
            finally
            {
                if (hProc != IntPtr.Zero)
                {
                    Win32Api.CloseHandle(hProc);
                }
            }
            return string.Empty;
        }
    }
}
