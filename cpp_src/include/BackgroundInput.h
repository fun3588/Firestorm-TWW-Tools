#pragma once
#include <windows.h>
#include <cstdint>
#include <thread>
#include <chrono>

class BackgroundInput {
public:
    // 后台向指定窗口句柄发送指定虚拟按键（包含 WM_ACTIVATE 唤醒 + WM_SETFOCUS + 硬件扫描码）
    static bool SendBackgroundKey(HWND hWnd, int virtualKey, int holdMs = 60) {
        if (!hWnd || !IsWindow(hWnd)) return false;

        uint32_t scanCode = (virtualKey == VK_RETURN) ? 0x1C : MapVirtualKeyA(virtualKey, 0);
        uintptr_t lParamDown = 1 | (scanCode << 16);
        uintptr_t lParamUp = 1 | (scanCode << 16) | (1ULL << 30) | (1ULL << 31);

        // 1. 发送 WM_ACTIVATE 与 WM_SETFOCUS 唤醒游戏后台输入队列
        PostMessageA(hWnd, WM_ACTIVATE, 1, 0);
        PostMessageA(hWnd, WM_SETFOCUS, 0, 0);

        // 2. 发送 WM_KEYDOWN
        PostMessageA(hWnd, WM_KEYDOWN, (WPARAM)virtualKey, (LPARAM)lParamDown);

        // 3. 若为回车键，补发字符消息 WM_CHAR ('\r' / 13) 确保被 GlueXML/UI 捕获
        if (virtualKey == VK_RETURN) {
            PostMessageA(hWnd, WM_CHAR, 13, (LPARAM)lParamDown);
        }

        if (holdMs > 0) {
            std::this_thread::sleep_for(std::chrono::milliseconds(holdMs));
        }

        // 4. 发送 WM_KEYUP
        PostMessageA(hWnd, WM_KEYUP, (WPARAM)virtualKey, (LPARAM)lParamUp);
        return true;
    }

    // 后台向窗口指定相对客户区坐标 (clientX, clientY) 模拟点击左键
    static bool SendBackgroundClick(HWND hWnd, int clientX, int clientY, int holdMs = 50) {
        if (!hWnd || !IsWindow(hWnd)) return false;

        LPARAM lParam = (LPARAM)(((uint32_t)clientY << 16) | ((uint32_t)clientX & 0xFFFF));
        PostMessageA(hWnd, WM_ACTIVATE, 1, 0);
        PostMessageA(hWnd, WM_LBUTTONDOWN, 1, lParam);

        if (holdMs > 0) {
            std::this_thread::sleep_for(std::chrono::milliseconds(holdMs));
        }

        PostMessageA(hWnd, WM_LBUTTONUP, 0, lParam);
        return true;
    }

    // 后台向窗口指定相对比例坐标 (relX, relY) 模拟点击左键
    static bool SendBackgroundClickRel(HWND hWnd, double relX, double relY, int holdMs = 50) {
        if (!hWnd || !IsWindow(hWnd)) return false;

        RECT rect;
        if (GetClientRect(hWnd, &rect) && (rect.right - rect.left > 100) && (rect.bottom - rect.top > 100)) {
            int cx = static_cast<int>((rect.right - rect.left) * relX);
            int cy = static_cast<int>((rect.bottom - rect.top) * relY);
            return SendBackgroundClick(hWnd, cx, cy, holdMs);
        }
        return false;
    }

    // 执行重连步骤动作：纯后台回车键 + 选角界面底部【进入魔兽世界】按钮相对点击双保险
    // 100% 纯后台静默执行，绝不抢占前台焦点，绝不移动鼠标
    static bool SendReconnectAction(HWND hWnd, double relX = 0.50, double relY = 0.92) {
        if (!hWnd || !IsWindow(hWnd)) return false;

        // 若窗口最小化，先以不激活方式恢复尺寸，确保客户区坐标计算和消息投递有效
        if (IsIconic(hWnd)) {
            ShowWindow(hWnd, SW_SHOWNOACTIVATE);
            std::this_thread::sleep_for(std::chrono::milliseconds(80));
        }

        // 1. 发送后台回车（包含 WM_ACTIVATE + WM_SETFOCUS + WM_KEYDOWN + WM_CHAR + WM_KEYUP）
        SendBackgroundKey(hWnd, VK_RETURN, 60);

        std::this_thread::sleep_for(std::chrono::milliseconds(80));

        // 2. 双保险：在角色选择界面，同时向屏幕底部中间【进入游戏】按钮 (50%, 92%) 发送后台左键点击
        SendBackgroundClickRel(hWnd, relX, relY, 50);

        return true;
    }
};
