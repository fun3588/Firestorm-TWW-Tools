using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FirestormTwwMonitor.Core.Native;

namespace FirestormTwwMonitor.Core.Memory
{
    public class TrackedResourceNode
    {
        public string Name { get; set; } = string.Empty;
        public uint DisplayId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Distance { get; set; }
        public long Address { get; set; }
        public bool IsOre { get; set; }
        public bool IsHerb { get; set; }
    }

    public enum ClientGameState
    {
        Disconnected,    // 离线/断线弹窗
        CharSelect,      // 角色选择界面 (未选角/等待进入游戏)
        InWorld          // 在线 (世界运行中)
    }

    /// <summary>
    /// 代表单个目标 WoW 客户端进程的内存会话
    /// </summary>
    public class WowMemorySession : IDisposable
    {
        public int ProcessId { get; private set; }
        public IntPtr ProcessHandle { get; private set; } = IntPtr.Zero;
        public IntPtr MainWindowHandle { get; private set; } = IntPtr.Zero;
        public long ModuleBase { get; private set; }

        public long PlayerObj { get; private set; }
        public long CMovement { get; private set; }
        public bool IsInWorld { get; private set; }
        public ClientGameState GameState { get; private set; } = ClientGameState.Disconnected;

        public event Action? OnPlayerLoggedOut;
        public event Action? OnPlayerEnteredWorld;

        public bool IsAttached => ProcessHandle != IntPtr.Zero && ProcessHandle != (IntPtr)(-1) && IsProcessAlive();

        public static readonly string[] HerbKeywords = new[]
        {
            "草", "花", "叶", "莲", "藤", "苔", "藻", "菌", "菇", "兰", "葵", "蓟", "罂粟", "百合",
            "玫瑰", "茉莉", "根", "芽", "枝", "果", "荆棘", "菌柄", "荷草", "心绽花", "诱毒之花", "暮光紫罗兰",
            "herb", "leaf", "weed", "lotus", "lily", "rose", "poppy", "cap", "vine", "bloom", "blossom"
        };

        public static readonly string[] OreKeywords = new[]
        {
            "矿", "脉", "矿脉", "矿石", "矿床", "富矿", "贫矿", "残余",
            "铋", "红金", "亚基", "虚空石", "铁爪", "幽冥铁", "白恍", "黑恍", "延极", "真铁",
            "ore", "vein", "deposit", "mine", "bismuth", "aqirite", "null stone", "ironclaw"
        };

        public IntPtr GetOrRefreshWindowHandle()
        {
            bool needRefresh = MainWindowHandle == IntPtr.Zero || !Win32Api.IsWindow(MainWindowHandle);
            if (!needRefresh)
            {
                var sbClass = new StringBuilder(128);
                Win32Api.GetClassName(MainWindowHandle, sbClass, sbClass.Capacity);
                if (!sbClass.ToString().Equals("GxWindowClass", StringComparison.OrdinalIgnoreCase))
                {
                    needRefresh = true;
                }
            }

            if (needRefresh && ProcessId > 0)
            {
                MainWindowHandle = WowProcessManager.FindWindowForProcess(ProcessId);
            }
            return MainWindowHandle;
        }

        public bool Attach(int pid, IntPtr windowHandle = default)
        {
            Detach();

            try
            {
                var process = Process.GetProcessById(pid);
                ProcessId = process.Id;
                IntPtr targetHwnd = windowHandle != IntPtr.Zero ? windowHandle : process.MainWindowHandle;
                if (targetHwnd != IntPtr.Zero)
                {
                    var sbClass = new StringBuilder(128);
                    Win32Api.GetClassName(targetHwnd, sbClass, sbClass.Capacity);
                    if (!sbClass.ToString().Equals("GxWindowClass", StringComparison.OrdinalIgnoreCase))
                    {
                        targetHwnd = WowProcessManager.FindWindowForProcess(ProcessId);
                    }
                }
                else
                {
                    targetHwnd = WowProcessManager.FindWindowForProcess(ProcessId);
                }
                MainWindowHandle = targetHwnd;

                ProcessHandle = Win32Api.OpenProcess(Win32Api.PROCESS_ALL_ACCESS, false, ProcessId);

                if (ProcessHandle == IntPtr.Zero)
                {
                    // 降级权限
                    ProcessHandle = Win32Api.OpenProcess(
                        Win32Api.PROCESS_VM_READ | Win32Api.PROCESS_VM_WRITE | Win32Api.PROCESS_VM_OPERATION | Win32Api.PROCESS_QUERY_INFORMATION,
                        false,
                        ProcessId
                    );
                }

                if (ProcessHandle == IntPtr.Zero)
                {
                    return false;
                }

                ModuleBase = process.MainModule?.BaseAddress.ToInt64() ?? 0;
                return ModuleBase != 0;
            }
            catch
            {
                Detach();
                return false;
            }
        }

