namespace FirestormTwwMonitor.Core.Memory
{
    /// <summary>
    /// WoW 11.2.5.63704 (The War Within - Firestorm) 核心内存偏移量常量
    /// 来源：WRobot 11.2.5.63704 原生反编译核心库 (Class559 / Class574)
    /// </summary>
    public static class Offsets
    {
        public const string TARGET_BUILD = "11.2.5.63704";

        #region Game State Offsets (权威游戏与连接状态)
        /// <summary>
        /// 权威游戏状态标志位 (InGame)。
        /// 单次读取 1 字节：> 0 表示角色在世界中运行；== 0 表示在登录/掉线/角色选择界面。
        /// 对应 WRobot Class574.smethod_2(85864984uL, 77675624uL) => 0x4A13C68
        /// </summary>
        public const long OFF_IN_GAME = 0x4A13C68;

        /// <summary>
        /// 是否处于加载过图界面 (IsLoadingOrConnecting) 指针偏移。
        /// 对应 WRobot Class574.smethod_3(105878192uL, 71527248uL) => 0x4436B50
        /// </summary>
        public const long OFF_IS_LOADING_PTR = 0x4436B50;
        #endregion

        #region Object Manager & Player Offsets
        public const long OBJ_MGR_GLOBAL = 0x47DEE48;
        public const long OFF_OBJ_BUCKETS = 0x08;
        public const long OFF_OBJ_COUNT = 0x18;
        public const long OFF_NODE_OBJ = 0x28;
        public const long OFF_NODE_GUID = 0x18;
        public const long OFF_OBJ_GUID = 0x18;
        public const long OFF_OBJ_TYPE = 0x08;

        public const byte TYPE_ITEM = 1;
        public const byte TYPE_CONTAINER = 2;
        public const byte TYPE_UNIT = 5;
        public const byte TYPE_PLAYER_LOCAL = 7;
        public const byte TYPE_GAMEOBJECT = 8;

        public const long OFF_ITEM_ID = 0x88;
        public const long OFF_ITEM_STACK_COUNT = 0x188;

        public const long OFF_UNIT_MOVEMENT = 0xD8;
        public const long OFF_POS = 0x18;
        public const long OFF_FACING = 0x28;
        public const long OFF_FLAGS = 0x190;

        /// <summary>
        /// 11.2.5 客户端全局真实角色名内存偏移量
        /// </summary>
        public const long OFF_NAME_GLOBAL = 0x47F98B0;

        /// <summary>
        /// 玩家等级偏移量
        /// </summary>
        public const long OFF_LEVEL = 6960;

        /// <summary>
        /// 玩家生命值与最大生命值偏移量
        /// </summary>
        public const long OFF_HEALTH = 6944;
        public const long OFF_MAX_HEALTH = 6952;
        #endregion

        #region Script & UI Execution Offsets
        /// <summary>
        /// 远程注入执行 Lua 代码入口函数 (FrameScript_Execute) 偏移量
        /// </summary>
        public const long OFF_FRAMESCRIPT_EXECUTE = 0x617BDD0;

        /// <summary>
        /// CVar 系统名称存储基址
        /// </summary>
        public const long CVAR_NAME_TABLE = 0x4371C50;
        #endregion

        #region Network Packet Injection Offsets (Build 63704)
        /// <summary>
        /// NetClient 单例指针全局偏移量（指向 NetClient* 的指针）
        /// 用于 CMSG_GOSSIP_HELLO 报文注入（远程对话，完全绕过客户端距离检测）
        /// TODO: 使用 IDA/Ghidra 在 WoW.exe 中搜索 "ClientServices::SendGossipHello"
        ///       或 cross-ref CMSG_GOSSIP_HELLO opcode 来定位，然后填入正确偏移
        /// </summary>
        public const long OFF_NET_CLIENT_INSTANCE = 0; // ← 待填写（0 = 功能禁用，将触发 Lua 回退）

        /// <summary>
        /// NetClient::Send 函数偏移量（方案B 直接发包核心）
        /// 函数原型（x64）：void NetClient::Send(CDataStore* packet, uint connectionIndex)
        /// TODO: 同上，需要逆向确认
        /// </summary>
        public const long OFF_NET_CLIENT_SEND = 0; // ← 待填写

        /// <summary>
        /// CMSG_GOSSIP_HELLO 操作码（服务端接收 NPC 对话请求）
        /// WoW 11.2.5.63704 Firestorm — 实际值需从 WoW.exe 或 Firestorm 源码确认
        /// 参考：https://github.com/TrinityCore/TrinityCore (Opcodes.h)
        /// </summary>
        public const uint OPCODE_GOSSIP_HELLO = 0; // ← 待填写（TrinityCore 11.x 值约 0x???）
        #endregion
    }
}
