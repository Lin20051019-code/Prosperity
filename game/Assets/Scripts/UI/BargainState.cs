using System.Collections.Generic;
using System.IO;
using System.Text;
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

        // ===== 常量 =====
        public static readonly float[] firstRoundAdvantage = { -0.10f, -0.10f, 0.30f, 0.30f };
        public static readonly float[] priceChangeRate = { 0.20f, -0.10f, 0.20f, -0.20f };
        public static readonly int[] reputationChange = { 5, 3, -2, -3 };
        public static readonly string[] cardNames = { "交个朋友", "实价交易", "看人下菜", "极限压价" };

        // ===== 对话文案 =====
        private static readonly Dictionary<(int card, bool seller, int round), string> dialogDict = new Dictionary<(int, bool, int), string>();
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

        public static void LoadDialogs()
        {
            if (dialogsLoaded) return;
            dialogsLoaded = true;

            string path = "Assets/Bargain文案 - Sheet1.csv";
            if (!File.Exists(path)) { Debug.LogError($"[Bargain] Dialog CSV not found: {path}"); return; }

            var lines = new List<string>();
            using (var reader = new StreamReader(path, new UTF8Encoding(false)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    lines.Add(line);
            }
            if (lines.Count < 2) return;

            int[] cardForCol = { 3, 2, 1, 0, 3, 2, 1, 0 };
            bool[] sellerForCol = { true, true, true, true, false, false, false, false };
            int[] roundForRow = { 0, 1, 2, 3, 4, 5, 6 };

            for (int rowIdx = 1; rowIdx <= 7 && rowIdx < lines.Count; rowIdx++)
            {
                string line = lines[rowIdx].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] fields = ParseCSVLine(line);
                int roundType = roundForRow[rowIdx - 1];

                for (int col = 0; col < 8 && col + 1 < fields.Length; col++)
                {
                    string text = fields[col + 1].Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    dialogDict[(cardForCol[col], sellerForCol[col], roundType)] = text;
                }
            }
            Debug.Log($"[Bargain] Loaded {dialogDict.Count} dialog entries");
        }

        public static string GetDialog(int cardIndex, bool isSeller, int roundType)
        {
            if (dialogDict.TryGetValue((cardIndex, isSeller, roundType), out string text))
                return text;
            return null;
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
