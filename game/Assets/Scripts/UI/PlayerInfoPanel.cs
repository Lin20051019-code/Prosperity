using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 玩家信息面板：状态栏图片背景 + 每行一个文本框显示数值。
    /// </summary>
    public class PlayerInfoPanel : MonoBehaviour
    {
        [Header("Stat Texts")]
        [SerializeField] private Text wealthText;   // 财富值
        [SerializeField] private Text cashText;      // 现金
        [SerializeField] private Text reputationText; // 声望

        [Header("Colors")]
        [SerializeField] private Color wealthColor = new Color(0.620f, 0.251f, 0.314f, 1f);   // #9E4050
        [SerializeField] private Color cashColor = new Color(0.525f, 0.384f, 0.067f, 1f);     // #866211
        [SerializeField] private Color reputationColor = new Color(0.278f, 0.459f, 0.141f, 1f); // #477524

        [Header("Data")]
        [SerializeField] private PlayerData data = new PlayerData();

        private void Start()
        {
            UpdateDisplay();
        }

        public void Init(string name, Color avatarColor)
        {
            data.playerName = name;
            UpdateDisplay();
        }

        public void UpdateDisplay()
        {
            if (cashText != null)
            {
                cashText.text = $"现金 {data.cash}";
                cashText.color = cashColor;
                cashText.fontStyle = FontStyle.Bold;
            }
            if (wealthText != null)
            {
                wealthText.text = $"财富 {data.Wealth}";
                wealthText.color = wealthColor;
                wealthText.fontStyle = FontStyle.Bold;
            }
            if (reputationText != null)
            {
                reputationText.text = $"声望 {data.reputation}";
                // v2.2：声望<45 恶名预警红字（危机维持费翻倍/通胀起征点更低的提前预告）
                reputationText.color = data.reputation < 45 ? new Color(0.65f, 0.12f, 0.1f) : reputationColor;
                reputationText.fontStyle = FontStyle.Bold;
            }
        }

        public PlayerData Data => data;
    }
}
