using System;
using System.IO;
using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 存档数据：一局游戏的完整快照（JsonUtility 序列化为 JSON）。
    /// 字段与 DiceRollController 的运行时状态一一对应。
    /// </summary>
    [Serializable]
    public class SaveData
    {
        // ===== 全局/回合 =====
        public int currentTurn;            // 0=玩家, 1-3=AI
        public int currentRound;           // 当前回合数
        public int currentProsperity;      // 繁荣值
        public int borderClosedRounds;      // 边境封锁剩余回合数
        public bool gameEnded;
        public string savedAt;             // 保存时间（槽位显示用）

        // ===== 玩家（4人）=====
        public string[] playerNames = new string[4];
        public int[] playerCash = new int[4];
        public int[] playerProperty = new int[4];
        public int[] playerRep = new int[4];
        public bool[] playerAlive = new bool[4];
        public int[] playerSelfInterest = new int[4];
        public float[] playerCashPref = new float[4];
        // 本局性格槽（AI人设，读档后AI行为与存档时一致）
        public string[] personaNames = new string[4];
        public int[] personaSafeLine = new int[4];
        public float[] personaProsperityCare = new float[4];
        public float[] personaBargainToughness = new float[4];

        // ===== 棋子位置与回合标记 =====
        public int[] tileIndices = new int[4];
        public bool[] skipNextTurn = new bool[4];
        public bool[] rerollNextTurn = new bool[4];

        // ===== 建筑 =====
        public int[] buildingLevel;
        public int[] buildingOwner;
        public int[] buildingBaseValue;
    }

    /// <summary>
    /// 存档管理器：3 个槽位，JSON 文件持久化到 Application.persistentDataPath。
    /// 主菜单「继续游戏」通过 pendingLoadSlot 把所选槽位传给 GameScene。
    /// </summary>
    public static class SaveLoadManager
    {
        public const int SlotCount = 3;
        public const int NoSlot = -1;

        /// <summary>主菜单选择存档后写入；GameScene.Start() 消费后立即归位，防跨局残留。</summary>
        public static int pendingLoadSlot = NoSlot;

        private static string SlotPath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.json");
        }

        /// <summary>指定槽位是否有存档。</summary>
        public static bool HasSave(int slot)
        {
            return slot >= 1 && slot <= SlotCount && File.Exists(SlotPath(slot));
        }

        /// <summary>是否存在任意存档（主菜单「继续游戏」按钮可用性判断）。</summary>
        public static bool HasAnySave()
        {
            for (int i = 1; i <= SlotCount; i++)
                if (HasSave(i)) return true;
            return false;
        }

        /// <summary>写入存档（时间戳在此打上）。</summary>
        public static void SaveToSlot(int slot, SaveData data)
        {
            data.savedAt = DateTime.Now.ToString("yyyy/M/d HH:mm");
            File.WriteAllText(SlotPath(slot), JsonUtility.ToJson(data));
            Debug.Log($"[Save] 存档槽{slot}已写入：第{data.currentRound}回合（{data.savedAt}）");
        }

        /// <summary>读取存档；无存档或解析失败返回 null。</summary>
        public static SaveData LoadFromSlot(int slot)
        {
            if (!HasSave(slot)) return null;
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SlotPath(slot)));
                if (data == null || data.playerNames == null)
                {
                    Debug.LogError($"[Save] 存档槽{slot}内容无效");
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] 存档槽{slot}读取失败: {e.Message}");
                return null;
            }
        }

        /// <summary>槽位显示文字：空槽位返回"空"，否则"第N回合 · 保存时间"。</summary>
        public static string GetSlotDisplay(int slot)
        {
            var d = LoadFromSlot(slot);
            if (d == null) return "空";
            return $"第{d.currentRound}回合 · {d.savedAt}";
        }
    }
}