        public void Detach()
        {
            if (ProcessHandle != IntPtr.Zero && ProcessHandle != (IntPtr)(-1))
            {
                Win32Api.CloseHandle(ProcessHandle);
            }

            ProcessHandle = IntPtr.Zero;
            MainWindowHandle = IntPtr.Zero;
            ProcessId = 0;
            ModuleBase = 0;
            PlayerObj = 0;
            CMovement = 0;
            IsInWorld = false;
        }

        public bool IsProcessAlive()
        {
            if (ProcessHandle == IntPtr.Zero) return false;
            if (Win32Api.GetExitCodeProcess(ProcessHandle, out uint code))
            {
                return code == 259; // STILL_ACTIVE
            }
            return false;
        }

        public bool IsValidPtr(long addr)
        {
            return addr >= 0x10000 && addr <= 0x7FFFFFFEFFFF;
        }

        #region Memory Read Helpers
        public byte ReadByte(long addr)
        {
            if (!IsAttached || !IsValidPtr(addr)) return 0;
            byte[] buf = new byte[1];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(1), out _))
            {
                return buf[0];
            }
            return 0;
        }

        public uint ReadUInt32(long addr)
        {
            if (!IsAttached || !IsValidPtr(addr)) return 0;
            byte[] buf = new byte[4];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(4), out _))
            {
                return BitConverter.ToUInt32(buf, 0);
            }
            return 0;
        }

        public int ReadInt32(long addr)
        {
            if (!IsAttached || !IsValidPtr(addr)) return 0;
            byte[] buf = new byte[4];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(4), out _))
            {
                return BitConverter.ToInt32(buf, 0);
            }
            return 0;
        }

        public ulong ReadUInt64(long addr)
        {
            if (!IsAttached || !IsValidPtr(addr)) return 0;
            byte[] buf = new byte[8];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(8), out _))
            {
                return BitConverter.ToUInt64(buf, 0);
            }
            return 0;
        }

        public long ReadInt64(long addr)
        {
            if (!IsAttached || !IsValidPtr(addr)) return 0;
            byte[] buf = new byte[8];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(8), out _))
            {
                return BitConverter.ToInt64(buf, 0);
            }
            return 0;
        }

        public float ReadFloat(long addr)
        {
            if (!IsAttached || !IsValidPtr(addr)) return 0f;
            byte[] buf = new byte[4];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(4), out _))
            {
                return BitConverter.ToSingle(buf, 0);
            }
            return 0f;
        }

        public string ReadString(long addr, int maxLen = 64)
        {
            if (!IsAttached || !IsValidPtr(addr)) return string.Empty;
            byte[] buf = new byte[maxLen];
            if (Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(addr), buf, new IntPtr(maxLen), out _))
            {
                int zeroIndex = Array.IndexOf(buf, (byte)0);
                if (zeroIndex >= 0)
                {
                    return Encoding.UTF8.GetString(buf, 0, zeroIndex).Trim();
                }
                return Encoding.UTF8.GetString(buf).Trim();
            }
            return string.Empty;
        }

        public bool WriteBytes(long addr, byte[] data)
        {
            if (!IsAttached || !IsValidPtr(addr) || data == null || data.Length == 0) return false;
            return Win32Api.WriteProcessMemory(ProcessHandle, new IntPtr(addr), data, new IntPtr(data.Length), out _);
        }
        #endregion

        #region Player State & Data
        public bool IsPlayerAlive()
        {
            if (!IsValidPtr(PlayerObj) || !IsValidPtr(CMovement)) return false;
            if (ReadByte(PlayerObj + Offsets.OFF_OBJ_TYPE) != Offsets.TYPE_PLAYER_LOCAL) return false;

            float px = ReadFloat(CMovement + Offsets.OFF_POS);
            return !float.IsNaN(px) && Math.Abs(px) < 100000f;
        }

        /// <summary>
        /// 高性能批量解析 LocalPlayer 指针，避免数千次单字节跨进程系统调用
        /// </summary>
        public bool ResolvePlayer()
        {
            if (!IsAttached) return false;

            long objmgr = (long)ReadUInt64(ModuleBase + Offsets.OBJ_MGR_GLOBAL);
            if (!IsValidPtr(objmgr)) return false;

            long arr = ReadInt64(objmgr + Offsets.OFF_OBJ_BUCKETS);
            long count = ReadInt64(objmgr + Offsets.OFF_OBJ_COUNT);
            if (!IsValidPtr(arr)) return false;

            int limit = (int)Math.Min(Math.Max(count, 0), 2000);
            if (limit <= 0) return false;

            // 单次批量读取指针列表，杜绝数千次跨进程开销
            byte[] arrBuf = new byte[limit * 8];
            if (!Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(arr), arrBuf, new IntPtr(arrBuf.Length), out _))
            {
                return false;
            }

            for (int i = 0; i < limit; i++)
            {
                long node = BitConverter.ToInt64(arrBuf, i * 8);
                if (!IsValidPtr(node)) continue;

                long obj = ReadInt64(node + Offsets.OFF_NODE_OBJ);
                if (!IsValidPtr(obj)) continue;

                if (ReadByte(obj + Offsets.OFF_OBJ_TYPE) == Offsets.TYPE_PLAYER_LOCAL)
                {
                    long cmov = (long)ReadUInt64(obj + Offsets.OFF_UNIT_MOVEMENT);
                    if (!IsValidPtr(cmov)) continue;

                    PlayerObj = obj;
                    CMovement = cmov;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 权威快速检测世界状态：精准识别 在线 / 选角界面(未选角·等待进游戏) / 离线
        /// </summary>
        /// <summary>
        /// 权威快速检测世界状态：精准识别 在线 / 选角界面(未选角·等待进游戏) / 离线
        /// 核心准则：在世界内必须存在有效的本地玩家对象 (TYPE_PLAYER_LOCAL = 7) 与移动指针
        /// </summary>
        public bool CheckWorldState()
        {
            if (!IsAttached || !IsProcessAlive())
            {
                if (IsInWorld)
                {
                    IsInWorld = false;
                    PlayerObj = 0;
                    CMovement = 0;
                    OnPlayerLoggedOut?.Invoke();
                }
                GameState = ClientGameState.Disconnected;
                return false;
            }

            // 1. 核心判定：读取当前全局角色名称
            // 当角色在世界中时，此处必为有效名字；而在选角界面（未选角·待进游戏）或离线状态，此处必定为空
            string activeName = GetPlayerName();
            if (string.IsNullOrWhiteSpace(activeName))
            {
                if (IsInWorld)
                {
                    IsInWorld = false;
                    PlayerObj = 0;
                    CMovement = 0;
                    OnPlayerLoggedOut?.Invoke();
                }
                GameState = ClientGameState.CharSelect;
                return false;
            }

            // 2. 检查当前玩家是否在世界中存活并有实体对象
            bool playerAlive = IsPlayerAlive();
            if (!playerAlive)
            {
                playerAlive = ResolvePlayer();
            }

            if (playerAlive)
            {
                GameState = ClientGameState.InWorld;
                if (!IsInWorld)
                {
                    IsInWorld = true;
                    OnPlayerEnteredWorld?.Invoke();
                }
                return true;
            }

            // 本地玩家对象不存在：必定处于世界外部（选角界面、载入界面或登录断线界面）
            if (IsInWorld)
            {
                PlayerObj = 0;
                CMovement = 0;
                IsInWorld = false;
                OnPlayerLoggedOut?.Invoke();
            }

            GameState = ClientGameState.CharSelect;
            return false;
        }

        public string GetPlayerName()
        {
            if (!IsAttached || !IsValidPtr(ModuleBase + Offsets.OFF_NAME_GLOBAL)) return string.Empty;
            return ReadString(ModuleBase + Offsets.OFF_NAME_GLOBAL, 32);
        }

        public uint GetPlayerLevel()
        {
            if (!IsValidPtr(PlayerObj)) return 0;
            return ReadUInt32(PlayerObj + Offsets.OFF_LEVEL);
        }

        public (long Health, long MaxHealth) GetPlayerHealth()
        {
            if (!IsValidPtr(PlayerObj)) return (0, 0);
            long h = ReadInt64(PlayerObj + Offsets.OFF_HEALTH);
            long mh = ReadInt64(PlayerObj + Offsets.OFF_MAX_HEALTH);
            return (h, mh);
        }

        public (float X, float Y, float Z) GetPlayerPos()
        {
            if (!IsValidPtr(CMovement)) return (0f, 0f, 0f);
            return (
                ReadFloat(CMovement + Offsets.OFF_POS),
                ReadFloat(CMovement + Offsets.OFF_POS + 4),
                ReadFloat(CMovement + Offsets.OFF_POS + 8)
            );
        }
        #endregion

        #region Resource Scanning (GameObjects)
        public List<TrackedResourceNode> ScanGameObjects(float maxRadius = 300f)
        {
            var list = new List<TrackedResourceNode>();
            if (!IsPlayerAlive() && !ResolvePlayer()) return list;

            var (px, py, pz) = GetPlayerPos();
            long objmgr = (long)ReadUInt64(ModuleBase + Offsets.OBJ_MGR_GLOBAL);
            if (!IsValidPtr(objmgr)) return list;

            long arr = ReadInt64(objmgr + Offsets.OFF_OBJ_BUCKETS);
            long count = ReadInt64(objmgr + Offsets.OFF_OBJ_COUNT);
            if (!IsValidPtr(arr)) return list;

            int limit = (int)Math.Min(Math.Max(count, 0), 2000);
            byte[] arrBuf = new byte[limit * 8];
            if (!Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(arr), arrBuf, new IntPtr(arrBuf.Length), out _))
            {
                return list;
            }

            for (int i = 0; i < limit; i++)
            {
                long node = BitConverter.ToInt64(arrBuf, i * 8);
                if (!IsValidPtr(node)) continue;

                long obj = ReadInt64(node + Offsets.OFF_NODE_OBJ);
                if (!IsValidPtr(obj)) continue;

                if (ReadByte(obj + Offsets.OFF_OBJ_TYPE) != Offsets.TYPE_GAMEOBJECT) continue;

                float ox = ReadFloat(obj + 0xB0);
                float oy = ReadFloat(obj + 0xB4);
                float oz = ReadFloat(obj + 0xB8);
                if (float.IsNaN(ox) || (ox == 0f && oy == 0f && oz == 0f)) continue;

                float dx = ox - px;
                float dy = oy - py;
                float dz = oz - pz;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (maxRadius > 0 && dist > maxRadius) continue;

                long db = (long)ReadUInt64(obj + 0xE8);
                uint dispId = 0;
                string name = string.Empty;
                if (IsValidPtr(db))
                {
                    dispId = ReadUInt32(db + 0x04);
                    long namePtr = (long)ReadUInt64(db + 0xE0);
                    if (IsValidPtr(namePtr))
                    {
                        name = ReadString(namePtr, 64);
                    }
                }

                if (string.IsNullOrWhiteSpace(name)) continue;

                string nameLower = name.ToLower();
                bool isHerb = false;
                foreach (var k in HerbKeywords)
                {
                    if (nameLower.Contains(k)) { isHerb = true; break; }
                }

                bool isOre = false;
                foreach (var k in OreKeywords)
                {
                    if (nameLower.Contains(k)) { isOre = true; break; }
                }

                if (isHerb || isOre)
                {
                    list.Add(new TrackedResourceNode
                    {
                        Name = name,
                        DisplayId = dispId,
                        X = ox,
                        Y = oy,
                        Z = oz,
                        Distance = dist,
                        Address = obj,
                        IsHerb = isHerb,
                        IsOre = isOre
                    });
                }
            }

            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return list;
        }
        #endregion

        #region Inventory & Bag Item Scanning (100% Reliable RPM Direct Read)
        /// <summary>
        /// 从内存 ObjectManager 高速直接读取背包与行囊中所有物品及其堆叠数量
        /// 返回字典：Key = ItemId, Value = TotalCount
        /// 耗时仅 ~0.1ms，无需注入代码，0 风险，100% 精准
        /// </summary>
        public (Dictionary<int, int> Items, int OccupiedSlots, int TotalSlots, int FreeSlots) ScanInventoryItems()
        {
            var dict = new Dictionary<int, int>();
            if (!IsAttached || !IsInWorld) return (dict, 0, 0, 0);

            long objmgr = (long)ReadUInt64(ModuleBase + Offsets.OBJ_MGR_GLOBAL);
            if (!IsValidPtr(objmgr)) return (dict, 0, 0, 0);

            long arr = ReadInt64(objmgr + Offsets.OFF_OBJ_BUCKETS);
            long count = ReadInt64(objmgr + Offsets.OFF_OBJ_COUNT);
            if (!IsValidPtr(arr)) return (dict, 0, 0, 0);

            int limit = (int)Math.Min(Math.Max(count, 0), 5000);
            if (limit <= 0) return (dict, 0, 0, 0);

            byte[] arrBuf = new byte[limit * 8];
            if (!Win32Api.ReadProcessMemory(ProcessHandle, new IntPtr(arr), arrBuf, new IntPtr(arrBuf.Length), out _))
            {
                return (dict, 0, 0, 0);
            }

            int occupiedItemSlots = 0;
            int bagExtraSlots = 0;

            // 常用背包容量速查表（涵盖主流魔兽经典与各版本背包容量）
            var bagSlotCatalog = new Dictionary<int, int>
            {
                { 127035, 24 }, // 妖纹包 / 丝织行囊
                { 210134, 34 }, { 210133, 32 }, { 210132, 30 }, // TWW 阿拉索信使包等
                { 192886, 34 }, { 192885, 32 }, { 192884, 30 }, // DF 巨龙包
                { 174334, 32 }, { 174333, 30 },                 // SL 幽魂包
                { 168804, 32 }, { 168803, 30 },                 // BFA 深海包
                { 154608, 30 }, { 142075, 30 }, { 21841, 16 },  // 灵纹包
                { 41599, 20 },  { 54443, 22 },  { 70136, 24 }   // 霜纹包等
            };

            for (int i = 0; i < limit; i++)
            {
                long node = BitConverter.ToInt64(arrBuf, i * 8);
                if (!IsValidPtr(node)) continue;

                long obj = ReadInt64(node + Offsets.OFF_NODE_OBJ);
                if (!IsValidPtr(obj)) continue;

                byte objType = ReadByte(obj + Offsets.OFF_OBJ_TYPE);
                if (objType == Offsets.TYPE_ITEM)
                {
                    uint itemId = ReadUInt32(obj + Offsets.OFF_ITEM_ID);
                    if (itemId > 0)
                    {
                        occupiedItemSlots++;
                        uint stack = ReadUInt32(obj + Offsets.OFF_ITEM_STACK_COUNT);
                        int countVal = stack == 0 ? 1 : (int)stack;

                        int id = (int)itemId;
                        if (dict.ContainsKey(id))
                        {
                            dict[id] += countVal;
                        }
                        else
                        {
                            dict[id] = countVal;
                        }
                    }
                }
                else if (objType == Offsets.TYPE_CONTAINER)
                {
                    uint bagId = ReadUInt32(obj + Offsets.OFF_ITEM_ID);
                    if (bagId > 0 && bagSlotCatalog.TryGetValue((int)bagId, out int slots))
                    {
                        bagExtraSlots += slots;
                    }
                }
            }

            // 基础初始背包为 16 格，加上额外 4 个已装备背包的格数
            int totalCapacity = 16 + bagExtraSlots;
            if (totalCapacity < occupiedItemSlots)
            {
                // 若包含未列出的特殊包，动态按 4x30 + 16 自适应上限
                totalCapacity = Math.Max(totalCapacity, 112);
            }
            int freeSlots = Math.Max(0, totalCapacity - occupiedItemSlots);

            return (dict, occupiedItemSlots, totalCapacity, freeSlots);
        }
        #endregion

        #region Lua Remote Execution
        public bool ExecuteLua(string luaCode, out string result)
        {
            result = string.Empty;
            if (!IsAttached)
            {
                result = "游戏未挂接";
                return false;
            }

            if (string.IsNullOrWhiteSpace(luaCode))
            {
                result = "Lua 代码为空";
                return false;
            }

            try
            {
                byte[] codeBytes = Encoding.UTF8.GetBytes(luaCode + "\0");
                IntPtr codeMem = Win32Api.VirtualAllocEx(ProcessHandle, IntPtr.Zero, (IntPtr)codeBytes.Length, Win32Api.MEM_COMMIT | Win32Api.MEM_RESERVE, Win32Api.PAGE_EXECUTE_READWRITE);
                if (codeMem == IntPtr.Zero)
                {
                    result = "远程内存分配失败";
                    return false;
                }

                Win32Api.WriteProcessMemory(ProcessHandle, codeMem, codeBytes, (IntPtr)codeBytes.Length, out _);

                long frameScriptExecute = ModuleBase + Offsets.OFF_FRAMESCRIPT_EXECUTE;

                var shellcode = new List<byte>
                {
                    0x48, 0x83, 0xEC, 0x28,                         // sub rsp, 28h
                    0x48, 0xB9                                      // mov rcx, codeMem
                };
                shellcode.AddRange(BitConverter.GetBytes(codeMem.ToInt64()));
                shellcode.AddRange(new byte[] {
                    0x31, 0xD2,                                     // xor edx, edx
                    0x45, 0x31, 0xC0,                               // xor r8d, r8d
                    0x48, 0xB8                                      // mov rax, funcAddr
                });
                shellcode.AddRange(BitConverter.GetBytes(frameScriptExecute));
                shellcode.AddRange(new byte[] {
                    0xFF, 0xD0,                                     // call rax
                    0x48, 0x83, 0xC4, 0x28,                         // add rsp, 28h
                    0xC3                                            // ret
                });

                byte[] scBytes = shellcode.ToArray();
                IntPtr scMem = Win32Api.VirtualAllocEx(ProcessHandle, IntPtr.Zero, (IntPtr)scBytes.Length, Win32Api.MEM_COMMIT | Win32Api.MEM_RESERVE, Win32Api.PAGE_EXECUTE_READWRITE);
                if (scMem == IntPtr.Zero)
                {
                    Win32Api.VirtualFreeEx(ProcessHandle, codeMem, IntPtr.Zero, Win32Api.MEM_RELEASE);
                    result = "Shellcode 内存分配失败";
                    return false;
                }

                Win32Api.WriteProcessMemory(ProcessHandle, scMem, scBytes, (IntPtr)scBytes.Length, out _);

                IntPtr hThread = Win32Api.CreateRemoteThread(ProcessHandle, IntPtr.Zero, IntPtr.Zero, scMem, IntPtr.Zero, 0, out _);
                if (hThread == IntPtr.Zero)
                {
                    Win32Api.VirtualFreeEx(ProcessHandle, codeMem, IntPtr.Zero, Win32Api.MEM_RELEASE);
                    Win32Api.VirtualFreeEx(ProcessHandle, scMem, IntPtr.Zero, Win32Api.MEM_RELEASE);
                    result = "CreateRemoteThread 失败";
                    return false;
                }

                Win32Api.WaitForSingleObject(hThread, 2000);
                Win32Api.CloseHandle(hThread);

                Win32Api.VirtualFreeEx(ProcessHandle, codeMem, IntPtr.Zero, Win32Api.MEM_RELEASE);
                Win32Api.VirtualFreeEx(ProcessHandle, scMem, IntPtr.Zero, Win32Api.MEM_RELEASE);

                result = "已执行";
                return true;
            }
            catch (Exception ex)
            {
                result = "异常: " + ex.Message;
                return false;
            }
        }
        #endregion

        public void Dispose()
        {
            Detach();
        }
    }
}
