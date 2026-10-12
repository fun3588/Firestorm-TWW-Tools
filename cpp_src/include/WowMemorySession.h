#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <string>
#include <vector>
#include <cstdint>
#include <cmath>
#include <map>
#include <algorithm>
#include "Offsets.h"

enum class ClientGameState {
    Disconnected,
    CharSelect,
    InWorld
};

class WowMemorySession {
public:
    uint32_t processId = 0;
    HWND windowHandle = nullptr;
    HANDLE processHandle = nullptr;
    uintptr_t moduleBase = 0;
    uintptr_t playerObj = 0;
    uintptr_t cMovement = 0;
    bool isInWorld = false;
    ClientGameState gameState = ClientGameState::Disconnected;

    WowMemorySession() = default;
    ~WowMemorySession() {
        Detach();
    }

    bool Attach(uint32_t pid, HWND hWnd = nullptr) {
        Detach();
        processId = pid;
        windowHandle = hWnd;

        // 打开进程句柄
        processHandle = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
        if (!processHandle) {
            processHandle = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, FALSE, pid);
        }
        if (!processHandle) return false;

        // 获取主模块基址 (WOW 11.2.5 - Firestorm.exe)
        moduleBase = GetModuleBase(pid);
        if (!moduleBase) {
            Detach();
            return false;
        }

        if (!windowHandle || !IsWindow(windowHandle)) {
            windowHandle = FindGameWindow(pid);
        }

