using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// Bargain系统：人格卡选择 + 3轮报价 + 成交/失败。
    /// 4种人格卡：1=交个朋友, 2=实价交易, 3=看人下菜, 4=极限压价
    /// </summary>
    public class BargainController : MonoBehaviour
    {
        [Header("Background")]
        [SerializeField] private BargainBackgroundScroller backgroundScroller;

        [Header("Personality Cards")]
        [SerializeField] private List<Image> personalityCardImages = new List<Image>();
        [SerializeField] private List<Button> personalityCardButtons = new List<Button>();

        [Header("Bargain UI")]
        [SerializeField] private GameObject personalitySelectPanel; // 人格卡选择面板
        [SerializeField] private GameObject bargainPanel;            // 报价面板
        [SerializeField] private Text infoText;                      // 显示市场价、双方报价等
        [SerializeField] private Text sellerOfferText;               // 卖方报价
        [SerializeField] private Text buyerOfferText;                // 买方报价
        [SerializeField] private Text roundText;                      // 当前轮次
        [SerializeField] private List<Button> offerButtons = new List<Button>(); // 3个报价态度按钮
        [SerializeField] private List<Text> offerButtonTexts = new List<Text>();
        [SerializeField] private Text resultText;                   // 结果显示
        [SerializeField] private GameObject resultPanel;             // 结果面板
        [SerializeField] private Button confirmResultButton;        // 确认结果

        [Header("Sprites")]
        [SerializeField] private List<Sprite> personalitySprites = new List<Sprite>();
        [SerializeField] private List<Sprite> personalityBackSprites = new List<Sprite>();

        [Header("Character Portraits & Dialog")]
        [SerializeField] private List<Image> characterPortraits = new List<Image>(); // 0=lizzie, 1=老资, 2=女彪, 3=财主
        [SerializeField] private GameObject dialogBox;                // 对话框容器
        [SerializeField] private Text dialogText;                    // 对话框文字
        [SerializeField] private Sprite dialogBgSprite;              // 文本框.png

        // 人格卡属性表
        // [0]=交个朋友: 首轮优势-10%, 改价幅度+20%, 声望+5
        // [1]=实价交易: 首轮优势-10%, 改价幅度-10%, 声望+3
        // [2]=看人下菜: 首轮优势+30%, 改价幅度+20%, 声望-2
        // [3]=极限压价: 首轮优势+30%, 改价幅度-20%, 声望-3
        private readonly float[] firstRoundAdvantage = { -0.10f, -0.10f, 0.30f, 0.30f };
        private readonly float[] priceChangeRate = { 0.20f, -0.10f, 0.20f, -0.20f };
        private readonly int[] reputationChange = { 5, 3, -2, -3 };
        private readonly string[] cardNames = { "交个朋友", "实价交易", "看人下菜", "极限压价" };

        // Bargain数据
        private int marketPrice;
        private int sellerPlayerIndex;
        private int buyerPlayerIndex;
        private int sellerReputation;
        private int buyerReputation;
        private int sellerSelfInterest;
        private int buyerSelfInterest;
        private int selectedCardIndex = -1; // 玩家选择的人格卡
        private int aiCardIndex = -1;       // AI选择的人格卡
        private int currentRound = 1;
        private float sellerOffer;
        private float buyerOffer;
        private bool isPlayerSeller;
        private bool isPlayerBuyer;
        private bool isAIVsAI;
        private string sellerName;
        private string buyerName;

        // 翻转动画
        private bool isFlipping = false;
        private readonly List<GameObject> cardInfoTexts = new List<GameObject>();

        // 对话文案数据: key=(cardIndex, isSeller, roundType), value=文案
        // roundType: 0=首轮, 1-3=第二轮(强硬/中立/让步), 4-6=第三轮
        private readonly Dictionary<(int card, bool seller, int round), string> dialogDict = new Dictionary<(int, bool, int), string>();
        private bool dialogsLoaded = false;
        private Coroutine dialogCoroutine;

        // 回调
        private System.Action<bool, int> onBargainComplete; // (success, finalPrice)

        private void Start()
        {
            // 加载对话文案
            LoadBargainDialogs();

            // 重置所有面板位置
            ResetLayoutPositions();

            // 绑定人格卡按钮
            for (int i = 0; i < personalityCardButtons.Count; i++)
            {
                int index = i;
                if (personalityCardButtons[i] != null)
                    personalityCardButtons[i].onClick.AddListener(() => SelectPersonality(index));
            }

            // 绑定报价按钮
            for (int i = 0; i < offerButtons.Count; i++)
            {
                int index = i;
                if (offerButtons[i] != null)
                    offerButtons[i].onClick.AddListener(() => SelectOffer(index));
            }

            // 绑定确认按钮
            if (confirmResultButton != null)
                confirmResultButton.onClick.AddListener(ConfirmResult);

            // 默认隐藏
            if (bargainPanel != null) bargainPanel.SetActive(false);
            if (resultPanel != null) resultPanel.SetActive(false);

            // 读取跨场景BargainData并初始化
            if (DiceRollController.BargainData.bargainActive)
            {
                StartBargain(
                    DiceRollController.BargainData.marketPrice,
                    DiceRollController.BargainData.sellerIndex,
                    DiceRollController.BargainData.buyerIndex,
                    DiceRollController.BargainData.sellerRep,
                    DiceRollController.BargainData.buyerRep,
                    DiceRollController.BargainData.sellerSelfInterest,
                    DiceRollController.BargainData.buyerSelfInterest,
                    DiceRollController.BargainData.playerIsBuyer,
                    null
                );
            }
        }

        /// <summary>
        /// 开始Bargain
        /// </summary>
        public void StartBargain(int marketPrice, int sellerIdx, int buyerIdx,
            int sellerRep, int buyerRep, int sellerSelf, int buyerSelf,
            bool playerIsBuyer, System.Action<bool, int> onComplete)
        {
            this.marketPrice = marketPrice;
            this.sellerPlayerIndex = sellerIdx;
            this.buyerPlayerIndex = buyerIdx;
            this.sellerReputation = sellerRep;
            this.buyerReputation = buyerRep;
            this.sellerSelfInterest = sellerSelf;
            this.buyerSelfInterest = buyerSelf;
            this.isPlayerBuyer = playerIsBuyer;
            this.isPlayerSeller = !playerIsBuyer && sellerIdx == 0;
            this.isAIVsAI = sellerIdx != 0 && buyerIdx != 0;
            this.onBargainComplete = onComplete;
            this.currentRound = 1;
            this.sellerName = DiceRollController.BargainData.sellerName;
            this.buyerName = DiceRollController.BargainData.buyerName;

            // 设置立绘可见性
            SetupPortraits();

            // 设置人格卡图片（正面）
            for (int i = 0; i < personalityCardImages.Count && i < personalitySprites.Count; i++)
            {
                if (personalityCardImages[i] != null && personalitySprites[i] != null)
                {
                    personalityCardImages[i].sprite = personalitySprites[i];
                    personalityCardImages[i].preserveAspect = true;
                    personalityCardImages[i].enabled = true;
                }
            }

            // AI vs AI: 自动结算
            if (isAIVsAI)
            {
                AutoResolveBargain();
                return;
            }

            // 显示人格卡选择
            if (personalitySelectPanel != null) personalitySelectPanel.SetActive(true);
            if (bargainPanel != null) bargainPanel.SetActive(false);

            // 设置卡背文字并启动翻转动画
            SetupCardBacks();
            foreach (var btn in personalityCardButtons)
            {
                if (btn != null) btn.interactable = false;
            }
            StartCoroutine(CardFlipAnimation());
        }

        /// <summary>
        /// 创建卡背文字子物体并设置内容
        /// </summary>
        private void SetupCardBacks()
        {
            cardInfoTexts.Clear();

            // 从场景中现有 Text 获取字体
            Font font = null;
            if (infoText != null) font = infoText.font;
            if (font == null && roundText != null) font = roundText.font;

            for (int i = 0; i < personalityCardImages.Count; i++)
            {
                var card = personalityCardImages[i];
                if (card == null)
                {
                    cardInfoTexts.Add(null);
                    continue;
                }

                // 如果已存在则先删除
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

                string adv = FormatPercent(firstRoundAdvantage[i]);
                string rate = FormatPercent(priceChangeRate[i]);
                string rep = reputationChange[i] >= 0 ? $"+{reputationChange[i]}" : $"{reputationChange[i]}";
                text.text = $"{I18n.T("card_" + i, cardNames[i])}\n" +
                            $"{I18n.T("card_adv", $"首轮优势: {adv}", ("adv", adv))}\n" +
                            $"{I18n.T("card_rate", $"改价幅度: {rate}", ("rate", rate))}\n" +
                            $"{I18n.T("card_rep", $"声望: {rep}", ("rep", rep))}";

                textObj.SetActive(false);
                cardInfoTexts.Add(textObj);
            }
        }

        private static string FormatPercent(float value)
        {
            int percent = Mathf.RoundToInt(value * 100f);
            return percent >= 0 ? $"+{percent}%" : $"{percent}%";
        }

        /// <summary>
        /// 卡牌翻转动画：正面1秒 → 翻转 → 显示卡背+属性文字
        /// </summary>
        private IEnumerator CardFlipAnimation()
        {
            isFlipping = true;

            // 正面展示1秒
            yield return new WaitForSeconds(1f);

            float flipDuration = 0.3f;

            // 翻转前半段：scaleX 1→0
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

            // 翻转中点：切换到卡背
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

            // 翻转后半段：scaleX 0→1
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
                if (card != null)
                    card.rectTransform.localScale = Vector3.one;
            }

            foreach (var btn in personalityCardButtons)
            {
                if (btn != null) btn.interactable = true;
            }

            isFlipping = false;
        }

        /// <summary>
        /// 玩家选择人格卡
        /// </summary>
        private void SelectPersonality(int cardIndex)
        {
            if (isFlipping) return;
            selectedCardIndex = cardIndex;
            Debug.Log($"[Bargain] Player selected: {cardNames[cardIndex]}");

            // AI选择人格卡
            int aiRep = isPlayerBuyer ? sellerReputation : buyerReputation;
            int aiSelf = isPlayerBuyer ? sellerSelfInterest : buyerSelfInterest;
            aiCardIndex = SelectAIPersonality(aiRep, aiSelf);
            Debug.Log($"[Bargain] AI selected: {cardNames[aiCardIndex]}");

            // 隐藏人格卡选择，显示报价面板
            if (personalitySelectPanel != null) personalitySelectPanel.SetActive(false);
            if (bargainPanel != null) bargainPanel.SetActive(true);

            // 计算首轮报价
            CalculateFirstRoundOffers();
            UpdateBargainDisplay();

            // 显示卖方首轮报价对话
            StartCoroutine(ShowFirstRoundDialog());
        }

        /// <summary>首轮报价后显示卖方对话</summary>
        private IEnumerator ShowFirstRoundDialog()
        {
            // 卖方先说话
            int sellerCard = isPlayerSeller ? selectedCardIndex : aiCardIndex;
            string sellerText = GetDialog(sellerCard, true, 0);
            if (!string.IsNullOrEmpty(sellerText) && sellerPlayerIndex < characterPortraits.Count)
            {
                yield return ShowDialogCoroutine(sellerPlayerIndex, sellerText, 2.5f);
            }

            // 买方回应
            int buyerCard = isPlayerBuyer ? selectedCardIndex : aiCardIndex;
            string buyerText = GetDialog(buyerCard, false, 0);
            if (!string.IsNullOrEmpty(buyerText) && buyerPlayerIndex < characterPortraits.Count)
            {
                yield return ShowDialogCoroutine(buyerPlayerIndex, buyerText, 2.5f);
            }
        }

        /// <summary>
        /// AI选择人格卡：x = (声望 + 利己值) / 25
        /// x>=3→卡4, x=2→卡3, x=1→卡2, x<=0→卡1
        /// </summary>
        private int SelectAIPersonality(int reputation, int selfInterest)
        {
            float x = (reputation + selfInterest) / 25f;
            if (x >= 3f) return 3; // 极限压价
            if (x >= 2f) return 2; // 看人下菜
            if (x >= 1f) return 1; // 实价交易
            return 0;               // 交个朋友
        }

        /// <summary>
        /// 计算首轮报价
        /// 卖方：marketPrice * (1 + 首轮优势 + 声望/200)
        /// 买方：marketPrice * (1 - 首轮优势 - 声望/200)
        /// </summary>
        private void CalculateFirstRoundOffers()
        {
            float sellerAdv = firstRoundAdvantage[isPlayerSeller ? selectedCardIndex : aiCardIndex];
            float buyerAdv = firstRoundAdvantage[isPlayerBuyer ? selectedCardIndex : aiCardIndex];

            sellerOffer = marketPrice * (1f + sellerAdv + sellerReputation / 200f);
            buyerOffer = marketPrice * (1f - buyerAdv - buyerReputation / 200f);

            Debug.Log($"[Bargain] Round 1: seller={sellerOffer:F0}, buyer={buyerOffer:F0}");
        }

        /// <summary>
        /// 更新报价显示
        /// </summary>
        private void UpdateBargainDisplay()
        {
            if (roundText != null)
                roundText.text = I18n.T("bargain_round_no", $"第{currentRound}轮", ("n", currentRound));
            if (sellerOfferText != null)
                sellerOfferText.text = I18n.T("bargain_seller_offer", $"卖方({sellerName}): {Mathf.RoundToInt(sellerOffer)}元", ("name", sellerName), ("offer", Mathf.RoundToInt(sellerOffer)));
            if (buyerOfferText != null)
                buyerOfferText.text = I18n.T("bargain_buyer_offer", $"买方({buyerName}): {Mathf.RoundToInt(buyerOffer)}元", ("name", buyerName), ("offer", Mathf.RoundToInt(buyerOffer)));

            // 检查是否重叠
            bool overlap = CheckOverlap();
            if (overlap)
            {
                // 成交
                int finalPrice = Mathf.RoundToInt((sellerOffer + buyerOffer) / 2f);
                ShowResult(true, finalPrice);
                return;
            }

            // 更新报价按钮文字
            float buyerDiff = sellerOffer - buyerOffer;   // 买方diff为正，报价上升
            float sellerDiff = buyerOffer - sellerOffer;  // 卖方diff为负，报价下降

            // 第2轮和第3轮的系数不同
            float[] roundFactors;
            if (currentRound == 1)
                roundFactors = new float[] { 0.30f, 0.50f, 0.70f }; // 第2轮系数
            else
                roundFactors = new float[] { 0.40f, 0.60f, 0.80f }; // 第3轮系数

            for (int i = 0; i < offerButtonTexts.Count; i++)
            {
                float factor = roundFactors[i];
                if (offerButtonTexts[i] != null)
                {
                    float playerRate = priceChangeRate[selectedCardIndex];
                    float previewOffer;
                    if (isPlayerBuyer)
                        previewOffer = buyerOffer + buyerDiff * (factor + playerRate);
                    else
                        previewOffer = sellerOffer + sellerDiff * (factor + playerRate);

                    string role = isPlayerBuyer ? I18n.T("bargain_role_buy", "买") : I18n.T("bargain_role_sell", "卖");
                    offerButtonTexts[i].text = $"{role}{Mathf.RoundToInt(previewOffer)}元";
                }
            }

            // 如果是AI vs AI 已经在AutoResolve处理
            // 如果是玩家回合，等待玩家点击报价按钮
        }

        /// <summary>
        /// 检查双方报价是否重叠（买方报价 >= 卖方报价）
        /// </summary>
        private bool CheckOverlap()
        {
            return buyerOffer >= sellerOffer;
        }

        /// <summary>
        /// 玩家选择报价态度
        /// </summary>
        private void SelectOffer(int offerIndex)
        {
            StartCoroutine(SelectOfferRoutine(offerIndex));
        }

        private IEnumerator SelectOfferRoutine(int offerIndex)
        {
            float[] roundFactors;
            if (currentRound == 1)
                roundFactors = new float[] { 0.30f, 0.50f, 0.70f };
            else
                roundFactors = new float[] { 0.40f, 0.60f, 0.80f };

            float factor = roundFactors[offerIndex];

            // 买方diff为正（报价上升），卖方diff为负（报价下降）
            float buyerDiff = sellerOffer - buyerOffer;
            float sellerDiff = buyerOffer - sellerOffer;

            // 玩家报价
            float playerRate;
            if (isPlayerBuyer)
            {
                playerRate = priceChangeRate[selectedCardIndex];
                buyerOffer = buyerOffer + buyerDiff * (factor + playerRate);
                // AI卖方报价
                float aiRate = priceChangeRate[aiCardIndex];
                sellerOffer = sellerOffer + sellerDiff * (factor + aiRate);
            }
            else
            {
                playerRate = priceChangeRate[selectedCardIndex];
                sellerOffer = sellerOffer + sellerDiff * (factor + playerRate);
                // AI买方报价
                float aiRate = priceChangeRate[aiCardIndex];
                buyerOffer = buyerOffer + buyerDiff * (factor + aiRate);
            }

            Debug.Log($"[Bargain] Round {currentRound + 1}: seller={sellerOffer:F0}, buyer={buyerOffer:F0}");

            // 计算对话文案的 roundType
            // currentRound 1 → 第2轮: roundType = 1 + offerIndex (1=强硬, 2=中立, 3=让步)
            // currentRound 2 → 第3轮: roundType = 4 + offerIndex (4=强硬, 5=中立, 6=让步)
            int playerRoundType = (currentRound == 1) ? (1 + offerIndex) : (4 + offerIndex);

            // 显示玩家对话
            int playerCard = selectedCardIndex;
            bool playerIsSeller = isPlayerSeller;
            string playerDialog = GetDialog(playerCard, playerIsSeller, playerRoundType);
            int playerIdx = isPlayerBuyer ? buyerPlayerIndex : sellerPlayerIndex;
            if (!string.IsNullOrEmpty(playerDialog))
            {
                yield return ShowDialogCoroutine(playerIdx, playerDialog, 2.5f);
            }

            // 显示AI对话
            int aiCard = aiCardIndex;
            bool aiIsSeller = !isPlayerSeller;
            string aiDialog = GetDialog(aiCard, aiIsSeller, playerRoundType);
            int aiIdx = isPlayerBuyer ? sellerPlayerIndex : buyerPlayerIndex;
            if (!string.IsNullOrEmpty(aiDialog))
            {
                yield return ShowDialogCoroutine(aiIdx, aiDialog, 2.5f);
            }

            // 检查重叠
            if (CheckOverlap())
            {
                int finalPrice = Mathf.RoundToInt((sellerOffer + buyerOffer) / 2f);
                ShowResult(true, finalPrice);
                yield break;
            }

            // 进入下一轮
            currentRound++;
            if (currentRound > 3)
            {
                // 3轮失败
                ShowResult(false, 0);
                yield break;
            }

            UpdateBargainDisplay();
        }

        /// <summary>
        /// AI vs AI 自动结算
        /// </summary>
        private void AutoResolveBargain()
        {
            // AI双方选择人格卡
            int sellerCard = SelectAIPersonality(sellerReputation, sellerSelfInterest);
            int buyerCard = SelectAIPersonality(buyerReputation, buyerSelfInterest);

            // 首轮报价
            float sOffer = marketPrice * (1f + firstRoundAdvantage[sellerCard] + sellerReputation / 200f);
            float bOffer = marketPrice * (1f - firstRoundAdvantage[buyerCard] - buyerReputation / 200f);

            Debug.Log($"[Bargain] AIvsAI Round1: seller={sOffer:F0}, buyer={bOffer:F0}");

            // 检查首轮
            if (bOffer >= sOffer)
            {
                int finalPrice = Mathf.RoundToInt((sOffer + bOffer) / 2f);
                FinishBargain(true, finalPrice, sellerCard, buyerCard);
                return;
            }

            // 第2轮
            float buyerDiff2 = sOffer - bOffer;   // 买方diff为正
            float sellerDiff2 = bOffer - sOffer;   // 卖方diff为负
            float[] r2Factors = { 0.30f, 0.50f, 0.70f };
            int sChoice2 = Random.Range(0, 3);
            int bChoice2 = Random.Range(0, 3);
            sOffer += sellerDiff2 * (r2Factors[sChoice2] + priceChangeRate[sellerCard]);
            bOffer += buyerDiff2 * (r2Factors[bChoice2] + priceChangeRate[buyerCard]);

            if (bOffer >= sOffer)
            {
                int finalPrice = Mathf.RoundToInt((sOffer + bOffer) / 2f);
                FinishBargain(true, finalPrice, sellerCard, buyerCard);
                return;
            }

            // 第3轮
            float buyerDiff3 = sOffer - bOffer;
            float sellerDiff3 = bOffer - sOffer;
            float[] r3Factors = { 0.40f, 0.60f, 0.80f };
            int sChoice3 = Random.Range(0, 3);
            int bChoice3 = Random.Range(0, 3);
            sOffer += sellerDiff3 * (r3Factors[sChoice3] + priceChangeRate[sellerCard]);
            bOffer += buyerDiff3 * (r3Factors[bChoice3] + priceChangeRate[buyerCard]);

            if (bOffer >= sOffer)
            {
                int finalPrice = Mathf.RoundToInt((sOffer + bOffer) / 2f);
                FinishBargain(true, finalPrice, sellerCard, buyerCard);
            }
            else
            {
                FinishBargain(false, 0, sellerCard, buyerCard);
            }
        }

        /// <summary>
        /// 显示结果
        /// </summary>
        private void ShowResult(bool success, int finalPrice)
        {
            if (bargainPanel != null) bargainPanel.SetActive(false);
            if (resultPanel != null) resultPanel.SetActive(true);

            // 应用声望变化
            int playerCard = isPlayerSeller || isPlayerBuyer ? selectedCardIndex : -1;
            if (playerCard >= 0 && playerCard < reputationChange.Length)
            {
                // 声望变化通过回调处理
            }

            if (resultText != null)
            {
                if (success)
                    resultText.text = $"交易成功！\n成交价: {finalPrice}元";
                else
                    resultText.text = "交易失败\n将支付租金";
            }

            _bargainSuccess = success;
            _bargainPrice = finalPrice;

            // 不在此处设置BargainResult，等玩家确认后由ConfirmResult设置
        }

        /// <summary>跨场景Bargain结果</summary>
        public static class BargainResult
        {
            public static bool completed;
            public static bool isSuccess;
            public static int finalPrice;
        }

        private bool _bargainSuccess;
        private int _bargainPrice;

        /// <summary>
        /// AI vs AI 完成Bargain
        /// </summary>
        private void FinishBargain(bool success, int finalPrice, int sellerCard, int buyerCard)
        {
            Debug.Log($"[Bargain] AIvsAI result: success={success}, price={finalPrice}");

            // 设置BargainResult供DiceRollController读取
            BargainResult.isSuccess = success;
            BargainResult.finalPrice = finalPrice;
            BargainResult.completed = true;

            // AI vs AI 直接卸载场景
            SceneManager.UnloadSceneAsync("BargainScene");
        }

        /// <summary>
        /// 玩家确认结果
        /// </summary>
        private void ConfirmResult()
        {
            // 设置BargainResult供DiceRollController读取
            BargainResult.isSuccess = _bargainSuccess;
            BargainResult.finalPrice = _bargainPrice;
            BargainResult.completed = true;

            // 卸载BargainScene，GameScene保持不变
            SceneManager.UnloadSceneAsync("BargainScene");
        }

        // ==================== 布局重置 ====================

        /// <summary>
        /// 在Start时重置所有面板到正确位置，避免编辑器中拖动后位置错乱。
        /// Canvas 1920×1080，坐标基于 center anchor。
        /// </summary>
        private void ResetLayoutPositions()
        {
            // PersonalitySelectPanel: 全屏stretch，居中
            ResetStretchPanel(personalitySelectPanel, Vector2.zero);

            // BargainPanel: center anchor，居中
            if (bargainPanel != null)
            {
                var rt = bargainPanel.GetComponent<RectTransform>();
                rt.anchoredPosition = Vector2.zero;
            }

            // ResultPanel: 全屏stretch，居中
            ResetStretchPanel(resultPanel, Vector2.zero);

            // DialogBox: center anchor，居中（运行时由PositionDialog动态定位）
            if (dialogBox != null)
            {
                var rt = dialogBox.GetComponent<RectTransform>();
                rt.anchoredPosition = Vector2.zero;
            }

            // 立绘位置（center anchor，基于1920×1080）
            // Lizzie(0): 左下角
            SetPortraitPos(0, new Vector2(-600, -350));
            // 老资(1): 右上角
            SetPortraitPos(1, new Vector2(500, 200));
            // 女彪(2): 右上角偏左
            SetPortraitPos(2, new Vector2(650, 180));
            // 财主(3): 右上角偏右
            SetPortraitPos(3, new Vector2(800, 150));

            // PersonalitySelectPanel 内的卡牌：4张水平排列
            if (personalitySelectPanel != null)
            {
                float[] cardX = { -690, -230, 230, 690 };
                for (int i = 0; i < 4; i++)
                {
                    var card = personalitySelectPanel.transform.Find($"Card{i}");
                    if (card != null)
                    {
                        var rt = card as RectTransform;
                        rt.anchoredPosition = new Vector2(cardX[i], 0);
                    }
                }
                // 标题居中顶部
                var title = personalitySelectPanel.transform.Find("Title");
                if (title != null)
                {
                    var rt = title as RectTransform;
                    rt.anchoredPosition = new Vector2(0, 350);
                }
            }

            // BargainPanel 内部元素
            if (bargainPanel != null)
            {
                ResetChild(bargainPanel.transform, "RoundText", new Vector2(0, 180));
                ResetChild(bargainPanel.transform, "InfoText", new Vector2(0, 80));
                ResetChild(bargainPanel.transform, "SellerOffer", new Vector2(0, 40));
                ResetChild(bargainPanel.transform, "BuyerOffer", new Vector2(0, 0));
                ResetChild(bargainPanel.transform, "OfferBtn0", new Vector2(-220, -100));
                ResetChild(bargainPanel.transform, "OfferBtn1", new Vector2(0, -100));
                ResetChild(bargainPanel.transform, "OfferBtn2", new Vector2(220, -100));
            }

            // ResultPanel 内部
            if (resultPanel != null)
            {
                var card = resultPanel.transform.Find("Card");
                if (card != null)
                {
                    var rt = card as RectTransform;
                    rt.anchoredPosition = Vector2.zero;
                }
            }
        }

        private void ResetStretchPanel(GameObject panel, Vector2 offset)
        {
            if (panel == null) return;
            var rt = panel.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = offset;
        }

        private void SetPortraitPos(int index, Vector2 pos)
        {
            if (characterPortraits == null || index >= characterPortraits.Count) return;
            if (characterPortraits[index] == null) return;
            var rt = characterPortraits[index].rectTransform;
            rt.anchoredPosition = pos;
        }

        private void ResetChild(Transform parent, string name, Vector2 pos)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                var rt = child as RectTransform;
                rt.anchoredPosition = pos;
            }
        }

        // ==================== 对话系统 ====================

        /// <summary>加载Bargain对话文案CSV</summary>
        private void LoadBargainDialogs()
        {
            if (dialogsLoaded) return;
            dialogsLoaded = true;

            string resourcePath = "Data/bargain_dialogs";
            var csv = Resources.Load<TextAsset>(resourcePath);
            if (csv == null) { Debug.LogError($"[Bargain] Dialog CSV not found in Resources: {resourcePath}"); return; }

            var lines = new List<string>(csv.text.Split('\n'));
            if (lines.Count < 2) return;

            // 解析表头获取列映射
            // CSV列顺序: 空, 4卖方, 3卖方, 2卖方, 1卖方, 4买方, 3买方, 2买方, 1买方, ...
            // 映射到 (cardIndex, isSeller): 
            //   col1=4卖方→(3,true), col2=3卖方→(2,true), col3=2卖方→(1,true), col4=1卖方→(0,true)
            //   col5=4买方→(3,false), col6=3买方→(2,false), col7=2买方→(1,false), col8=1买方→(0,false)
            int[] cardForCol = { 3, 2, 1, 0, 3, 2, 1, 0 };
            bool[] sellerForCol = { true, true, true, true, false, false, false, false };

            // 行映射: 
            // [1]=首轮报价 → roundType=0
            // [2]=第二轮强硬 → roundType=1 (offerIndex=0)
            // [3]=第二轮中立 → roundType=2 (offerIndex=1)
            // [4]=第二轮让步 → roundType=3 (offerIndex=2)
            // [5]=第三轮强硬 → roundType=4 (offerIndex=0)
            // [6]=第三轮中立 → roundType=5 (offerIndex=1)
            // [7]=第三轮让步 → roundType=6 (offerIndex=2)
            int[] roundForRow = { 0, 1, 2, 3, 4, 5, 6 };

            for (int rowIdx = 1; rowIdx <= 7 && rowIdx < lines.Count; rowIdx++)
            {
                string line = lines[rowIdx].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] fields = ParseCSVLine(line);
                int roundType = roundForRow[rowIdx - 1];

                for (int col = 0; col < 8 && col + 1 < fields.Length; col++)
                {
                    string text = fields[col + 1].Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    int card = cardForCol[col];
                    bool isSeller = sellerForCol[col];
                    dialogDict[(card, isSeller, roundType)] = text;
                }
            }

            Debug.Log($"[Bargain] Loaded {dialogDict.Count} dialog entries");
        }

        private string[] ParseCSVLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inQuotes = !inQuotes;
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(line.Substring(start, i - start).Trim('"', ' '));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start).Trim('"', ' '));
            return result.ToArray();
        }

        /// <summary>获取对话文案</summary>
        private string GetDialog(int cardIndex, bool isSeller, int roundType)
        {
            if (dialogDict.TryGetValue((cardIndex, isSeller, roundType), out string text))
                return text;
            return null;
        }

        /// <summary>设置立绘可见性</summary>
        private void SetupPortraits()
        {
            // 隐藏所有立绘
            foreach (var p in characterPortraits)
            {
                if (p != null) p.gameObject.SetActive(false);
            }

            // 显示卖方和买方立绘
            if (sellerPlayerIndex >= 0 && sellerPlayerIndex < characterPortraits.Count && characterPortraits[sellerPlayerIndex] != null)
                characterPortraits[sellerPlayerIndex].gameObject.SetActive(true);
            if (buyerPlayerIndex >= 0 && buyerPlayerIndex < characterPortraits.Count && characterPortraits[buyerPlayerIndex] != null)
                characterPortraits[buyerPlayerIndex].gameObject.SetActive(true);
        }

        /// <summary>显示对话（协程控制显示→等待→隐藏）</summary>
        private IEnumerator ShowDialogCoroutine(int speakerIndex, string text, float duration = 2.5f)
        {
            if (dialogBox == null || dialogText == null || string.IsNullOrEmpty(text)) yield break;

            // 隐藏报价按钮期间不可点击
            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = false;
            }

            dialogText.text = text;
            dialogBox.SetActive(true);

            // 定位对话框到说话者旁边
            PositionDialog(speakerIndex);

            yield return new WaitForSeconds(duration);

            dialogBox.SetActive(false);

            // 恢复报价按钮
            foreach (var btn in offerButtons)
            {
                if (btn != null) btn.interactable = true;
            }
        }

        /// <summary>根据说话者位置定位对话框</summary>
        private void PositionDialog(int speakerIndex)
        {
            if (speakerIndex >= characterPortraits.Count || characterPortraits[speakerIndex] == null) return;

            var portraitRt = characterPortraits[speakerIndex].rectTransform;
            var dialogRt = dialogBox.GetComponent<RectTransform>();

            // 玩家(index=0)在左下角，对话框放角色右上方
            if (speakerIndex == 0)
            {
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x + 250, portraitRt.anchoredPosition.y + 200);
            }
            else
            {
                // AI在右上角，对话框放角色左下方
                dialogRt.anchoredPosition = new Vector2(portraitRt.anchoredPosition.x - 420, portraitRt.anchoredPosition.y - 150);
            }
        }

        /// <summary>显示对话并等待完成</summary>
        private void ShowDialog(int speakerIndex, int cardIndex, bool isSeller, int roundType)
        {
            string text = GetDialog(cardIndex, isSeller, roundType);
            if (string.IsNullOrEmpty(text)) return;

            if (dialogCoroutine != null) StopCoroutine(dialogCoroutine);
            dialogCoroutine = StartCoroutine(ShowDialogCoroutine(speakerIndex, text));
        }
    }
}
