using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// 报价场景控制器：3轮报价，成交或失败后加载BargainScene_Result。
    /// v3移植：台词池分阶段台词+表情差分+说话弹跳；保留防重入与台词框位置修复。
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
        private bool offerBusy; // 防连点：同一时间只允许一个报价协程
        private bool resultDecided; // 结果已判定后禁止再报价（场景切换窗口内重入会改写已判定结果）

        // 表情差分：key=(角色索引, 情绪)，情绪 0平1怒2惊3得意；0用场景原始立绘
        private readonly Dictionary<(int, int), Sprite> expressionSprites = new Dictionary<(int, int), Sprite>();
        private readonly Dictionary<int, Sprite> baseSprites = new Dictionary<int, Sprite>();

        private void Start()
        {
            BargainState.LoadDialogs();

            // 运行时隐藏对话框（编辑器中保持可见便于调整）
            if (dialogBox != null)
                dialogBox.SetActive(false);

            SetupPortraits();

            for (int i = 0; i < offerButtons.Count; i++)
            {
                int index = i;
                if (offerButtons[i] != null)
                    offerButtons[i].onClick.AddListener(() => SelectOffer(index));
            }

            // 计算首轮报价
            BargainState.CalculateFirstRoundOffers();
            UpdateBargainDisplay();

            // 显示首轮对话
            StartCoroutine(ShowFirstRoundDialog());
        }

        private void SetupPortraits()
        {
            for (int i = 0; i < characterPortraits.Count; i++)
            {
                var p = characterPortraits[i];
                if (p == null) continue;
                p.gameObject.SetActive(false);
                if (!baseSprites.ContainsKey(i)) baseSprites[i] = p.sprite; // 记录场景原始立绘，作为情绪0
            }
            if (BargainState.sellerIndex >= 0 && BargainState.sellerIndex < characterPortraits.Count && characterPortraits[BargainState.sellerIndex] != null)
                characterPortraits[BargainState.sellerIndex].gameObject.SetActive(true);
            if (BargainState.buyerIndex >= 0 && BargainState.buyerIndex < characterPortraits.Count && characterPortraits[BargainState.buyerIndex] != null)
                characterPortraits[BargainState.buyerIndex].gameObject.SetActive(true);
        }

        private void UpdateBargainDisplay()
        {
            if (roundText != null)
                roundText.text = $"第{BargainState.currentRound}轮";
            if (sellerOfferText != null)
                sellerOfferText.text = $"卖方({BargainState.sellerName}): {Mathf.RoundToInt(BargainState.sellerOffer)}元";
            if (buyerOfferText != null)
                buyerOfferText.text = $"买方({BargainState.buyerName}): {Mathf.RoundToInt(BargainState.buyerOffer)}元";

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

                    string role = BargainState.isPlayerBuyer ? "买" : "卖";
                    offerButtonTexts[i].text = $"{role}{Mathf.RoundToInt(previewOffer)}元";
                }
            }
        }

        private void SelectOffer(int offerIndex)
        {
            if (offerBusy || resultDecided) return;
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

                float playerRate = BargainState.priceChangeRate[BargainState.selectedCardIndex];
                float aiRate = BargainState.priceChangeRate[BargainState.aiCardIndex];

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

                // 玩家说出自己的让步姿态（选项1~3）
                var playerLine = BargainState.GetLine(
                    BargainState.selectedCardIndex, BargainState.isPlayerSeller,
                    $"选项{offerIndex + 1}",
                    opponentName: aiName, offer: playerPrice, diff: diff);
                if (!string.IsNullOrEmpty(playerLine.text))
                    yield return ShowDialogCoroutine(playerIdx, playerLine, 2.5f);

                // AI根据玩家的让步幅度做出反应
                float movePct = Mathf.Clamp01(factor + playerRate);
                var aiLine = BargainState.GetLine(
                    BargainState.aiCardIndex, !BargainState.isPlayerSeller,
                    BargainState.ReactionStage(movePct),
                    opponentName: BargainState.isPlayerBuyer ? BargainState.buyerName : BargainState.sellerName,
                    myPrice: aiPrice, diff: diff);
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
            // 卖方开场白
            int sellerCard = BargainState.isPlayerSeller ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
            var sellerLine = BargainState.GetLine(sellerCard, true, "开场",
                opponentName: BargainState.buyerName,
                myPrice: Mathf.RoundToInt(BargainState.sellerOffer));
            if (!string.IsNullOrEmpty(sellerLine.text) && BargainState.sellerIndex < characterPortraits.Count)
                yield return ShowDialogCoroutine(BargainState.sellerIndex, sellerLine, 2.5f);

            // 买方开场白
            int buyerCard = BargainState.isPlayerBuyer ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
            var buyerLine = BargainState.GetLine(buyerCard, false, "开场",
                opponentName: BargainState.sellerName,
                myPrice: Mathf.RoundToInt(BargainState.buyerOffer));
            if (!string.IsNullOrEmpty(buyerLine.text) && BargainState.buyerIndex < characterPortraits.Count)
                yield return ShowDialogCoroutine(BargainState.buyerIndex, buyerLine, 2.5f);
        }

        private void GotoResult(bool success, int finalPrice)
        {
            if (resultDecided) return; // 幂等：结果只判定一次，后续重入不得改写
            resultDecided = true;

            // 立即禁用报价按钮，防止场景切换窗口内的点击重入
            foreach (var btn in offerButtons)
                if (btn != null) btn.interactable = false;

            BargainState.resultSuccess = success;
            BargainState.resultFinalPrice = finalPrice;

            // 生成收尾台词（以玩家的人格卡与立场为准）
            int speakerCard = BargainState.isPlayerBuyer || BargainState.isPlayerSeller ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
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

        // ==================== 对话系统 ====================

        private IEnumerator ShowDialogCoroutine(int speakerIndex, BargainState.DialogLine line, float duration = 2.5f)
        {
            if (dialogBox == null || dialogText == null || string.IsNullOrEmpty(line.text)) yield break;

            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = false;
            }

            SetPortraitExpression(speakerIndex, line.emotion);
            StartCoroutine(PortraitPunch(speakerIndex));

            dialogText.text = line.text;
            dialogBox.SetActive(true);
            PositionDialog(speakerIndex);

            yield return new WaitForSeconds(duration);

            dialogBox.SetActive(false);

            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = true;
            }
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
            {
                // 没有差分图时退回原始立绘，保证不缺图
                return baseSprites.TryGetValue(charIndex, out var fallback) ? fallback : null;
            }
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            expressionSprites[key] = sprite;
            return sprite;
        }

        /// <summary>说话时立绘轻微放大回弹，强调"谁在说话"</summary>
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
                float scale = Mathf.LerpUnclamped(punch, 1f, t * t); // ease-out回落
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

            // 台词框靠近说话人头像：玩家(0)在左下，AI(1-3)在右上。
            // （此前两组偏移写反，说话人的台词框会弹到对方头像旁，台词归属视觉错位）
            if (speakerIndex == 0)
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x - 420, portraitRt.anchoredPosition.y - 150);
            else
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x + 250, portraitRt.anchoredPosition.y + 200);
        }
    }
}
