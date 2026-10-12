#pragma once
#include <cstdint>

namespace WowOffsets {
    constexpr const char* TARGET_BUILD = "11.2.5.63704";

    // 权威游戏状态标志位
    constexpr int64_t OFF_IN_GAME = 0x4A13C68;
    constexpr int64_t OFF_IS_LOADING_PTR = 0x4436B50;

    // 对象管理器与全局基址
    constexpr int64_t OBJ_MGR_GLOBAL = 0x47DEE48;
    constexpr int64_t OFF_OBJ_BUCKETS = 0x08;
    constexpr int64_t OFF_OBJ_COUNT = 0x18;
    constexpr int64_t OFF_NODE_OBJ = 0x28;
    constexpr int64_t OFF_OBJ_TYPE = 0x08;

    constexpr uint8_t TYPE_ITEM = 1;
    constexpr uint8_t TYPE_CONTAINER = 2;
    constexpr uint8_t TYPE_UNIT = 5;
    constexpr uint8_t TYPE_PLAYER_LOCAL = 7;
    constexpr uint8_t TYPE_GAMEOBJECT = 8;

    constexpr int64_t OFF_ITEM_ID = 0x88;
    constexpr int64_t OFF_ITEM_STACK_COUNT = 0x188;

    constexpr int64_t OFF_UNIT_MOVEMENT = 0xD8;
    constexpr int64_t OFF_POS = 0x18;

    // 全局真实角色名内存偏移
    constexpr int64_t OFF_NAME_GLOBAL = 0x47F98B0;

    // 等级与生命值
    constexpr int64_t OFF_LEVEL = 6960;
    constexpr int64_t OFF_HEALTH = 6944;
    constexpr int64_t OFF_MAX_HEALTH = 6952;
}
