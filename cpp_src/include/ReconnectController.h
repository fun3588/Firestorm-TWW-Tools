#pragma once
#include <windows.h>
#include <string>
#include <thread>
#include <atomic>
#include <functional>
#include <chrono>
#include "WowMemorySession.h"
#include "BackgroundInput.h"

enum class ReconnectState {
    InWorld,
    Step3_EnterWorld,
    WaitingForWorldLoad
};

class ReconnectController {
private:
    WowMemorySession* session = nullptr;
    std::thread workerThread;
    std::atomic<bool> isRunning{false};

public:
    std::atomic<bool> autoReconnectEnabled{true};
    int stepIntervalSeconds = 10; // 默认 10 秒自动重试
    std::atomic<int> reconnectCount{0};
    std::atomic<int> currentRetryAttempts{0};
    ReconnectState currentState = ReconnectState::InWorld;
    std::string statusDescription = "就绪";

    std::function<void(const std::string&)> onLogCallback;
    std::function<void(const std::string&)> onStatusCallback;

    ReconnectController(WowMemorySession* s = nullptr) : session(s) {}

    ~ReconnectController() {
        Stop();
    }

    void SetSession(WowMemorySession* s) {
        session = s;
    }

    void Start() {
        Stop();
        if (!session) return;
        isRunning = true;
        workerThread = std::thread(&ReconnectController::MonitorLoop, this);
        Log("重连控制器已启动 (默认 " + std::to_string(stepIntervalSeconds) + " 秒自动重试机制)");
    }

    void Stop() {
        isRunning = false;
        if (workerThread.joinable()) {
            workerThread.join();
        }
        currentRetryAttempts = 0;
        SetStatus("已停止监控");
    }

    // 立即手动触发重连（绝不等待，立即发送回车并进入4秒循环）
    void TriggerImmediateReconnect() {
        if (!session) return;
        autoReconnectEnabled = true;
        currentRetryAttempts++;

        HWND hWnd = session->GetOrRefreshWindowHandle();
        if (hWnd && IsWindow(hWnd)) {
            Log("[立即重连] 手动触发：立即发送后台回车进游戏 (第 " + std::to_string(currentRetryAttempts) + " 次)...");
            ExecuteSendEnter(hWnd);
        } else {
            Log("[立即重连] 警告: 尚未找到有效游戏窗口句柄 (GxWindowClass)");
        }

        if (!isRunning) {
            Start();
        }
    }

private:
    void MonitorLoop() {
        while (isRunning) {
            try {
                if (!session || !session->IsAttached()) {
                    SetStatus("未连接进程");
                    SleepMs(1000);
                    continue;
                }

                bool inWorld = session->CheckWorldState();
                if (inWorld) {
                    currentRetryAttempts = 0;
                    if (currentState != ReconnectState::InWorld || statusDescription != "运行中") {
                        currentState = ReconnectState::InWorld;
                        SetStatus("运行中");
                    }
                    SleepMs(1000);
                    continue;
                }

                // 未在世界中（选角界面/待进游戏）
                if (!autoReconnectEnabled) {
                    SetStatus("重连未开启");
                    SleepMs(1000);
                    continue;
                }

                HWND hWnd = session->GetOrRefreshWindowHandle();
                if (!hWnd || !IsWindow(hWnd)) {
                    SetStatus("窗口未就绪");
                    Log("未找到游戏窗口句柄 (GxWindowClass)，稍后重试...");
                    SleepMs(1000);
                    continue;
                }

                currentRetryAttempts++;
                currentState = ReconnectState::Step3_EnterWorld;
                SetStatus("重连中 (第" + std::to_string(currentRetryAttempts) + "次, 每" + std::to_string(stepIntervalSeconds) + "s重试)...");
                Log("[未在线·待进游戏·PID " + std::to_string(session->processId) + "] 自动发送后台回车进游戏 (第 " + std::to_string(currentRetryAttempts) + " 次重试)...");

                ExecuteSendEnter(hWnd);

                // 循环间隔等待（默认4秒），期间高频检测是否已进游戏
                int checkIntervalMs = 500;
                int maxChecks = (std::max)(1, (stepIntervalSeconds * 1000) / checkIntervalMs);
                for (int i = 0; i < maxChecks && isRunning; i++) {
                    SleepMs(checkIntervalMs);
                    if (session->CheckWorldState()) {
                        CompleteReconnect();
                        break;
                    }
                }
            }
            catch (...) {
                SleepMs(1000);
            }
        }
    }

    void ExecuteSendEnter(HWND hWnd) {
        if (!hWnd) return;
        // 纯后台双保险：WM_ACTIVATE + 回车 + 选角界面底部 (50%, 92%) 点击
        BackgroundInput::SendReconnectAction(hWnd, 0.50, 0.92);
    }

    void CompleteReconnect() {
        reconnectCount++;
        currentRetryAttempts = 0;
        currentState = ReconnectState::InWorld;
        SetStatus("运行中");
        Log("重连并进入游戏成功！累计重连次数: " + std::to_string(reconnectCount));
    }

    void SleepMs(int ms) {
        int elapsed = 0;
        while (elapsed < ms && isRunning) {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
            elapsed += 50;
        }
    }

    void SetStatus(const std::string& st) {
        statusDescription = st;
        if (onStatusCallback) onStatusCallback(st);
    }

    void Log(const std::string& msg) {
        if (onLogCallback) {
            SYSTEMTIME st;
            GetLocalTime(&st);
            char timeBuf[32];
            snprintf(timeBuf, sizeof(timeBuf), "[%02d:%02d:%02d] ", st.wHour, st.wMinute, st.wSecond);
            onLogCallback(std::string(timeBuf) + msg);
        }
    }
};
