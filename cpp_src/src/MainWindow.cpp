#define UNICODE
#define _UNICODE
#include <windows.h>
#include <commctrl.h>
#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <sstream>
#include <iomanip>

#include "Offsets.h"
#include "BackgroundInput.h"
#include "WowMemorySession.h"
#include "GatheringTracker.h"
#include "ReconnectController.h"
#include "WowProcessManager.h"

// 启用 Visual Styles
#pragma comment(lib, "comctl32.lib")

// 客户端监控上下文
struct ClientContext {
    WowProcessItem procInfo;
    std::unique_ptr<WowMemorySession> session;
    std::unique_ptr<GatheringTracker> tracker;
    std::unique_ptr<ReconnectController> reconnectCtrl;
    bool isMonitored = false;

    // 缓存数据
    std::string playerName;
    uint32_t level = 0;
    bool inWorld = false;

    ClientContext() {
        session = std::make_unique<WowMemorySession>();
        tracker = std::make_unique<GatheringTracker>();
        reconnectCtrl = std::make_unique<ReconnectController>(session.get());
    }
};

// 全局变量
HINSTANCE g_hInstance = nullptr;
HWND g_hMainWnd = nullptr;
HWND g_hListView = nullptr;
HWND g_hStatusBar = nullptr;
HWND g_hHeaderBanner = nullptr;
HWND g_hDetailLabel = nullptr;
HFONT g_hFontNormal = nullptr;
HFONT g_hFontBold = nullptr;
HFONT g_hFontTitle = nullptr;

std::vector<std::unique_ptr<ClientContext>> g_contexts;
std::mutex g_contextMutex;
int g_selectedIndex = -1;
bool g_isUpdatingUi = false;

// 控件 ID
enum {
    ID_BTN_SCAN = 1001,
    ID_BTN_RECONNECT_NOW,
    ID_BTN_START_ALL,
    ID_BTN_STOP_ALL,
    ID_BTN_CLOSE_PROC,
    ID_BTN_RESET_STATS,
    ID_BTN_TEST_ENTER,
    ID_EDIT_INTERVAL,
    ID_TIMER_UI = 2001,
    ID_TIMER_SCAN = 2002
};

// 重试间隔设置 (默认10秒)
int g_globalRetryInterval = 10;
HWND g_hIntervalLabel = nullptr;
HWND g_hIntervalEdit = nullptr;
HWND g_hIntervalSecLabel = nullptr;

// 辅助宽字符转换
std::wstring Utf8ToWide(const std::string& str) {
    if (str.empty()) return L"";
    int size = MultiByteToWideChar(CP_UTF8, 0, str.c_str(), -1, nullptr, 0);
    std::wstring wstr(size, 0);
    MultiByteToWideChar(CP_UTF8, 0, str.c_str(), -1, &wstr[0], size);
    if (!wstr.empty() && wstr.back() == L'\0') wstr.pop_back();
    return wstr;
}

// 扫描客户端
void ScanProcesses(bool forceAttach = false) {
    auto procs = WowProcessManager::FindWowProcesses();
    std::lock_guard<std::mutex> lock(g_contextMutex);

    // 移除已关闭的
    for (auto it = g_contexts.begin(); it != g_contexts.end();) {
        bool stillRunning = false;
        for (const auto& p : procs) {
            if (p.pid == (*it)->procInfo.pid) {
                stillRunning = true;
                break;
            }
        }
        if (!stillRunning) {
            (*it)->reconnectCtrl->Stop();
            (*it)->session->Detach();
            it = g_contexts.erase(it);
        } else {
            ++it;
        }
    }

    // 发现新增客户端
    for (const auto& p : procs) {
        bool exists = false;
        for (const auto& ctx : g_contexts) {
            if (ctx->procInfo.pid == p.pid) {
                exists = true;
                break;
            }
        }
        if (!exists) {
            auto ctx = std::make_unique<ClientContext>();
            ctx->procInfo = p;
            ctx->reconnectCtrl->stepIntervalSeconds = g_globalRetryInterval;
            ctx->session->Attach(p.pid, p.hWnd);
            ctx->reconnectCtrl->SetSession(ctx->session.get());
            if (forceAttach) {
                ctx->isMonitored = true;
                ctx->reconnectCtrl->Start();
                ctx->tracker->Start();
            }
            g_contexts.push_back(std::move(ctx));
        }
    }
}

