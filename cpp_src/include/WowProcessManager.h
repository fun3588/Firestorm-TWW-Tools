#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <vector>
#include <string>

struct WowProcessItem {
    uint32_t pid = 0;
    std::string processName;
    std::string windowTitle;
    HWND hWnd = nullptr;
};

class WowProcessManager {
public:
    static std::vector<WowProcessItem> FindWowProcesses() {
        std::vector<WowProcessItem> results;
        HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (hSnap == INVALID_HANDLE_VALUE) return results;

        PROCESSENTRY32W pe;
        pe.dwSize = sizeof(pe);

        if (Process32FirstW(hSnap, &pe)) {
            do {
                std::wstring exeName = pe.szExeFile;
                // 匹配 Firestorm / WoW / Wow.exe
                if (exeName.find(L"Firestorm") != std::wstring::npos ||
                    exeName.find(L"WoW") != std::wstring::npos ||
                    exeName.find(L"Wow") != std::wstring::npos) {
                    
                    HWND hWnd = WowMemorySession::FindGameWindow(pe.th32ProcessID);
                    if (hWnd) {
                        WowProcessItem item;
                        item.pid = pe.th32ProcessID;
                        
                        char mbName[256] = {0};
                        WideCharToMultiByte(CP_UTF8, 0, pe.szExeFile, -1, mbName, sizeof(mbName), nullptr, nullptr);
                        item.processName = mbName;

                        wchar_t title[256] = {0};
                        GetWindowTextW(hWnd, title, 256);
                        char mbTitle[256] = {0};
                        WideCharToMultiByte(CP_UTF8, 0, title, -1, mbTitle, sizeof(mbTitle), nullptr, nullptr);
                        item.windowTitle = mbTitle;

                        item.hWnd = hWnd;
                        results.push_back(item);
                    }
                }
            } while (Process32NextW(hSnap, &pe));
        }

        CloseHandle(hSnap);
        return results;
    }
};
