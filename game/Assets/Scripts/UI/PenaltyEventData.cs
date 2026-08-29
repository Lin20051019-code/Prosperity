using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 惩罚事件数据，可配置的ScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "NewPenaltyEvent", menuName = "SheNicest/PenaltyEvent")]
    public class PenaltyEventData : ScriptableObject
    {
        [Header("基础信息")]
        public string eventName;
        [TextArea] public string description;

        [Header("效果类型")]
        public PenaltyEffect effectType;

        [Header("效果数值")]
        public int cashChange;       // 现金变化（负数=扣除）
        public int reputationChange; // 声望变化
        public bool skipNextTurn;     // 是否跳过下回合

        [Header("说明")]
        [TextArea] public string effectDescription; // 显示给玩家的效果说明（加粗）
        [TextArea] public string flavorText;         // 文案（正常字体）
    }

    /// <summary>
    /// 效果类型枚举（惩罚/事件/奖励共用）
    /// </summary>
    public enum PenaltyEffect
    {
        // 惩罚
        CashLoss,           // 扣现金
        ReputationLoss,     // 扣声望
        CashAndRepLoss,     // 扣现金+声望
        SkipTurn,           // 跳过回合
        SellProperty,       // 卖地（后期实现）
        DowngradeProperty,  // 房产降级（后期实现）
        // 奖励
        CashGain,           // 获得现金
        ReputationGain,     // 获得声望
        CashGainPercent,    // 现金百分比增加
        CashLossPercent,    // 现金百分比减少
        // 通用
        Custom              // 自定义/特殊效果
    }
}