// 刷新 ListView 列表
void RefreshListView() {
    if (!g_hListView) return;
    g_isUpdatingUi = true;
    std::lock_guard<std::mutex> lock(g_contextMutex);

    int count = ListView_GetItemCount(g_hListView);
    int total = static_cast<int>(g_contexts.size());

    // 调整项目数量
    if (count < total) {
        for (int i = count; i < total; i++) {
            LVITEMW lvi = {0};
            lvi.mask = LVIF_TEXT;
            lvi.iItem = i;
            lvi.pszText = (LPWSTR)L"";
            ListView_InsertItem(g_hListView, &lvi);
            ListView_SetCheckState(g_hListView, i, g_contexts[i]->isMonitored ? TRUE : FALSE);
        }
    } else if (count > total) {
        for (int i = count - 1; i >= total; i--) {
            ListView_DeleteItem(g_hListView, i);
        }
    }

    double totalRate = 0.0;
    int monitoredCount = 0;

    for (int i = 0; i < total; i++) {
        auto& ctx = g_contexts[i];
        if (ctx->isMonitored) monitoredCount++;

        // 同步勾选框
        BOOL isChecked = ListView_GetCheckState(g_hListView, i);
        if ((isChecked != FALSE) != ctx->isMonitored) {
            ListView_SetCheckState(g_hListView, i, ctx->isMonitored ? TRUE : FALSE);
        }

        // 序号
        std::wstring idxStr = std::to_wstring(i + 1);
        ListView_SetItemText(g_hListView, i, 1, (LPWSTR)idxStr.c_str());

        // PID
        std::wstring pidStr = std::to_wstring(ctx->procInfo.pid);
        ListView_SetItemText(g_hListView, i, 2, (LPWSTR)pidStr.c_str());

        // 角色名
        std::wstring nameStr = ctx->playerName.empty() ? L"(待进游戏/未选角)" : Utf8ToWide(ctx->playerName);
        ListView_SetItemText(g_hListView, i, 3, (LPWSTR)nameStr.c_str());

        // 等级
        std::wstring lvlStr = (ctx->level > 0) ? std::to_wstring(ctx->level) : L"-";
        ListView_SetItemText(g_hListView, i, 4, (LPWSTR)lvlStr.c_str());

        // 在线状态
        std::wstring onlineStr = ctx->inWorld ? L"🟢 在线中" : L"🔴 未在线(待进)";
        ListView_SetItemText(g_hListView, i, 5, (LPWSTR)onlineStr.c_str());

        // 重连状态
        std::wstring recStatusStr = Utf8ToWide(ctx->reconnectCtrl->statusDescription);
        ListView_SetItemText(g_hListView, i, 6, (LPWSTR)recStatusStr.c_str());

        // 重连次数
        std::wstring recCountStr = std::to_wstring(ctx->reconnectCtrl->reconnectCount.load());
        ListView_SetItemText(g_hListView, i, 7, (LPWSTR)recCountStr.c_str());

        // 背包矿草摘要
        std::string bagSummary = ctx->tracker->GetInventorySummary();
        std::wstring bagWStr = Utf8ToWide(bagSummary);
        ListView_SetItemText(g_hListView, i, 8, (LPWSTR)bagWStr.c_str());

        // 采集时速
        double rate = ctx->tracker->GetTotalPerHour();
        totalRate += rate;
        std::wstringstream rateSs;
        rateSs << std::fixed << std::setprecision(1) << rate << L"/h";
        std::wstring rateStr = rateSs.str();
        ListView_SetItemText(g_hListView, i, 9, (LPWSTR)rateStr.c_str());
    }

    // 更新底部状态栏信息
    std::wstringstream sbStatus;
    sbStatus << L" Firestorm TWW 极速C++版 | 客户端总数: " << total
             << L" | 监控中: " << monitoredCount
             << L" | 全局总采集时速: " << std::fixed << std::setprecision(1) << totalRate << L"/h";
    SetWindowTextW(g_hStatusBar, sbStatus.str().c_str());

    // 更新选中项详情面板
    if (g_selectedIndex >= 0 && g_selectedIndex < total && g_hDetailLabel) {
        auto& sCtx = g_contexts[g_selectedIndex];
        std::wstringstream ssDet;
        ssDet << L"选中客户端 [PID: " << sCtx->procInfo.pid << L"] "
              << (sCtx->playerName.empty() ? L"(待进游戏/未选角)" : Utf8ToWide(sCtx->playerName))
              << L"  | 状态: " << (sCtx->inWorld ? L"🟢 在线中" : L"🔴 待进游戏")
              << L"  | 重连次数: " << sCtx->reconnectCtrl->reconnectCount.load()
              << L"  | 采集时速: " << std::fixed << std::setprecision(1) << sCtx->tracker->GetTotalPerHour() << L"/h";
        SetWindowTextW(g_hDetailLabel, ssDet.str().c_str());
    }

    g_isUpdatingUi = false;
}

