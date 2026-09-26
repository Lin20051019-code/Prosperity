using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 卡牌面板：翻牌动画 → 显示事件 → 确认后应用效果。
    /// 支持惩罚(红)、事件(蓝)、奖励(金)三种模式。
    /// 事件数据从 CSV 文件读取。
    /// </summary>
    public class PenaltyCardPanel : MonoBehaviour
    {
        public enum CardType { Penalty, Event, Reward }

        [Header("UI References")]
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform cardTransform;
        [SerializeField] private Text titleText;           // 顶部标题（⚠ 惩罚事件 ⚠ 等）
        [SerializeField] private Text eventNameText;
        [SerializeField] private Text effectText;
        [SerializeField] private Text flavorText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Image cardImage;          // 卡牌背景（用于改颜色）

        [Header("Animation")]
        [SerializeField] private float flipDuration = 0.6f;
        [SerializeField] private float slideDuration = 0.5f;
        [SerializeField] private float slideDelay = 0.3f;
        [SerializeField] private float slideOffset = 600f;

        [Header("Card Background Sprites")]
        [SerializeField] private Sprite penaltySprite;
        [SerializeField] private Sprite eventSprite;
        [SerializeField] private Sprite rewardSprite;

        [Header("Data Source (Resources 相对路径，不含扩展名)")]
        [SerializeField] private string penaltyCsvPath = "Data/penalty_events";
        [SerializeField] private string eventCsvPath = "Data/event_events";
        [SerializeField] private string rewardCsvPath = "Data/reward_events";

        // 各类型事件池
        private List<PenaltyEventData> penaltyPool = new List<PenaltyEventData>();
        private List<PenaltyEventData> eventPool = new List<PenaltyEventData>();
        public List<PenaltyEventData> rewardPool = new List<PenaltyEventData>();

        // 调试面板用：暴露三个卡池（rewardPool 本为 public）
        public List<PenaltyEventData> PenaltyPool => penaltyPool;
        public List<PenaltyEventData> EventPool => eventPool;

        // 当前使用的池和类型
        private List<PenaltyEventData> currentPool;
        private CardType currentType;

        private System.Action onConfirmCallback;

        [Header("Font")]
        [SerializeField] private Font pixelFont;

        private void Start()
        {
            if (confirmButton != null)
                confirmButton.onClick.AddListener(OnConfirm);

            // v4.2 i18n：按语言加载（英文版CSV带第9列eventKey中文逻辑键）
            bool isEn = BargainState.language == "en";
            penaltyPool = LoadCSV(isEn ? penaltyCsvPath + "_en" : penaltyCsvPath);
            eventPool = LoadCSV(isEn ? eventCsvPath + "_en" : eventCsvPath);
            rewardPool = LoadCSV(isEn ? rewardCsvPath + "_en" : rewardCsvPath);

            // 设置像素字体
            SetPixelFont();
        }

        private void SetPixelFont()
        {
            if (pixelFont == null) return;
            if (eventNameText != null) eventNameText.font = pixelFont;
            if (effectText != null) effectText.font = pixelFont;
            if (flavorText != null) flavorText.font = pixelFont;
            if (titleText != null) titleText.font = pixelFont;
        }

        /// <summary>
        /// 初始化事件池
        /// </summary>
        public void SetEventPool(List<PenaltyEventData> events)
        {
            eventPool = events;
        }

        /// <summary>
        /// 从 Resources 加载惩罚事件 CSV。
        /// CSV 格式: eventName,description,effectType,cashChange,reputationChange,skipNextTurn,effectDescription
        /// </summary>
        /// <param name="resourcePath">Resources 下的相对路径（不含扩展名），如 Data/penalty_events</param>
        private List<PenaltyEventData> LoadCSV(string resourcePath)
        {
            var pool = new List<PenaltyEventData>();

            var csv = Resources.Load<TextAsset>(resourcePath);
            if (csv == null)
            {
                Debug.LogError($"[CardPanel] CSV not found in Resources: {resourcePath}");
                return pool;
            }

            var lines = new List<string>(csv.text.Split('\n'));
            if (lines.Count < 2) return pool;

            for (int i = 1; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] fields = ParseCSVLine(line);
                if (fields.Length < 7) continue;

                var data = ScriptableObject.CreateInstance<PenaltyEventData>();
                data.eventName = fields[0];
                data.description = fields[1];

                if (System.Enum.TryParse<PenaltyEffect>(fields[2], out var effect))
                    data.effectType = effect;
                else
                    data.effectType = PenaltyEffect.Custom;

                int.TryParse(fields[3], out data.cashChange);
                int.TryParse(fields[4], out data.reputationChange);
                bool.TryParse(fields[5], out data.skipNextTurn);
                data.effectDescription = fields[6];

                if (fields.Length >= 8)
                    data.flavorText = fields[7];

                pool.Add(data);
            }

            Debug.Log($"[CardPanel] Loaded {pool.Count} events from {resourcePath}");
            return pool;
        }

        /// <summary>
        /// 解析CSV行（支持引号包裹的逗号）
        /// </summary>
        private string[] ParseCSVLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(line.Substring(start, i - start).Trim('"', ' '));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start).Trim('"', ' '));
            return result.ToArray();
        }

        /// <summary>
        /// 弹出卡牌面板，按类型选择事件池
        /// </summary>
        public void Show(CardType type, System.Action<PenaltyEventData> onResult, System.Action onConfirm = null)
        {
            currentType = type;
            currentPool = type switch
            {
                CardType.Penalty => penaltyPool,
                CardType.Event => eventPool,
                CardType.Reward => rewardPool,
                _ => penaltyPool
            };

            // 设置卡牌颜色和标题
            ApplyCardStyle(type);

            if (currentPool == null || currentPool.Count == 0)
            {
                onResult?.Invoke(null);
                onConfirm?.Invoke();
                return;
            }

            // 随机抽取一个事件
            var selected = currentPool[Random.Range(0, currentPool.Count)];

            // 显示面板
            if (panel != null)
                panel.SetActive(true);

            // 设置卡牌初始状态（背面朝上）
            if (cardTransform != null)
            {
                cardTransform.localRotation = Quaternion.Euler(0, 180, 0);
                cardTransform.localScale = Vector3.one;
            }

            // 设置像素字体
            SetPixelFont();

            // 设置文字（翻牌前全部隐藏，包括标题）
            if (eventNameText != null) eventNameText.gameObject.SetActive(false);
            if (effectText != null) effectText.gameObject.SetActive(false);
            if (flavorText != null) flavorText.gameObject.SetActive(false);
            if (confirmButton != null) confirmButton.gameObject.SetActive(false);

            onConfirmCallback = onConfirm;

            // 回调选中的事件
            onResult?.Invoke(selected);

            // 开始抽卡动画流程：滑入 → 停顿 → 翻牌
            StartCoroutine(DrawCardAnimation(selected));
        }

        /// <summary>
        /// 完整抽卡动画：卡牌从屏幕下方滑入并弹跳 → 停顿 → 翻牌显示内容
        /// </summary>
        private System.Collections.IEnumerator DrawCardAnimation(PenaltyEventData eventData)
        {
            // === 阶段1：抽卡滑入（从下方滑入 + 弹跳效果）===
            if (cardTransform != null)
            {
                Vector2 finalPos = Vector2.zero; // 卡牌中心位置
                Vector2 startPos = new Vector2(0, -slideOffset); // 从屏幕下方开始

                float elapsed = 0f;
                while (elapsed < slideDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / slideDuration;
                    // 使用 EaseOutBack 效果：滑入后轻微弹跳
                    float smoothT = 1f + 2.7f * Mathf.Pow(t - 1f, 3) + 1.7f * Mathf.Pow(t - 1f, 2);
                    smoothT = Mathf.Clamp01(smoothT);
                    cardTransform.anchoredPosition = Vector2.Lerp(startPos, finalPos, smoothT);
                    yield return null;
                }
                cardTransform.anchoredPosition = finalPos;
            }

            // === 阶段2：停顿（让玩家看到卡牌背面）===
            yield return new WaitForSeconds(slideDelay);

            // === 阶段3：翻牌 ===
            yield return FlipCardAnimation(eventData);
        }

        private System.Collections.IEnumerator FlipCardAnimation(PenaltyEventData eventData)
        {
            if (cardTransform != null)
            {
                float elapsed = 0f;
                while (elapsed < flipDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / flipDuration;
                    // Y轴旋转从180到0
                    float angle = Mathf.Lerp(180f, 0f, t);
                    cardTransform.localRotation = Quaternion.Euler(0, angle, 0);
                    yield return null;
                }
                cardTransform.localRotation = Quaternion.Euler(0, 0, 0);
            }

            // 显示事件内容（翻牌完成后才显示，包括标题）
            if (eventNameText != null)
            {
                eventNameText.text = eventData.eventName;
                eventNameText.gameObject.SetActive(true);
            }
            if (effectText != null)
            {
                effectText.text = eventData.effectDescription;
                effectText.fontStyle = FontStyle.Bold; // 效果加粗
                effectText.gameObject.SetActive(true);
            }
            if (flavorText != null)
            {
                flavorText.text = eventData.flavorText;
                flavorText.fontStyle = FontStyle.Normal; // 文案正常字体
                flavorText.gameObject.SetActive(true);
            }
            if (confirmButton != null)
                confirmButton.gameObject.SetActive(true);
        }

        private void OnConfirm()
        {
            if (panel != null)
                panel.SetActive(false);
            onConfirmCallback?.Invoke();
        }

        /// <summary>
        /// 根据卡牌类型设置颜色和标题
        /// </summary>
        private void ApplyCardStyle(CardType type)
        {
            Color bgColor = Color.black;
            Color outlineColor = Color.white;
            string title = "";
            Sprite bgSprite = null;

            switch (type)
            {
                case CardType.Penalty:
                    bgColor = new Color(0.15f, 0.1f, 0.12f, 0.98f);
                    outlineColor = new Color(0.8f, 0.2f, 0.2f, 1f); // Red
                    title = "⚠ 惩罚事件 ⚠";
                    bgSprite = penaltySprite;
                    break;
                case CardType.Event:
                    bgColor = new Color(0.1f, 0.12f, 0.18f, 0.98f);
                    outlineColor = new Color(0.2f, 0.5f, 0.8f, 1f); // Blue
                    title = "◆ 随机事件 ◆";
                    bgSprite = eventSprite;
                    break;
                case CardType.Reward:
                    bgColor = new Color(0.18f, 0.15f, 0.08f, 0.98f);
                    outlineColor = new Color(0.9f, 0.75f, 0.2f, 1f); // Gold
                    title = "★ 奖励事件 ★";
                    bgSprite = rewardSprite;
                    break;
            }

            if (cardImage != null)
            {
                if (bgSprite != null)
                {
                    cardImage.sprite = bgSprite;
                    cardImage.color = Color.white;
                }
                else
                {
                    cardImage.color = bgColor;
                }
            }

            var outline = cardTransform?.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = outlineColor;

            if (titleText != null)
                titleText.text = title;
        }
    }
}
