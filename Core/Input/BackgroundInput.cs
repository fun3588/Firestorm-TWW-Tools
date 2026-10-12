using System;
using System.Runtime.InteropServices;
using System.Threading;
using FirestormTwwMonitor.Core.Native;

namespace FirestormTwwMonitor.Core.Input
{
    public enum ReconnectInputMode
    {
        /// <summary>
        /// 前台硬件级模拟（短暂激活窗口 + DirectInput 硬件扫描码回车 + 屏幕相对比例点击，100% 稳定）
        /// </summary>
        ForegroundHardware = 0,

        /// <summary>
        /// 纯后台消息模式（不抢窗口焦点，发送 WM_ACTIVATE + WM_KEYDOWN/CHAR/KEYUP + 后台相对坐标点击）
        /// </summary>
        BackgroundMessage = 1
    }

    public static class BackgroundInput
    {
        /// <summary>
        /// 后台向指定窗口句柄发送指定虚拟按键（包含 DirectInput 硬件扫描码与 WM_ACTIVATE 唤醒）
        /// </summary>
        public static bool SendBackgroundKey(IntPtr hWnd, int virtualKey, int holdMs = 60)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                uint scanCode = virtualKey == Win32Api.VK_RETURN ? 0x1C : Win32Api.MapVirtualKey((uint)virtualKey, 0);

                long lParamDown = 1 | (scanCode << 16);
                long lParamUp = 1 | (scanCode << 16) | (1L << 30) | (1L << 31);

