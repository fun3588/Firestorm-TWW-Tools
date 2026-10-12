using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FirestormTwwMonitor.Core.Memory;
using FirestormTwwMonitor.Core.Native;

namespace FirestormTwwMonitor.Core.SpeedHack
{
    /// <summary>
    /// 工业级高精度游戏变速器 (QPC Inline Hook 变速齿轮)。
    /// 专为 WoW 11.2.5 (The War Within) 引擎打造。
    /// 
    /// 解决问题：
    /// 现代魔兽世界客户端主模块抹除了静态导入表（IAT为0），纯 IAT Hook 无法生效。
    /// 本实现直接 Hook 系统高精度时钟入口 QueryPerformanceCounter (位于 kernelbase.dll)。
    /// 通过 Trampoline 备份原指令，并将返回值按倍率进行连续时间插值，
    /// 使得游戏引擎感知到的时间流速变快/变慢，达到 100% 可靠的变速效果。
    /// </summary>
    public sealed class SpeedHackController : IDisposable
    {
        private const int CAVE_SIZE = 512;

        // 数据区偏移
        private const int OFF_SPEED        = 0x00; // double (8 bytes)
        private const int OFF_INIT_REAL    = 0x08; // int64  (8 bytes)
        private const int OFF_INIT_FAKE    = 0x10; // int64  (8 bytes)
        private const int OFF_INITIALIZED  = 0x18; // int32  (4 bytes)
        private const int OFF_TRAMP_ADDR   = 0x20; // int64  (8 bytes)

        // 代码区偏移
        private const int OFF_TRAMPOLINE   = 0x40; // Trampoline 执行原始指令
        private const int OFF_HOOK_FUNC    = 0x80; // Hook 拦截函数入口

        private readonly WowMemorySession _session;

        private IntPtr _caveMem = IntPtr.Zero;
        private IntPtr _pQpcTarget = IntPtr.Zero;
        private byte[] _origBytes = new byte[16];
        private bool _installed = false;

        public float CurrentSpeed { get; private set; } = 1.0f;
        public bool IsInstalled => _installed;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr dwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

        public SpeedHackController(WowMemorySession session)
        {
            _session = session;
        }

        /// <summary>
        /// 安装变速 Hook 并设定初始倍率。
        /// </summary>
        public bool Install(float speed = 1.0f)
        {
            if (_installed) return SetSpeed(speed);
            if (!_session.IsAttached) return false;

            speed = Math.Clamp(speed, 0.1f, 10.0f);

            // 1. 获取目标系统函数 QueryPerformanceCounter 地址
            IntPtr hKernelBase = GetModuleHandle("kernelbase.dll");
            if (hKernelBase == IntPtr.Zero) hKernelBase = GetModuleHandle("kernel32.dll");
            if (hKernelBase == IntPtr.Zero) return false;

            _pQpcTarget = GetProcAddress(hKernelBase, "QueryPerformanceCounter");
            if (_pQpcTarget == IntPtr.Zero) return false;

            // 2. 读取原始 16 字节
            for (int i = 0; i < 16; i++)
            {
                _origBytes[i] = _session.ReadByte(_pQpcTarget.ToInt64() + i);
            }

            // 3. 在目标进程分配 Code Cave
            _caveMem = Win32Api.VirtualAllocEx(_session.ProcessHandle, IntPtr.Zero, (IntPtr)CAVE_SIZE,
                Win32Api.MEM_COMMIT | Win32Api.MEM_RESERVE, Win32Api.PAGE_EXECUTE_READWRITE);
            if (_caveMem == IntPtr.Zero) return false;

            long caveBase = _caveMem.ToInt64();

            // 4. 初始化基准时间
            QueryPerformanceCounter(out long currentReal);
            _session.WriteBytes(caveBase + OFF_SPEED, BitConverter.GetBytes((double)speed));
            _session.WriteBytes(caveBase + OFF_INIT_REAL, BitConverter.GetBytes(currentReal));
            _session.WriteBytes(caveBase + OFF_INIT_FAKE, BitConverter.GetBytes(currentReal));
            _session.WriteBytes(caveBase + OFF_INITIALIZED, BitConverter.GetBytes(1));

            // 5. 构建 Trampoline 函数 (0x40)：执行原始 16 字节，然后跳回 QPC + 16
            long trampAddr = caveBase + OFF_TRAMPOLINE;
            var tramp = new List<byte>();
            tramp.AddRange(_origBytes);
            // FF 25 00 00 00 00 [8 bytes QPC + 16]
            tramp.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 });
            tramp.AddRange(BitConverter.GetBytes(_pQpcTarget.ToInt64() + 16));
            _session.WriteBytes(trampAddr, tramp.ToArray());
            _session.WriteBytes(caveBase + OFF_TRAMP_ADDR, BitConverter.GetBytes(trampAddr));