        return true;
    }

    void Detach() {
        if (processHandle && processHandle != INVALID_HANDLE_VALUE) {
            CloseHandle(processHandle);
        }
        processHandle = nullptr;
        processId = 0;
        windowHandle = nullptr;
        moduleBase = 0;
        playerObj = 0;
        cMovement = 0;
        isInWorld = false;
        gameState = ClientGameState::Disconnected;
    }

    bool IsAttached() const {
        return processHandle != nullptr && moduleBase != 0;
    }

    bool IsProcessAlive() const {
        if (!processHandle) return false;
        DWORD code = 0;
        if (GetExitCodeProcess(processHandle, &code)) {
            return code == STILL_ACTIVE;
        }
        return false;
    }

    static bool IsValidPtr(uintptr_t addr) {
        return addr >= 0x10000 && addr <= 0x7FFFFFFEFFFF;
    }

    // 内存直读辅助函数
    uint8_t ReadByte(uintptr_t addr) const {
        if (!processHandle || !IsValidPtr(addr)) return 0;
        uint8_t val = 0;
        SIZE_T bytesRead = 0;
        ReadProcessMemory(processHandle, (LPCVOID)addr, &val, sizeof(val), &bytesRead);
        return val;
    }

    uint32_t ReadUInt32(uintptr_t addr) const {
        if (!processHandle || !IsValidPtr(addr)) return 0;
        uint32_t val = 0;
        SIZE_T bytesRead = 0;
        ReadProcessMemory(processHandle, (LPCVOID)addr, &val, sizeof(val), &bytesRead);
        return val;
    }

    int32_t ReadInt32(uintptr_t addr) const {
        if (!processHandle || !IsValidPtr(addr)) return 0;
        int32_t val = 0;
        SIZE_T bytesRead = 0;
        ReadProcessMemory(processHandle, (LPCVOID)addr, &val, sizeof(val), &bytesRead);
        return val;
    }

    uintptr_t ReadInt64(uintptr_t addr) const {
        if (!processHandle || !IsValidPtr(addr)) return 0;
        uintptr_t val = 0;
        SIZE_T bytesRead = 0;
        ReadProcessMemory(processHandle, (LPCVOID)addr, &val, sizeof(val), &bytesRead);
        return val;
    }

    float ReadFloat(uintptr_t addr) const {
        if (!processHandle || !IsValidPtr(addr)) return 0.0f;
        float val = 0.0f;
        SIZE_T bytesRead = 0;
        ReadProcessMemory(processHandle, (LPCVOID)addr, &val, sizeof(val), &bytesRead);
        return val;
    }

    std::string ReadString(uintptr_t addr, size_t maxLen = 32) const {
        if (!processHandle || !IsValidPtr(addr)) return "";
        std::vector<char> buf(maxLen + 1, 0);
        SIZE_T bytesRead = 0;
        if (ReadProcessMemory(processHandle, (LPCVOID)addr, buf.data(), maxLen, &bytesRead)) {
            buf[maxLen] = '\0';
            return std::string(buf.data());
        }
        return "";
    }

    std::string GetPlayerName() const {
        if (!IsAttached() || !IsValidPtr(moduleBase + WowOffsets::OFF_NAME_GLOBAL)) return "";
        return ReadString(moduleBase + WowOffsets::OFF_NAME_GLOBAL, 32);
    }

    bool IsPlayerAlive() const {
        if (!IsValidPtr(playerObj) || !IsValidPtr(cMovement)) return false;
        if (ReadByte(playerObj + WowOffsets::OFF_OBJ_TYPE) != WowOffsets::TYPE_PLAYER_LOCAL) return false;
        float px = ReadFloat(cMovement + WowOffsets::OFF_POS);
        return !std::isnan(px) && std::abs(px) < 100000.0f;
    }

    // 高性能批量解析本地玩家指针（单次 RPM 读 2000 个桶节点）
    bool ResolvePlayer() {
        if (!IsAttached()) return false;
        uintptr_t objmgr = ReadInt64(moduleBase + WowOffsets::OBJ_MGR_GLOBAL);
        if (!IsValidPtr(objmgr)) return false;

        uintptr_t arr = ReadInt64(objmgr + WowOffsets::OFF_OBJ_BUCKETS);
        int64_t count = (int64_t)ReadInt64(objmgr + WowOffsets::OFF_OBJ_COUNT);
        if (!IsValidPtr(arr)) return false;

        int limit = static_cast<int>(std::clamp<int64_t>(count, 0, 2000));
        if (limit <= 0) return false;

        std::vector<uintptr_t> nodes(limit, 0);
        SIZE_T bytesRead = 0;
        if (!ReadProcessMemory(processHandle, (LPCVOID)arr, nodes.data(), limit * sizeof(uintptr_t), &bytesRead)) {
            return false;
        }

        for (int i = 0; i < limit; i++) {
            uintptr_t node = nodes[i];
            if (!IsValidPtr(node)) continue;

            uintptr_t obj = ReadInt64(node + WowOffsets::OFF_NODE_OBJ);
            if (!IsValidPtr(obj)) continue;

            if (ReadByte(obj + WowOffsets::OFF_OBJ_TYPE) == WowOffsets::TYPE_PLAYER_LOCAL) {
                uintptr_t cmov = ReadInt64(obj + WowOffsets::OFF_UNIT_MOVEMENT);
                if (!IsValidPtr(cmov)) continue;

                playerObj = obj;
                cMovement = cmov;
                return true;
            }
        }
        return false;
    }

    // 权威快速检测世界状态（对齐 C# 逻辑）
    // 选角界面或未进入世界时，OFF_NAME_GLOBAL 必为空
    bool CheckWorldState() {
        if (!IsAttached() || !IsProcessAlive()) {
            isInWorld = false;
            playerObj = 0;
            cMovement = 0;
            gameState = ClientGameState::Disconnected;
            return false;
        }

        // 1. 核心判定：读取当前全局角色名称
        std::string activeName = GetPlayerName();
        if (activeName.empty()) {
            isInWorld = false;
            playerObj = 0;
            cMovement = 0;
            gameState = ClientGameState::CharSelect;
            return false;
        }

        // 2. 检查玩家存活与实体指针
        bool playerAlive = IsPlayerAlive();
        if (!playerAlive) {
            playerAlive = ResolvePlayer();
        }

        if (playerAlive) {
            gameState = ClientGameState::InWorld;
            isInWorld = true;
            return true;
        }

        // 本地对象不存在，处于选角或载入阶段
        playerObj = 0;
        cMovement = 0;
        isInWorld = false;
        gameState = ClientGameState::CharSelect;
        return false;
    }

    uint32_t GetPlayerLevel() const {
        if (!IsValidPtr(playerObj)) return 0;
        return ReadUInt32(playerObj + WowOffsets::OFF_LEVEL);
    }

    struct InventoryInfo {
        std::map<int, int> items;
        int occupiedSlots = 0;
        int totalSlots = 0;
        int freeSlots = 0;
    };

    // 极速内存背包扫描（耗时仅 0.1ms）
    InventoryInfo ScanInventoryItems() {
        InventoryInfo result;
        if (!IsAttached() || !isInWorld) return result;

        uintptr_t objmgr = ReadInt64(moduleBase + WowOffsets::OBJ_MGR_GLOBAL);
        if (!IsValidPtr(objmgr)) return result;

        uintptr_t arr = ReadInt64(objmgr + WowOffsets::OFF_OBJ_BUCKETS);
        int64_t count = (int64_t)ReadInt64(objmgr + WowOffsets::OFF_OBJ_COUNT);
        if (!IsValidPtr(arr)) return result;

        int limit = static_cast<int>(std::clamp<int64_t>(count, 0, 4000));
        if (limit <= 0) return result;

        std::vector<uintptr_t> nodes(limit, 0);
        SIZE_T bytesRead = 0;
        if (!ReadProcessMemory(processHandle, (LPCVOID)arr, nodes.data(), limit * sizeof(uintptr_t), &bytesRead)) {
            return result;
        }

        int bagExtraSlots = 0;
        const std::map<int, int> bagSlotCatalog = {
            { 127035, 24 }, { 210134, 34 }, { 210133, 32 }, { 210132, 30 },
            { 192886, 34 }, { 192885, 32 }, { 192884, 30 }, { 174334, 32 },
            { 174333, 30 }, { 168804, 32 }, { 168803, 30 }, { 154608, 30 },
            { 142075, 30 }, { 21841, 16 },  { 41599, 20 },  { 54443, 22 }, { 70136, 24 }
        };

        for (int i = 0; i < limit; i++) {
            uintptr_t node = nodes[i];
            if (!IsValidPtr(node)) continue;

            uintptr_t obj = ReadInt64(node + WowOffsets::OFF_NODE_OBJ);
            if (!IsValidPtr(obj)) continue;

            uint8_t objType = ReadByte(obj + WowOffsets::OFF_OBJ_TYPE);
            if (objType == WowOffsets::TYPE_ITEM) {
                uint32_t itemId = ReadUInt32(obj + WowOffsets::OFF_ITEM_ID);
                if (itemId > 0) {
                    result.occupiedSlots++;
                    uint32_t stack = ReadUInt32(obj + WowOffsets::OFF_ITEM_STACK_COUNT);
                    int countVal = (stack == 0) ? 1 : static_cast<int>(stack);
                    result.items[itemId] += countVal;
                }
            } else if (objType == WowOffsets::TYPE_CONTAINER) {
                uint32_t bagId = ReadUInt32(obj + WowOffsets::OFF_ITEM_ID);
                auto it = bagSlotCatalog.find(bagId);
                if (it != bagSlotCatalog.end()) {
                    bagExtraSlots += it->second;
                }
            }
        }

        result.totalSlots = 16 + bagExtraSlots;
        if (result.totalSlots < result.occupiedSlots) {
            result.totalSlots = std::max(result.totalSlots, 112);
        }
        result.freeSlots = std::max(0, result.totalSlots - result.occupiedSlots);
        return result;
    }

    HWND GetOrRefreshWindowHandle() {
        if (windowHandle && IsWindow(windowHandle)) {
            return windowHandle;
        }
        if (processId != 0) {
            windowHandle = FindGameWindow(processId);
        }
        return windowHandle;
    }

    static uintptr_t GetModuleBase(DWORD pid) {
        uintptr_t modBase = 0;
        HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (hSnap != INVALID_HANDLE_VALUE) {
            MODULEENTRY32W me;
            me.dwSize = sizeof(me);
            if (Module32FirstW(hSnap, &me)) {
                modBase = (uintptr_t)me.modBaseAddr;
            }
            CloseHandle(hSnap);
        }
        return modBase;
    }

    static HWND FindGameWindow(DWORD pid) {
        struct EnumData {
            DWORD targetPid;
            HWND foundWnd;
        } data = { pid, nullptr };

        EnumWindows([](HWND hWnd, LPARAM lParam) -> BOOL {
            auto* pData = reinterpret_cast<EnumData*>(lParam);
            DWORD procId = 0;
            GetWindowThreadProcessId(hWnd, &procId);
            if (procId == pData->targetPid) {
                wchar_t className[128] = {0};
                GetClassNameW(hWnd, className, 128);
                if (wcscmp(className, L"GxWindowClass") == 0) {
                    pData->foundWnd = hWnd;
                    return FALSE; // 找到了
                }
                if (!pData->foundWnd && IsWindowVisible(hWnd)) {
                    pData->foundWnd = hWnd;
                }
            }
            return TRUE;
        }, (LPARAM)&data);

        return data.foundWnd;
    }
};
