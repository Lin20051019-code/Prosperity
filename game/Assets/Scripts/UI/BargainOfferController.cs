using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// 报价场景控制器：3轮报价，成交或失败后加载讨价还价_结果。
    /// UI风格：P5式谈判演出——黑色斜切面板 + 彩色角标 + 打字机逐字 + 立绘弹跳 + 数字闪色。
    /// v4.2合并：P5演出（Mac侧）+ 防重入/防连点/幂等结算（团队侧）+ 全量i18n。
    /// </summary>
    public class BargainOfferController : MonoBehaviour
    {
        [Header("Background")]
        [SerializeField] private BargainBackgroundScroller backgroundScroller;

        [Header("Bargain UI")]
        [SerializeField] private Text infoText;
        [SerializeField] private Text sellerOfferText;
        [SerializeField] private Text buyerOfferText;
        [SerializeField] private Text roundText;
        [SerializeField] private List<Button> offerButtons = new List<Button>();
        [SerializeField] private List<Text> offerButtonTexts = new List<Text>();

        [Header("Character Portraits & Dialog")]
        [SerializeField] private List<Image> characterPortraits = new List<Image>();
        [SerializeField] private GameObject dialogBox;
        [SerializeField] private Text dialogText;

        private Coroutine dialogCoroutine;
        private bool offerBusy;      // v4.2 防连点：同一时间只允许一个报价协程（团队移植）
        private bool resultDecided; // v4.2 防重入：结果已判定后禁止再报价（场景切换窗口内重入会改写结果）

        // ===== P5 UI 动态元素 =====
        private Text speakerTag;              // 说话人名角标
        private Image speakerTagBg;           // 角标底色
        private Coroutine typewriterCoroutine;
        private Coroutine punchCoroutine;

        // ===== 颜色系统（角色专属色 = 撕纸剪影色） =====
        private static readonly Color[] CharColors =
        {
            new Color(0.15f, 0.55f, 0.12f),  // 莉兹·绿
            new Color(0.12f, 0.20f, 0.55f),  // 罗斯韦尔·蓝
            new Color(0.45f, 0.45f, 0.15f),  // 露丝·橄榄
            new Color(0.65f, 0.12f, 0.10f),  // 徐丰·红
        };
        private static readonly string[] CharColorHex =
        {
            "#268C1F", "#1F338C", "#737326", "#A61E19",
        };

        // 表情差分
        private readonly Dictionary<(int, int), Sprite> expressionSprites = new Dictionary<(int, int), Sprite>();
        private readonly Dictionary<int, Sprite> baseSprites = new Dictionary<int, Sprite>();

        // 上一次报价缓存（用于数字闪色检测）
        private float lastSellerOffer = -1f;
        private float lastBuyerOffer = -1f;

        private void Start()
        {
            BargainState.LoadDialogs();

            // 运行时隐藏对话框（编辑器中保持可见便于调整）——v4.2团队移植
            if (dialogBox != null)
                dialogBox.SetActive(false);

            SetupPortraits();

            // P5风格改造（带保护，失败不阻塞游戏逻辑）
            try { SetupP5DialogStyle(); }
            catch (System.Exception e) { Debug.LogWarning($"[P5] Style setup skipped: {e.Message}"); }

            for (int i = 0; i < offerButtons.Count; i++)
            {
                int index = i;
                if (offerButtons[i] != null)
                    offerButtons[i].onClick.AddListener(() => SelectOffer(index));
            }

            BargainState.CalculateFirstRoundOffers();
            UpdateBargainDisplay();
            StartCoroutine(ShowFirstRoundDialog());
        }

        private void SetupPortraits()
        {
            for (int i = 0; i < characterPortraits.Count; i++)
            {
                var p = characterPortraits[i];
                if (p == null) continue;
                p.gameObject.SetActive(false);
                if (!baseSprites.ContainsKey(i)) baseSprites[i] = p.sprite;
            }

            int playerIdx = 0;
            int aiIdx = -1;
            if (BargainState.sellerIndex != 0 && BargainState.sellerIndex >= 0) aiIdx = BargainState.sellerIndex;
            if (BargainState.buyerIndex != 0 && BargainState.buyerIndex >= 0) aiIdx = BargainState.buyerIndex;

            if (BargainState.isAIVsAI)
            {
                if (BargainState.sellerIndex < characterPortraits.Count && characterPortraits[BargainState.sellerIndex] != null)
                {
                    var sp = characterPortraits[BargainState.sellerIndex];
                    sp.gameObject.SetActive(true);
                    sp.rectTransform.anchoredPosition = new Vector2(-300, 300);
                }
                if (BargainState.buyerIndex < characterPortraits.Count && characterPortraits[BargainState.buyerIndex] != null)
                {
                    var bp = characterPortraits[BargainState.buyerIndex];
                    bp.gameObject.SetActive(true);
                    bp.rectTransform.anchoredPosition = new Vector2(300, 300);
                }
                return;
            }

            // 玩家恒在下、AI恒在上
            if (playerIdx < characterPortraits.Count && characterPortraits[playerIdx] != null)
            {
                var pp = characterPortraits[playerIdx];
                pp.gameObject.SetActive(true);
                pp.rectTransform.anchoredPosition = BargainState.isPlayerBuyer
                    ? new Vector2(-350, -500) : new Vector2(350, -500);
                pp.rectTransform.localScale = BargainState.isPlayerBuyer ? Vector3.one : new Vector3(-1, 1, 1);
            }
            if (aiIdx >= 0 && aiIdx < characterPortraits.Count && characterPortraits[aiIdx] != null)
            {
                var ap = characterPortraits[aiIdx];
                ap.gameObject.SetActive(true);
                ap.rectTransform.anchoredPosition = (aiIdx == BargainState.buyerIndex)
                    ? new Vector2(300, 500) : new Vector2(-300, 500);
                ap.rectTransform.localScale = (aiIdx == BargainState.buyerIndex)
                    ? new Vector3(-1, 1, 1) : Vector3.one;
            }
        }

        // ==================== P5 对话框 ====================

        /// <summary>P5风格改造：把现有 dialogBox 改成黑色斜切面板 + 说话人彩色角标。通过代码动态创建，不改场景。</summary>
        private void SetupP5DialogStyle()
        {
            if (dialogBox == null) return;
            var rt = dialogBox.GetComponent<RectTransform>();

            // 1) 面板底色改为黑色半透明
            var img = dialogBox.GetComponent<Image>();
            if (img != null)
                img.color = new Color(0.05f, 0.02f, 0.05f, 0.92f);

            // 2) 添加左侧彩色竖条（说话人色条）
            var colorBar = new GameObject("SpeakerColorBar");
            colorBar.transform.SetParent(dialogBox.transform, false);
            var barImg = colorBar.AddComponent<Image>();
            barImg.raycastTarget = false;
            var barRt = colorBar.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0, 0);
            barRt.anchorMax = new Vector2(0, 1);
            barRt.pivot = new Vector2(0, 0.5f);
            barRt.anchoredPosition = Vector2.zero;
            barRt.sizeDelta = new Vector2(6, 0);

            // 3) 创建说话人名角标（挂在面板上方）
            var tagObj = new GameObject("SpeakerTag");
            tagObj.transform.SetParent(dialogBox.transform, false);
            speakerTagBg = tagObj.AddComponent<Image>();
            speakerTagBg.raycastTarget = false;
            var tagRt = tagObj.GetComponent<RectTransform>();
            tagRt.anchorMin = new Vector2(0, 1);
            tagRt.anchorMax = new Vector2(0, 1);
            tagRt.pivot = new Vector2(0, 1);
            tagRt.anchoredPosition = new Vector2(15, -5);
            tagRt.sizeDelta = new Vector2(200, 36);

            var tagTxtObj = new GameObject("SpeakerTagText");
            tagTxtObj.transform.SetParent(tagObj.transform, false);
            speakerTag = tagTxtObj.AddComponent<Text>();
            speakerTag.font = GetSafeFontLocal();
            speakerTag.fontSize = 22;
            speakerTag.fontStyle = FontStyle.Bold;
            speakerTag.alignment = TextAnchor.MiddleCenter;
            speakerTag.color = Color.white;
            speakerTag.raycastTarget = false;
            speakerTag.rectTransform.anchorMin = Vector2.zero;
            speakerTag.rectTransform.anchorMax = Vector2.one;
            speakerTag.rectTransform.offsetMin = Vector2.zero;
            speakerTag.rectTransform.offsetMax = Vector2.zero;

            // 4) 对话文字改为白色+描边
            if (dialogText != null)
            {
                dialogText.color = Color.white;
                var outline = dialogText.gameObject.GetComponent<Outline>();
                if (outline == null) outline = dialogText.gameObject.AddComponent<Outline>(); // 修复：返回值必须赋回（原代码漏赋值→空引用→P5样式被跳过）
                outline.effectColor = new Color(0, 0, 0, 0.8f);
                outline.effectDistance = new Vector2(1.5f, 1.5f);
                dialogText.gameObject.transform.SetAsLastSibling();
            }
        }

        /// <summary>安全获取字体：优先项目像素字体VonwaonBitmap（含中文），再回退内置字体</summary>
        private static Font _safeFont;
        private static Font GetSafeFontLocal()
        {
            if (_safeFont != null) return _safeFont;
            // 项目字体优先（内置LegacyRuntime无中文字形，中文说话人名会出豆腐块）
            _safeFont = Resources.Load<Font>("VonwaonBitmap-16px");
            if (_safeFont != null) return _safeFont;
            try { _safeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_safeFont == null) { try { _safeFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            if (_safeFont == null)
            {
                // 最后手段：从已加载的Text组件偷字体
                var existing = FindObjectOfType<Text>();
                if (existing != null && existing.font != null) _safeFont = existing.font;
            }
            return _safeFont;
        }

        /// <summary>设置当前说话人的角标颜色和名字</summary>
        private void SetSpeakerTag(int charIndex)
        {
            if (speakerTag == null || speakerTagBg == null) return;
            string charName = charIndex < playerInfoCount() && charIndex >= 0
                ? GetCharName(charIndex) : "?";
            Color c = charIndex >= 0 && charIndex < CharColors.Length ? CharColors[charIndex] : Color.gray;

            speakerTag.text = charName;
            speakerTagBg.color = new Color(c.r, c.g, c.b, 0.95f);

            // 彩色竖条同步变色
            var bar = dialogBox?.transform.Find("SpeakerColorBar")?.GetComponent<Image>();
            if (bar != null) bar.color = new Color(c.r, c.g, c.b, 0.9f);
        }

        private string GetCharName(int idx)
        {
            var names = new[] { BargainState.sellerName, "", "", "" };
            // 简化：从 BargainState 获取
            if (idx == BargainState.sellerIndex) return BargainState.sellerName;
            if (idx == BargainState.buyerIndex) return BargainState.buyerName;
            return idx == 0 ? "Lizzie" : idx == 1 ? "Rothwell" : idx == 2 ? "Ruth" : "Xu Feng";
        }

        private int playerInfoCount() => 4;

        // ==================== 报价数字闪色 ====================

        /// <summary>数字变化时闪色动画：涨价绿色闪、降价红色闪</summary>
        private IEnumerator FlashOfferText(Text target, bool isIncrease)
        {
            if (target == null) yield break;
            Color flashColor = isIncrease
                ? new Color(0.4f, 1f, 0.4f)  // 涨=亮绿
                : new Color(1f, 0.4f, 0.4f); // 跌=亮红
            Color original = target.color;
            float duration = 0.4f;
            float elapsed = 0f;
            target.color = flashColor;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                target.color = Color.Lerp(flashColor, original, elapsed / duration);
                yield return null;
            }
            target.color = original;
        }

        // ==================== 主逻辑 ====================

        private void UpdateBargainDisplay()
        {
            if (roundText != null)
                roundText.text = I18n.T("bargain_round_no", $"第{BargainState.currentRound}轮", ("n", BargainState.currentRound));

            // 报价数字变化时闪色
            bool sellerChanged = Mathf.Abs(BargainState.sellerOffer - lastSellerOffer) > 0.5f && lastSellerOffer >= 0;
            bool buyerChanged = Mathf.Abs(BargainState.buyerOffer - lastBuyerOffer) > 0.5f && lastBuyerOffer >= 0;

            if (sellerOfferText != null)
            {
                sellerOfferText.text = $"{BargainState.sellerName}: {Mathf.RoundToInt(BargainState.sellerOffer)}";
                if (sellerChanged)
                {
                    bool up = BargainState.sellerOffer > lastSellerOffer;
                    StartCoroutine(FlashOfferText(sellerOfferText, up));
                }
            }
            if (buyerOfferText != null)
            {
                buyerOfferText.text = $"{BargainState.buyerName}: {Mathf.RoundToInt(BargainState.buyerOffer)}";
                if (buyerChanged)
                {
                    bool up = BargainState.buyerOffer > lastBuyerOffer;
                    StartCoroutine(FlashOfferText(buyerOfferText, up));
                }
            }
            lastSellerOffer = BargainState.sellerOffer;
            lastBuyerOffer = BargainState.buyerOffer;

            if (BargainState.CheckOverlap())
            {
                int finalPrice = Mathf.RoundToInt((BargainState.sellerOffer + BargainState.buyerOffer) / 2f);
                GotoResult(true, finalPrice);
                return;
            }

            float buyerDiff = BargainState.sellerOffer - BargainState.buyerOffer;
            float sellerDiff = BargainState.buyerOffer - BargainState.sellerOffer;

            float[] roundFactors = BargainState.currentRound == 1
                ? new float[] { 0.30f, 0.50f, 0.70f }
                : new float[] { 0.40f, 0.60f, 0.80f };

            for (int i = 0; i < offerButtonTexts.Count; i++)
            {
                float factor = roundFactors[i];
                if (offerButtonTexts[i] != null)
                {
                    float playerRate = BargainState.priceChangeRate[BargainState.selectedCardIndex];
                    float previewOffer;
                    if (BargainState.isPlayerBuyer)
                        previewOffer = BargainState.buyerOffer + buyerDiff * (factor + playerRate);
                    else
                        previewOffer = BargainState.sellerOffer + sellerDiff * (factor + playerRate);

                    string role = BargainState.isPlayerBuyer ? I18n.T("bargain_role_buy", "买") : I18n.T("bargain_role_sell", "卖");
                    offerButtonTexts[i].text = $"{role}{Mathf.RoundToInt(previewOffer)}元";
                }
            }
        }

        private void SelectOffer(int offerIndex)
        {
            if (offerBusy || resultDecided) return; // v4.2 防重入
            StartCoroutine(SelectOfferRoutine(offerIndex));
        }

        private IEnumerator SelectOfferRoutine(int offerIndex)
        {
            offerBusy = true;
            try
            {
            float[] roundFactors = BargainState.currentRound == 1
                ? new float[] { 0.30f, 0.50f, 0.70f }
                : new float[] { 0.40f, 0.60f, 0.80f };

            float factor = roundFactors[offerIndex];
            float buyerDiff = BargainState.sellerOffer - BargainState.buyerOffer;
            float sellerDiff = BargainState.buyerOffer - BargainState.sellerOffer;

            // 对手（玩家）改价前的报价，供LLM台词描述本轮让步幅度
            float oppPrevOffer = BargainState.isPlayerBuyer ? BargainState.buyerOffer : BargainState.sellerOffer;

            float playerRate = BargainState.priceChangeRate[BargainState.selectedCardIndex];
            // LLM态度修正（±5%，来自AI上一句话）叠加到AI让步率；未启用/回落时为0
            float aiRate = BargainState.priceChangeRate[BargainState.aiCardIndex] + BargainState.aiAttitudeAdjust;

            if (BargainState.isPlayerBuyer)
            {
                BargainState.buyerOffer += buyerDiff * (factor + playerRate);
                BargainState.sellerOffer += sellerDiff * (factor + aiRate);
            }
            else
            {
                BargainState.sellerOffer += sellerDiff * (factor + playerRate);
                BargainState.buyerOffer += buyerDiff * (factor + aiRate);
            }

            Debug.Log($"[BargainOffer] Round {BargainState.currentRound + 1}: seller={BargainState.sellerOffer:F0}, buyer={BargainState.buyerOffer:F0}");

            int playerIdx = BargainState.isPlayerBuyer ? BargainState.buyerIndex : BargainState.sellerIndex;
            int aiIdx = BargainState.isPlayerBuyer ? BargainState.sellerIndex : BargainState.buyerIndex;
            int playerPrice = BargainState.isPlayerBuyer
                ? Mathf.RoundToInt(BargainState.buyerOffer)
                : Mathf.RoundToInt(BargainState.sellerOffer);
            int aiPrice = BargainState.isPlayerBuyer
                ? Mathf.RoundToInt(BargainState.sellerOffer)
                : Mathf.RoundToInt(BargainState.buyerOffer);
            int diff = Mathf.RoundToInt(Mathf.Abs(BargainState.sellerOffer - BargainState.buyerOffer));
            string aiName = BargainState.isPlayerBuyer ? BargainState.sellerName : BargainState.buyerName;

            // AI反应台词：LLM预取与玩家台词显示并行（第一层试点；失败/超时回落CSV台词池）
            float movePct = Mathf.Clamp01(factor + playerRate);
            string reactionStage = BargainState.ReactionStage(movePct);
            LlmDialogService.Fetch aiFetch = StartAiLineFetch(
                stage: reactionStage, roundNo: BargainState.currentRound + 1,
                aiPrice: aiPrice, oppPrev: Mathf.RoundToInt(oppPrevOffer), oppNew: playerPrice, diff: diff);

            // 玩家说出自己的让步姿态（选项1~3）
            var playerLine = BargainState.GetLine(
                BargainState.selectedCardIndex, BargainState.isPlayerSeller,
                I18n.T("bargain_option", $"选项{offerIndex + 1}", ("n", offerIndex + 1)),
                opponentName: aiName, offer: playerPrice, diff: diff);
            if (!string.IsNullOrEmpty(playerLine.text))
                yield return ShowDialogCoroutine(playerIdx, playerLine, 2.5f);

            if (aiFetch != null)
                yield return LlmDialogService.WaitFor(aiFetch, 2.5f);
            var aiLine = TakeLlmOrCsv(aiFetch, BargainState.GetLine(
                BargainState.aiCardIndex, !BargainState.isPlayerSeller,
                reactionStage,
                opponentName: BargainState.isPlayerBuyer ? BargainState.buyerName : BargainState.sellerName,
                myPrice: aiPrice, diff: diff));
            if (!string.IsNullOrEmpty(aiLine.text))
                yield return ShowDialogCoroutine(aiIdx, aiLine, 2.5f);

            if (BargainState.CheckOverlap())
            {
                int finalPrice = Mathf.RoundToInt((BargainState.sellerOffer + BargainState.buyerOffer) / 2f);
                GotoResult(true, finalPrice);
                yield break;
            }

            BargainState.currentRound++;
            if (BargainState.currentRound > 3)
            {
                GotoResult(false, 0);
                yield break;
            }

            UpdateBargainDisplay();
            }
            finally
            {
                offerBusy = false;
            }
        }

        private IEnumerator ShowFirstRoundDialog()
        {
            // LLM预取/等待期间禁止点报价，防开场台词与反应台词演出交叠（等待最长约5秒）
            offerBusy = true;
            try
            {
                int sellerCard = BargainState.isPlayerSeller ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
                int buyerCard = BargainState.isPlayerBuyer ? BargainState.selectedCardIndex : BargainState.aiCardIndex;

                // AI侧开场台词LLM预取（玩家侧台词保持CSV台词池不变）
                bool aiIsSeller = !BargainState.isPlayerSeller && !BargainState.isAIVsAI;
                int aiOpeningPrice = aiIsSeller ? Mathf.RoundToInt(BargainState.sellerOffer) : Mathf.RoundToInt(BargainState.buyerOffer);
                int oppOpeningPrice = aiIsSeller ? Mathf.RoundToInt(BargainState.buyerOffer) : Mathf.RoundToInt(BargainState.sellerOffer);
                LlmDialogService.Fetch aiFetch = StartAiLineFetch(
                    stage: "开场", roundNo: 1,
                    aiPrice: aiOpeningPrice, oppPrev: oppOpeningPrice, oppNew: oppOpeningPrice,
                    diff: Mathf.RoundToInt(Mathf.Abs(BargainState.sellerOffer - BargainState.buyerOffer)));

                var sellerLine = BargainState.GetLine(sellerCard, true, "开场",
                    opponentName: BargainState.buyerName,
                    myPrice: Mathf.RoundToInt(BargainState.sellerOffer));
                if (!string.IsNullOrEmpty(sellerLine.text) && BargainState.sellerIndex < characterPortraits.Count)
                {
                    if (aiIsSeller && aiFetch != null)
                    {
                        yield return LlmDialogService.WaitFor(aiFetch, 5f);
                        sellerLine = TakeLlmOrCsv(aiFetch, sellerLine);
                    }
                    yield return ShowDialogCoroutine(BargainState.sellerIndex, sellerLine, 2.5f);
                }

                var buyerLine = BargainState.GetLine(buyerCard, false, "开场",
                    opponentName: BargainState.sellerName,
                    myPrice: Mathf.RoundToInt(BargainState.buyerOffer));
                if (!string.IsNullOrEmpty(buyerLine.text) && BargainState.buyerIndex < characterPortraits.Count)
                {
                    if (!aiIsSeller && aiFetch != null)
                    {
                        yield return LlmDialogService.WaitFor(aiFetch, 5f);
                        buyerLine = TakeLlmOrCsv(aiFetch, buyerLine);
                    }
                    yield return ShowDialogCoroutine(BargainState.buyerIndex, buyerLine, 2.5f);
                }
            }
            finally
            {
                offerBusy = false;
            }
        }

        private void GotoResult(bool success, int finalPrice)
        {
            if (resultDecided) return; // v4.2 幂等：结果只判定一次，后续重入不得改写
            resultDecided = true;

            // v4.2 立即禁用报价按钮，防止场景切换窗口内的点击重入
            foreach (var btn in offerButtons)
                if (btn != null) btn.interactable = false;

            BargainState.resultSuccess = success;
            BargainState.resultFinalPrice = finalPrice;

            int speakerCard = BargainState.isPlayerBuyer || BargainState.isPlayerSeller
                ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
            bool speakerIsSeller = BargainState.isPlayerSeller;
            if (speakerCard >= 0)
            {
                var line = BargainState.GetLine(speakerCard, speakerIsSeller, success ? "成交" : "失败",
                    opponentName: speakerIsSeller ? BargainState.buyerName : BargainState.sellerName,
                    final: finalPrice);
                BargainState.resultLine = line.text;
            }

            SceneManager.LoadScene("讨价还价_结果");
        }

        // ==================== P5 对话演出 ====================

        private IEnumerator ShowDialogCoroutine(int speakerIndex, BargainState.DialogLine line, float duration = 2.5f)
        {
            if (dialogBox == null || dialogText == null || string.IsNullOrEmpty(line.text)) yield break;

            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = false;
            }

            // 切换表情 + 弹跳
            SetPortraitExpression(speakerIndex, line.emotion);
            StartCoroutine(PortraitPunch(speakerIndex));

            // 设置说话人角标
            SetSpeakerTag(speakerIndex);

            // 打字机逐字显示
            dialogBox.SetActive(true);
            PositionDialog(speakerIndex);

            if (typewriterCoroutine != null) StopCoroutine(typewriterCoroutine);
            typewriterCoroutine = StartCoroutine(TypewriterEffect(line.text, dialogText, 0.03f));

            // 打字完成后等剩余时间
            float typeTime = line.text.Length * 0.03f;
            float remaining = Mathf.Max(0, duration - typeTime);
            yield return new WaitForSeconds(remaining + 0.3f);

            dialogBox.SetActive(false);

            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = true;
            }
        }

        /// <summary>打字机逐字显示效果</summary>
        private IEnumerator TypewriterEffect(string text, Text target, float charDelay)
        {
            target.text = "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                sb.Append(c);
                target.text = sb.ToString();
                yield return new WaitForSeconds(charDelay);
            }
            target.text = text; // 确保完整显示
        }

        /// <summary>切换说话人的立绘表情（0=原始立绘，1怒/2惊/3得意从Resources/BargainExpressions加载）</summary>
        private void SetPortraitExpression(int charIndex, int emotion)
        {
            if (charIndex < 0 || charIndex >= characterPortraits.Count || characterPortraits[charIndex] == null) return;
            characterPortraits[charIndex].sprite = GetExpressionSprite(charIndex, emotion);
        }

        private Sprite GetExpressionSprite(int charIndex, int emotion)
        {
            if (emotion <= 0)
                return baseSprites.TryGetValue(charIndex, out var b) ? b : null;

            var key = (charIndex, emotion);
            if (expressionSprites.TryGetValue(key, out var cached)) return cached;

            var tex = Resources.Load<Texture2D>($"BargainExpressions/expr_{charIndex}_{emotion}");
            if (tex == null)
                return baseSprites.TryGetValue(charIndex, out var fallback) ? fallback : null;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            expressionSprites[key] = sprite;
            return sprite;
        }

        /// <summary>说话时立绘轻微放大回弹</summary>
        private IEnumerator PortraitPunch(int speakerIndex)
        {
            if (speakerIndex < 0 || speakerIndex >= characterPortraits.Count || characterPortraits[speakerIndex] == null) yield break;

            var rt = characterPortraits[speakerIndex].rectTransform;
            float punch = 1.08f, duration = 0.22f, elapsed = 0f;
            Vector3 baseScale = Vector3.one;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float scale = Mathf.LerpUnclamped(punch, 1f, t * t);
                rt.localScale = baseScale * scale;
                yield return null;
            }
            rt.localScale = baseScale;
        }

        private void PositionDialog(int speakerIndex)
        {
            if (speakerIndex >= characterPortraits.Count || characterPortraits[speakerIndex] == null) return;

            var portraitRt = characterPortraits[speakerIndex].rectTransform;
            var dialogRt = dialogBox.GetComponent<RectTransform>();

            bool isBottom = portraitRt.anchoredPosition.y < 0;
            if (isBottom)
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x, portraitRt.anchoredPosition.y + 600f);
            else
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x, portraitRt.anchoredPosition.y - 600f);
        }

        // ==================== LLM台词试点（2026-09-25，第一层：只产台词，决策仍确定性）====================

        /// <summary>AI谈判卡人格描述（LLM system prompt用），索引对应BargainState.cardNames</summary>
        private static readonly string[] CardPersonas =
        {
            "热情随和，重视关系口碑，愿意让利交朋友",
            "务实冷静，讲究行情公道，不占便宜也不吃亏",
            "精明势利，会掂量对手身份身家，见人下菜碟",
            "强硬贪婪，寸土不让，认定的事绝不松口",
        };

        /// <summary>启动AI台词LLM预取；开关关/已熔断/AIvsAI返回null（调用方直接走CSV）</summary>
        private LlmDialogService.Fetch StartAiLineFetch(string stage, int roundNo, int aiPrice, int oppPrev, int oppNew, int diff)
        {
            if (BargainState.isAIVsAI) return null;
            if (!LlmDialogService.IsEnabled) return null;
            bool aiIsSeller = !BargainState.isPlayerSeller;
            return LlmDialogService.StartFetch(this, BuildLlmSystemPrompt(aiIsSeller), BuildLlmUserPrompt(stage, roundNo, aiPrice, oppPrev, oppNew, diff));
        }

        /// <summary>LLM成功→用LLM台词+情绪，态度落成±5%让步率修正（作用于下轮报价）；失败→回落CSV并清零修正</summary>
        private BargainState.DialogLine TakeLlmOrCsv(LlmDialogService.Fetch fetch, BargainState.DialogLine csvLine)
        {
            if (fetch != null && fetch.done && fetch.ok)
            {
                BargainState.aiAttitudeAdjust = fetch.attitude * LlmDialogService.AttitudeToRateRange;
                Debug.Log($"[LlmDialog] 采用LLM台词，下轮AI让步率修正={BargainState.aiAttitudeAdjust:F3}");
                return new BargainState.DialogLine { text = fetch.text, emotion = fetch.emotion };
            }
            BargainState.aiAttitudeAdjust = 0f;
            return csvLine;
        }

        private string BuildLlmSystemPrompt(bool aiIsSeller)
        {
            string aiName = aiIsSeller ? BargainState.sellerName : BargainState.buyerName;
            string oppName = aiIsSeller ? BargainState.buyerName : BargainState.sellerName;
            int rep = aiIsSeller ? BargainState.sellerRep : BargainState.buyerRep;
            int selfInterest = aiIsSeller ? BargainState.sellerSelfInterest : BargainState.buyerSelfInterest;
            string role = aiIsSeller ? "卖方（想把房产卖出好价钱）" : "买方（想低价买下房产）";
            int personaIdx = Mathf.Clamp(BargainState.aiCardIndex, 0, CardPersonas.Length - 1);
            string lang = BargainState.language == "en" ? "English" : "中文";

            return
                $"你在像素风大富翁游戏《繁荣》的讨价还价场景中扮演NPC角色「{aiName}」，正与玩家「{oppName}」当面砍价。\n" +
                $"你的性格：{CardPersonas[personaIdx]}。你当前声望{rep}，利己程度{selfInterest}，本次是{role}。\n" +
                $"交易标的：{BargainState.sellerName}的房产（市场价约{BargainState.marketPrice}元）。\n" +
                "严格按以下要求输出：\n" +
                "1. 只输出一个JSON对象，格式为 {\"text\":\"台词\",\"emotion\":\"平\",\"attitude\":0.0}，不要输出任何其它内容。\n" +
                $"2. 台词用{lang}，一句话，不超过24个字，符合角色口吻，可以带点市井气。\n" +
                "3. 台词中只允许提到我给你的报价数字，严禁编造其它任何金额或数字。\n" +
                "4. emotion四选一：平=平静，怒=生气，惊=意外，意=得意。\n" +
                "5. attitude表示让步意愿：-1=强硬不让步，1=很愿意让步，取-1到1之间的小数。";
        }

        private string BuildLlmUserPrompt(string stage, int roundNo, int aiPrice, int oppPrev, int oppNew, int diff)
        {
            if (stage == "开场")
            {
                return $"第1轮开场。你方开价{aiPrice}元，对方开价{oppNew}元，价差约{diff}元。" +
                    "说一句开场白表明你的要价/出价态度。只输出JSON对象。";
            }
            string moveDesc;
            switch (stage)
            {
                case "让步低": moveDesc = "对方让步很小，几乎没动"; break;
                case "让步中": moveDesc = "对方让步了一部分"; break;
                case "让步高": moveDesc = "对方大幅让步，很有诚意"; break;
                default: moveDesc = "对方维持原报价"; break;
            }
            return $"第{roundNo}轮报价。你方报价{aiPrice}元。对方刚把报价从{oppPrev}元改到{oppNew}元（{moveDesc}），目前价差约{diff}元。" +
                "对对方这次的让步说一句回应，符合你的性格和当前情绪。只输出JSON对象。";
        }
    }
}
