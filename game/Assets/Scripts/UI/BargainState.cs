using System.Collections.Generic;
using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// Bargain跨场景共享状态。在Select/Offer/Result三个场景间传递数据。
    /// </summary>
    public static class BargainState
    {
        // ===== 初始数据（从DiceRollController.BargainData复制） =====
        public static int marketPrice;
        public static int sellerIndex;
        public static int buyerIndex;
        public static int sellerRep;
        public static int buyerRep;
        public static int sellerSelfInterest;
        public static int buyerSelfInterest;
        public static bool playerIsBuyer;
        public static string sellerName;
        public static string buyerName;

        // ===== 角色判定 =====
        public static bool isPlayerSeller;
        public static bool isPlayerBuyer;
        public static bool isAIVsAI;

        // ===== 过程状态 =====
        public static int selectedCardIndex = -1;
        public static int aiCardIndex = -1;
        public static int currentRound = 1;
        public static float sellerOffer;
        public static float buyerOffer;

        // ===== 结果 =====
        public static bool resultCompleted;
        public static bool resultSuccess;
        public static int resultFinalPrice;
        public static string resultLine; // 成交/失败的收尾台词，由报价场景生成，结果场景展示

        // ===== 常量 =====
        public static readonly float[] firstRoundAdvantage = { -0.10f, -0.10f, 0.30f, 0.30f };
        public static readonly float[] priceChangeRate = { 0.20f, -0.10f, 0.20f, -0.20f };
        public static readonly int[] reputationChange = { 5, 3, -2, -3 };
        public static readonly string[] cardNames = { "交个朋友", "实价交易", "看人下菜", "极限压价" };

        /// <summary>安全字体获取（Vonwaon像素字体优先，回退内置字体）——供动态创建的UI文本使用（v4移植）</summary>
        public static Font GetSafeFont()
        {
            var f = Resources.Load<Font>("VonwaonBitmap-16px");
            if (f != null) return f;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ===== i18n（v4.2）=====
        /// <summary>当前语言（"zh"中文 / "en"英文，默认中文）。主菜单语言按钮切换。</summary>
        public static string language = "zh";

        // ===== 对话系统（台词池 + 情绪标签 + 动态变量，v3移植） =====
        /// <summary>台词：text为文案（支持{opponent}{price}{diff}{offer}{final}占位符），emotion为表情索引 0平1怒2惊3得意</summary>
        public struct DialogLine
        {
            public string text;
            public int emotion;
        }

        private static readonly Dictionary<string, List<DialogLine>> dialogPools = new Dictionary<string, List<DialogLine>>();
        private static readonly Dictionary<string, int> lastPick = new Dictionary<string, int>();
        private static bool dialogsLoaded = false;

        /// <summary>从DiceRollController.BargainData初始化共享状态</summary>
        public static void InitFromBargainData()
        {
            marketPrice = DiceRollController.BargainData.marketPrice;
            sellerIndex = DiceRollController.BargainData.sellerIndex;
            buyerIndex = DiceRollController.BargainData.buyerIndex;
            sellerRep = DiceRollController.BargainData.sellerRep;
            buyerRep = DiceRollController.BargainData.buyerRep;
            sellerSelfInterest = DiceRollController.BargainData.sellerSelfInterest;
            buyerSelfInterest = DiceRollController.BargainData.buyerSelfInterest;
            playerIsBuyer = DiceRollController.BargainData.playerIsBuyer;
            sellerName = DiceRollController.BargainData.sellerName;
            buyerName = DiceRollController.BargainData.buyerName;

            isPlayerBuyer = playerIsBuyer;
            isPlayerSeller = !playerIsBuyer && sellerIndex == 0;
            isAIVsAI = sellerIndex != 0 && buyerIndex != 0;

            selectedCardIndex = -1;
            aiCardIndex = -1;
            currentRound = 1;
            sellerOffer = 0;
            buyerOffer = 0;
            resultCompleted = false;
            resultLine = null;
        }

        /// <summary>AI选择人格卡</summary>
        public static int SelectAIPersonality(int reputation, int selfInterest)
        {
            float x = (reputation + selfInterest) / 25f;
            if (x >= 3f) return 3;
            if (x >= 2f) return 2;
            if (x >= 1f) return 1;
            return 0;
        }

        /// <summary>计算首轮报价</summary>
        public static void CalculateFirstRoundOffers()
        {
            float sellerAdv = firstRoundAdvantage[isPlayerSeller ? selectedCardIndex : aiCardIndex];
            float buyerAdv = firstRoundAdvantage[isPlayerBuyer ? selectedCardIndex : aiCardIndex];
            sellerOffer = marketPrice * (1f + sellerAdv + sellerRep / 200f);
            buyerOffer = marketPrice * (1f - buyerAdv - buyerRep / 200f);
        }

        /// <summary>检查报价重叠</summary>
        public static bool CheckOverlap() => buyerOffer >= sellerOffer;

        // ===== 对话系统 =====

        /// <summary>从Resources/BargainDialogs.csv加载台词池（列：卡牌,角色,阶段,情绪,台词；v3台词池系统）</summary>
        public static void LoadDialogs()
        {
            if (dialogsLoaded) return;
            dialogsLoaded = true;

            var asset = Resources.Load<TextAsset>(language == "en" ? "BargainDialogs_en" : "BargainDialogs");
            if (asset == null) { Debug.LogError("[Bargain] Resources/BargainDialogs.csv not found"); return; }

            dialogPools.Clear();
            // 按CSV引号规则切逻辑行（沿用SplitLogicalLines，防引号内换行破坏行结构）
            var rows = SplitLogicalLines(asset.text);
            int count = 0;
            for (int i = 1; i < rows.Count; i++)
            {
                string row = rows[i].Trim();
                if (string.IsNullOrEmpty(row)) continue;

                string[] fields = ParseCSVLine(row);
                if (fields.Length < 5) continue;

                string key = $"{fields[0].Trim()}|{fields[1].Trim()}|{fields[2].Trim()}";
                var line = new DialogLine { text = fields[4].Trim(), emotion = EmotionIndex(fields[3].Trim()) };
                if (!dialogPools.TryGetValue(key, out var pool))
                    dialogPools[key] = pool = new List<DialogLine>();
                pool.Add(line);
                count++;
            }
            Debug.Log($"[Bargain] Loaded {count} dialog lines into {dialogPools.Count} pools");
        }

        /// <summary>把整段CSV文本切成逻辑行：引号内的换行不切行（属于单元格内容）</summary>
        private static List<string> SplitLogicalLines(string text)
        {
            var lines = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;
            foreach (char c in text)
            {
                if (c == '"') inQuotes = !inQuotes;

                if (c == '\n' && !inQuotes)
                {
                    lines.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c != '\r')
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) lines.Add(sb.ToString());
            return lines;
        }

        private static int EmotionIndex(string tag)
        {
            switch (tag)
            {
                case "怒": return 1;
                case "惊": return 2;
                case "意": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// 从台词池随机取一条（不与上次重复）并替换变量。
        /// 阶段取值：开场 / 选项1~3（说话方的让步姿态）/ 让步低~高（对对方让步幅度的反应）/ 成交 / 失败
        /// </summary>
        public static DialogLine GetLine(int cardIndex, bool isSeller, string stage,
            string opponentName = null, int? myPrice = null, int? diff = null, int? offer = null, int? final = null)
        {
            var key = $"{cardIndex}|{(isSeller ? "卖" : "买")}|{stage}";
            if (!dialogPools.TryGetValue(key, out var pool) || pool.Count == 0)
                return new DialogLine { text = null, emotion = 0 };

            int pick = 0;
            if (pool.Count > 1)
            {
                lastPick.TryGetValue(key, out int last);
                do { pick = Random.Range(0, pool.Count); } while (pick == last);
            }
            lastPick[key] = pick;

            var chosen = pool[pick];
            string text = chosen.text
                .Replace("{opponent}", opponentName ?? "")
                .Replace("{price}", myPrice?.ToString() ?? "")
                .Replace("{diff}", diff?.ToString() ?? "")
                .Replace("{offer}", offer?.ToString() ?? "")
                .Replace("{final}", final?.ToString() ?? "");
            return new DialogLine { text = text, emotion = chosen.emotion };
        }

        /// <summary>按对方让步幅度（价差收敛比例）选择反应阶段</summary>
        public static string ReactionStage(float movePct)
        {
            if (movePct < 0.35f) return "让步低";
            if (movePct < 0.65f) return "让步中";
            return "让步高";
        }

        private static string[] ParseCSVLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inQuotes = !inQuotes;
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(line.Substring(start, i - start).Trim('"', ' '));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start).Trim('"', ' '));
            return result.ToArray();
        }

        public static string FormatPercent(float value)
        {
            int percent = Mathf.RoundToInt(value * 100f);
            return percent >= 0 ? $"+{percent}%" : $"{percent}%";
        }
    }
}
