#pragma once
#include <string>
#include <vector>
#include <map>
#include <chrono>
#include <cmath>
#include <algorithm>
#include "WowMemorySession.h"

struct TrackedResourceItem {
    int itemId = 0;
    std::string name;
    std::string category; // "矿石" 或 "草药"
    int initialCount = 0;
    int currentCount = 0;

    int GetGainedCount() const {
        return (std::max)(0, currentCount - initialCount);
    }

    double CalculatePerHour(double elapsedHours) const {
        if (elapsedHours < (5.0 / 3600.0) || GetGainedCount() <= 0) return 0.0;
        return static_cast<double>(GetGainedCount()) / elapsedHours;
    }
};

class GatheringTracker {
public:
    std::vector<TrackedResourceItem> items;
    std::chrono::steady_clock::time_point startTime;
    bool isRunning = false;
    bool baselineInitialized = false;

    int totalSlots = 0;
    int occupiedSlots = 0;
    int freeSlots = 0;

    GatheringTracker() {
        InitCatalog();
    }

    void InitCatalog() {
        items.clear();
        static const struct CatalogEntry {
            int id;
            const char* name;
            const char* cat;
        } catalog[] = {
            // TWW 矿石
            { 210210, "铋矿石", "矿石" },
            { 210214, "铁爪矿石", "矿石" },
            { 212234, "亚基矿石", "矿石" },
            { 212450, "虚空石", "矿石" },
            // TWW 草药
            { 210259, "菌柄", "草药" },
            { 210242, "荷草", "草药" },
            { 204443, "诱毒之花", "草药" },
            { 212175, "心绽花", "草药" },
            { 223847, "阿拉索之矛", "草药" },
            // MoP 矿石草药
            { 72092, "幽冥铁矿石", "矿石" },
            { 72093, "凯帕琥珀", "矿石" },
            { 72094, "黑微粒", "矿石" },
            { 72103, "白微粒", "矿石" },
            { 72234, "绿茶叶", "草药" },
            { 72235, "丝草", "草药" },
            { 72237, "雨罂粟", "草药" },
            { 72238, "雪百合", "草药" },
            { 72236, "愚人菇", "草药" },
            { 79010, "黄金莲", "草药" }
        };

        for (const auto& e : catalog) {
            TrackedResourceItem item;
            item.itemId = e.id;
            item.name = e.name;
            item.category = e.cat;
            items.push_back(item);
        }
    }

    void Start() {
        startTime = std::chrono::steady_clock::now();
        isRunning = true;
        baselineInitialized = false;
    }

    void Reset() {
        startTime = std::chrono::steady_clock::now();
        baselineInitialized = false;
        for (auto& it : items) {
            it.initialCount = it.currentCount;
        }
    }

    double GetElapsedHours() const {
        if (!isRunning) return 0.0;
        auto now = std::chrono::steady_clock::now();
        std::chrono::duration<double> sec = now - startTime;
        return sec.count() / 3600.0;
    }

    void Update(const WowMemorySession::InventoryInfo& inv) {
        totalSlots = inv.totalSlots;
        occupiedSlots = inv.occupiedSlots;
        freeSlots = inv.freeSlots;

        for (auto& it : items) {
            auto findIt = inv.items.find(it.itemId);
            int current = (findIt != inv.items.end()) ? findIt->second : 0;
            it.currentCount = current;
            if (!baselineInitialized) {
                it.initialCount = current;
            }
        }
        baselineInitialized = true;
    }

    int GetTotalGained() const {
        int total = 0;
        for (const auto& it : items) {
            if (it.category == "矿石") {
                total += it.GetGainedCount();
            }
        }
        return total;
    }

    double GetTotalPerHour() const {
        double hours = GetElapsedHours();
        if (hours < (5.0 / 3600.0) || GetTotalGained() <= 0) return 0.0;
        return static_cast<double>(GetTotalGained()) / hours;
    }

    // 格式化输出简明背包矿石与空位摘要：只显示矿石存量与背包空位
    std::string GetInventorySummary() const {
        std::string summary;
        int activeCount = 0;
        for (const auto& it : items) {
            if (it.category == "矿石" && it.currentCount > 0) {
                if (activeCount > 0) summary += " | ";
                summary += it.name + ":" + std::to_string(it.currentCount);
                if (it.GetGainedCount() > 0) {
                    summary += "(+" + std::to_string(it.GetGainedCount()) + ")";
                }
                activeCount++;
            }
        }
        if (summary.empty()) {
            return "空位: " + std::to_string(freeSlots) + "格 (无矿石)";
        }
        return summary + " [空位:" + std::to_string(freeSlots) + "]";
    }
};
