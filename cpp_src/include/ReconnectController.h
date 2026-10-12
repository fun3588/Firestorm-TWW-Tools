#pragma once
#include <windows.h>
#include <string>
#include <atomic>
#include <chrono>
#include "WowMemorySession.h"
#include "BackgroundInput.h"

enum class ReconnectState {
    InWorld,
    Step3_EnterWorld,
    WaitingForWorldLoad
};

// 纯单线程状态机重连控制器（零死锁、零阻塞、零线程竞争）
class ReconnectController {
private:
    WowMemorySession* session = nullptr;
    bool isRunning = false;
    std::chrono::steady_clock::time_point lastActionTime;
    bool hasSentFirstAction = false;

public:
    bool autoReconnectEnabled = true;
    int stepIntervalSeconds = 10; // 默认 10 秒自动重试
    int reconnectCount = 0;
    int currentRetryAttempts = 0;
    ReconnectState currentState = ReconnectState::InWorld;
    std::string statusDescription = "已停止监控";

    ReconnectController(WowMemorySession* s = nullptr) : session(s) {}

    ~ReconnectController() = default;

    void SetSession(WowMemorySession* s) {
        session = s;
    }

    void Start() {
        if (!session) return;
        isRunning = true;
        autoReconnectEnabled = true;
        hasSentFirstAction = false;
        statusDescription = "监控就绪";
    }

    void Stop() {
        isRunning = false;
        currentRetryAttempts = 0;
        hasSentFirstAction = false;
        statusDescription = "已停止监控";
    }

    // 立即手动触发重连（不等待间隔，立即在后台异步发送一次回车并启动监控）
    void TriggerImmediateReconnect() {
        if (!session) return;
        isRunning = true;
        autoReconnectEnabled = true;
        currentRetryAttempts++;
        hasSentFirstAction = true;
        lastActionTime = std::chrono::steady_clock::now();

        HWND hWnd = session->GetOrRefreshWindowHandle();
        if (hWnd && IsWindow(hWnd)) {
            statusDescription = "重连中 (第" + std::to_string(currentRetryAttempts) + "次, 间隔" + std::to_string(stepIntervalSeconds) + "s)";
            BackgroundInput::SendReconnectActionAsync(hWnd, 0.50, 0.92);
        } else {
            statusDescription = "窗口未就绪";
        }
    }

    // 每秒由主定时器调用一次，绝不阻塞 GUI，耗时 < 0.05ms
    void Tick(bool inWorld) {
        if (!isRunning || !autoReconnectEnabled) {
            statusDescription = "已停止监控";
            return;
        }

        if (!session || !session->IsAttached() || !session->IsProcessAlive()) {
            statusDescription = "客户端已退出";
            return;
        }

        if (inWorld) {
            if (currentRetryAttempts > 0) {
                reconnectCount++;
                currentRetryAttempts = 0;
                hasSentFirstAction = false;
            }
            currentState = ReconnectState::InWorld;
            statusDescription = "运行中";
            return;
        }

        // 未在世界中（选角界面 / 待进游戏）
        HWND hWnd = session->GetOrRefreshWindowHandle();
        if (!hWnd || !IsWindow(hWnd)) {
            statusDescription = "窗口未就绪";
            return;
        }

        auto now = std::chrono::steady_clock::now();
        double elapsedSec = std::chrono::duration<double>(now - lastActionTime).count();

        if (!hasSentFirstAction || elapsedSec >= static_cast<double>(stepIntervalSeconds)) {
            hasSentFirstAction = true;
            lastActionTime = now;
            currentRetryAttempts++;
            currentState = ReconnectState::Step3_EnterWorld;
            statusDescription = "重连中 (第" + std::to_string(currentRetryAttempts) + "次, 间隔" + std::to_string(stepIntervalSeconds) + "s)";

            // 异步非阻塞发送后台双保险回车+点击
            BackgroundInput::SendReconnectActionAsync(hWnd, 0.50, 0.92);
        } else {
            int remainSec = (std::max)(1, stepIntervalSeconds - static_cast<int>(elapsedSec));
            statusDescription = "等待进游戏 (" + std::to_string(remainSec) + "s后第" + std::to_string(currentRetryAttempts + 1) + "次重试)";
        }
    }
};
