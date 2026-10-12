using System;
using System.Threading;
using System.Threading.Tasks;
using FirestormTwwMonitor.Core.Input;
using FirestormTwwMonitor.Core.Memory;
using FirestormTwwMonitor.Core.Native;

namespace FirestormTwwMonitor.Core.Automation
{
    public enum ReconnectState
    {
        InWorld,
        Step3_EnterWorld,
        WaitingForWorldLoad
    }

    public class ReconnectController
    {
        private readonly WowMemorySession _session;
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;

        public bool AutoReconnectEnabled { get; set; } = true;
        public ReconnectInputMode InputMode { get; set; } = ReconnectInputMode.BackgroundMessage;
        public int DisconnectDebounceSeconds { get; set; } = 0;
        public int StepIntervalSeconds { get; set; } = 4;

        public ReconnectState CurrentState { get; private set; } = ReconnectState.InWorld;
        public string StatusDescription { get; private set; } = "就绪";
        public int ReconnectCount { get; private set; } = 0;
        public int CurrentRetryAttempts { get; private set; } = 0;
        public DateTime LastReconnectTime { get; private set; } = DateTime.MinValue;

        public event Action<string>? OnLogMessage;
        public event Action<string>? OnStatusChanged;

        public ReconnectController(WowMemorySession session)
        {
            _session = session;
        }

        public void Start()
        {
            Stop();
            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoop(_cts.Token));
            Log($"重连监控控制器已启动 (默认{StepIntervalSeconds}秒自动重试机制)");
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            _monitorTask = null;
            CurrentRetryAttempts = 0;
            SetStatus("已停止监控");
        }

        /// <summary>
        /// 外部/按钮手动触发立即重连：不等待任何防抖，立即发送回车并启动4秒重试循环
        /// </summary>
        public void TriggerImmediateReconnect()
        {
            AutoReconnectEnabled = true;
            CurrentRetryAttempts++;

            IntPtr hWnd = _session.GetOrRefreshWindowHandle();
            if (hWnd != IntPtr.Zero)
            {
                string modeDesc = InputMode == ReconnectInputMode.BackgroundMessage ? "后台回车" : "前台硬件回车";
                Log($"[立即重连] 手动触发：立即发送 {modeDesc} 进游戏 (第 {CurrentRetryAttempts} 次，以 {StepIntervalSeconds} 秒间隔重试)...");
                ExecuteSendEnter(hWnd);
            }
            else
            {
                Log("[立即重连] 警告: 尚未找到有效游戏窗口句柄 (GxWindowClass)");
            }

            if (_cts == null || _monitorTask == null || _monitorTask.IsCompleted)
            {
                Start();
            }
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_session.IsAttached)
                    {
                        SetStatus("未连接进程");
                        await Task.Delay(1000, token);
                        continue;
                    }

                    bool inWorld = _session.CheckWorldState();
                    if (inWorld)
                    {
                        CurrentRetryAttempts = 0;
                        if (CurrentState != ReconnectState.InWorld || StatusDescription != "运行中")
                        {
                            CurrentState = ReconnectState.InWorld;
                            SetStatus("运行中");
                        }
                        await Task.Delay(1000, token);
                        continue;
                    }

                    // 未在世界中（选角界面/断线）
                    if (!AutoReconnectEnabled)
                    {
                        SetStatus("重连未开启");
                        await Task.Delay(1000, token);
                        continue;
                    }

                    IntPtr hWnd = _session.GetOrRefreshWindowHandle();
                    if (hWnd == IntPtr.Zero)
                    {
                        SetStatus("窗口未就绪");
                        Log("未找到游戏窗口句柄 (GxWindowClass)，稍后重试...");
                        await Task.Delay(1000, token);
                        continue;
                    }

                    CurrentRetryAttempts++;
                    CurrentState = ReconnectState.Step3_EnterWorld;
                    SetStatus($"重连中 (第{CurrentRetryAttempts}次, 每{StepIntervalSeconds}s重试)...");
                    Log($"[未进世界·PID {_session.ProcessId}] 发送后台回车进游戏 (第 {CurrentRetryAttempts} 次重试, 间隔 {StepIntervalSeconds} 秒)...");

                    // 发送回车
                    ExecuteSendEnter(hWnd);

                    // 等待 StepIntervalSeconds（默认4秒）周期，期间高频检测是否已成功进入世界
                    int checkIntervalMs = 500;
                    int maxChecks = Math.Max(1, (StepIntervalSeconds * 1000) / checkIntervalMs);
                    for (int i = 0; i < maxChecks; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        await Task.Delay(checkIntervalMs, token);

                        if (_session.CheckWorldState())
                        {
                            CompleteReconnect();
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"监控循环异常: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        private void ExecuteSendEnter(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;

            // 执行与测试按钮完全一致的双保险动作：WM_ACTIVATE + 回车 + 选角界面底部【进入游戏】按钮点击
            BackgroundInput.SendReconnectAction(hWnd, InputMode, 0.50, 0.92);
        }

        private void CompleteReconnect()
        {
            ReconnectCount++;
            CurrentRetryAttempts = 0;
            LastReconnectTime = DateTime.Now;
            CurrentState = ReconnectState.InWorld;
            SetStatus("运行中");
            Log($"重连并进入游戏成功！累计重连次数: {ReconnectCount}");
        }

        private void SetStatus(string status)
        {
            StatusDescription = status;
            OnStatusChanged?.Invoke(status);
        }

        private void Log(string msg)
        {
            OnLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }
    }
}
