#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <vector>
#include <string>
#include <algorithm>
#include <cwctype>

struct WowProcessItem {
    uint32_t pid = 0;
    std::string processName;
    std::string windowTitle;
    HWND hWnd = nullptr;
};

class WowProcessManager {
public:
    // 严格精确匹配窗口标题：一字不差地等于 "魔兽世界" 或 "World of Warcraft"（忽略大小写与首尾空格）
    static bool IsExactWowWindowTitle(const std::wstring& title) {
        if (title.empty()) return false;
        
        // 去除首尾空白
        size_t first = title.find_first_not_of(L" \t\r\n");
        if (first == std::wstring::npos) return false;
        size_t last = title.find_last_not_of(L" \t\r\n");
        std::wstring trimmed = title.substr(first, (last - first + 1));

        if (trimmed == L"魔兽世界") return true;

        std::wstring lower = trimmed;
        std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
        return (lower == L"world of warcraft");
    }

    static std::vector<WowProcessItem> FindWowProcesses() {
        std::vector<WowProcessItem> results;
        DWORD currentPid = GetCurrentProcessId();

        // 1. 通过 EnumWindows 枚举顶层窗口，严格精确匹配标题为 "魔兽世界" 或 "world of warcraft"
        struct MatchedWin {
            HWND hWnd = nullptr;
            std::wstring title;
            bool isGx = false;
        };
        std::vector<std::pair<DWORD, MatchedWin>> matchedWins;

        EnumWindows([](HWND hWnd, LPARAM lParam) -> BOOL {
            if (!IsWindowVisible(hWnd)) return TRUE;

            wchar_t titleBuf[256] = {0};
            int len = GetWindowTextW(hWnd, titleBuf, 256);
            if (len <= 0) return TRUE;

            std::wstring title(titleBuf);
            if (IsExactWowWindowTitle(title)) {
                DWORD pid = 0;
                GetWindowThreadProcessId(hWnd, &pid);
                if (pid > 0 && pid != GetCurrentProcessId()) {
                    wchar_t classBuf[128] = {0};
                    GetClassNameW(hWnd, classBuf, 128);
                    bool isGx = (wcscmp(classBuf, L"GxWindowClass") == 0);

                    auto* pList = reinterpret_cast<std::vector<std::pair<DWORD, MatchedWin>>*>(lParam);
                    
                    bool found = false;
                    for (auto& item : *pList) {
                        if (item.first == pid) {
                            found = true;
                            if (isGx) {
                                item.second.hWnd = hWnd;
                                item.second.title = title;
                                item.second.isGx = true;
                            }
                            break;
                        }
                    }
                    if (!found) {
                        MatchedWin mw;
                        mw.hWnd = hWnd;
                        mw.title = title;
                        mw.isGx = isGx;
                        pList->push_back({ pid, mw });
                    }
                }
            }
            return TRUE;
        }, (LPARAM)&matchedWins);

        // 2. 遍历系统进程快照，确认进程有效并关联 (最多 8 个窗口)
        for (const auto& entry : matchedWins) {
            if (results.size() >= 8) break; // 一般最多 8 个窗体

            DWORD pid = entry.first;
            HWND hWnd = entry.second.hWnd;

            WowProcessItem item;
            item.pid = pid;
            item.hWnd = hWnd;

            // 转换窗口标题
            char mbTitle[256] = {0};
            WideCharToMultiByte(CP_UTF8, 0, entry.second.title.c_str(), -1, mbTitle, sizeof(mbTitle), nullptr, nullptr);
            item.windowTitle = mbTitle;

            // 获取进程名称
            HANDLE hProcSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (hProcSnap != INVALID_HANDLE_VALUE) {
                PROCESSENTRY32W pe;
                pe.dwSize = sizeof(pe);
                if (Process32FirstW(hProcSnap, &pe)) {
                    do {
                        if (pe.th32ProcessID == pid) {
                            char mbName[256] = {0};
                            WideCharToMultiByte(CP_UTF8, 0, pe.szExeFile, -1, mbName, sizeof(mbName), nullptr, nullptr);
                            item.processName = mbName;
                            break;
                        }
                    } while (Process32NextW(hProcSnap, &pe));
                }
                CloseHandle(hProcSnap);
            }

            if (item.processName.empty()) {
                item.processName = "WoW 11.2.5 - Firestorm.exe";
            }

            results.push_back(item);
        }

        return results;
    }
};
