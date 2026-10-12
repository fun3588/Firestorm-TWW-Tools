using System;
using System.Diagnostics;
using System.Linq;
using FirestormTwwMonitor.Core.Memory;
using FirestormTwwMonitor.Core.SpeedHack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FirestormTwwMonitor.Tests
{
    [TestClass]
    public class DiagnosticTests
    {
        [TestMethod]
        public void TestRealSpeedHackOnWoW()
        {
            var proc = Process.GetProcessesByName("WoW 11.2.5 - Firestorm").FirstOrDefault();
            Assert.IsNotNull(proc, "未找到运行中的 WoW 客户端");

            using var session = new WowMemorySession();
            bool attached = session.Attach(proc.Id);
            Assert.IsTrue(attached, "Attach 进程失败");

            using var controller = new SpeedHackController(session);

            Console.WriteLine($"准备对 WoW PID {proc.Id} 安装 2.0x 变速 Hook...");
            bool installOk = controller.Install(2.0f);
            Console.WriteLine($"Install(2.0f) 结果: {installOk}");
            Assert.IsTrue(installOk, "SpeedHackController.Install 失败！");

            Console.WriteLine("正在运行 2.0x 变速中 (保持 1 秒)...");
            System.Threading.Thread.Sleep(1000);

            Console.WriteLine("动态调速为 3.0x...");
            bool setOk = controller.SetSpeed(3.0f);
            Console.WriteLine($"SetSpeed(3.0f) 结果: {setOk}");
            Assert.IsTrue(setOk, "SetSpeed 失败！");

            System.Threading.Thread.Sleep(500);

            Console.WriteLine("恢复原速并卸载 Hook...");
            bool unhookOk = controller.Uninstall();
            Console.WriteLine($"Uninstall 结果: {unhookOk}");
            Assert.IsTrue(unhookOk, "Uninstall 失败！");

            Console.WriteLine("🎉 变速器在 WoW 11.2.5 上全流程测试圆满成功！");
        }
    }
}
