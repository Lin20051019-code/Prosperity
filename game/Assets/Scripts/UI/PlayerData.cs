using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 性格槽定义：性格与角色解绑，每局开局从5套中洗牌3套随机分配给AI（Bot v2）。
    /// 数值经5000局蒙特卡洛验证：胜36/败26/平38、时长6.9分、救援0.55次/局，基线无扰动。
    /// </summary>
    [System.Serializable]
    public class PersonaSlot
    {
        public string name;              // 槽名（播报暗示/调试）
        public int safeLine;             // 现金安全线：低于此值不消费
        public float prosperityCare;     // 繁荣意识：≥0.7危机区停消费救市；≥0.9触发救援
        public float bargainToughness;   // 谈判狠度：心理价 = 市价 × 此系数

        public static readonly PersonaSlot[] Table =
        {
            new PersonaSlot { name = "囤地豪客", safeLine = 300, prosperityCare = 0.2f, bargainToughness = 0.75f }, // 疯狂买地，留300就敢出手
            new PersonaSlot { name = "现金奶牛", safeLine = 700, prosperityCare = 0.2f, bargainToughness = 0.90f }, // 攒钱成瘾，出手大方但频率低
            new PersonaSlot { name = "慈善家",   safeLine = 500, prosperityCare = 0.9f, bargainToughness = 0.90f }, // 危机区勒紧裤腰带；主动救援最穷者
            new PersonaSlot { name = "均衡商人", safeLine = 500, prosperityCare = 0.5f, bargainToughness = 0.80f }, // 标准行为
            new PersonaSlot { name = "吸血鬼",   safeLine = 400, prosperityCare = 0.1f, bargainToughness = 0.70f }, // 谈判最狠，危机区也照买不误
        };
    }

    /// <summary>
    /// 玩家数据：现金、财富值、声望、AI性格槽。
    /// 数值体系见《数值设计文档v2.1》（初始现金1300、保命线500、最低操作门槛300）
    /// </summary>
    [System.Serializable]
    public class PlayerData
    {
        public string playerName;
        public int cash = 1300;
        public int propertyValue = 0;
        public int reputation = 50;
        public bool alive = true;   // 破产制：资不抵债出局后置false（全员失败）

        // AI专用属性
        public int selfInterest;           // 利己值 (-30~30)（保留：影响人格卡选择）
        public float cashPreference;       // 现金偏好值（保留字段，v2.1起不再用于消费判断）

        // ===== 性格槽系统（Bot v2）=====
        public int safeLine = 500;             // 本局人设：现金安全线
        public float prosperityCare = 0.5f;    // 本局人设：繁荣意识
        public float bargainToughness = 0.8f;  // 本局人设：谈判狠度
        public string personaName = "均衡商人"; // 本局人设名（播报/调试）

        /// <summary>财富值 = 现金 + 房产估价</summary>
        public int Wealth => cash + propertyValue;

        /// <summary>现金占财富值的比例（保留字段，v2.1起不再驱动消费）</summary>
        public float CashRatio => Wealth > 0 ? (float)cash / Wealth : 1f;

        /// <summary>保命线：现金≥本局人设安全线才允许消费（囤地豪客300/现金奶牛700…）</summary>
        public bool IsSafe => cash >= safeLine;

        /// <summary>能否负担某笔开销（最低操作门槛300）</summary>
        public bool CanAfford(int cost) => cash >= Mathf.Max(300, cost);

        /// <summary>v2.1 兼容接口：等价于本局人设安全线判断</summary>
        public bool WillingToSpend => IsSafe;

        /// <summary>危机区是否勒紧裤腰带（繁荣意识≥0.7 的AI停止非必要消费救市）</summary>
        public bool WithholdsInCrisis => prosperityCare >= 0.7f;

        /// <summary>是否具备慈善家救援行为（繁荣意识≥0.9）</summary>
        public bool IsRescuer => prosperityCare >= 0.9f;

        /// <summary>初始化AI属性（利己值保留原逻辑；性格槽由 DiceRollController 开局分配）</summary>
        public void InitAIAttributes()
        {
            selfInterest = Random.Range(-30, 31);
            cashPreference = Random.Range(0.1f, 0.51f);
        }

        /// <summary>应用一套性格槽</summary>
        public void ApplyPersona(PersonaSlot slot)
        {
            safeLine = slot.safeLine;
            prosperityCare = slot.prosperityCare;
            bargainToughness = slot.bargainToughness;
            personaName = slot.name;
        }
    }
}
