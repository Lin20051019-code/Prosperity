using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// 人格卡选择场景控制器。选完卡后加载BargainScene_Offer。
    /// </summary>
    public class BargainSelectController : MonoBehaviour
    {
        [Header("Background")]
        [SerializeField] private BargainBackgroundScroller backgroundScroller;

        [Header("Personality Cards")]
        [SerializeField] private List<Image> personalityCardImages = new List<Image>();
        [SerializeField] private List<Button> personalityCardButtons = new List<Button>();

        [Header("Sprites")]
        [SerializeField] private List<Sprite> personalitySprites = new List<Sprite>();
        [SerializeField] private List<Sprite> personalityBackSprites = new List<Sprite>();

        [Header("Character Portraits")]
        [SerializeField] private List<Image> characterPortraits = new List<Image>();

        private bool isFlipping = false;
        private readonly List<GameObject> cardInfoTexts = new List<GameObject>();

        private void Start()
        {
            BargainState.LoadDialogs();
            BargainState.InitFromBargainData();

            SetupPortraits();
            SetupCardSprites();

            // 绑定按钮
            for (int i = 0; i < personalityCardButtons.Count; i++)
            {
                int index = i;
                if (personalityCardButtons[i] != null)
                    personalityCardButtons[i].onClick.AddListener(() => SelectPersonality(index));
            }

            // AI vs AI: 自动结算，直接跳到结果场景
            if (BargainState.isAIVsAI)
            {
                AutoResolveAndGotoResult();
                return;
            }

            // 设置卡背并启动翻转动画
            SetupCardBacks();
            foreach (var btn in personalityCardButtons)
            {
                if (btn != null) btn.interactable = false;
            }
            StartCoroutine(CardFlipAnimation());
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

        private void SetupCardSprites()
        {
            for (int i = 0; i < personalityCardImages.Count && i < personalitySprites.Count; i++)
            {
                if (personalityCardImages[i] != null && personalitySprites[i] != null)
                {
                    personalityCardImages[i].sprite = personalitySprites[i];
                    personalityCardImages[i].preserveAspect = true;
                    personalityCardImages[i].enabled = true;
                }
            }
        }

        private void SetupCardBacks()
        {
            cardInfoTexts.Clear();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            for (int i = 0; i < personalityCardImages.Count; i++)
            {
                var card = personalityCardImages[i];
                if (card == null) { cardInfoTexts.Add(null); continue; }

                var existing = card.transform.Find("CardInfoText");
                if (existing != null) DestroyImmediate(existing.gameObject);

                var textObj = new GameObject("CardInfoText");
                textObj.transform.SetParent(card.transform, false);
                textObj.transform.SetAsLastSibling();

                var textRect = textObj.AddComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                var text = textObj.AddComponent<Text>();
                text.font = font;
                text.fontSize = 28;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.supportRichText = true;
                text.raycastTarget = false;

                string adv = BargainState.FormatPercent(BargainState.firstRoundAdvantage[i]);
                string rate = BargainState.FormatPercent(BargainState.priceChangeRate[i]);
                string rep = BargainState.reputationChange[i] >= 0 ? $"+{BargainState.reputationChange[i]}" : $"{BargainState.reputationChange[i]}";
                text.text = $"{BargainState.cardNames[i]}\n首轮优势: {adv}\n改价幅度: {rate}\n声望: {rep}";

                textObj.SetActive(false);
                cardInfoTexts.Add(textObj);
            }
        }

        private IEnumerator CardFlipAnimation()
        {
            isFlipping = true;
            yield return new WaitForSeconds(1f);

            float flipDuration = 0.3f;
            float elapsed = 0f;
            while (elapsed < flipDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flipDuration);
                float scaleX = 1f - t;
                foreach (var card in personalityCardImages)
                {
                    if (card != null)
                        card.rectTransform.localScale = new Vector3(scaleX, 1f, 1f);
                }
                yield return null;
            }

            for (int i = 0; i < personalityCardImages.Count; i++)
            {
                if (personalityCardImages[i] == null) continue;
                if (i < personalityBackSprites.Count && personalityBackSprites[i] != null)
                {
                    personalityCardImages[i].sprite = personalityBackSprites[i];
                    personalityCardImages[i].overrideSprite = personalityBackSprites[i];
                }
                if (i < cardInfoTexts.Count && cardInfoTexts[i] != null)
                    cardInfoTexts[i].SetActive(true);
            }

            elapsed = 0f;
            while (elapsed < flipDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flipDuration);
                float scaleX = t;
                foreach (var card in personalityCardImages)
                {
                    if (card != null)
                        card.rectTransform.localScale = new Vector3(scaleX, 1f, 1f);
                }
                yield return null;
            }

            foreach (var card in personalityCardImages)
            {
                if (card != null) card.rectTransform.localScale = Vector3.one;
            }
            foreach (var btn in personalityCardButtons)
            {
                if (btn != null) btn.interactable = true;
            }
            isFlipping = false;
        }

        private void SelectPersonality(int cardIndex)
        {
            if (isFlipping) return;
            BargainState.selectedCardIndex = cardIndex;
            Debug.Log($"[BargainSelect] Player selected: {BargainState.cardNames[cardIndex]}");

            int aiRep = BargainState.isPlayerBuyer ? BargainState.sellerRep : BargainState.buyerRep;
            int aiSelf = BargainState.isPlayerBuyer ? BargainState.sellerSelfInterest : BargainState.buyerSelfInterest;
            BargainState.aiCardIndex = BargainState.SelectAIPersonality(aiRep, aiSelf);
            Debug.Log($"[BargainSelect] AI selected: {BargainState.cardNames[BargainState.aiCardIndex]}");

            // 加载报价场景
            SceneManager.LoadScene("讨价还价_报价");
        }

        /// <summary>AI vs AI 自动结算</summary>
        private void AutoResolveAndGotoResult()
        {
            int sellerCard = BargainState.SelectAIPersonality(BargainState.sellerRep, BargainState.sellerSelfInterest);
            int buyerCard = BargainState.SelectAIPersonality(BargainState.buyerRep, BargainState.buyerSelfInterest);

            float sOffer = BargainState.marketPrice * (1f + BargainState.firstRoundAdvantage[sellerCard] + BargainState.sellerRep / 200f);
            float bOffer = BargainState.marketPrice * (1f - BargainState.firstRoundAdvantage[buyerCard] - BargainState.buyerRep / 200f);

            if (bOffer >= sOffer)
            {
                BargainState.resultSuccess = true;
                BargainState.resultFinalPrice = Mathf.RoundToInt((sOffer + bOffer) / 2f);
            }
            else
            {
                float buyerDiff2 = sOffer - bOffer;
                float sellerDiff2 = bOffer - sOffer;
                float[] r2 = { 0.30f, 0.50f, 0.70f };
                sOffer += sellerDiff2 * (r2[Random.Range(0, 3)] + BargainState.priceChangeRate[sellerCard]);
                bOffer += buyerDiff2 * (r2[Random.Range(0, 3)] + BargainState.priceChangeRate[buyerCard]);

                if (bOffer >= sOffer)
                {
                    BargainState.resultSuccess = true;
                    BargainState.resultFinalPrice = Mathf.RoundToInt((sOffer + bOffer) / 2f);
                }
                else
                {
                    float buyerDiff3 = sOffer - bOffer;
                    float sellerDiff3 = bOffer - sOffer;
                    float[] r3 = { 0.40f, 0.60f, 0.80f };
                    sOffer += sellerDiff3 * (r3[Random.Range(0, 3)] + BargainState.priceChangeRate[sellerCard]);
                    bOffer += buyerDiff3 * (r3[Random.Range(0, 3)] + BargainState.priceChangeRate[buyerCard]);

                    if (bOffer >= sOffer)
                    {
                        BargainState.resultSuccess = true;
                        BargainState.resultFinalPrice = Mathf.RoundToInt((sOffer + bOffer) / 2f);
                    }
                    else
                    {
                        BargainState.resultSuccess = false;
                        BargainState.resultFinalPrice = 0;
                    }
                }
            }

            Debug.Log($"[BargainSelect] AIvsAI result: success={BargainState.resultSuccess}, price={BargainState.resultFinalPrice}");
            SceneManager.LoadScene("讨价还价_结果");
        }
    }
}
