# Firestorm-TWW-Tools · 魔兽世界 TWW 窗口监控与断线自动重连 / 采矿时速统计工具

专为 **World of Warcraft 11.2.5.63704 (The War Within - Firestorm)** 设计的高性能桌面监控工具。支持单开与多开（最多 8 个客户端）独立监控、自动化掉线重连处理、角色内存数据实时直读、**背包矿石存量与剩余空位监控**以及**每小时采矿效率统计**。

本项目提供 **双版本** 实现：
1. **C++ 原生极速版 (`FirestormTwwMonitor_Cpp.exe`)**：纯 Win32 原生界面，零运行时依赖，专用内存 **~5.5 MB**，CPU 占用 **< 0.05%**，单线程无锁状态机 + 异步后台输入，极致轻量且绝不卡顿。
2. **C# WinForms 完整版 (`FirestormTwwMonitor.exe`)**：基于 .NET 8，界面丰富现代化。

---

## ⚡ C++ 原生极速版 VS C# 版 性能对比

| 核心指标 | C++ 原生版 (`FirestormTwwMonitor_Cpp.exe`) | C# WinForms 版 (`FirestormTwwMonitor.exe`) | 优势与说明 |
| :--- | :--- | :--- | :--- |
| **专用私有内存 (Private Working Set)** | **~5.5 MB** | **~67.0 MB** | **降低 92%**，不依赖 CLR 运行时，多开占用极低 |
| **常驻工作集 (Working Set)** | **~30 MB** (含系统共享DLL) | **~125 MB** | 极小内存足迹，8 开同时运行毫无压力 |
| **CPU 占用率 (2秒轮询增量)** | **~0.015 秒 CPU 时间 (< 0.05%)** | ~0.20 秒 CPU 时间 (~0.5%) | 极低系统负载，单次内存直读耗时仅 0.05ms |
| **UI 响应与防卡死架构** | **无锁单线程状态机 + 异步后台投递** | 异步任务驱动 | 点击按钮或游戏闪退/未响应时 GUI 100% 顺滑不卡死 |
| **启动速度** | **瞬时秒开 (< 0.1s)** | ~1.5s (JIT 预热) | 纯原生二进制文件直接执行 |
| **独立执行依赖** | **静态链接，单文件绿色无任何依赖** | 需要 .NET 8.0 Runtime | 即拷即用 |

---

## 🌟 核心特性

### 1. 自动化断线重连 (100% 纯后台异步双保险机制)
- **精准掉线与选角识别**：
  - 核心依据基址全局角色名（`OFF_NAME_GLOBAL = 0x47F98B0`）与世界内状态权威判定。
  - **在选角界面（未选角/待进游戏）、载入界面或断线界面，全局名字必定为空**，100% 精准识别为“⏳ 待进游戏(未在线)”，杜绝历史指针缓存误判。
- **纯后台异步双保险重连动作 (`SendReconnectActionAsync`)**：
  1. **唤醒与键盘**：后台向魔兽渲染主窗口（`GxWindowClass`）投递 `WM_ACTIVATE (WA_ACTIVE=1)` 与 `WM_SETFOCUS` 消息，配合 `WM_KEYDOWN` + `WM_CHAR ('\r')` + `WM_KEYUP` 发送回车。
  2. **底部按钮相对比例点击**：间隔 80ms 自动向角色选择界面底部中间【进入魔兽世界】按钮（相对客户区 `50%, 92%`）发送后台左键点击形成双保险。
  3. **最小化静默异步还原**：如果客户端被最小化，通过异步非阻塞的 `ShowWindowAsync(hWnd, SW_SHOWNOACTIVATE)` 在后台恢复尺寸，保证客户区尺寸与点击坐标计算准确，**全程不抢占当前前台窗口焦点、不移动鼠标指针，且即使目标客户端卡死或正在退出也绝不阻塞监控器 GUI**。
- **可自定义重试间隔（默认 10 秒）**：
  - 底部卡片内置重连间隔输入框，默认每隔 **10 秒**自动重试发送后台回车与按钮点击，支持用户随时修改并点击【应用】立即生效（范围 2~300 秒）。

### 2. 严格窗口过滤与多档字体美化布局 (C++ 原生版)
- **严格窗口标题匹配**：仅识别窗口标题**完全等于** `"魔兽世界"` 或 `"World of Warcraft"`（或窗口类名为 `GxWindowClass`）的真实游戏窗口，彻底过滤浏览器、文件夹或同名辅助工具。
- **紧凑 8 窗口容量**：列表高度专为最多 **8 个客户端**优化，精简清爽不冗余。
- **三档字体实时切换（默认【中】号）**：
  - 顶部工具栏右上角提供 **【小】/【中】/【大】** 三档字号一键切换按钮，程序启动**默认采用【中】号字体（18px，行高 30px）**；
  - 切换字号时自动缩放表格行高与列宽，适应不同分辨率屏幕。