// 内存读核心数据更新循环 (每秒)
void UpdateTick() {
    std::lock_guard<std::mutex> lock(g_contextMutex);
    for (auto& ctx : g_contexts) {
        if (!ctx->session->IsAttached()) continue;

        ctx->inWorld = ctx->session->CheckWorldState();
        if (ctx->inWorld) {
            ctx->playerName = ctx->session->GetPlayerName();
            ctx->level = ctx->session->GetPlayerLevel();
            auto inv = ctx->session->ScanInventoryItems();
            ctx->tracker->Update(inv);
        } else {
            ctx->playerName.clear();
            ctx->level = 0;
        }
    }
}

// 窗口过程
LRESULT CALLBACK WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
    case WM_CREATE: {
        // 创建状态栏
        g_hStatusBar = CreateWindowExW(0, STATUSCLASSNAMEW, nullptr,
            WS_CHILD | WS_VISIBLE | SBARS_SIZEGRIP,
            0, 0, 0, 0, hWnd, nullptr, g_hInstance, nullptr);

        // 创建顶部信息栏
        g_hHeaderBanner = CreateWindowExW(0, L"STATIC",
            L" FIRESTORM TWW MONITOR (C++ Native) - 极速微内核 · 极致低内存/CPU · 纯后台自动重连",
            WS_CHILD | WS_VISIBLE | SS_LEFT | SS_CENTERIMAGE,
            12, 10, 860, 32, hWnd, nullptr, g_hInstance, nullptr);
        SendMessageW(g_hHeaderBanner, WM_SETFONT, (WPARAM)g_hFontTitle, TRUE);

        // 创建扁平工具栏按钮与重试间隔输入框 (高度 28)
        int btnX = 12, btnY = 48, btnW = 86, btnH = 28, gap = 6;
        HWND hBtnScan = CreateWindowExW(0, L"BUTTON", L"🔍 刷新", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            btnX, btnY, 78, btnH, hWnd, (HMENU)ID_BTN_SCAN, g_hInstance, nullptr);
        btnX += 78 + gap;

        HWND hBtnRec = CreateWindowExW(0, L"BUTTON", L"🔄 立即重连", WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON,
            btnX, btnY, 96, btnH, hWnd, (HMENU)ID_BTN_RECONNECT_NOW, g_hInstance, nullptr);
        btnX += 96 + gap;

        HWND hBtnStartAll = CreateWindowExW(0, L"BUTTON", L"▶ 全部开启", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            btnX, btnY, 92, btnH, hWnd, (HMENU)ID_BTN_START_ALL, g_hInstance, nullptr);
        btnX += 92 + gap;

        HWND hBtnStopAll = CreateWindowExW(0, L"BUTTON", L"⏹ 全部停止", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            btnX, btnY, 92, btnH, hWnd, (HMENU)ID_BTN_STOP_ALL, g_hInstance, nullptr);
        btnX += 92 + gap;

        HWND hBtnClose = CreateWindowExW(0, L"BUTTON", L"❌ 关闭窗口", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            btnX, btnY, 94, btnH, hWnd, (HMENU)ID_BTN_CLOSE_PROC, g_hInstance, nullptr);
        btnX += 94 + gap;

        HWND hBtnReset = CreateWindowExW(0, L"BUTTON", L"🔄 重置时速", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            btnX, btnY, 92, btnH, hWnd, (HMENU)ID_BTN_RESET_STATS, g_hInstance, nullptr);
        btnX += 92 + gap;

        HWND hBtnTest = CreateWindowExW(0, L"BUTTON", L"↵ 后台回车", WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
            btnX, btnY, 92, btnH, hWnd, (HMENU)ID_BTN_TEST_ENTER, g_hInstance, nullptr);
        btnX += 92 + gap + 4;

        // 重试间隔输入 (支持用户自定义填写，默认 10 秒)
        g_hIntervalLabel = CreateWindowExW(0, L"STATIC", L"重试间隔:", WS_CHILD | WS_VISIBLE | SS_RIGHT | SS_CENTERIMAGE,
            btnX, btnY, 65, btnH, hWnd, nullptr, g_hInstance, nullptr);
        btnX += 68;

        g_hIntervalEdit = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"10", WS_CHILD | WS_VISIBLE | ES_NUMBER | ES_CENTER,
            btnX, btnY + 2, 38, btnH - 4, hWnd, (HMENU)ID_EDIT_INTERVAL, g_hInstance, nullptr);
        btnX += 42;

        g_hIntervalSecLabel = CreateWindowExW(0, L"STATIC", L"秒 (默认10s)", WS_CHILD | WS_VISIBLE | SS_LEFT | SS_CENTERIMAGE,
            btnX, btnY, 80, btnH, hWnd, nullptr, g_hInstance, nullptr);

        SendMessageW(hBtnScan, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(hBtnRec, WM_SETFONT, (WPARAM)g_hFontBold, TRUE);
        SendMessageW(hBtnStartAll, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(hBtnStopAll, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(hBtnClose, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(hBtnReset, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(hBtnTest, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(g_hIntervalLabel, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        SendMessageW(g_hIntervalEdit, WM_SETFONT, (WPARAM)g_hFontBold, TRUE);
        SendMessageW(g_hIntervalSecLabel, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);

        // 创建多账号核心 ListView 表格 (高度适中，最多8个窗体舒适展示，绝无过长)
        g_hListView = CreateWindowExW(WS_EX_CLIENTEDGE, WC_LISTVIEWW, L"",
            WS_CHILD | WS_VISIBLE | LVS_REPORT | LVS_SHOWSELALWAYS | LVS_SINGLESEL,
            12, 84, 836, 275, hWnd, nullptr, g_hInstance, nullptr);

        SendMessageW(g_hListView, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);
        ListView_SetExtendedListViewStyle(g_hListView, LVS_EX_FULLROWSELECT | LVS_EX_GRIDLINES | LVS_EX_CHECKBOXES | LVS_EX_DOUBLEBUFFER);

        // 列标题
        LVCOLUMNW lvc = {0};
        lvc.mask = LVCF_TEXT | LVCF_WIDTH | LVCF_SUBITEM;

        struct Col { const wchar_t* title; int width; };
        Col cols[] = {
            { L"重连", 55 },
            { L"序号", 45 },
            { L"PID", 65 },
            { L"角色名称", 125 },
            { L"等级", 45 },
            { L"在线状态", 95 },
            { L"重连状态", 100 },
            { L"重连次", 55 },
            { L"矿草存量", 220 },
            { L"采集时速", 90 }
        };

        for (int i = 0; i < sizeof(cols)/sizeof(cols[0]); i++) {
            lvc.iSubItem = i;
            lvc.pszText = (LPWSTR)cols[i].title;
            lvc.cx = cols[i].width;
            ListView_InsertColumn(g_hListView, i, &lvc);
        }

        // 底部选中卡片详情
        g_hDetailLabel = CreateWindowExW(0, L"STATIC", L"请在上方列表选择一个魔兽客户端以查看详细信息与操作",
            WS_CHILD | WS_VISIBLE | SS_LEFT | SS_CENTERIMAGE,
            12, 490, 860, 24, hWnd, nullptr, g_hInstance, nullptr);
        SendMessageW(g_hDetailLabel, WM_SETFONT, (WPARAM)g_hFontNormal, TRUE);

        // 启动定时器
        SetTimer(hWnd, ID_TIMER_UI, 1000, nullptr);
        SetTimer(hWnd, ID_TIMER_SCAN, 4000, nullptr);

        // 初始扫描
        ScanProcesses(true);
        return 0;
    }

    case WM_SIZE: {
        int w = LOWORD(lParam);
        int h = HIWORD(lParam);
        SendMessageW(g_hStatusBar, WM_SIZE, 0, 0);

        RECT rcSb;
        GetWindowRect(g_hStatusBar, &rcSb);
        int sbH = rcSb.bottom - rcSb.top;

        if (g_hHeaderBanner) SetWindowPos(g_hHeaderBanner, nullptr, 12, 10, w - 24, 30, SWP_NOZORDER);
        if (g_hDetailLabel) SetWindowPos(g_hDetailLabel, nullptr, 12, h - sbH - 28, w - 24, 22, SWP_NOZORDER);
        if (g_hListView) SetWindowPos(g_hListView, nullptr, 12, 84, w - 24, h - sbH - 120, SWP_NOZORDER);
        break;
    }

    case WM_COMMAND: {
        int id = LOWORD(wParam);
        int code = HIWORD(wParam);

        if (id == ID_EDIT_INTERVAL && code == EN_CHANGE) {
            wchar_t buf[32] = {0};
            GetWindowTextW(g_hIntervalEdit, buf, 32);
            int val = _wtoi(buf);
            if (val >= 1 && val <= 300) {
                g_globalRetryInterval = val;
                std::lock_guard<std::mutex> lock(g_contextMutex);
                for (auto& ctx : g_contexts) {
                    ctx->reconnectCtrl->stepIntervalSeconds = val;
                }
            }
            break;
        }

        switch (id) {
        case ID_BTN_SCAN:
            ScanProcesses(false);
            RefreshListView();
            break;

        case ID_BTN_RECONNECT_NOW: {
            std::lock_guard<std::mutex> lock(g_contextMutex);
            if (g_selectedIndex >= 0 && g_selectedIndex < (int)g_contexts.size()) {
                g_contexts[g_selectedIndex]->reconnectCtrl->TriggerImmediateReconnect();
            } else if (!g_contexts.empty()) {
                g_contexts[0]->reconnectCtrl->TriggerImmediateReconnect();
            }
            break;
        }

        case ID_BTN_START_ALL: {
            std::lock_guard<std::mutex> lock(g_contextMutex);
            for (auto& ctx : g_contexts) {
                ctx->isMonitored = true;
                ctx->reconnectCtrl->Start();
                ctx->tracker->Start();
            }
            RefreshListView();
            break;
        }

        case ID_BTN_STOP_ALL: {
            std::lock_guard<std::mutex> lock(g_contextMutex);
            for (auto& ctx : g_contexts) {
                ctx->isMonitored = false;
                ctx->reconnectCtrl->Stop();
            }
            RefreshListView();
            break;
        }

        case ID_BTN_CLOSE_PROC: {
            std::lock_guard<std::mutex> lock(g_contextMutex);
            if (g_selectedIndex >= 0 && g_selectedIndex < (int)g_contexts.size()) {
                auto& ctx = g_contexts[g_selectedIndex];
                if (ctx->session->processHandle) {
                    TerminateProcess(ctx->session->processHandle, 0);
                }
            }
            break;
        }

        case ID_BTN_RESET_STATS: {
            std::lock_guard<std::mutex> lock(g_contextMutex);
            for (auto& ctx : g_contexts) {
                ctx->tracker->Reset();
            }
            break;
        }

        case ID_BTN_TEST_ENTER: {
            std::lock_guard<std::mutex> lock(g_contextMutex);
            HWND targetHwnd = nullptr;
            if (g_selectedIndex >= 0 && g_selectedIndex < (int)g_contexts.size()) {
                targetHwnd = g_contexts[g_selectedIndex]->session->GetOrRefreshWindowHandle();
            } else if (!g_contexts.empty()) {
                targetHwnd = g_contexts[0]->session->GetOrRefreshWindowHandle();
            }
            if (targetHwnd) {
                BackgroundInput::SendReconnectAction(targetHwnd);
            }
            break;
        }
        }
        break;
    }

    case WM_NOTIFY: {
        LPNMHDR pnm = (LPNMHDR)lParam;
        if (pnm->hwndFrom == g_hListView) {
            if (pnm->code == LVN_ITEMCHANGED) {
                NMLISTVIEW* pnmv = (NMLISTVIEW*)lParam;
                if (!g_isUpdatingUi && (pnmv->uChanged & LVIF_STATE)) {
                    // 处理选择行改变
                    if (pnmv->uNewState & LVIS_SELECTED) {
                        g_selectedIndex = pnmv->iItem;
                    }
                    // 处理勾选框改变
                    bool oldCheck = (pnmv->uOldState >> 12) == 2;
                    bool newCheck = (pnmv->uNewState >> 12) == 2;
                    if (oldCheck != newCheck && pnmv->iItem >= 0) {
                        std::lock_guard<std::mutex> lock(g_contextMutex);
                        if (pnmv->iItem < (int)g_contexts.size()) {
                            auto& ctx = g_contexts[pnmv->iItem];
                            ctx->isMonitored = newCheck;
                            if (newCheck) {
                                ctx->reconnectCtrl->Start();
                                ctx->tracker->Start();
                            } else {
                                ctx->reconnectCtrl->Stop();
                            }
                        }
                    }
                }
            }
        }
        break;
    }

    case WM_TIMER: {
        if (wParam == ID_TIMER_UI) {
            UpdateTick();
            RefreshListView();
        } else if (wParam == ID_TIMER_SCAN) {
            ScanProcesses(false);
        }
        break;
    }

    case WM_DESTROY:
        KillTimer(hWnd, ID_TIMER_UI);
        KillTimer(hWnd, ID_TIMER_SCAN);
        PostQuitMessage(0);
        break;

    default:
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }
    return 0;
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE, LPWSTR, int nCmdShow) {
    g_hInstance = hInstance;

    // 初始化 Common Controls
    INITCOMMONCONTROLSEX icex;
    icex.dwSize = sizeof(icex);
    icex.dwICC = ICC_LISTVIEW_CLASSES | ICC_BAR_CLASSES;
    InitCommonControlsEx(&icex);

    // 创建字体
    g_hFontNormal = CreateFontW(16, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Microsoft YaHei UI");
    g_hFontBold = CreateFontW(16, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Microsoft YaHei UI");
    g_hFontTitle = CreateFontW(18, 0, 0, 0, FW_BOLD, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, L"Segoe UI");

    // 注册主窗口类
    WNDCLASSEXW wc = {0};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.hIcon = LoadIcon(nullptr, IDI_APPLICATION);
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = L"FirestormTwwMonitorNativeWnd";

    if (!RegisterClassExW(&wc)) return 1;

    // 创建主窗口 (紧凑尺寸 860x460，针对最多8个窗体深度优化，不过长)
    g_hMainWnd = CreateWindowExW(0, wc.lpszClassName,
        L"Firestorm TWW 窗口监控器 · 极速C++版 (内存<6MB · CPU<0.05%)",
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, 860, 460,
        nullptr, nullptr, hInstance, nullptr);

    if (!g_hMainWnd) return 2;

    ShowWindow(g_hMainWnd, nCmdShow);
    UpdateWindow(g_hMainWnd);

    // 消息循环
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    // 清理资源
    if (g_hFontNormal) DeleteObject(g_hFontNormal);
    if (g_hFontBold) DeleteObject(g_hFontBold);
    if (g_hFontTitle) DeleteObject(g_hFontTitle);

    return (int)msg.wParam;
}

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    return wWinMain(hInstance, hPrevInstance, GetCommandLineW(), nCmdShow);
}

int main(int argc, char* argv[]) {
    return wWinMain(GetModuleHandleW(nullptr), nullptr, GetCommandLineW(), SW_SHOW);
}
