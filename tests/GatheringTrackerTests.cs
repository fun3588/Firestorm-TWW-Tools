using System;
using FirestormTwwMonitor.Core.Gathering;
using FirestormTwwMonitor.Core.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FirestormTwwMonitor.Tests
{
    [TestClass]
    public class GatheringTrackerTests
    {
        [TestMethod]
        public void TestTrackedItem_CalculatePerHour()
        {
            var item = new TrackedItem
            {
                Name = "铋矿石",
                ItemId = 210796,
                InitialCount = 10,
                CurrentCount = 70
            };

            Assert.AreEqual(60, item.GainedCount);

            // 运行 30 分钟 (0.5 小时) -> 时速应为 120
            var halfHour = TimeSpan.FromMinutes(30);
            double rate = item.CalculatePerHour(halfHour);
            Assert.AreEqual(120.0, rate, 0.001);

            // 运行 2 小时 -> 时速应为 30
            var twoHours = TimeSpan.FromHours(2);
            double rate2 = item.CalculatePerHour(twoHours);
            Assert.AreEqual(30.0, rate2, 0.001);
        }

        [TestMethod]
        public void TestGatheringTracker_AddAndRemoveItem()
        {
            var session = new WowMemorySession();
            var tracker = new GatheringTracker(session);

            int initialCount = tracker.Items.Count;
            Assert.IsTrue(initialCount > 0, "预设物品库应该包含默认矿草");

            tracker.AddItem("测试草药", 999999, "草药");
            Assert.AreEqual(initialCount + 1, tracker.Items.Count);

            bool removed = tracker.RemoveItem("测试草药");
            Assert.IsTrue(removed);
            Assert.AreEqual(initialCount, tracker.Items.Count);
        }

        [TestMethod]
        public void TestKeywordMatching()
        {
            string herbName = "肥沃的菌柄";
            string oreName = "富铋矿石";

            bool isHerb = false;
            foreach (var k in WowMemorySession.HerbKeywords)
            {
                if (herbName.Contains(k)) { isHerb = true; break; }
            }

            bool isOre = false;
            foreach (var k in WowMemorySession.OreKeywords)
            {
                if (oreName.Contains(k)) { isOre = true; break; }
            }

            Assert.IsTrue(isHerb, "菌柄应被识别为草药");
            Assert.IsTrue(isOre, "铋矿石应被识别为矿石");
        }

        [TestMethod]
        public void TestExactWowWindowTitle()
        {
            Assert.IsTrue(WowProcessManager.IsExactWowWindowTitle("魔兽世界"));
            Assert.IsTrue(WowProcessManager.IsExactWowWindowTitle("World of Warcraft"));
            Assert.IsTrue(WowProcessManager.IsExactWowWindowTitle("world of warcraft"));

            Assert.IsFalse(WowProcessManager.IsExactWowWindowTitle("魔兽世界 - 浏览器"));
            Assert.IsFalse(WowProcessManager.IsExactWowWindowTitle("WoW_NoNoNo"));
            Assert.IsFalse(WowProcessManager.IsExactWowWindowTitle("Firestorm"));
            Assert.IsFalse(WowProcessManager.IsExactWowWindowTitle("World of Warcraft Launcher"));
        }
    }
}
