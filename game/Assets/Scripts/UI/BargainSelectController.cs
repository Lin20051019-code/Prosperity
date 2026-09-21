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

        [Header("Title")]
        [SerializeField] private Text titleText; // 顶部标题（按角色显示）

        private bool isFlipping = false;
        private readonly List<GameObject> cardInfoTexts = new List<GameObject>();

        private void Start()
        {
            BargainState.LoadDialogs();
            BargainState.InitFromBargainData();

            // 按角色显示标题，让玩家明确自己是买方还是卖方
            if (titleText != null)
            {
                if (BargainState.isAIVsAI)
                    titleText.text = I18n.T("bargain_title_aivai", "AI 之间的讨价还价");
                else if (BargainState.isPlayerSeller)
                    titleText.text = I18n.T("bargain_title_seller", "你是卖方——选择你的谈判策略");
                else if (BargainState.isPlayerBuyer)
                    titleText.text = I18n.T("bargain_title_buyer", "你是买方——选择你的谈判策略");
                else
                    titleText.text = "选择你的人格卡";
            }

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

            // 设置卡背并直接展示策略（取消翻转动画，按钮立即可点）
            SetupCardBacks();
            ShowCardsDirectly();
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
            // WebGL 注意: 内置字体不含中文且打包后无系统字体兜底，必须加载项目字体
            Font font = Resources.Load<Font>("VonwaonBitmap-16px");

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
                text.text = $"{I18n.T("card_" + i, BargainState.cardNames[i])}\n" +
                            $"{I18n.T("card_adv", $"首轮优势: {adv}", ("adv", adv))}\n" +
                            $"{I18n.T("card_rate", $"改价幅度: {rate}", ("rate", rate))}\n" +
                            $"{I18n.T("card_rep", $"声望: {rep}", ("rep", rep))}";

                textObj.SetActive(false);
                cardInfoTexts.Add(textObj);
            }
        }

        /// <summary>直接展示策略卡背与数据文字（跳过翻转动画）</summary>
        private void ShowCardsDirectly()
        {
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

            foreach (var btn in personalityCardButtons)
            {
                if (btn != null) btn.interactable = true;
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
            if (isFlipping || BargainState.selectedCardIndex >= 0) return; // 防连点：选过即忽略，避免重复加载报价场景
            BargainState.selectedCardIndex = cardIndex;
            Debug.Log($"[BargainSelect] Player selected: {BargainState.cardNames[cardIndex]}");

            int aiRep = BargainState.isPlayerBuyer ? BargainState.sellerRep : BargainState.buyerRep;
            int aiSelf = BargainState.isPlayerBuyer ? BargainState.sellerSelfInterest : BargainState.buyerSelfInterest;
            BargainState.aiCardIndex = BargainState.SelectAIPersonality(aiRep, aiSelf);
            Debug.Log($"[BargainSelect] AI selected: {BargainState.cardNames[BargainState.aiCardIndex]}");

            // 记录双方所选人格卡，供回 GameScene 结算声望
            if (BargainState.isPlayerSeller)
            {
                DiceRollController.BargainData.sellerCardIndex = cardIndex;
                DiceRollController.BargainData.buyerCardIndex = BargainState.aiCardIndex;
            }
            else
            {
                DiceRollController.BargainData.sellerCardIndex = BargainState.aiCardIndex;
                DiceRollController.BargainData.buyerCardIndex = cardIndex;
            }

            // 加载报价场景
            SceneManager.LoadScene("讨价还价_报价");
        }

        /// <summary>AI vs AI 自动结算（不依赖本场景任何实例；供DiceRollController在GameScene内直接调用，
        /// 跳过选卡场景加载——同步LoadScene下一帧才切换，选卡会渲染1~2帧造成"闪一下卡面朝上的选卡界面"）</summary>
        public static void AutoResolve()
        {
            BargainState.LoadDialogs();
            BargainState.InitFromBargainData();

            int sellerCard = BargainState.SelectAIPersonality(BargainState.sellerRep, BargainState.sellerSelfInterest);
            int buyerCard = BargainState.SelectAIPersonality(BargainState.buyerRep, BargainState.buyerSelfInterest);
            DiceRollController.BargainData.sellerCardIndex = sellerCard;
            DiceRollController.BargainData.buyerCardIndex = buyerCard;

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

            // AI vs AI 的收尾台词（借卖方人格卡的成交/失败台词，v3移植）
            BargainState.resultLine = BargainState.GetLine(
                sellerCard, true, BargainState.resultSuccess ? "成交" : "失败",
                opponentName: BargainState.buyerName,
                final: BargainState.resultFinalPrice).text;

            Debug.Log($"[BargainSelect] AIvsAI result: success={BargainState.resultSuccess}, price={BargainState.resultFinalPrice}");
        }

        /// <summary>AI vs AI 自动结算后跳结果场景（选卡场景内兜底路径，正常流程已被DiceRollController直接调用AutoResolve后绕过）</summary>
        private void AutoResolveAndGotoResult()
        {
            AutoResolve();
            SceneManager.LoadScene("讨价还价_结果");
        }
    }
}