- **顶部/中间/底部三段式卡片美化布局**：
  - **顶部工具栏**：左侧放置进程扫描、全部开启/停止、置顶、手动发回车及关闭进程按钮；右侧放置小/中/大字号切换组。
  - **中部监控表格**：双缓冲 + 逐单元格 Diff 对比更新，零闪烁。
  - **底部详情与参数卡片**：左侧实时展示选中客户端的角色、坐标、矿石与背包空位详情，右侧提供【重连间隔(秒)】输入框与【应用】按钮。

### 3. 背包矿石与剩余空位监控 & 采矿时速 (Ores/Hour)
- **专注矿石与背包空位**：
  - 列表的 **「矿石与空位」** 列仅过滤并展示**矿石数量**与**背包剩余空位格数**（已剔除草药干扰），例如：`[空位:24] 铋矿:120 | 亚基矿:45`；当背包内暂无矿石时直观显示 `空位: 24格 (无矿石)`。
- **内置全版本矿石目录**：
  - **地心之战 (TWW)**：铋矿 (210205/210206/210207/210210)、铁爪矿 (210208/210209/210211/210214)、亚基矿 (210212/210213/210215/212234)、虚空石 (212450)
  - **潘达利亚 (MoP)**：幽冥铁矿 (72092)、凯帕琥珀 (72093)、白钛矿 (72094/72103)
  - **巨龙时代 (DF) & 暗影国度 (SL)**：赛瑞拜特矿、德拉诺尔矿、苷铜矿、阴铁矿等
- **采矿时速精确计算**：自动锁定初始矿石基线，实时计算净增采矿量与每小时采矿时速（`+X (Y/h)`）。

---

## 🚀 快速启动

### 方式一：C++ 原生极速版 (推荐，超低内存与CPU)
```powershell
# 使用 CMake 编译 (需要 GCC / MinGW 或 MSVC)
cmake -B build_cpp -G "Ninja" -DCMAKE_BUILD_TYPE=Release
cmake --build build_cpp --config Release

# 启动原生极速版程序
.\build_cpp\bin\FirestormTwwMonitor_Cpp.exe
```

### 方式二：C# WinForms 版 (一键启动或命令行)
直接双击运行根目录下的 `run.bat`，或通过命令行：
```powershell
# 编译 Release
dotnet build -c Release

# 启动程序
.\bin\Release\net8.0-windows\FirestormTwwMonitor.exe
```

### 运行单元测试 (C#)
```powershell
dotnet test tests/FirestormTwwMonitor.Tests.csproj
```

---

## 📐 架构设计

```
Firestorm-TWW-Tools/
├── cpp_src/                         # C++ 原生极速版源码 (~5.5MB 内存, <0.05% CPU)
│   ├── include/
│   │   ├── Offsets.h                # TWW 11.2.5.63704 专用权威基址偏移
│   │   ├── BackgroundInput.h        # 异步纯后台唤醒 + 扫描码回车 + 相对坐标点击
│   │   ├── WowMemorySession.h       # 原生 ReadProcessMemory 角色、坐标、背包空位与物品直读
│   │   ├── WowProcessManager.h      # 严格标题匹配 ("魔兽世界" / "World of Warcraft") 与最多 8 窗口管理
│   │   ├── GatheringTracker.h       # 矿石过滤、背包空位汇总与采矿时速计算
│   │   └── ReconnectController.h    # 无锁单线程非阻塞自动重连状态机 (默认 10s 重试)
│   └── src/
│       └── MainWindow.cpp           # Win32 原生 GUI、小/中/大字号切换(默认中)与三段式卡片布局
├── Core/                            # C# WinForms 版核心模块
│   ├── Native/Win32Api.cs
│   ├── Memory/
│   │   ├── Offsets.cs
│   │   ├── WowMemorySession.cs
│   │   └── WowProcessManager.cs
│   ├── Input/BackgroundInput.cs
│   ├── Automation/ReconnectController.cs
│   └── Gathering/GatheringTracker.cs
├── UI/
│   └── MainForm.cs                  # C# WinForms 主交互面板
├── tests/                           # xUnit 单元测试套件
├── CMakeLists.txt                   # C++ 原生版 CMake 构建配置
├── FirestormTwwMonitor.csproj       # C# 版项目配置
├── ai.md                            # AI 工作交接与技术规范文档
└── README.md                        # 项目说明文档
```