            // 6. 构建 Hook 拦截函数体 (0x80)
            long hookAddr = caveBase + OFF_HOOK_FUNC;
            byte[] hookCode = BuildHookCode(caveBase, trampAddr);
            _session.WriteBytes(hookAddr, hookCode);

            // 7. 在 QPC 函数入口写入 14 字节绝对跳转 + 2 字节 NOP (共 16 字节)
            var patch = new List<byte>();
            patch.AddRange(new byte[] { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 }); // jmp qword ptr [rip+0]
            patch.AddRange(BitConverter.GetBytes(hookAddr));
            patch.Add(0x90); // nop
            patch.Add(0x90); // nop

            bool patchOk = WriteProtectedMemory(_pQpcTarget, patch.ToArray());
            if (!patchOk)
            {
                Cleanup();
                return false;
            }

            FlushInstructionCache(_session.ProcessHandle, _pQpcTarget, (IntPtr)16);
            FlushInstructionCache(_session.ProcessHandle, _caveMem, (IntPtr)CAVE_SIZE);

            _installed = true;
            CurrentSpeed = speed;
            return true;
        }

        /// <summary>
        /// 动态调整倍率（使用平滑时间插值算法，无跳帧、无崩溃）。
        /// </summary>
        public bool SetSpeed(float newSpeed)
        {
            if (!_installed || _caveMem == IntPtr.Zero) return false;

            newSpeed = Math.Clamp(newSpeed, 0.1f, 10.0f);
            long caveBase = _caveMem.ToInt64();

            // 1. 读取当前基准和当前系统实时时钟
            long oldInitReal = _session.ReadInt64(caveBase + OFF_INIT_REAL);
            long oldInitFake = _session.ReadInt64(caveBase + OFF_INIT_FAKE);
            double oldSpeed = BitConverter.Int64BitsToDouble(_session.ReadInt64(caveBase + OFF_SPEED));

            QueryPerformanceCounter(out long nowReal);

            // 2. 计算当前平滑累积的虚拟时钟值
            long deltaReal = nowReal - oldInitReal;
            double deltaFake = (double)deltaReal * oldSpeed;
            long nowFake = oldInitFake + (long)deltaFake;

            // 3. 将 (nowReal, nowFake, newSpeed) 更新为新的时钟基准
            _session.WriteBytes(caveBase + OFF_INIT_REAL, BitConverter.GetBytes(nowReal));
            _session.WriteBytes(caveBase + OFF_INIT_FAKE, BitConverter.GetBytes(nowFake));
            _session.WriteBytes(caveBase + OFF_SPEED, BitConverter.GetBytes((double)newSpeed));

            CurrentSpeed = newSpeed;
            return true;
        }

        /// <summary>
        /// 干净卸载 Hook，将原版机器码写回 QPC，释放所有分配内存。
        /// </summary>
        public bool Uninstall()
        {
            if (!_installed) return true;

            bool ok = true;
            if (_pQpcTarget != IntPtr.Zero && _origBytes.Length == 16)
            {
                ok = WriteProtectedMemory(_pQpcTarget, _origBytes);
                FlushInstructionCache(_session.ProcessHandle, _pQpcTarget, (IntPtr)16);
            }

            Cleanup();
            _installed = false;
            CurrentSpeed = 1.0f;
            return ok;
        }

        private byte[] BuildHookCode(long caveBase, long trampAddr)
        {
            var code = new List<byte>();

            // sub rsp, 28h             ; 为 Trampoline 预留 32 字节 shadow space + 8 字节对齐
            code.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 });

            // mov [rsp+30h], rcx       ; 保存入参 lpPerformanceCount 指针
            code.AddRange(new byte[] { 0x48, 0x89, 0x4C, 0x24, 0x30 });

            // mov rax, trampAddr       ; 调用原版 Trampoline
            code.AddRange(new byte[] { 0x48, 0xB8 });
            code.AddRange(BitConverter.GetBytes(trampAddr));
            // call rax
            code.AddRange(new byte[] { 0xFF, 0xD0 });

            // test eax, eax            ; 检查原版函数返回值
            // jz .exit_fail
            code.AddRange(new byte[] { 0x85, 0xC0 });
            code.AddRange(new byte[] { 0x0F, 0x84 }); // 32-bit rel jump
            int jzOffsetPos = code.Count;
            code.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // 占位

            // mov rcx, [rsp+30h]       ; 恢复 rcx
            code.AddRange(new byte[] { 0x48, 0x8B, 0x4C, 0x24, 0x30 });

            // test rcx, rcx            ; 指针为空直接成功退出
            // jz .exit_ok
            code.AddRange(new byte[] { 0x48, 0x85, 0xC9 });
            code.AddRange(new byte[] { 0x0F, 0x84 });
            int jzNullPos = code.Count;
            code.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // 占位

            // mov rdx, [rcx]           ; rdx = currentReal (原版 QPC 写入的时间)
            code.AddRange(new byte[] { 0x48, 0x8B, 0x11 });

            // mov r10, caveBase        ; r10 = 数据区基址 (volatile register)
            code.AddRange(new byte[] { 0x49, 0xBA });
            code.AddRange(BitConverter.GetBytes(caveBase));

            // mov rax, rdx
            code.AddRange(new byte[] { 0x48, 0x89, 0xD0 });
            // sub rax, [r10 + OFF_INIT_REAL]   ; rax = deltaReal = currentReal - initialReal
            code.AddRange(new byte[] { 0x49, 0x2B, 0x42, (byte)OFF_INIT_REAL });

            // cvtsi2sd xmm0, rax       ; xmm0 = (double)deltaReal
            code.AddRange(new byte[] { 0xF2, 0x48, 0x0F, 0x2A, 0xC0 });

            // mulsd xmm0, [r10 + OFF_SPEED]    ; xmm0 = deltaReal * speedMultiplier
            code.AddRange(new byte[] { 0xF2, 0x41, 0x0F, 0x59, 0x42, (byte)OFF_SPEED });

            // cvttsd2si rax, xmm0      ; rax = (int64)deltaFake
            code.AddRange(new byte[] { 0xF2, 0x48, 0x0F, 0x2C, 0xC0 });

            // add rax, [r10 + OFF_INIT_FAKE]   ; fakeTime = initialFake + deltaFake
            code.AddRange(new byte[] { 0x49, 0x03, 0x42, (byte)OFF_INIT_FAKE });

            // mov [rcx], rax           ; 将计算出的虚拟时间写回游戏提供的指针！
            code.AddRange(new byte[] { 0x48, 0x89, 0x01 });

            // ── .exit_ok ──
            int okTargetPos = code.Count;
            // 修复 jzNullPos 的相对偏移
            int okRel = okTargetPos - (jzNullPos + 4);
            Buffer.BlockCopy(BitConverter.GetBytes(okRel), 0, code.ToArray(), jzNullPos, 4);
            code[jzNullPos + 0] = (byte)(okRel & 0xFF);
            code[jzNullPos + 1] = (byte)((okRel >> 8) & 0xFF);
            code[jzNullPos + 2] = (byte)((okRel >> 16) & 0xFF);
            code[jzNullPos + 3] = (byte)((okRel >> 24) & 0xFF);

            // mov eax, 1               ; return TRUE
            code.AddRange(new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00 });
            // add rsp, 28h
            code.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });
            // ret
            code.Add(0xC3);

            // ── .exit_fail ──
            int failTargetPos = code.Count;
            int failRel = failTargetPos - (jzOffsetPos + 4);
            code[jzOffsetPos + 0] = (byte)(failRel & 0xFF);
            code[jzOffsetPos + 1] = (byte)((failRel >> 8) & 0xFF);
            code[jzOffsetPos + 2] = (byte)((failRel >> 16) & 0xFF);
            code[jzOffsetPos + 3] = (byte)((failRel >> 24) & 0xFF);

            // xor eax, eax             ; return FALSE
            code.AddRange(new byte[] { 0x31, 0xC0 });
            // add rsp, 28h
            code.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });
            // ret
            code.Add(0xC3);

            return code.ToArray();
        }

        private bool WriteProtectedMemory(IntPtr targetAddr, byte[] data)
        {
            if (!VirtualProtectEx(_session.ProcessHandle, targetAddr, (IntPtr)data.Length, Win32Api.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                return false;
            }

            bool written = Win32Api.WriteProcessMemory(_session.ProcessHandle, targetAddr, data, (IntPtr)data.Length, out _);
            VirtualProtectEx(_session.ProcessHandle, targetAddr, (IntPtr)data.Length, oldProtect, out _);
            return written;
        }

        private void Cleanup()
        {
            if (_caveMem != IntPtr.Zero && _session.IsAttached)
            {
                Win32Api.VirtualFreeEx(_session.ProcessHandle, _caveMem, IntPtr.Zero, Win32Api.MEM_RELEASE);
                _caveMem = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Uninstall();
        }
    }
}