                // 1. 发送 WM_ACTIVATE (WA_ACTIVE=1) 与 WM_SETFOCUS，让后台窗口就绪接收键盘消息
                Win32Api.PostMessage(hWnd, Win32Api.WM_ACTIVATE, (IntPtr)1, IntPtr.Zero);
                Win32Api.PostMessage(hWnd, Win32Api.WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

                // 2. 发送 WM_KEYDOWN
                Win32Api.PostMessage(hWnd, Win32Api.WM_KEYDOWN, (IntPtr)virtualKey, (IntPtr)lParamDown);

                // 3. 若为回车键，补发字符消息 WM_CHAR ('\r' / 13) 确保被 GlueXML/UI 侦听
                if (virtualKey == Win32Api.VK_RETURN)
                {
                    Win32Api.PostMessage(hWnd, Win32Api.WM_CHAR, (IntPtr)13, (IntPtr)lParamDown);
                }

                if (holdMs > 0)
                {
                    Thread.Sleep(holdMs);
                }

                // 4. 发送 WM_KEYUP
                Win32Api.PostMessage(hWnd, Win32Api.WM_KEYUP, (IntPtr)virtualKey, (IntPtr)lParamUp);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 后台向指定窗口发送回车键 + 选角界面底部【进入游戏】按钮点击双保险
        /// </summary>
        public static bool SendBackgroundEnter(IntPtr hWnd, int holdMs = 60)
        {
            bool ok = SendBackgroundKey(hWnd, Win32Api.VK_RETURN, holdMs);
            // 双保险：在角色选择界面，同时向屏幕底部中间【进入游戏】按钮 (50%, 92%) 发送后台点击
            SendBackgroundClickRel(hWnd, 0.50, 0.92, 40);
            return ok;
        }

        /// <summary>
        /// 前台硬件级扫描码按键发送（兼容 DirectX / DirectInput）
        /// </summary>
        public static bool SendHardwareKey(IntPtr hWnd, int virtualKey, int holdMs = 60)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                ActivateWindow(hWnd);
                Thread.Sleep(80);

                ushort vk = (ushort)virtualKey;
                ushort scanCode = (ushort)Win32Api.MapVirtualKey(vk, 0);

                var downInput = new Win32Api.INPUT
                {
                    type = Win32Api.INPUT_KEYBOARD,
                    u = new Win32Api.InputUnion
                    {
                        ki = new Win32Api.KEYBDINPUT
                        {
                            wVk = vk,
                            wScan = scanCode,
                            dwFlags = Win32Api.KEYEVENTF_SCANCODE,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                var upInput = new Win32Api.INPUT
                {
                    type = Win32Api.INPUT_KEYBOARD,
                    u = new Win32Api.InputUnion
                    {
                        ki = new Win32Api.KEYBDINPUT
                        {
                            wVk = vk,
                            wScan = scanCode,
                            dwFlags = Win32Api.KEYEVENTF_SCANCODE | Win32Api.KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                int cbSize = Marshal.SizeOf(typeof(Win32Api.INPUT));
                Win32Api.SendInput(1, new[] { downInput }, cbSize);
                Thread.Sleep(Math.Max(holdMs, 40));
                Win32Api.SendInput(1, new[] { upInput }, cbSize);

                // 同时补发一次窗口消息保底
                SendBackgroundKey(hWnd, virtualKey, 30);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 后台向窗口指定相对客户区坐标 (x, y) 模拟点击左键
        /// </summary>
        public static bool SendBackgroundClick(IntPtr hWnd, int clientX, int clientY, int holdMs = 60)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                IntPtr lParam = Win32Api.MakeLParam(clientX, clientY);
                Win32Api.PostMessage(hWnd, Win32Api.WM_ACTIVATE, (IntPtr)1, IntPtr.Zero);
                Win32Api.PostMessage(hWnd, Win32Api.WM_LBUTTONDOWN, (IntPtr)1, lParam);
                if (holdMs > 0)
                {
                    Thread.Sleep(holdMs);
                }
                Win32Api.PostMessage(hWnd, Win32Api.WM_LBUTTONUP, IntPtr.Zero, lParam);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 后台向窗口指定相对客户区比例坐标 (relX, relY) 模拟点击左键
        /// </summary>
        public static bool SendBackgroundClickRel(IntPtr hWnd, double relX, double relY, int holdMs = 60)
        {
            if (hWnd == IntPtr.Zero) return false;
            if (Win32Api.GetClientRect(hWnd, out var rect) && rect.Width > 100 && rect.Height > 100)
            {
                int cx = (int)(rect.Width * relX);
                int cy = (int)(rect.Height * relY);
                return SendBackgroundClick(hWnd, cx, cy, holdMs);
            }
            return false;
        }

        /// <summary>
        /// 执行重连步骤动作：回车键 + 窗口客户区相对比例位置 (relX, relY) 点击双保险
        /// </summary>
        public static bool SendReconnectAction(IntPtr hWnd, ReconnectInputMode mode, double relX = 0.50, double relY = 0.92)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                // 若窗口处于最小化状态，先以不激活方式恢复尺寸，确保能正常接收后台消息与坐标点击
                if (Win32Api.IsIconic(hWnd))
                {
                    Win32Api.ShowWindow(hWnd, Win32Api.SW_SHOWNOACTIVATE);
                    Thread.Sleep(80);
                }

                // 1. 计算窗口客户区相对像素坐标
                int cx = 0, cy = 0;
                bool hasRect = Win32Api.GetClientRect(hWnd, out var rect) && rect.Width > 100 && rect.Height > 100;
                if (hasRect)
                {
                    cx = (int)(rect.Width * relX);
                    cy = (int)(rect.Height * relY);
                }

                if (mode == ReconnectInputMode.ForegroundHardware)
                {
                    // 前台硬件模式：短暂置前 -> 硬件扫描码回车 -> 相对比例位置点击
                    ActivateWindow(hWnd);
                    Thread.Sleep(80);

                    SendHardwareKey(hWnd, Win32Api.VK_RETURN, 60);

                    if (hasRect && relX > 0 && relY > 0)
                    {
                        Thread.Sleep(100);
                        var pt = new Win32Api.POINT { X = cx, Y = cy };
                        if (Win32Api.ClientToScreen(hWnd, ref pt))
                        {
                            Win32Api.SetCursorPos(pt.X, pt.Y);
                            Thread.Sleep(30);
                            Win32Api.mouse_event(Win32Api.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                            Thread.Sleep(50);
                            Win32Api.mouse_event(Win32Api.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                        }
                        SendBackgroundClick(hWnd, cx, cy, 40);
                    }
                }
                else
                {
                    // 纯后台消息模式：不置前，发送后台回车 + 后台坐标点击
                    SendBackgroundEnter(hWnd, 60);
                    if (hasRect && relX > 0 && relY > 0)
                    {
                        Thread.Sleep(80);
                        SendBackgroundClick(hWnd, cx, cy, 50);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 激活窗口到前台（若最小化则先恢复）
        /// </summary>
        public static bool ActivateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;
            try
            {
                if (Win32Api.IsIconic(hWnd))
                {
                    Win32Api.ShowWindow(hWnd, Win32Api.SW_RESTORE);
                    Thread.Sleep(80);
                }
                return Win32Api.SetForegroundWindow(hWnd);
            }
            catch
            {
                return false;
            }
        }
    }
}
