using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// 报价场景控制器：3轮报价，成交或失败后加载BargainScene_Result。
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

        private void Start()
        {
            BargainState.LoadDialogs();

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
            foreach (var p in characterPortraits)
            {
                if (p != null) p.gameObject.SetActive(false);
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
            StartCoroutine(SelectOfferRoutine(offerIndex));
        }

        private IEnumerator SelectOfferRoutine(int offerIndex)
        {
            float[] roundFactors = BargainState.currentRound == 1
                ? new float[] { 0.30f, 0.50f, 0.70f }
                : new float[] { 0.40f, 0.60f, 0.80f };

            float factor = roundFactors[offerIndex];
            float buyerDiff = BargainState.sellerOffer - BargainState.buyerOffer;
            float sellerDiff = BargainState.buyerOffer - BargainState.sellerOffer;

            if (BargainState.isPlayerBuyer)
            {
                float playerRate = BargainState.priceChangeRate[BargainState.selectedCardIndex];
                BargainState.buyerOffer += buyerDiff * (factor + playerRate);
                float aiRate = BargainState.priceChangeRate[BargainState.aiCardIndex];
                BargainState.sellerOffer += sellerDiff * (factor + aiRate);
            }
            else
            {
                float playerRate = BargainState.priceChangeRate[BargainState.selectedCardIndex];
                BargainState.sellerOffer += sellerDiff * (factor + playerRate);
                float aiRate = BargainState.priceChangeRate[BargainState.aiCardIndex];
                BargainState.buyerOffer += buyerDiff * (factor + aiRate);
            }

            Debug.Log($"[BargainOffer] Round {BargainState.currentRound + 1}: seller={BargainState.sellerOffer:F0}, buyer={BargainState.buyerOffer:F0}");

            int playerRoundType = (BargainState.currentRound == 1) ? (1 + offerIndex) : (4 + offerIndex);

            // 玩家对话
            string playerDialog = BargainState.GetDialog(BargainState.selectedCardIndex, BargainState.isPlayerSeller, playerRoundType);
            int playerIdx = BargainState.isPlayerBuyer ? BargainState.buyerIndex : BargainState.sellerIndex;
            if (!string.IsNullOrEmpty(playerDialog))
                yield return ShowDialogCoroutine(playerIdx, playerDialog, 2.5f);

            // AI对话
            string aiDialog = BargainState.GetDialog(BargainState.aiCardIndex, !BargainState.isPlayerSeller, playerRoundType);
            int aiIdx = BargainState.isPlayerBuyer ? BargainState.sellerIndex : BargainState.buyerIndex;
            if (!string.IsNullOrEmpty(aiDialog))
                yield return ShowDialogCoroutine(aiIdx, aiDialog, 2.5f);

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

        private IEnumerator ShowFirstRoundDialog()
        {
            int sellerCard = BargainState.isPlayerSeller ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
            string sellerText = BargainState.GetDialog(sellerCard, true, 0);
            if (!string.IsNullOrEmpty(sellerText) && BargainState.sellerIndex < characterPortraits.Count)
                yield return ShowDialogCoroutine(BargainState.sellerIndex, sellerText, 2.5f);

            int buyerCard = BargainState.isPlayerBuyer ? BargainState.selectedCardIndex : BargainState.aiCardIndex;
            string buyerText = BargainState.GetDialog(buyerCard, false, 0);
            if (!string.IsNullOrEmpty(buyerText) && BargainState.buyerIndex < characterPortraits.Count)
                yield return ShowDialogCoroutine(BargainState.buyerIndex, buyerText, 2.5f);
        }

        private void GotoResult(bool success, int finalPrice)
        {
            BargainState.resultSuccess = success;
            BargainState.resultFinalPrice = finalPrice;
            SceneManager.LoadScene("讨价还价_结果");
        }

        // ==================== 对话系统 ====================

        private IEnumerator ShowDialogCoroutine(int speakerIndex, string text, float duration = 2.5f)
        {
            if (dialogBox == null || dialogText == null || string.IsNullOrEmpty(text)) yield break;

            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = false;
            }

            dialogText.text = text;
            dialogBox.SetActive(true);
            PositionDialog(speakerIndex);

            yield return new WaitForSeconds(duration);

            dialogBox.SetActive(false);

            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = true;
            }
        }

        private void PositionDialog(int speakerIndex)
        {
            if (speakerIndex >= characterPortraits.Count || characterPortraits[speakerIndex] == null) return;

            var portraitRt = characterPortraits[speakerIndex].rectTransform;
            var dialogRt = dialogBox.GetComponent<RectTransform>();

            if (speakerIndex == 0)
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x + 250, portraitRt.anchoredPosition.y + 200);
            else
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x - 420, portraitRt.anchoredPosition.y - 150);
        }
    }
}
