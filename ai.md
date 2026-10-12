# Firestorm-TWW-Tools 交接

> 本文件是给 AI 的工作交接文档，遵循 [aimd 规范](https://ai.drx.ac.cn/aimd)，请先阅读此文档再继续对本仓库进行操作。

---

## 一、还没做完的事 & 动手前注意

### 当前状态
- [x] **C# WinForms 完整版 (`FirestormTwwMonitor.exe`)**：已完成纯后台自动重连（选角界面全局角色名判空识别 + `WM_ACTIVATE` 唤醒 + 扫描码回车 + 选角底部按钮相对点击双保险）。
- [x] **C++ 原生极速版 (`build_cpp/bin/FirestormTwwMonitor_Cpp.exe`)**：已完成超轻量 Win32 GUI（专用内存 ~5.5MB，CPU < 0.05%），支持严格窗口标题匹配、最多 8 个客户端、底部 10 秒默认可调重连间隔、小/中/大三档字号切换（默认【中】）、仅显示矿石与背包空位，并彻底消除了互斥锁死锁与窗口阻塞卡死问题。

### 动手前注意事项（极其重要）
1. **C++ 版绝对禁止在主 UI 线程使用非递归锁嵌套或同步跨进程窗口调用**：
   - **禁止使用 `std::mutex` 跨 UI 刷新函数加锁**：此前在按钮事件（如“全部开启”）中持有 `std::mutex` 并直接调用 `RefreshListView()`（或触发 `LVN_ITEMCHANGED` 通知），会导致同一线程二次加锁引发永久死锁（Window Not Responding）。当前 `MainWindow.cpp` 与 `ReconnectController.h` 已重构为**纯单线程无锁状态机 (`Tick(bool inWorld)`)**，所有状态机推进均在主线程 1 秒定时器中顺畅完成（耗时 < 0.2ms），请保持无锁单线程架构。
   - **禁止调用同步的 `ShowWindow(hWnd, ...)` 或 `SendMessage(hWnd, ...)`**：当某个魔兽客户端正在退出、闪退或无响应时，同步窗口 API 会无限期挂起调用方线程。必须使用异步非阻塞的 **`ShowWindowAsync(hWnd, SW_SHOWNOACTIVATE)`** 与 **`PostMessageW`**，且包含 `Sleep` 的按键序列必须通过 **`BackgroundInput::SendReconnectActionAsync`** 在分离的后台轻量线程中执行。
2. **选角界面（待进游戏）的权威判定准则**：
   - 严禁仅凭 `PlayerObj != 0` 或 `IsPlayerAlive()` 判断角色是否在线！当角色小退回到选角界面时，旧的 `PlayerObj` 内存块经常残留在堆中未被清零。
   - 必须以 **`ReadGlobalPlayerName()` (`BaseAddress + 0x47F98B0`) 非空** 作为进入世界的必要条件：在角色选择界面、加载界面或断线界面，全局名字必定为空，此时 100% 识别为 `⏳ 待进游戏(未在线)` 并触发后台重连。
3. **窗口标题严格匹配与数量上限**：
   - 扫描客户端窗口时，窗口标题必须**精确匹配** `"魔兽世界"` 或 `"World of Warcraft"`（英文忽略大小写），或窗口类名为 `GxWindowClass`，防止误把浏览器、记事本或监控器自身当作游戏窗口。
   - 列表最多纳管 **8 个** 客户端窗口 (`MAX_CLIENT_COUNT = 8`)。
4. **C++ 版特定 UI 约束**：
   - 字体档位为 **【小】(15px) / 【中】(18px) / 【大】(21px)**，程序启动时**默认必须为【中】号字体**。
   - 采集列只显示**矿石**与**背包剩余空位**（列名 `矿石与空位`，时速列名 `采矿时速`），不显示草药。
   - 重连间隔默认 **10 秒**，放置在**底部卡片右侧**，支持用户随时输入秒数并点击【应用】生效。

---

## 二、各子系统定位与核心架构速查

| 模块目录 / 文件 | 语言 / 依赖 | 核心定位与技术职责 |
|---|---|---|
| `cpp_src/include/Offsets.h` | C++17 Header | TWW 11.2.5.63704 专用权威内存偏移常量（ObjectMgr、全局角色名、账号、背包、坐标等） |
| `cpp_src/include/BackgroundInput.h` | C++17 / Win32 | 纯后台输入模拟：`ShowWindowAsync` 异步还原 + `WM_ACTIVATE` 唤醒 + 硬件扫描码回车 + `(50%, 92%)` 底部按钮异步点击 (`SendReconnectActionAsync`) |
| `cpp_src/include/WowMemorySession.h` | C++17 / Win32 | 单客户端内存直读会话：基于 `ReadProcessMemory` 读取全局角色名、等级、生命值、三维坐标、背包 0~4 物品 ID/数量及剩余空位数 |
| `cpp_src/include/WowProcessManager.h` | C++17 / Win32 | 进程与窗口发现器：基于 `CreateToolhelp32Snapshot` 与 `EnumWindows` 严格匹配标题为 `"魔兽世界"` / `"World of Warcraft"` 的客户端（最多 8 个） |
| `cpp_src/include/GatheringTracker.h` | C++17 Header | 采矿追踪器：内置 TWW / MoP / DF / SL 矿石 ID 表，过滤草药，输出 `[空位:X] 矿石名:数量` 与每小时采矿速率 (`+X (Y/h)`) |
| `cpp_src/include/ReconnectController.h` | C++17 Header | 无锁单线程非阻塞重连状态机：由主窗口定时器每秒调用 `Tick(bool inWorld)`，按可配置间隔（默认 10s）触发异步后台重连 |
| `cpp_src/src/MainWindow.cpp` | C++17 / Win32 GUI | 原生 Win32 界面主控：顶部操作与小/中/大字号栏、中部双缓冲防闪烁 ListView（通过 1x1 ImageList 动态控制行高）、底部详情与重连间隔设置卡片 |
| `Core/` & `UI/` | C# (.NET 8 WinForms) | C# 版对应实现：包含 `WowMemorySession.cs`、`BackgroundInput.cs`、`ReconnectController.cs`、`GatheringTracker.cs` 及 `MainForm.cs` |

---

## 三、历史记录（我们做了什么、怎么做的）

- **2026-10-12（阶段 1：修复选角界面不触发后台回车 & 纯后台双保险重连）**：
  - 定位并修复了角色退回选角界面时因堆内存残留旧 `PlayerObj` 导致误判为“在线”的问题，改用全局角色名 `0x47F98B0` 判空权威识别 `待进游戏(未在线)`。
  - 移除前台置顶方案，统一使用纯后台 `WM_ACTIVATE(WA_ACTIVE)` + `WM_SETFOCUS` + 扫描码回车 + 选角界面底部中央 (`50%, 92%`) 相对坐标点击双保险。
- **2026-10-13（阶段 2：交付超低资源占用的 C++ 原生 Win32 GUI 极速版）**：
  - 使用 CMake + MinGW-w64 静态链接构建 `FirestormTwwMonitor_Cpp.exe`，无需任何运行时依赖，单文件体积 ~220KB，实测专用私有内存仅 ~5.5MB，CPU 占用 < 0.05%。
- **2026-10-13（阶段 3：严格标题过滤、8 窗口紧凑布局、底部 10s 间隔配置与小/中/大字号切换）**：
  - 窗口枚举对齐 C# 严格标题规则（仅限 `"魔兽世界"` 或 `"World of Warcraft"`），上限设为 8 个窗口。
  - 将重连间隔设置移至底部卡片右侧（默认 10 秒，可自由修改应用）。
  - 实现【小 / 中 / 大】三档字号实时切换，启动默认【中】号字（18px，行高 30px）。
- **2026-10-13（阶段 4：仅显示矿石与背包空位 & 根除 GUI 死锁/未响应）**：
  - 调整 `GatheringTracker.h` 仅统计矿石并附带背包剩余空位格数。
  - 重构 `ReconnectController.h` 为单线程非阻塞状态机 `Tick`，移除 `g_contextMutex` 递归加锁死锁，并在 `BackgroundInput.h` 中引入 `ShowWindowAsync` 与 `SendReconnectActionAsync`，彻底解决点击“全部开启”或客户端退出/重连时 GUI 卡死的问题。

---

## 四、关键内存偏移速查 (WoW 11.2.5.63704)

| 名称 | 偏移量 / 常量 | 说明 |
|---|---|---|
| `OFF_VISIBLE_OBJECTS` | `0x4AD2090` | ObjectMgr 基址指针 |
| `OFF_NAME_GLOBAL` | `0x47F98B0` | 全局当前登录角色名（UTF-8 字符串，选角/断线界面为空） |
| `OFF_ACCOUNT_PRIMARY` | `0x4679A80` | 主账号名偏移 |
| `OFF_UNIT_POSITION` | `0x150` | 单位三维坐标 `(float X, Y, Z)` |
| `DESC_LEVEL` / `DESC_HP` | `6960` / `6944` | 玩家等级 / 当前生命值描述符偏移 |
| `PLAYER_BACKPACK_SLOTS` | `0x4698` | 玩家主背包槽位数组起始偏移（16~20 格，每格 16 字节 `ObjectGuid`） |
| `PLAYER_BAG_CONTAINERS` | `0x47D8` | 扩展背包容器 GUID 数组偏移（4 个扩展包） |
| `ITEM_ID_OFFSET` | `0x150` | 物品对象内部的 ItemID (`int32`) |
| `ITEM_STACK_COUNT` | `0x190` | 物品对象内部的堆叠数量 (`int32`) |
| `CONTAINER_NUM_SLOTS` | `0x450` | 容器对象内部的总格子数 (`int32`) |
| `CONTAINER_SLOTS` | `0x458` | 容器对象内部的槽位 GUID 数组起始偏移 |
