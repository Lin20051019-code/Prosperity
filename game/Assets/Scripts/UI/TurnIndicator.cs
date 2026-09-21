using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 回合提示系统（v4移植，批次A）：
    /// ① 骰子按钮脉动 + 弹跳箭头
    /// ② "你的回合" 横幅
    /// ③ 回合进度 HUD（常驻）
    /// ④ AI 回合横幅
    /// ⑤ 危机/冲刺阶段变色
    /// 全部由代码动态创建 UI 元素，无需场景修改或美术资产。
    /// </summary>
    public class TurnIndicator : MonoBehaviour
    {
        // ===== 单例 =====
        public static TurnIndicator Instance { get; private set; }

        // ===== 配置 =====
        [Header("脉动配置")]
        [SerializeField] private float pulseMin = 1.0f;
        [SerializeField] private float pulseMax = 1.15f;
        [SerializeField] private float pulseSpeed = 3f;

        [Header("箭头配置")]
        [SerializeField] private float arrowBounceSpeed = 4f;
        [SerializeField] private float arrowBounceDist = 20f;

        // ===== 内部状态 =====
        private RectTransform pulseTarget;
        private Coroutine pulseCoroutine;
        private GameObject bannerObj;
        private GameObject arrowObj;
        private Coroutine arrowCoroutine;
        private Text roundHudText;
        private Image roundHudBar;
        private Image roundHudBg;
        private Canvas parentCanvas;
        private bool initialized = false;

        // ===== 颜色 =====
        public static readonly Color PlayerColor = new Color(0.2f, 0.65f, 0.9f);   // 蓝
        private static readonly Color AIWarningColor = new Color(0.9f, 0.6f, 0.15f); // 橙
        private static readonly Color CrisisColor = new Color(0.85f, 0.2f, 0.2f);    // 红
        private static readonly Color SprintColor = new Color(1f, 0.85f, 0.2f);      // 金
        private static readonly Color DangerColor = new Color(0.95f, 0.3f, 0.3f);    // 红闪

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>初始化 UI 元素（由 DiceRollController.Start 调用一次）</summary>
        public void Initialize(Canvas canvas)
        {
            if (initialized) return;
            initialized = true;
            parentCanvas = canvas;
        }

        // ============================================================
        // ① 骰子按钮脉动 + 弹跳箭头
        // ============================================================

        /// <summary>开始脉动（玩家回合）</summary>
        public void StartPulse(Button diceButton)
        {
            StopPulse();
            if (diceButton == null) return;
            pulseTarget = diceButton.GetComponent<RectTransform>();
            if (pulseTarget == null) return;

            pulseCoroutine = StartCoroutine(PulseRoutine());
            SpawnArrow(diceButton.gameObject);
        }

        /// <summary>停止脉动</summary>
        public void StopPulse()
        {
            if (pulseCoroutine != null) { StopCoroutine(pulseCoroutine); pulseCoroutine = null; }
            if (pulseTarget != null) pulseTarget.localScale = Vector3.one;
            if (arrowObj != null) Destroy(arrowObj);
        }

        private IEnumerator PulseRoutine()
        {
            float timer = 0f;
            while (true)
            {
                timer += Time.deltaTime * pulseSpeed;
                float t = (Mathf.Sin(timer * Mathf.PI) + 1f) / 2f; // 0~1 平滑
                float scale = Mathf.Lerp(pulseMin, pulseMax, t);
                if (pulseTarget != null)
                    pulseTarget.localScale = Vector3.one * scale;
                yield return null;
            }
        }

        private void SpawnArrow(GameObject target)
        {
            if (arrowObj != null) Destroy(arrowObj);
            // 创建三角箭头（代码绘制）
            arrowObj = new GameObject("TurnArrow");
            arrowObj.transform.SetParent(target.transform.parent, false);

            var img = arrowObj.AddComponent<Image>();
            img.color = new Color(1f, 0.85f, 0.2f, 1f); // 金色
            img.sprite = CreateTriangleSprite();
            img.raycastTarget = false;

            var rt = arrowObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(40, 30);
            rt.anchorMin = target.GetComponent<RectTransform>().anchorMin;
            rt.anchorMax = target.GetComponent<RectTransform>().anchorMax;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, 0); // 挂在目标上方

            arrowCoroutine = StartCoroutine(BounceArrow(rt));
        }

        private IEnumerator BounceArrow(RectTransform rt)
        {
            float baseY = rt.anchoredPosition.y;
            float timer = 0f;
            while (rt != null && arrowObj != null)
            {
                timer += Time.deltaTime * arrowBounceSpeed;
                float offset = Mathf.Sin(timer * Mathf.PI) * arrowBounceDist;
                rt.anchoredPosition = new Vector2(0, baseY + offset);
                yield return null;
            }
        }

        private Sprite CreateTriangleSprite()
        {
            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 倒三角（尖朝下）
                float half = (size - y) * 0.5f * (size * 0.8f / size);
                bool inside = Mathf.Abs(x - size / 2f) < half && y < size * 0.8f;
                tex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // ============================================================
        // ② "你的回合" 横幅
        // ============================================================

        /// <summary>显示回合横幅（从侧面甩入，停留，收起）</summary>
        public void ShowBanner(string text, Color color, float duration = 1.2f)
        {
            if (bannerObj != null) Destroy(bannerObj);
            StartCoroutine(BannerRoutine(text, color, duration));
        }

        private IEnumerator BannerRoutine(string text, Color color, float duration)
        {
            // 自愈：Initialize未被调用（如从砍价场景返回的恢复路径）时兜底找Canvas，避免空引用
            if (parentCanvas == null)
            {
                parentCanvas = FindObjectOfType<Canvas>();
                if (parentCanvas == null) yield break;
            }
            // 创建横幅
            bannerObj = new GameObject("TurnBanner");
            bannerObj.transform.SetParent(parentCanvas.transform, false);

            var img = bannerObj.AddComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, 0.9f);
            img.raycastTarget = false;

            var rt = bannerObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(700, 70);
            rt.anchoredPosition = new Vector2(0, 100);

            // 文本
            var txtObj = new GameObject("BannerText");
            txtObj.transform.SetParent(bannerObj.transform, false);
            var txt = txtObj.AddComponent<Text>();
            txt.font = BargainState.GetSafeFont();
            txt.fontSize = 36;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.rectTransform.sizeDelta = new Vector2(680, 60);

            // 横幅动画：滑入 → 停留 → 滑出
            float slideDuration = 0.3f;
            Vector2 finalPos = Vector2.zero;
            Vector2 startPos = new Vector2(800, 100); // 从右侧甩入

            float elapsed = 0f;
            while (elapsed < slideDuration)
            {
                elapsed += Time.deltaTime;
                float t = 1f - Mathf.Pow(1f - elapsed / slideDuration, 3f); // ease-out
                rt.anchoredPosition = Vector2.Lerp(startPos, finalPos, t);
                yield return null;
            }

            txt.text = text;
            yield return new WaitForSeconds(duration);

            // 滑出
            elapsed = 0f;
            while (elapsed < slideDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / slideDuration;
                rt.anchoredPosition = Vector2.Lerp(finalPos, new Vector2(-800, 100), t);
                img.color = new Color(color.r, color.g, color.b, 0.9f * (1f - t));
                yield return null;
            }

            Destroy(bannerObj);
        }

        // ============================================================
        // ③ 回合进度 HUD（常驻）
        // ============================================================

        private GameObject hudObj;

        /// <summary>创建回合进度 HUD（由 DiceRollController.Start 调用一次）</summary>
        public void CreateRoundHud(Canvas canvas, int maxRounds)
        {
            if (hudObj != null) Destroy(hudObj);
            hudObj = new GameObject("RoundHUD");
            hudObj.transform.SetParent(canvas.transform, false);

            var rt = hudObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);   // 左上角
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(20, -20);
            rt.sizeDelta = new Vector2(300, 40);

            // 背景面板
            roundHudBg = hudObj.AddComponent<Image>();
            roundHudBg.color = new Color(0, 0, 0, 0.6f);
            roundHudBg.raycastTarget = false;

            // 回合文字
            var txtObj = new GameObject("RoundText");
            txtObj.transform.SetParent(hudObj.transform, false);
            roundHudText = txtObj.AddComponent<Text>();
            roundHudText.font = BargainState.GetSafeFont();
            roundHudText.fontSize = 22;
            roundHudText.alignment = TextAnchor.MiddleLeft;
            roundHudText.color = Color.white;
            roundHudText.raycastTarget = false;
            roundHudText.rectTransform.anchorMin = Vector2.zero;
            roundHudText.rectTransform.anchorMax = Vector2.one;
            roundHudText.rectTransform.offsetMin = new Vector2(10, 2);
            roundHudText.rectTransform.offsetMax = new Vector2(-10, -2);

            // 进度条
            var barObj = new GameObject("ProgressBar");
            barObj.transform.SetParent(hudObj.transform, false);
            roundHudBar = barObj.AddComponent<Image>();
            roundHudBar.color = new Color(0.3f, 0.7f, 0.4f, 0.8f);
            roundHudBar.raycastTarget = false;
            roundHudBar.rectTransform.anchorMin = new Vector2(0, 0);
            roundHudBar.rectTransform.anchorMax = new Vector2(0, 0);
            roundHudBar.rectTransform.pivot = new Vector2(0, 0);
            roundHudBar.rectTransform.sizeDelta = new Vector2(0, 4);
            roundHudBar.rectTransform.anchoredPosition = new Vector2(0, 0);
        }

        /// <summary>更新回合 HUD 显示（phase: 0=Normal, 1=Crisis, 2=Sprint）</summary>
        public void UpdateRoundHud(int currentRound, int maxRounds, int prosperity, int phase)
        {
            if (roundHudText == null) return;

            roundHudText.text = I18n.T("ui_round_hud", $"回合 {currentRound}/{maxRounds}  |  繁荣 {prosperity}", ("round", currentRound), ("max", maxRounds), ("prosperity", prosperity));

            // 进度条按回合填充
            float pct = (float)(currentRound - 1) / maxRounds;
            if (roundHudBar != null)
            {
                var parentW = roundHudText.rectTransform.rect.width;
                roundHudBar.rectTransform.sizeDelta = new Vector2(parentW * pct, 4);
            }

            // 阶段变色
            if (roundHudBg != null)
            {
                switch (phase)
                {
                    case 1: // Crisis
                        roundHudBg.color = new Color(CrisisColor.r, CrisisColor.g, CrisisColor.b, 0.7f);
                        roundHudText.color = Color.white;
                        break;
                    case 2: // Sprint
                        roundHudBg.color = new Color(SprintColor.r, SprintColor.g, SprintColor.b, 0.7f);
                        roundHudText.color = Color.black;
                        break;
                    default:
                        roundHudBg.color = new Color(0, 0, 0, 0.6f);
                        roundHudText.color = Color.white;
                        break;
                }
            }
        }

        /// <summary>AI 回合高亮提示</summary>
        public void HighlightAITurn(string aiName)
        {
            ShowBanner(I18n.T("ui_ai_turn", $"{aiName} 正在行动…", ("name", aiName)), AIWarningColor, 0.8f);
        }

        /// <summary>危机区闪烁警告（一次性）</summary>
        public void FlashDanger()
        {
            ShowBanner(I18n.T("broadcast_phase_crisis", "【警告】城市进入衰退期！"), DangerColor, 2f);
        }
    }
}
