using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 玩家数据：现金、财富值、声望、AI利己值和现金偏好。
    /// </summary>
    [System.Serializable]
    public class PlayerData
    {
        public string playerName;
        public int cash = 2000;
        public int propertyValue = 0;
        public int reputation = 50;

        // AI专用属性
        public int selfInterest;           // 利己值 (-30~30)
        public float cashPreference;       // 现金偏好值 (0.1~0.5)

        /// <summary>财富值 = 现金 + 房产估价</summary>
        public int Wealth => cash + propertyValue;

        /// <summary>现金占财富值的比例</summary>
        public float CashRatio => Wealth > 0 ? (float)cash / Wealth : 1f;

        /// <summary>
        /// AI是否愿意花钱（买地/升级/付费选项）
        /// 现金占比 >= 现金偏好时才愿意
        /// </summary>
        public bool WillingToSpend => CashRatio >= cashPreference;

        /// <summary>初始化AI属性</summary>
        public void InitAIAttributes()
        {
            selfInterest = Random.Range(-30, 31);
            cashPreference = Random.Range(0.1f, 0.51f);
        }
    }
}
