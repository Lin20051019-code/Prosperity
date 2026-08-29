using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 建筑数据：等级、所有者、价值、市场价格。
    /// ownerIndex: -2=政府所有, -1=无主(已废弃), 0-3=玩家索引
    /// </summary>
    [System.Serializable]
    public class BuildingData
    {
        public int level = 0;          // 0=空地, 1-3=建筑等级
        public int ownerIndex = -2;    // -2=政府所有, 0-3=玩家索引
        public int baseValue = 100;    // 基础购买价格

        /// <summary>当前总价值（0级=100, 1级=200, 2级=300, 3级=400）</summary>
        public int TotalValue => baseValue + level * 100;

        /// <summary>升级费用（购买=100, 1→2=100, 2→3=100）</summary>
        public int UpgradeCost => 100;

        /// <summary>缓存的当前市场价格，由 SetMarketPrice 设置</summary>
        public int currentMarketPrice { get; private set; }

        /// <summary>设置市场价格（在繁荣值更新后统一调用）</summary>
        public void SetMarketPrice(int prosperity)
        {
            float baseMultiplier = (prosperity + 50f) / 100f;
            float randomMultiplier = Random.Range(0.96f, 1.05f);
            currentMarketPrice = Mathf.RoundToInt(TotalValue * baseMultiplier * randomMultiplier);
        }

        /// <summary>获取缓存的当前市场价格</summary>
        public int GetMarketPrice(int prosperity = 0)
        {
            return currentMarketPrice;
        }

        /// <summary>租金 = 市场价格 × 10%</summary>
        public int GetRent(int prosperity = 0)
        {
            return Mathf.RoundToInt(currentMarketPrice * 0.1f);
        }

        /// <summary>是否可以升级（最高3级）</summary>
        public bool CanUpgrade => level < 3;

        /// <summary>是否政府所有</summary>
        public bool IsGovernmentOwned => ownerIndex == -2;

        /// <summary>是否有主（玩家拥有）</summary>
        public bool HasOwner => ownerIndex >= 0;
    }
}
