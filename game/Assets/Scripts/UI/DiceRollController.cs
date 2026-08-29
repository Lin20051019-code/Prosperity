using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// 骰子掷出与玩家移动控制器（2D UI版本，支持4人轮流行动）。
    /// 回合顺序：玩家 → AI1 → AI2 → AI3 → 玩家 → ...
    /// 玩家回合：点击按钮 → 选骰子 → Token移动
    /// AI回合：自动掷骰子 → 自动选较大点数 → Token自动移动
    /// </summary>
    public class DiceRollController : MonoBehaviour
    {
        [Header("Path Tiles (counter-clockwise from start)")]
        [SerializeField] private List<RectTransform> tilePath = new List<RectTransform>();

        [Header("Tokens (0=Player, 1=AI1, 2=AI2, 3=AI3)")]
        [SerializeField] private List<RectTransform> tokens = new List<RectTransform>();

        [Header("Dice UI")]
        [SerializeField] private Button rollDiceButton;
        [SerializeField] private GameObject dicePanel;
        [SerializeField] private Button dice1Button;
        [SerializeField] private Button dice2Button;
        [SerializeField] private Image dice1FaceImage;
        [SerializeField] private Image dice2FaceImage;
        [SerializeField] private Text hintText;
        [SerializeField] private List<Sprite> diceSprites = new List<Sprite>(); // 骰子1-6的图片

        [Header("Timing")]
        [SerializeField] private float rollDuration = 2f;
        [SerializeField] private float rollInterval = 0.08f;
        [SerializeField] private float moveStepDuration = 0.18f;
        [SerializeField] private float turnDelay = 1f; // 回合间延迟（秒）

        [Header("Player Info Panels")]
        [SerializeField] private List<GameObject> playerPanels = new List<GameObject>(); // 4个状态面板，移动时隐藏对应面板

        [Header("Penalty System")]
        [SerializeField] private PenaltyCardPanel penaltyCardPanel;
        [SerializeField] private List<PlayerInfoPanel> playerInfoPanels = new List<PlayerInfoPanel>();

        [Header("Building System")]
        [SerializeField] private BuildingPanel buildingPanel;
        [SerializeField] private List<Sprite> buildingSprites = new List<Sprite>(); // 0级,1级,2级,3级建筑sprite

        [Header("Broadcast")]
        [SerializeField] private BroadcastBar broadcastBar;

        // 每个建筑格的数据（按tilePath索引）
        private BuildingData[] buildingData;

        [Header("Camera Zoom")]
        [SerializeField] private RectTransform board;              // 棋盘RectTransform，用于缩放和移动
        [SerializeField] private float normalScale = 1.8f;          // 正常缩放
        [SerializeField] private float zoomedScale = 2.8f;          // 玩家移动时的缩放
        [SerializeField] private float zoomTransitionDuration = 1.0f; // 缩放过渡时间（秒）
        [SerializeField] private float postMoveDelay = 0.5f; // 玩家移动完成后等待多久再缩回（秒）
        [SerializeField] private Vector2 boardCenterPosition = new Vector2(0, 224f); // 正常状态Board居中位置

        // 4个Token当前在路径上的索引
        private int[] tileIndices = new int[4] { 0, 0, 0, 0 };

        // ===== 测试模式 =====
        private bool testMode = true;
        private bool testPlayerDone = false;
        // ===== 测试模式结束 =====
        // 当前回合（0=玩家, 1-3=AI）
        private int currentTurn = 0;
        // 是否正在处理中（掷骰子或移动中）
        private bool isBusy = false;
        // 是否在等待玩家选择骰子
        private bool waitingForChoice = false;
        // 两个骰子的结果
        private int dice1Result;
        private int dice2Result;

        private void Start()
        {
            // 绑定按钮事件
            if (rollDiceButton != null)
                rollDiceButton.onClick.AddListener(OnRollDice);

            if (dice1Button != null)
                dice1Button.onClick.AddListener(() => ChooseDice(1));

            if (dice2Button != null)
                dice2Button.onClick.AddListener(() => ChooseDice(2));

            // 初始化建筑数据（初始为政府所有=ownerIndex=-2）
            buildingData = new BuildingData[tilePath.Count];
            for (int i = 0; i < tilePath.Count; i++)
            {
                buildingData[i] = new BuildingData();
                var tileImg = tilePath[i]?.GetComponent<Image>();
                if (tileImg != null && tileImg.sprite != null && tileImg.sprite.name == "房屋块")
                {
                    buildingData[i].ownerIndex = -2;
                }
            }

            // 初始化AI属性
            for (int i = 1; i < 4; i++)
            {
                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    playerInfoPanels[i].Data.InitAIAttributes();
                }
            }

            // 设置初始缩放
            if (board != null)
            {
                board.localScale = Vector3.one * normalScale;
                board.anchoredPosition = boardCenterPosition;
            }

            // 设置格子悬停提示
            TileTooltip.SetupTooltips(this);

            // 播放游戏场景BGM
            AudioManager.Instance?.PlayGameSceneBGM();

            // 尝试恢复游戏状态（从Bargain返回时）
            if (RestoreGameState())
            {
                // 状态已恢复，Bargain结果已处理
                return;
            }

            // 全新游戏：初始化
            for (int i = 0; i < tokens.Count && i < 4; i++)
            {
                if (tokens[i] != null && tilePath.Count > 0)
                {
                    Vector2[] offsets = {
                        new Vector2(-8, -8),
                        new Vector2(8, -8),
                        new Vector2(-8, -25),
                        new Vector2(8, -25),
                    };
                    tokens[i].anchoredPosition = GetTileAnchorPos(0) + offsets[i];
                }
                tileIndices[i] = 0;
            }

            // 默认隐藏骰子面板
            if (dicePanel != null)
                dicePanel.SetActive(false);

            // 初始化繁荣值UI
            UpdateProsperity();

            // 第1回合开始播报
            BroadcastMsg("第1回合开始");

            // 玩家回合开始
            StartPlayerTurn();
        }

        private void OnDestroy()
        {
            if (rollDiceButton != null)
                rollDiceButton.onClick.RemoveListener(OnRollDice);
            if (dice1Button != null)
                dice1Button.onClick.RemoveAllListeners();
            if (dice2Button != null)
                dice2Button.onClick.RemoveAllListeners();
        }

        // ==================== 回合管理 ====================

        /// <summary>
        /// 开始玩家回合：启用骰子按钮
        /// </summary>
        private void StartPlayerTurn()
        {
            currentTurn = 0;

            // 检查是否跳过本回合
            if (skipNextTurn[0])
            {
                skipNextTurn[0] = false;
                Debug.Log("[Turn] Player turn skipped (penalty)");
                BroadcastMsg($"{playerInfoPanels[0].Data.playerName} 的回合被跳过（交通事故）");
                if (hintText != null)
                    hintText.text = "你的回合被跳过（堵车）";
                StartCoroutine(SkipAndNextTurn());
                return;
            }

            if (hintText != null)
                hintText.text = "你的回合";
            if (rollDiceButton != null)
                rollDiceButton.interactable = true;
            Debug.Log("[Turn] Player turn started");
        }

        /// <summary>
        /// 开始AI回合：禁用骰子按钮，自动掷骰子
        /// </summary>
        private IEnumerator StartAITurn(int aiIndex)
        {
            currentTurn = aiIndex;
            if (rollDiceButton != null)
                rollDiceButton.interactable = false;

            // 检查是否跳过本回合
            if (skipNextTurn[aiIndex])
            {
                skipNextTurn[aiIndex] = false;
                Debug.Log($"[Turn] AI{aiIndex} turn skipped (penalty)");
                BroadcastMsg($"{playerInfoPanels[aiIndex].Data.playerName} 的回合被跳过（交通事故）");
                yield return new WaitForSeconds(turnDelay);
                yield return NextTurn();
                yield break;
            }

            Debug.Log($"[Turn] AI{aiIndex} turn started");

            bool needsReroll;
            do
            {
                needsReroll = false;

                // 等待延迟，让玩家看清
                yield return new WaitForSeconds(turnDelay);

                // 显示骰子面板并播放滚动动画
                if (dicePanel != null)
                    dicePanel.SetActive(true);

                // 播放摇骰子音效
                AudioManager.Instance?.PlayDiceRoll();

                if (testMode && aiIndex == 1 && tileIndices[1] == 0)
                {
                    dice1Result = 1;
                    dice2Result = 1;
                }
                else
                {
                    dice1Result = Random.Range(1, 7);
                    dice2Result = Random.Range(1, 7);
                }

                if (hintText != null)
                    hintText.text = $"{playerInfoPanels[aiIndex].Data.playerName} 掷骰子中...";

                // 数字快速变化模拟旋转
                float diceElapsed = 0f;
                while (diceElapsed < rollDuration)
                {
                    diceElapsed += rollInterval;
                    if (dice1FaceImage != null && diceSprites.Count > 0)
                        dice1FaceImage.sprite = diceSprites[Random.Range(0, 6)];
                    if (dice2FaceImage != null && diceSprites.Count > 0)
                        dice2FaceImage.sprite = diceSprites[Random.Range(0, 6)];
                    yield return new WaitForSeconds(rollInterval);
                }

                // 播放骰子落下音效
                AudioManager.Instance?.PlayDiceLandDelayed(0f);

                // 显示最终结果
                if (dice1FaceImage != null && diceSprites.Count > 0)
                    dice1FaceImage.sprite = diceSprites[dice1Result - 1];
                if (dice2FaceImage != null && diceSprites.Count > 0)
                    dice2FaceImage.sprite = diceSprites[dice2Result - 1];

                int chosenSteps = dice1Result + dice2Result;

                Debug.Log($"[AI{aiIndex}] Moving {chosenSteps} steps (dice1={dice1Result} + dice2={dice2Result})");
                BroadcastMsg($"{playerInfoPanels[aiIndex].Data.playerName} 掷出 {dice1Result} + {dice2Result} = {chosenSteps} 步");

                // 等待让玩家看清结果
                yield return new WaitForSeconds(1.5f);

                // 隐藏骰子面板
                if (dicePanel != null)
                    dicePanel.SetActive(false);

                // AI Token移动
                yield return MoveToken(aiIndex, chosenSteps);

                // Bargain进行中，不进入下一回合（HandleBargainResult会调用NextTurn）
                if (BargainData.bargainActive)
                    yield break;

                // 道路修缮：再投一次骰子
                if (rerollNextTurn[aiIndex])
                {
                    rerollNextTurn[aiIndex] = false;
                    needsReroll = true;
                    BroadcastMsg($"{playerInfoPanels[aiIndex].Data.playerName} 道路修缮，再投一次骰子");
                }
            } while (needsReroll);

            // 进入下一回合
            yield return NextTurn();
        }

        /// <summary>
        /// 当前回合结束，进入下一回合
        /// </summary>
        /// <summary>
        /// 跳过回合后进入下一回合
        /// </summary>
        private IEnumerator SkipAndNextTurn()
        {
            yield return new WaitForSeconds(turnDelay);
            yield return NextTurn();
        }

        private IEnumerator NextTurn()
        {
            if (gameEnded) yield break;

            yield return new WaitForSeconds(turnDelay);

            currentTurn = (currentTurn + 1) % 4;

            // 一回合结束（回到玩家回合 = 新回合开始）
            if (currentTurn == 0)
            {
                // 计算繁荣值
                UpdateProsperity();

                // 检查游戏结束条件
                if (CheckGameEnd()) yield break;

                // 回合数+1
                currentRound++;
                if (currentRound > MaxRounds)
                {
                    // 15回合结束
                    EndGameByRounds();
                    yield break;
                }

                // 新回合播报
                BroadcastMsg($"第{currentRound}回合开始");
            }

            if (currentTurn == 0)
            {
                StartPlayerTurn();
            }
            else
            {
                yield return StartCoroutine(StartAITurn(currentTurn));
            }
        }

        // ==================== 骰子掷出 ====================

        /// <summary>
        /// 玩家点击"掷骰子"按钮
        /// </summary>
        private void OnRollDice()
        {
            if (isBusy || waitingForChoice) return;
            StartCoroutine(PlayerRollRoutine());
        }

        /// <summary>
        /// 玩家掷骰子协程：掷骰子 → 等待选择
        /// </summary>
        private IEnumerator PlayerRollRoutine()
        {
            isBusy = true;
            if (rollDiceButton != null)
                rollDiceButton.interactable = false;

            // 播放摇骰子音效，延迟后播放骰子落下音效
            AudioManager.Instance?.PlayDiceRoll();

            // 显示骰子面板并播放滚动动画
            if (dicePanel != null)
                dicePanel.SetActive(true);

            // 测试模式
            if (testMode && !testPlayerDone)
            {
                dice1Result = 1;
                dice2Result = 1;
                testPlayerDone = true;
            }
            else
            {
                dice1Result = Random.Range(1, 7);
                dice2Result = Random.Range(1, 7);
            }

            // 数字快速变化模拟旋转
            if (hintText != null)
                hintText.text = $"{playerInfoPanels[0].Data.playerName} 掷骰子中...";

            float elapsed = 0f;
            while (elapsed < rollDuration)
            {
                elapsed += rollInterval;
                if (dice1FaceImage != null && diceSprites.Count > 0)
                    dice1FaceImage.sprite = diceSprites[Random.Range(0, 6)];
                if (dice2FaceImage != null && diceSprites.Count > 0)
                    dice2FaceImage.sprite = diceSprites[Random.Range(0, 6)];
                yield return new WaitForSeconds(rollInterval);
            }

            // 播放骰子落下音效
            AudioManager.Instance?.PlayDiceLandDelayed(0f);

            // 显示最终结果
            if (dice1FaceImage != null && diceSprites.Count > 0)
                dice1FaceImage.sprite = diceSprites[dice1Result - 1];
            if (dice2FaceImage != null && diceSprites.Count > 0)
                dice2FaceImage.sprite = diceSprites[dice2Result - 1];

            int steps = dice1Result + dice2Result;

            if (hintText != null)
                hintText.text = $"掷出 {dice1Result} + {dice2Result} = {steps}";

            Debug.Log($"[Player] Dice sum={steps}");
            BroadcastMsg($"掷出 {dice1Result} + {dice2Result} = {steps} 步");

            isBusy = false;
            waitingForChoice = false;

            yield return new WaitForSeconds(1.5f);

            if (dicePanel != null)
                dicePanel.SetActive(false);

            StartCoroutine(PlayerMoveAndNext(steps));
        }

        /// <summary>
        /// 通用掷骰子协程（玩家和AI共用）
        /// 1. 显示面板 → 2. 数字快速变化 → 3. 显示最终结果
        /// </summary>
        private IEnumerator RollDiceRoutine(bool isAI)
        {
            // 显示骰子面板
            if (dicePanel != null)
                dicePanel.SetActive(true);

            // 禁用骰子按钮
            if (dice1Button != null) dice1Button.interactable = false;
            if (dice2Button != null) dice2Button.interactable = false;

            // 提示文字
            if (hintText != null)
                hintText.text = isAI ? $"{playerInfoPanels[currentTurn].Data.playerName} 掷骰子中..." : $"{playerInfoPanels[0].Data.playerName} 掷骰子中...";

            // 确定结果（两个骰子独立随机）
            dice1Result = Random.Range(1, 7);
            dice2Result = Random.Range(1, 7);

            // 数字快速变化模拟旋转
            float elapsed = 0f;
            while (elapsed < rollDuration)
            {
                elapsed += rollInterval;
                if (dice1FaceImage != null && diceSprites.Count > 0)
                    dice1FaceImage.sprite = diceSprites[Random.Range(0, 6)];
                if (dice2FaceImage != null && diceSprites.Count > 0)
                    dice2FaceImage.sprite = diceSprites[Random.Range(0, 6)];
                yield return new WaitForSeconds(rollInterval);
            }

            // 显示最终结果
            if (dice1FaceImage != null && diceSprites.Count > 0)
                dice1FaceImage.sprite = diceSprites[dice1Result - 1];
            if (dice2FaceImage != null && diceSprites.Count > 0)
                dice2FaceImage.sprite = diceSprites[dice2Result - 1];

            // 玩家回合显示结果提示（无需点击，自动行动）
            if (!isAI)
            {
                if (hintText != null)
                    hintText.text = $"掷出 {dice1Result} + {dice2Result} = {dice1Result + dice2Result}";
            }

            Debug.Log($"[Dice] Dice1={dice1Result}, Dice2={dice2Result}, Sum={dice1Result + dice2Result}");
            BroadcastMsg($"掷出 {dice1Result} 和 {dice2Result}，合计 {dice1Result + dice2Result} 步");
        }

        // ==================== 玩家选择 ====================

        /// <summary>
        /// 玩家确认骰子结果（两骰子之和为步数）
        /// </summary>
        private void ChooseDice(int which)
        {
            if (!waitingForChoice) return;
            waitingForChoice = false;

            int steps = dice1Result + dice2Result;
            Debug.Log($"[Player] Confirmed dice sum, moving {steps} steps");

            if (dice1Button != null) dice1Button.interactable = false;
            if (dice2Button != null) dice2Button.interactable = false;

            StartCoroutine(PlayerMoveAndNext(steps));
        }

        /// <summary>
        /// 玩家移动并进入下一回合
        /// </summary>
        private IEnumerator PlayerMoveAndNext(int steps)
        {
            yield return new WaitForSeconds(0.3f);

            if (dicePanel != null)
                dicePanel.SetActive(false);

            yield return MoveToken(0, steps);

            // Bargain进行中，不进入下一回合（HandleBargainResult会调用NextTurn）
            if (BargainData.bargainActive)
                yield break;

            // 道路修缮：再投一次骰子
            if (rerollNextTurn[0])
            {
                rerollNextTurn[0] = false;
                BroadcastMsg("道路修缮：可以再投一次骰子");
                if (rollDiceButton != null)
                    rollDiceButton.interactable = true;
                yield break;
            }

            yield return NextTurn();
        }

        // ==================== Token移动 ====================

        /// <summary>
        /// 指定Token沿路径移动指定步数。
        /// 玩家(tokenIndex==0)移动时：先放大棋盘并聚焦玩家 → 移动 → 缩回正常。
        /// AI移动时：保持正常缩放不变。
        /// </summary>
        private IEnumerator MoveToken(int tokenIndex, int steps)
        {
            bool isPlayer = (tokenIndex == 0);

            // 移动时隐藏所有玩家的状态栏
            foreach (var p in playerPanels)
            {
                if (p != null) p.SetActive(false);
            }

            // 玩家移动前：放大并聚焦到玩家Token位置
            if (isPlayer && board != null)
            {
                yield return ZoomToToken(tokens[tokenIndex], zoomedScale);
            }

            for (int i = 0; i < steps; i++)
            {
                int prevIndex = tileIndices[tokenIndex];
                tileIndices[tokenIndex] = (tileIndices[tokenIndex] + 1) % tilePath.Count;

                // 经过起点时获得工资200元（仅在正常移动时，坐火车不触发）
                if (prevIndex != 0 && tileIndices[tokenIndex] == 0)
                {
                    if (tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null)
                    {
                        var pd = playerInfoPanels[tokenIndex].Data;
                        pd.cash += 200;
                        playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                        AudioManager.Instance?.PlayCoin();
                        Debug.Log($"[Salary] Token{tokenIndex} passed start, +200 cash → {pd.cash}");
                        BroadcastMsg($"{pd.playerName} 经过起点，获得工资200元");
                    }
                }

                // 使用AnchorPoint的位置，确保Token在格子表面而非中心
                Vector2 targetPos = GetTileAnchorPos(tileIndices[tokenIndex]);

                if (tokens[tokenIndex] != null)
                {
                    // 跳跃移动：抛物线弧线
                    Vector2 startPos = tokens[tokenIndex].anchoredPosition;
                    float jumpHeight = 20f; // 跳跃高度
                    float elapsed = 0f;
                    while (elapsed < moveStepDuration)
                    {
                        elapsed += Time.deltaTime;
                        float t = elapsed / moveStepDuration;
                        // 水平方向线性插值
                        float x = Mathf.Lerp(startPos.x, targetPos.x, t);
                        // 垂直方向抛物线：y = start + (target-start)*t - 4*jumpHeight*t*(1-t)
                        float baseY = Mathf.Lerp(startPos.y, targetPos.y, t);
                        float arc = jumpHeight * 4f * t * (1f - t); // 抛物线，中间最高
                        tokens[tokenIndex].anchoredPosition = new Vector2(x, baseY + arc);

                        // 玩家移动时跟随Token的水平位置，但不跟随跳跃弧线（只跟baseY不跟arc）
                        if (isPlayer && board != null)
                        {
                            FollowTokenXY(tokens[tokenIndex], baseY);
                        }

                        yield return null;
                    }
                    tokens[tokenIndex].anchoredPosition = targetPos;

                    // 落地后目标格子上下弹跳
                    yield return TileBounce(tilePath[tileIndices[tokenIndex]]);
                }

                Debug.Log($"[Move] Token{tokenIndex} step {i + 1}/{steps}, now at tile {tileIndices[tokenIndex]}: {tilePath[tileIndices[tokenIndex]].name}");
            }

            // 玩家移动后：等待1秒再缩回正常大小
            if (isPlayer && board != null)
            {
                yield return new WaitForSeconds(postMoveDelay);
                yield return ZoomToNormal();
            }

            // 移动完成后检测格子类型，触发惩罚事件
            yield return CheckTileEffect(tokenIndex);

            // 移动完成后恢复所有状态栏显示
            foreach (var p in playerPanels)
            {
                if (p != null) p.SetActive(true);
            }
        }

        /// <summary>
        /// 平滑过渡缩放到目标值，同时将Board位置调整使指定Token居中
        /// </summary>
        private IEnumerator ZoomToToken(RectTransform token, float targetScale)
        {
            isZooming = true;
            float startScale = board.localScale.x;
            Vector2 startPos = board.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < zoomTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / zoomTransitionDuration;
                // 使用SmoothStep实现缓入缓出
                float smoothT = t * t * (3f - 2f * t);

                float curScale = Mathf.Lerp(startScale, targetScale, smoothT);
                board.localScale = Vector3.one * curScale;

                // 计算让Token居中的Board位置
                // Token在Board的localPosition就是其anchoredPosition
                // 放大后需要把Board往反方向移动，使Token出现在屏幕中心
                Vector2 tokenLocalPos = token.anchoredPosition;
                // 屏幕中心对应Board的localPosition为(0,0)，所以Board需要移动 -tokenLocalPos * scale
                Vector2 targetBoardPos = -tokenLocalPos * curScale;
                board.anchoredPosition = Vector2.Lerp(startPos, targetBoardPos, smoothT);

                yield return null;
            }

            board.localScale = Vector3.one * targetScale;
            board.anchoredPosition = -token.anchoredPosition * targetScale;
            isZooming = false;
        }

        /// <summary>
        /// 平滑过渡缩放回正常大小
        /// </summary>
        private IEnumerator ZoomToNormal()
        {
            isZooming = true;
            float startScale = board.localScale.x;
            Vector2 startPos = board.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < zoomTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / zoomTransitionDuration;
                float smoothT = t * t * (3f - 2f * t);

                float curScale = Mathf.Lerp(startScale, normalScale, smoothT);
                board.localScale = Vector3.one * curScale;

                // 回到居中位置
                board.anchoredPosition = Vector2.Lerp(startPos, boardCenterPosition, smoothT);

                yield return null;
            }

            board.localScale = Vector3.one * normalScale;
            board.anchoredPosition = boardCenterPosition;
            isZooming = false;
        }

        /// <summary>
        /// 实时跟随Token位置（在放大状态下，移动时调用）
        /// </summary>
        private void FollowToken(RectTransform token)
        {
            float curScale = board.localScale.x;
            board.anchoredPosition = -token.anchoredPosition * curScale;
        }

        /// <summary>
        /// 跟随Token的水平位置和指定Y（不含跳跃弧线）
        /// </summary>
        private void FollowTokenXY(RectTransform token, float baseY)
        {
            float curScale = board.localScale.x;
            // 只跟随X和baseY，不含跳跃arc
            Vector2 pos = new Vector2(token.anchoredPosition.x, baseY);
            board.anchoredPosition = -pos * curScale;
        }

        /// <summary>
        /// 格子落地弹跳动画：格子被踩到后上下抖动一下
        /// </summary>
        // ==================== 格子效果 ====================

        /// <summary>
        /// 检测当前格子的类型，触发对应效果。
        /// 惩罚块 → 弹出惩罚卡牌面板。
        /// </summary>
        private IEnumerator CheckTileEffect(int tokenIndex)
        {
            var tile = tilePath[tileIndices[tokenIndex]];
            if (tile == null) yield break;

            var img = tile.GetComponent<Image>();
            if (img == null || img.sprite == null) yield break;

            // 检测是否为惩罚块（惩罚块.png）
            if (img.sprite.name == "惩罚块")
            {
                AudioManager.Instance?.PlayPenalty();
                Debug.Log($"[Penalty] Token{tokenIndex} landed on penalty tile: {tile.name}");

                bool panelClosed = false;
                SheNicest.UI.PenaltyEventData selectedEvent = null;

                if (penaltyCardPanel != null)
                {
                    penaltyCardPanel.Show(PenaltyCardPanel.CardType.Penalty,
                        (evt) => { selectedEvent = evt; },
                        () => { panelClosed = true; }
                    );
                }
                else
                {
                    panelClosed = true;
                }

                // 等待面板关闭
                while (!panelClosed) yield return null;

                // 应用惩罚效果
                if (selectedEvent != null)
                {
                    ApplyPenaltyEffect(tokenIndex, selectedEvent);
                    BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 触发惩罚: {selectedEvent.eventName} — {selectedEvent.effectDescription}");
                }
            }

            // 检测是否为建筑格（房屋块）
            if (img.sprite.name == "房屋块")
            {
                yield return HandleBuilding(tileIndices[tokenIndex], tokenIndex);
            }

            // 检测是否为火车站（火车块）
            if (img.sprite.name == "火车块(1)")
            {
                yield return HandleTrainStation(tileIndices[tokenIndex], tokenIndex);
            }

            // 检测是否为事件格（事件块）
            if (img.sprite.name == "事件块")
            {
                AudioManager.Instance?.PlayRandomEvent();
                yield return HandleEventCard(tileIndices[tokenIndex], tokenIndex);
            }

            // 检测是否为奖励格（奖励块）
            if (img.sprite.name == "奖励块")
            {
                AudioManager.Instance?.PlayReward();
                yield return HandleRewardCard(tileIndices[tokenIndex], tokenIndex);
            }

            // 检测是否为公益中心（基金会）
            if (img.sprite.name == "基金会")
            {
                yield return HandleCharity(tokenIndex);
            }
        }

        /// <summary>
        /// 应用惩罚效果（特定目标逻辑）
        /// </summary>
        private void ApplyPenaltyEffect(int tokenIndex, PenaltyEventData eventData)
        {
            string evName = eventData.eventName;

            switch (eventData.effectType)
            {
                case PenaltyEffect.CashLoss:
                    // 道德败坏：只扣资产最低的玩家
                    if (evName == "道德败坏")
                    {
                        int minWealth = int.MaxValue;
                        // 找到最低财富值
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            int w = playerInfoPanels[i].Data.Wealth;
                            if (w < minWealth) minWealth = w;
                        }
                        // 扣所有财富等于最低值的玩家
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            if (playerInfoPanels[i].Data.Wealth == minWealth)
                            {
                                playerInfoPanels[i].Data.cash += eventData.cashChange;
                                playerInfoPanels[i].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"{playerInfoPanels[i].Data.playerName} 资产最低，现金{eventData.cashChange}");
                            }
                        }
                    }
                    else
                    {
                        ApplyEffectToPlayer(tokenIndex, eventData);
                    }
                    break;

                case PenaltyEffect.CashAndRepLoss:
                    // 恶性竞争：当前玩家+随机一个其他玩家，声望各-5
                    if (evName == "恶性竞争")
                    {
                        ApplyEffectToPlayer(tokenIndex, eventData);
                        int randomOther = GetRandomOtherPlayer(tokenIndex);
                        if (randomOther >= 0)
                        {
                            ApplyEffectToPlayer(randomOther, eventData);
                            BroadcastMsg($"{playerInfoPanels[randomOther].Data.playerName} 也被牵连，声望-5");
                        }
                    }
                    else
                    {
                        ApplyEffectToPlayer(tokenIndex, eventData);
                    }
                    break;

                case PenaltyEffect.SellProperty:
                    // 资金紧缺：随机卖一块自己的房产，退还价值一半
                    SellRandomProperty(tokenIndex);
                    break;

                case PenaltyEffect.DowngradeProperty:
                    // 年久失修：随机选自己的房产降一级
                    DowngradeRandomProperty(tokenIndex);
                    break;

                default:
                    ApplyEffectToPlayer(tokenIndex, eventData);
                    break;
            }
        }

        // ==================== 建筑系统 ====================

        /// <summary>
        // ==================== 公益中心 ====================

        /// <summary>
        /// 公益中心：玩家可支付400获得12声誉。
        /// AI：声望≤所有玩家平均值则捐款，大于则不捐。
        /// </summary>
        private IEnumerator HandleCharity(int tokenIndex)
        {
            Debug.Log($"[Charity] Token{tokenIndex} on charity tile");

            // AI自动判断
            if (tokenIndex != 0)
            {
                yield return new WaitForSeconds(0.5f);

                if (tokenIndex >= playerInfoPanels.Count || playerInfoPanels[tokenIndex] == null) yield break;
                var pd = playerInfoPanels[tokenIndex].Data;

                // 计算所有玩家声望平均值
                int totalRep = 0, count = 0;
                for (int i = 0; i < playerInfoPanels.Count; i++)
                {
                    if (playerInfoPanels[i] == null) continue;
                    totalRep += playerInfoPanels[i].Data.reputation;
                    count++;
                }
                int avgRep = count > 0 ? totalRep / count : 50;

                // 声望≤平均值且现金足够则捐款
                if (pd.reputation <= avgRep && pd.cash >= 400)
                {
                    pd.cash -= 400;
                    pd.reputation = Mathf.Clamp(pd.reputation + 12, 0, 100);
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"{pd.playerName} 向公益中心捐款400元，获得12声誉");
                }
                else
                {
                    Debug.Log($"[Charity] AI{tokenIndex} rep={pd.reputation} avg={avgRep}, chose not to donate");
                }
                yield break;
            }

            // 玩家：弹出选择面板
            bool panelClosed = false;
            bool choseDonate = false;

            if (buildingPanel != null)
            {
                buildingPanel.ShowGovernmentOwned(new BuildingData { level = 0, ownerIndex = -2, baseValue = 400 },
                    400,
                    () => { choseDonate = true; },
                    () => { panelClosed = true; }
                );
            }
            else
            {
                panelClosed = true;
            }

            while (!panelClosed) yield return null;

            if (choseDonate && tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null)
            {
                var pd = playerInfoPanels[tokenIndex].Data;
                if (pd.cash >= 400)
                {
                    pd.cash -= 400;
                    pd.reputation = Mathf.Clamp(pd.reputation + 12, 0, 100);
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"{pd.playerName} 向公益中心捐款400元，获得12声誉");
                }
                else
                {
                    BroadcastMsg("现金不足，无法捐款");
                }
            }
        }

        // ==================== 事件/奖励卡牌系统 ====================

        /// <summary>
        /// 处理事件格：弹出事件卡牌，效果作用于所有玩家或特定玩家
        /// </summary>
        private IEnumerator HandleEventCard(int tileIndex, int tokenIndex)
        {
            Debug.Log($"[Event] Token{tokenIndex} on event tile {tileIndex}");

            bool panelClosed = false;
            PenaltyEventData selectedEvent = null;

            if (penaltyCardPanel != null)
            {
                penaltyCardPanel.Show(PenaltyCardPanel.CardType.Event,
                    (evt) => { selectedEvent = evt; },
                    () => { panelClosed = true; }
                );
            }
            else { panelClosed = true; }

            while (!panelClosed) yield return null;

            if (selectedEvent != null)
            {
                ApplyEventEffect(selectedEvent, tokenIndex);
                BroadcastMsg($"全局事件: {selectedEvent.eventName} — {selectedEvent.effectDescription}");
            }
        }

        /// <summary>
        /// 处理奖励格：弹出奖励卡牌，效果作用于当前玩家
        /// </summary>
        private IEnumerator HandleRewardCard(int tileIndex, int tokenIndex)
        {
            Debug.Log($"[Reward] Token{tokenIndex} on reward tile {tileIndex}");

            // AI自动处理奖励（不弹面板）
            if (tokenIndex != 0)
            {
                yield return new WaitForSeconds(0.5f);
                // AI也需要从奖池随机抽取并应用效果
                // 简化：AI直接获得现金或声望，不弹面板
                if (penaltyCardPanel != null && penaltyCardPanel.rewardPool != null && penaltyCardPanel.rewardPool.Count > 0)
                {
                    var evt = penaltyCardPanel.rewardPool[Random.Range(0, penaltyCardPanel.rewardPool.Count)];
                    ApplyRewardEffect(evt, tokenIndex);
                    BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 获得奖励: {evt.eventName} — {evt.effectDescription}");
                }
                yield break;
            }

            bool panelClosed = false;
            PenaltyEventData selectedEvent = null;

            if (penaltyCardPanel != null)
            {
                penaltyCardPanel.Show(PenaltyCardPanel.CardType.Reward,
                    (evt) => { selectedEvent = evt; },
                    () => { panelClosed = true; }
                );
            }
            else { panelClosed = true; }

            while (!panelClosed) yield return null;

            if (selectedEvent != null)
            {
                ApplyRewardEffect(selectedEvent, tokenIndex);
                BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 获得奖励: {selectedEvent.eventName} — {selectedEvent.effectDescription}");
            }
        }

        // ==================== 事件效果实现 ====================

        /// <summary>
        /// 应用事件效果（全局事件）
        /// </summary>
        private void ApplyEventEffect(PenaltyEventData eventData, int triggerTokenIndex)
        {
            string name = eventData.eventName;

            switch (eventData.effectType)
            {
                case PenaltyEffect.DowngradeProperty:
                    // 地震：随机选一行（某条边）的房产降一级
                    if (name == "地震")
                    {
                        // 随机选一条边
                        string[] edges = { "Tile_T", "Tile_R", "Tile_B", "Tile_L" };
                        string edge = edges[Random.Range(0, 4)];
                        int downgraded = 0;
                        var shakeTiles = new List<int>();
                        for (int i = 0; i < buildingData.Length; i++)
                        {
                            if (tilePath[i].name.StartsWith(edge) && buildingData[i].level > 0)
                            {
                                int oldOwner = buildingData[i].ownerIndex;
                                buildingData[i].level--;
                                // 更新玩家房产价值
                                if (oldOwner >= 0 && oldOwner < playerInfoPanels.Count && playerInfoPanels[oldOwner] != null)
                                {
                                    playerInfoPanels[oldOwner].Data.propertyValue -= 100;
                                    playerInfoPanels[oldOwner].UpdateDisplay();
                                    UpdateProsperity();
                                }
                                UpdateBuildingSprite(i);
                                StartCoroutine(BuildingDowngradeAnim(i));
                                shakeTiles.Add(i);
                                downgraded++;
                                Debug.Log($"[Earthquake] {tilePath[i].name} downgraded to level {buildingData[i].level}");
                            }
                        }
                        if (shakeTiles.Count > 0)
                            StartCoroutine(EarthquakeShakeAnim(shakeTiles));
                        BroadcastMsg($"地震：{edge}边的{downgraded}处房产降级");
                    }
                    break;

                case PenaltyEffect.CashLoss:
                    // 黑帮火拼：所有玩家现金-100
                    for (int i = 0; i < playerInfoPanels.Count; i++)
                        ApplyEffectToPlayer(i, eventData);
                    break;

                case PenaltyEffect.ReputationLoss:
                    // 丑闻：财富最高的玩家声望-10，相同则都扣
                    {
                        int maxWealth = int.MinValue;
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            if (playerInfoPanels[i].Data.Wealth > maxWealth)
                                maxWealth = playerInfoPanels[i].Data.Wealth;
                        }
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            if (playerInfoPanels[i].Data.Wealth == maxWealth)
                            {
                                playerInfoPanels[i].Data.reputation = Mathf.Clamp(playerInfoPanels[i].Data.reputation - 10, 0, 100);
                                playerInfoPanels[i].UpdateDisplay(); UpdateProsperity();
                            }
                        }
                    }
                    break;

                case PenaltyEffect.ReputationGain:
                    // 公益晚会：所有玩家声望+5
                    for (int i = 0; i < playerInfoPanels.Count; i++)
                        ApplyEffectToPlayer(i, eventData);
                    break;

                case PenaltyEffect.SkipTurn:
                    // 交通事故：所有4人中随机选一个（包括触发者），跳过下回合并播报
                    {
                        int randomPlayer = Random.Range(0, 4);
                        skipNextTurn[randomPlayer] = true;
                        BroadcastMsg($"{playerInfoPanels[randomPlayer].Data.playerName} 因交通事故下回合被跳过");
                    }
                    break;

                case PenaltyEffect.CashGainPercent:
                    // 发现金矿：所有玩家现金+5%
                    for (int i = 0; i < playerInfoPanels.Count; i++)
                        ApplyEffectToPlayer(i, eventData);
                    break;

                case PenaltyEffect.CashLossPercent:
                    // 邻城暴雷：所有玩家现金-5%
                    for (int i = 0; i < playerInfoPanels.Count; i++)
                        ApplyEffectToPlayer(i, eventData);
                    break;

                case PenaltyEffect.Custom:
                    // 台风：所有玩家回到起点，不获得工资
                    if (name == "台风")
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            tileIndices[i] = 0; // 起点index
                            Vector2 pos = GetTileAnchorPos(0);
                            if (tokens[i] != null) tokens[i].anchoredPosition = pos;
                        }
                        BroadcastMsg("台风：所有玩家被送回起点");
                    }
                    break;
            }
        }

        /// <summary>
        /// 应用奖励效果（当前玩家）
        /// </summary>
        private void ApplyRewardEffect(PenaltyEventData eventData, int tokenIndex)
        {
            string name = eventData.eventName;

            switch (eventData.effectType)
            {
                case PenaltyEffect.CashGain:
                    ApplyEffectToPlayer(tokenIndex, eventData);
                    break;

                case PenaltyEffect.ReputationGain:
                    // 结盟：当前玩家+随机玩家声望各+5
                    if (name == "结盟")
                    {
                        ApplyEffectToPlayer(tokenIndex, eventData);
                        int other = GetRandomOtherPlayer(tokenIndex);
                        if (other >= 0)
                        {
                            ApplyEffectToPlayer(other, eventData);
                            BroadcastMsg($"{playerInfoPanels[other].Data.playerName} 也获得声望+5");
                        }
                    }
                    else
                    {
                        ApplyEffectToPlayer(tokenIndex, eventData);
                    }
                    break;

                case PenaltyEffect.Custom:
                    if (name == "交通补贴")
                    {
                        // 传送到左边火车站（Tile_B0_Start，path index 27）
                        int leftStationIndex = -1;
                        for (int i = 0; i < tilePath.Count; i++)
                        {
                            if (tilePath[i].name == "Tile_B0_Start") { leftStationIndex = i; break; }
                        }
                        if (leftStationIndex >= 0)
                        {
                            tileIndices[tokenIndex] = leftStationIndex;
                            Vector2 pos = GetTileAnchorPos(leftStationIndex);
                            if (tokens[tokenIndex] != null) tokens[tokenIndex].anchoredPosition = pos;
                            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 被传送到火车站");
                        }
                    }
                    else if (name == "商战")
                    {
                        // 随机获得一个非自己的房产（包括空地）
                        AcquireRandomProperty(tokenIndex, false);
                    }
                    else if (name == "人脉")
                    {
                        // 随机选一个非自己的房产按当前价值购买
                        AcquireRandomProperty(tokenIndex, true);
                    }
                    else if (name == "装修")
                    {
                        // 随机选自己的房产免费升级，满级则失效
                        UpgradeRandomOwnProperty(tokenIndex);
                    }
                    else if (name == "道路修缮")
                    {
                        // 再投一次骰子：设置标记
                        rerollNextTurn[tokenIndex] = true;
                        BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 可以再投一次骰子");
                    }
                    else if (name == "伸出援手")
                    {
                        // 最富-200最穷+200，相同则都触发
                        int maxW = int.MinValue, minW = int.MaxValue;
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            int w = playerInfoPanels[i].Data.Wealth;
                            if (w > maxW) maxW = w;
                            if (w < minW) minW = w;
                        }
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            int w = playerInfoPanels[i].Data.Wealth;
                            if (w == maxW)
                            {
                                playerInfoPanels[i].Data.cash -= 200;
                                playerInfoPanels[i].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"{playerInfoPanels[i].Data.playerName} 是最富的，-200");
                            }
                            if (w == minW)
                            {
                                playerInfoPanels[i].Data.cash += 200;
                                playerInfoPanels[i].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"{playerInfoPanels[i].Data.playerName} 是最穷的，+200");
                            }
                        }
                    }
                    break;

                default:
                    ApplyEffectToPlayer(tokenIndex, eventData);
                    break;
            }
        }

        // ==================== 效果辅助方法 ====================

        /// <summary>获取随机其他玩家索引</summary>
        private int GetRandomOtherPlayer(int excludeIndex)
        {
            var candidates = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (i != excludeIndex && playerInfoPanels[i] != null)
                    candidates.Add(i);
            }
            if (candidates.Count == 0) return -1;
            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>随机卖一块自己的房产（退还价值一半）</summary>
        private void SellRandomProperty(int tokenIndex)
        {
            var owned = new List<int>();
            for (int i = 0; i < buildingData.Length; i++)
            {
                if (buildingData[i].ownerIndex == tokenIndex && buildingData[i].level > 0)
                    owned.Add(i);
            }
            if (owned.Count == 0)
            {
                BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 没有可卖的房产");
                return;
            }
            int target = owned[Random.Range(0, owned.Count)];
            int refund = buildingData[target].TotalValue / 2;
            playerInfoPanels[tokenIndex].Data.cash += refund;
            playerInfoPanels[tokenIndex].Data.propertyValue -= buildingData[target].TotalValue;
            buildingData[target].level = 0;
            buildingData[target].ownerIndex = -2; // 回归政府所有
            UpdateBuildingSprite(target);
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 卖掉一块房产，获得{refund}元");
        }

        /// <summary>随机选自己的房产降一级</summary>
        private void DowngradeRandomProperty(int tokenIndex)
        {
            var owned = new List<int>();
            for (int i = 0; i < buildingData.Length; i++)
            {
                if (buildingData[i].ownerIndex == tokenIndex && buildingData[i].level > 0)
                    owned.Add(i);
            }
            if (owned.Count == 0)
            {
                BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 没有可降级的房产");
                return;
            }
            int target = owned[Random.Range(0, owned.Count)];
            buildingData[target].level--;
            playerInfoPanels[tokenIndex].Data.propertyValue -= 100;
            UpdateBuildingSprite(target);
            StartCoroutine(BuildingDowngradeAnim(target));
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 的一处房产降级");
        }

        /// <summary>随机获得一个非自己的房产</summary>
        private void AcquireRandomProperty(int tokenIndex, bool payForIt)
        {
            var candidates = new List<int>();
            for (int i = 0; i < buildingData.Length; i++)
            {
                // 包括空地(ownerIndex==-1)和其他人的房产
                if (buildingData[i].ownerIndex != tokenIndex)
                    candidates.Add(i);
            }
            if (candidates.Count == 0)
            {
                BroadcastMsg("没有可获得的房产");
                return;
            }
            int target = candidates[Random.Range(0, candidates.Count)];

            // 如果是别人的房产，先归还原主
            if (buildingData[target].ownerIndex >= 0 && buildingData[target].ownerIndex != tokenIndex)
            {
                int oldOwner = buildingData[target].ownerIndex;
                playerInfoPanels[oldOwner].Data.propertyValue -= buildingData[target].TotalValue;
                playerInfoPanels[oldOwner].UpdateDisplay(); UpdateProsperity();
            }

            if (payForIt)
            {
                // 人脉：按当前价值购买
                int cost = buildingData[target].TotalValue;
                if (playerInfoPanels[tokenIndex].Data.cash < cost)
                {
                    BroadcastMsg("现金不足，无法购买");
                    return;
                }
                playerInfoPanels[tokenIndex].Data.cash -= cost;
            }

            buildingData[target].ownerIndex = tokenIndex;
            if (buildingData[target].level == 0) buildingData[target].level = 1;
            playerInfoPanels[tokenIndex].Data.propertyValue += buildingData[target].TotalValue;
            UpdateBuildingSprite(target);
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 获得了一处房产");
        }

        /// <summary>随机选自己的房产免费升级，满级则失效</summary>
        private void UpgradeRandomOwnProperty(int tokenIndex)
        {
            var owned = new List<int>();
            for (int i = 0; i < buildingData.Length; i++)
            {
                if (buildingData[i].ownerIndex == tokenIndex && buildingData[i].CanUpgrade)
                    owned.Add(i);
            }
            if (owned.Count == 0)
            {
                BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 没有可升级的房产");
                return;
            }
            int target = owned[Random.Range(0, owned.Count)];
            buildingData[target].level++;
            playerInfoPanels[tokenIndex].Data.propertyValue += 100;
            UpdateBuildingSprite(target);
            StartCoroutine(BuildingUpgradeAnim(target));
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 的房产免费升级到{buildingData[target].level}级");
        }

        /// <summary>
        /// 通用效果应用（惩罚/事件/奖励共用）
        /// </summary>
        private void ApplyEffectToPlayer(int tokenIndex, PenaltyEventData eventData)
        {
            if (tokenIndex >= playerInfoPanels.Count || playerInfoPanels[tokenIndex] == null) return;
            var data = playerInfoPanels[tokenIndex].Data;

            switch (eventData.effectType)
            {
                case PenaltyEffect.CashLoss:
                    data.cash += eventData.cashChange;
                    break;
                case PenaltyEffect.CashGain:
                    data.cash += eventData.cashChange;
                    break;
                case PenaltyEffect.ReputationLoss:
                    data.reputation = Mathf.Clamp(data.reputation + eventData.reputationChange, 0, 100);
                    break;
                case PenaltyEffect.ReputationGain:
                    data.reputation = Mathf.Clamp(data.reputation + eventData.reputationChange, 0, 100);
                    break;
                case PenaltyEffect.CashAndRepLoss:
                    data.cash += eventData.cashChange;
                    data.reputation = Mathf.Clamp(data.reputation + eventData.reputationChange, 0, 100);
                    break;
                case PenaltyEffect.CashGainPercent:
                    data.cash += Mathf.RoundToInt(data.cash * eventData.cashChange / 100f);
                    break;
                case PenaltyEffect.CashLossPercent:
                    data.cash -= Mathf.RoundToInt(data.cash * Mathf.Abs(eventData.cashChange) / 100f);
                    break;
                case PenaltyEffect.SkipTurn:
                    skipNextTurn[tokenIndex] = true;
                    break;
                case PenaltyEffect.SellProperty:
                case PenaltyEffect.DowngradeProperty:
                case PenaltyEffect.Custom:
                    Debug.Log($"[Effect] {eventData.effectType} not fully implemented for {data.playerName}");
                    break;
            }

            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            Debug.Log($"[Effect] {data.playerName}: {eventData.eventName} applied");
        }

        // ==================== 火车站系统 ====================

        /// <summary>
        /// 处理火车站：花费50元传送到另一个火车站。
        /// AI有50%概率使用。
        /// </summary>
        private IEnumerator HandleTrainStation(int tileIndex, int tokenIndex)
        {
            Debug.Log($"[Train] Token{tokenIndex} on train station tile {tileIndex}");

            // AI：50%概率使用火车
            if (tokenIndex != 0)
            {
                if (Random.Range(0f, 1f) > 0.5f)
                {
                    yield return TeleportToOtherStation(tileIndex, tokenIndex);
                }
                else
                {
                    Debug.Log($"[Train] AI{tokenIndex} chose not to ride");
                }
                yield break;
            }

            // 玩家：弹出选择面板（复用BuildingPanel的UI结构不太合适，用简单方式）
            // 检查资金
            if (tokenIndex >= playerInfoPanels.Count || playerInfoPanels[tokenIndex] == null) yield break;
            var pd = playerInfoPanels[tokenIndex].Data;

            if (pd.cash < 50)
            {
                BroadcastMsg("现金不足，无法乘坐火车");
                yield break;
            }

            // 使用buildingPanel显示火车选项
            bool panelClosed = false;
            bool choseRide = false;

            if (buildingPanel != null)
            {
                // 临时复用面板显示火车信息
                buildingPanel.ShowTrainStation(50,
                    () => { choseRide = true; },
                    () => { panelClosed = true; }
                );
            }
            else
            {
                panelClosed = true;
            }

            while (!panelClosed) yield return null;

            if (choseRide)
            {
                yield return TeleportToOtherStation(tileIndex, tokenIndex);
            }
        }

        /// <summary>
        /// 传送到另一个火车站（花费50元，不触发经过起点工资）
        /// </summary>
        private IEnumerator TeleportToOtherStation(int currentTileIndex, int tokenIndex)
        {
            if (tokenIndex >= playerInfoPanels.Count || playerInfoPanels[tokenIndex] == null) yield break;
            var pd = playerInfoPanels[tokenIndex].Data;

            if (pd.cash < 50)
            {
                Debug.Log($"[Train] Not enough cash");
                yield break;
            }

            // 找到另一个火车站的tile index
            // 火车站: Tile_T9_TrainTR (path index 9) 和 Tile_B0_Start (path index 27)
            // Tile_R9_Start (path index 0) is 起点, Tile_T0_Charity (path index 18) is 基金会
            // 火车站sprite名 = "火车块(1)"
            int targetIndex = -1;
            for (int i = 0; i < tilePath.Count; i++)
            {
                if (i == currentTileIndex) continue;
                var img = tilePath[i].GetComponent<Image>();
                if (img != null && img.sprite != null && img.sprite.name == "火车块(1)")
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex == -1)
            {
                Debug.Log("[Train] No other train station found");
                yield break;
            }

            // 扣费
            pd.cash -= 50;
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();

            // 播放火车音效（2秒）
            AudioManager.Instance?.PlayTrain();

            // 传送Token（不触发经过起点工资）
            tileIndices[tokenIndex] = targetIndex;
            Vector2 targetPos = GetTileAnchorPos(targetIndex);
            if (tokens[tokenIndex] != null)
                tokens[tokenIndex].anchoredPosition = targetPos;

            Debug.Log($"[Train] Token{tokenIndex} teleported to tile {targetIndex} for 50");
            BroadcastMsg($"{pd.playerName} 花费50元乘坐火车");

            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>
        /// 处理建筑格交互：购买/升级/支付租金
        /// </summary>
        private IEnumerator HandleBuilding(int tileIndex, int tokenIndex)
        {
            if (buildingData == null || tileIndex >= buildingData.Length) yield break;
            var data = buildingData[tileIndex];
            if (buildingPanel == null) yield break;

            Debug.Log($"[Building] Token{tokenIndex} on building tile {tileIndex}, level={data.level}, owner={data.ownerIndex}");

            // AI自动处理建筑（不弹面板）
            if (tokenIndex != 0)
            {
                yield return AIBuildingAction(tileIndex, tokenIndex, data);
                yield break;
            }

            bool panelClosed = false;

            if (data.IsGovernmentOwned)
            {
                // 政府所有：可购买
                int marketPrice = data.GetMarketPrice(currentProsperity);
                buildingPanel.ShowGovernmentOwned(data, marketPrice,
                    () => { // 购买回调
                        if (tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null)
                        {
                            var pd = playerInfoPanels[tokenIndex].Data;
                            if (pd.cash >= data.UpgradeCost)
                            {
                                pd.cash -= data.UpgradeCost;
                                pd.propertyValue += data.TotalValue;
                                data.level = 1;
                                data.ownerIndex = tokenIndex;
                                UpdateBuildingSprite(tileIndex);
                                playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"{pd.playerName} 购买了建筑");
                                AudioManager.Instance?.PlayBuildingUpgrade();
                            }
                        }
                    },
                    () => { panelClosed = true; }
                );
            }
            else if (data.ownerIndex == tokenIndex)
            {
                // 自己的：可升级
                int marketPrice = data.GetMarketPrice(currentProsperity);
                buildingPanel.ShowOwned(data, marketPrice, playerInfoPanels[tokenIndex]?.Data?.playerName ?? $"玩家{tokenIndex}",
                    () => { // 升级回调
                        if (tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null)
                        {
                            var pd = playerInfoPanels[tokenIndex].Data;
                            if (data.CanUpgrade && pd.cash >= data.UpgradeCost)
                            {
                                pd.cash -= data.UpgradeCost;
                                pd.propertyValue += 100;
                                data.level++;
                                UpdateBuildingSprite(tileIndex);
                                StartCoroutine(BuildingUpgradeAnim(tileIndex));
                                playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"{pd.playerName} 升级建筑到{data.level}级");
                                AudioManager.Instance?.PlayBuildingUpgrade();
                            }
                        }
                    },
                    () => { panelClosed = true; }
                );
            }
            else
            {
                // 他人的：支付租金或Bargain
                int marketPrice = data.GetMarketPrice(currentProsperity);
                int rent = marketPrice / 10;
                string ownerName = (data.ownerIndex < playerInfoPanels.Count && playerInfoPanels[data.ownerIndex] != null)
                    ? playerInfoPanels[data.ownerIndex].Data.playerName : $"玩家{data.ownerIndex}";

                buildingPanel.ShowRented(data, marketPrice, ownerName,
                    () => { // 支付租金回调
                        if (tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null &&
                            data.ownerIndex < playerInfoPanels.Count && playerInfoPanels[data.ownerIndex] != null)
                        {
                            var payerData = playerInfoPanels[tokenIndex].Data;
                            var ownerData = playerInfoPanels[data.ownerIndex].Data;
                            payerData.cash -= rent;
                            ownerData.cash += rent;
                            AudioManager.Instance?.PlayCoin();
                            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                            playerInfoPanels[data.ownerIndex].UpdateDisplay(); UpdateProsperity();
                            Debug.Log($"[Building] Token{tokenIndex} paid {rent} rent to Token{data.ownerIndex}");
                            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 向 {playerInfoPanels[data.ownerIndex].Data.playerName} 支付租金{rent}元");
                        }
                    },
                    () => { // Bargain回调
                        // 进入Bargain场景
                        StartBargain(tileIndex, tokenIndex, data);
                    },
                    () => { panelClosed = true; }
                );
            }

            while (!panelClosed) yield return null;
        }

        /// <summary>
        /// AI自动处理建筑格：资金足够时自动购买/升级，否则自动支付租金
        /// </summary>
        private IEnumerator AIBuildingAction(int tileIndex, int tokenIndex, BuildingData data)
        {
            yield return new WaitForSeconds(0.5f);

            if (tokenIndex >= playerInfoPanels.Count || playerInfoPanels[tokenIndex] == null) yield break;
            var pd = playerInfoPanels[tokenIndex].Data;

            if (data.IsGovernmentOwned)
            {
                // 政府所有：AI现金偏好满足且资金足够则自动购买
                if (pd.WillingToSpend && pd.cash >= data.UpgradeCost)
                {
                    pd.cash -= data.UpgradeCost;
                    pd.propertyValue += data.TotalValue;
                    data.level = 1;
                    data.ownerIndex = tokenIndex;
                    UpdateBuildingSprite(tileIndex);
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"{pd.playerName} 购买了建筑");
                    AudioManager.Instance?.PlayBuildingUpgrade();
                }
            }
            else if (data.ownerIndex == tokenIndex)
            {
                // 自己的：AI现金偏好满足且资金足够则自动升级
                if (pd.WillingToSpend && data.CanUpgrade && pd.cash >= data.UpgradeCost)
                {
                    pd.cash -= data.UpgradeCost;
                    pd.propertyValue += 100;
                    data.level++;
                    UpdateBuildingSprite(tileIndex);
                    StartCoroutine(BuildingUpgradeAnim(tileIndex));
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"{pd.playerName} 升级建筑到{data.level}级");
                    AudioManager.Instance?.PlayBuildingUpgrade();
                }
            }
            else
            {
                // 他人的：现金占比 >= 现金偏好时选择Bargain（高付费），否则支付租金（低付费）
                if (pd.WillingToSpend && data.ownerIndex >= 0 && data.ownerIndex < playerInfoPanels.Count)
                {
                    Debug.Log($"[AI] {pd.playerName} cashRatio={pd.CashRatio:F2} >= pref={pd.cashPreference:F2}, chose Bargain");
                    StartBargain(tileIndex, tokenIndex, data);
                    yield break;
                }

                // 现金不足，选择支付租金（低付费选项）
                Debug.Log($"[AI] {pd.playerName} cashRatio={pd.CashRatio:F2} < pref={pd.cashPreference:F2}, chose pay rent");
                if (data.ownerIndex < playerInfoPanels.Count && playerInfoPanels[data.ownerIndex] != null)
                {
                    var ownerData = playerInfoPanels[data.ownerIndex].Data;
                    int rent = data.GetMarketPrice(currentProsperity) / 10;
                    pd.cash -= rent;
                    ownerData.cash += rent;
                    AudioManager.Instance?.PlayCoin();
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    playerInfoPanels[data.ownerIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"{pd.playerName} 向 {ownerData.playerName} 支付租金{rent}元");
                }
            }

            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>
        /// 更新建筑格上的建筑sprite（根据等级）
        /// </summary>
        private void UpdateBuildingSprite(int tileIndex)
        {
            if (buildingData == null || tileIndex >= buildingData.Length) return;
            var tile = tilePath[tileIndex];
            if (tile == null) return;

            var building = tile.Find("Building");
            if (building == null) return;

            var img = building.GetComponent<Image>();
            int level = buildingData[tileIndex].level;

            // 0级=0级建筑, 1级=1级房屋, 2级=2级建筑, 3级=3级建筑
            if (buildingSprites != null && level < buildingSprites.Count && buildingSprites[level] != null)
            {
                img.sprite = buildingSprites[level];
            }
        }

        // ==================== 建筑/格子动画 ====================

        /// <summary>
        /// 建筑升级动画：放大弹跳
        /// </summary>
        private IEnumerator BuildingUpgradeAnim(int tileIndex)
        {
            var tile = tilePath[tileIndex];
            if (tile == null) yield break;
            var building = tile.Find("Building");
            if (building == null) yield break;

            var rt = building as RectTransform;
            Vector3 originalScale = rt.localScale;
            float duration = 0.4f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // 先放大到1.5倍，再缩回1倍
                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.5f;
                rt.localScale = originalScale * scale;
                yield return null;
            }
            rt.localScale = originalScale;
        }

        /// <summary>
        /// 建筑降级动画：缩小抖动
        /// </summary>
        private IEnumerator BuildingDowngradeAnim(int tileIndex)
        {
            var tile = tilePath[tileIndex];
            if (tile == null) yield break;
            var building = tile.Find("Building");
            if (building == null) yield break;

            var rt = building as RectTransform;
            Vector3 originalScale = rt.localScale;
            Vector2 originalPos = rt.anchoredPosition;
            float duration = 0.4f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // 缩小到0.7倍再恢复
                float scale = 1f - Mathf.Sin(t * Mathf.PI) * 0.3f;
                rt.localScale = originalScale * scale;
                // 左右抖动
                float shake = Mathf.Sin(t * Mathf.PI * 6) * 4f;
                rt.anchoredPosition = originalPos + new Vector2(shake, 0);
                yield return null;
            }
            rt.localScale = originalScale;
            rt.anchoredPosition = originalPos;
        }

        /// <summary>
        /// 地震整排格子左右晃动动画
        /// </summary>
        private IEnumerator EarthquakeShakeAnim(System.Collections.Generic.List<int> tileIndices)
        {
            var originals = new System.Collections.Generic.List<(RectTransform rt, Vector2 pos)>();
            foreach (int idx in tileIndices)
            {
                var tile = tilePath[idx];
                if (tile != null)
                    originals.Add((tile, tile.anchoredPosition));
            }

            float duration = 0.8f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // 衰减的左右晃动
                float decay = 1f - t;
                float shake = Mathf.Sin(t * Mathf.PI * 10f) * 10f * decay;
                foreach (var (rt, pos) in originals)
                {
                    rt.anchoredPosition = pos + new Vector2(shake, 0);
                }
                yield return null;
            }

            // 恢复
            foreach (var (rt, pos) in originals)
            {
                rt.anchoredPosition = pos;
            }
        }

        // ==================== 格子弹跳 ====================

        private IEnumerator TileBounce(RectTransform tile)
        {
            if (tile == null) yield break;

            // 记录所有子物体的初始位置，建筑不跟着跳
            Vector2 tileOriginalPos = tile.anchoredPosition;
            var children = new System.Collections.Generic.List<(Transform child, Vector2 pos)>();
            for (int i = 0; i < tile.childCount; i++)
            {
                var child = tile.GetChild(i);
                children.Add((child, child.GetComponent<RectTransform>().anchoredPosition));
            }

            float bounceDuration = 0.15f;
            float bounceHeight = 6f;
            float elapsed = 0f;

            while (elapsed < bounceDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / bounceDuration;
                // 向下弹跳：负方向
                float bounce = -Mathf.Sin(t * Mathf.PI) * bounceHeight;
                tile.anchoredPosition = tileOriginalPos + new Vector2(0, bounce);

                // 子物体反向偏移，保持不动
                foreach (var (child, pos) in children)
                {
                    child.GetComponent<RectTransform>().anchoredPosition = pos - new Vector2(0, bounce);
                }

                yield return null;
            }

            tile.anchoredPosition = tileOriginalPos;
            // 恢复子物体位置
            foreach (var (child, pos) in children)
            {
                child.GetComponent<RectTransform>().anchoredPosition = pos;
            }
        }

        /// <summary>
        /// 获取指定路径格子上AnchorPoint的世界坐标（相对Board）。
        /// AnchorPoint位于格子表面，确保Token不会到格子下面。
        /// </summary>
        private Vector2 GetTileAnchorPos(int pathIndex)
        {
            if (pathIndex < 0 || pathIndex >= tilePath.Count) return Vector2.zero;
            var tile = tilePath[pathIndex];
            if (tile == null) return Vector2.zero;

            // 查找AnchorPoint子物体
            var anchor = tile.Find("AnchorPoint");
            if (anchor != null)
            {
                return tile.anchoredPosition + ((RectTransform)anchor).anchoredPosition;
            }

            // 如果没有AnchorPoint，使用格子位置 + 偏移
            return tile.anchoredPosition + new Vector2(0, 15);
        }

        /// <summary>
        /// 获取当前回合（0=玩家, 1-3=AI）
        /// </summary>
        public int CurrentTurn => currentTurn;
        public int CurrentProsperity => currentProsperity;
        public BuildingData GetBuildingData(int index)
        {
            if (buildingData == null || index < 0 || index >= buildingData.Length) return null;
            return buildingData[index];
        }

        /// <summary>播报消息</summary>
        private void BroadcastMsg(string message)
        {
            if (broadcastBar != null)
                broadcastBar.Broadcast(message);
        }

        // ==================== 繁荣值系统 ====================

        /// <summary>
        /// 计算并更新繁荣值 = 最穷玩家财富值 / 40
        /// 每次任何玩家数值变化后调用
        /// </summary>
        private void UpdateProsperity()
        {
            int minWealth = int.MaxValue;
            for (int i = 0; i < playerInfoPanels.Count; i++)
            {
                if (playerInfoPanels[i] == null) continue;
                int w = playerInfoPanels[i].Data.Wealth;
                if (w < minWealth) minWealth = w;
            }

            currentProsperity = Mathf.RoundToInt((float)minWealth / 40f);

            if (prosperityText != null)
                prosperityText.text = $"繁荣指数: {currentProsperity}";

            // 繁荣值更新后，统一刷新所有建筑的市场价格
            if (buildingData != null)
            {
                for (int i = 0; i < buildingData.Length; i++)
                {
                    if (buildingData[i] != null)
                        buildingData[i].SetMarketPrice(currentProsperity);
                }
            }

            Debug.Log($"[Prosperity] Min wealth={minWealth}, prosperity={currentProsperity}");
        }

        /// <summary>
        /// 检查游戏是否因繁荣值结束
        /// </summary>
        private bool CheckGameEnd()
        {
            if (currentProsperity <= 20)
            {
                EndGame("繁荣值过低，所有玩家失败！", false);
                return true;
            }
            if (currentProsperity >= 100)
            {
                EndGame("繁荣值达到100，所有玩家大获全胜！", true);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 15回合后游戏结束，根据繁荣值判断结果
        /// </summary>
        private void EndGameByRounds()
        {
            string result;
            bool victory;
            if (currentProsperity <= 20)
            {
                result = "繁荣值过低，所有玩家失败！";
                victory = false;
            }
            else if (currentProsperity >= 100)
            {
                result = "繁荣值达到100，大获全胜！";
                victory = true;
            }
            else
            {
                result = "繁荣值马马虎虎，游戏结束。";
                victory = false;
            }
            EndGame(result, victory);
        }

        /// <summary>
        /// 游戏结束，显示结果面板
        /// </summary>
        private void EndGame(string message, bool victory)
        {
            gameEnded = true;
            BroadcastMsg(message);

            // 播放胜利或失败音效
            if (victory)
                AudioManager.Instance?.PlayVictory();
            else
                AudioManager.Instance?.PlayRandomDefeat();

            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(true);
                if (gameOverText != null)
                    gameOverText.text = message;
            }

            if (rollDiceButton != null)
                rollDiceButton.interactable = false;

            Debug.Log($"[GameEnd] {message}");
        }

        /// <summary>
        /// 回到首页
        /// </summary>
        // ==================== Bargain 系统 ====================

        // ==================== 游戏状态保存/恢复 ====================

        /// <summary>
        /// 跨场景游戏状态（Bargain切换场景时保存，切回时恢复）
        /// </summary>
        public static class SavedGameState
        {
            public static bool hasSavedState = false;
            public static int currentTurn;
            public static int currentRound;
            public static int currentProsperity;
            public static int[] tileIndices = new int[4];
            public static int[] playerCash = new int[4];
            public static int[] playerProperty = new int[4];
            public static int[] playerRep = new int[4];
            public static int[] playerSelfInterest = new int[4];
            public static float[] playerCashPref = new float[4];
            public static string[] playerNames = new string[4];
            public static bool[] skipNextTurn = new bool[4];
            public static int borderBlockTurns;
            public static bool gameEnded;
            // 建筑数据
            public static int[] buildingLevel;
            public static int[] buildingOwner;
            public static int[] buildingBaseValue;
            // Bargain回调
            public static bool bargainResultCompleted;
            public static bool bargainResultSuccess;
            public static int bargainResultPrice;
            public static int bargainTileIndex;
            public static int bargainBuyerIndex;
            public static int bargainSellerIndex;
        }

        /// <summary>保存游戏状态到静态变量</summary>
        private void SaveGameState(int bargainTileIdx, int buyerIdx, int sellerIdx)
        {
            SavedGameState.hasSavedState = true;
            SavedGameState.currentTurn = currentTurn;
            SavedGameState.currentRound = currentRound;
            SavedGameState.currentProsperity = currentProsperity;
            SavedGameState.borderBlockTurns = 0;
            SavedGameState.gameEnded = gameEnded;

            for (int i = 0; i < 4; i++)
            {
                SavedGameState.tileIndices[i] = tileIndices[i];
                SavedGameState.skipNextTurn[i] = skipNextTurn[i];
                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    var pd = playerInfoPanels[i].Data;
                    SavedGameState.playerCash[i] = pd.cash;
                    SavedGameState.playerProperty[i] = pd.propertyValue;
                    SavedGameState.playerRep[i] = pd.reputation;
                    SavedGameState.playerSelfInterest[i] = pd.selfInterest;
                    SavedGameState.playerCashPref[i] = pd.cashPreference;
                    SavedGameState.playerNames[i] = pd.playerName;
                }
            }

            // 保存建筑数据
            if (buildingData != null)
            {
                SavedGameState.buildingLevel = new int[buildingData.Length];
                SavedGameState.buildingOwner = new int[buildingData.Length];
                SavedGameState.buildingBaseValue = new int[buildingData.Length];
                for (int i = 0; i < buildingData.Length; i++)
                {
                    SavedGameState.buildingLevel[i] = buildingData[i].level;
                    SavedGameState.buildingOwner[i] = buildingData[i].ownerIndex;
                    SavedGameState.buildingBaseValue[i] = buildingData[i].baseValue;
                }
            }

            SavedGameState.bargainTileIndex = bargainTileIdx;
            SavedGameState.bargainBuyerIndex = buyerIdx;
            SavedGameState.bargainSellerIndex = sellerIdx;
            SavedGameState.bargainResultCompleted = false;

            Debug.Log("[GameState] Saved");
        }

        /// <summary>从静态变量恢复游戏状态</summary>
        private bool RestoreGameState()
        {
            if (!SavedGameState.hasSavedState) return false;

            currentTurn = SavedGameState.currentTurn;
            currentRound = SavedGameState.currentRound;
            currentProsperity = SavedGameState.currentProsperity;
            // borderBlockTurns not used (边境封锁 deleted)
            gameEnded = SavedGameState.gameEnded;

            for (int i = 0; i < 4; i++)
            {
                tileIndices[i] = SavedGameState.tileIndices[i];
                skipNextTurn[i] = SavedGameState.skipNextTurn[i];

                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    var pd = playerInfoPanels[i].Data;
                    pd.cash = SavedGameState.playerCash[i];
                    pd.propertyValue = SavedGameState.playerProperty[i];
                    pd.reputation = SavedGameState.playerRep[i];
                    pd.selfInterest = SavedGameState.playerSelfInterest[i];
                    pd.cashPreference = SavedGameState.playerCashPref[i];
                    pd.playerName = SavedGameState.playerNames[i];
                    playerInfoPanels[i].UpdateDisplay();
                }

                // 恢复Token位置
                if (i < tokens.Count && tokens[i] != null && tilePath.Count > 0)
                {
                    tokens[i].anchoredPosition = GetTileAnchorPos(tileIndices[i]);
                }
            }

            // 恢复建筑数据
            if (buildingData != null && SavedGameState.buildingLevel != null)
            {
                for (int i = 0; i < buildingData.Length && i < SavedGameState.buildingLevel.Length; i++)
                {
                    buildingData[i].level = SavedGameState.buildingLevel[i];
                    buildingData[i].ownerIndex = SavedGameState.buildingOwner[i];
                    buildingData[i].baseValue = SavedGameState.buildingBaseValue[i];
                    UpdateBuildingSprite(i);
                }
            }

            UpdateProsperity();

            // 处理Bargain结果
            if (BargainController.BargainResult.completed)
            {
                bool success = BargainController.BargainResult.isSuccess;
                int price = BargainController.BargainResult.finalPrice;
                BargainController.BargainResult.completed = false;

                int tileIdx = SavedGameState.bargainTileIndex;
                int buyerIdx = SavedGameState.bargainBuyerIndex;
                int sellerIdx = SavedGameState.bargainSellerIndex;

                if (success)
                {
                    // 交易成功：房产转移
                    if (tileIdx >= 0 && tileIdx < buildingData.Length)
                    {
                        var bd = buildingData[tileIdx];
                        var buyerData = playerInfoPanels[buyerIdx]?.Data;
                        var sellerData = sellerIdx >= 0 ? playerInfoPanels[sellerIdx]?.Data : null;

                        if (buyerData != null && buyerData.cash >= price)
                        {
                            buyerData.cash -= price;
                            buyerData.propertyValue += bd.TotalValue;
                            if (sellerData != null)
                            {
                                sellerData.cash += price;
                                sellerData.propertyValue -= bd.TotalValue;
                                playerInfoPanels[sellerIdx].UpdateDisplay();
                            }
                            bd.ownerIndex = buyerIdx;
                            UpdateBuildingSprite(tileIdx);
                            playerInfoPanels[buyerIdx].UpdateDisplay();
                            UpdateProsperity();
                            BroadcastMsg($"Bargain成功！{buyerData.playerName} 以{price}元购得房产");
                        }
                    }
                }
                else
                {
                    // 交易失败：支付租金
                    if (tileIdx >= 0 && tileIdx < buildingData.Length)
                    {
                        var bd = buildingData[tileIdx];
                        int rent = bd.GetMarketPrice(currentProsperity) / 10;
                        var buyerData = playerInfoPanels[buyerIdx]?.Data;
                        var sellerData = sellerIdx >= 0 ? playerInfoPanels[sellerIdx]?.Data : null;

                        if (buyerData != null)
                        {
                            buyerData.cash -= rent;
                            if (sellerData != null)
                            {
                                sellerData.cash += rent;
                                playerInfoPanels[sellerIdx].UpdateDisplay();
                            }
                            playerInfoPanels[buyerIdx].UpdateDisplay();
                            UpdateProsperity();
                            BroadcastMsg($"Bargain失败，{buyerData.playerName} 支付租金{rent}元");
                        }
                    }
                }

                // 继续下一回合
                BargainData.bargainActive = false;
                StartCoroutine(NextTurnAfterDelay());
            }
            else
            {
                // 没有Bargain结果，继续当前回合
                if (currentTurn == 0 && !gameEnded)
                {
                    StartPlayerTurn();
                }
            }

            SavedGameState.hasSavedState = false;
            Debug.Log("[GameState] Restored");
            return true;
        }

        private IEnumerator NextTurnAfterDelay()
        {
            yield return new WaitForSeconds(turnDelay);
            yield return NextTurn();
        }

        // 跨场景传递的Bargain数据
        public static class BargainData
        {
            public static int marketPrice;
            public static int sellerIndex;
            public static int buyerIndex;
            public static int sellerRep;
            public static int buyerRep;
            public static int sellerSelfInterest;
            public static int buyerSelfInterest;
            public static int tileIndex;
            public static bool playerIsBuyer;
            public static bool bargainActive;
            public static string sellerName;
            public static string buyerName;
        }

        /// <summary>
        /// 启动Bargain：保存数据并跳转到BargainScene
        /// </summary>
        private void StartBargain(int tileIndex, int tokenIndex, BuildingData data)
        {
            int marketPrice = data.GetMarketPrice(currentProsperity);
            int ownerIndex = data.ownerIndex;

            BargainData.marketPrice = marketPrice;
            BargainData.sellerIndex = ownerIndex;
            BargainData.buyerIndex = tokenIndex;
            BargainData.sellerRep = playerInfoPanels[ownerIndex]?.Data?.reputation ?? 50;
            BargainData.buyerRep = playerInfoPanels[tokenIndex]?.Data?.reputation ?? 50;
            BargainData.sellerSelfInterest = playerInfoPanels[ownerIndex]?.Data?.selfInterest ?? 0;
            BargainData.buyerSelfInterest = playerInfoPanels[tokenIndex]?.Data?.selfInterest ?? 0;
            BargainData.tileIndex = tileIndex;
            BargainData.playerIsBuyer = (tokenIndex == 0);
            BargainData.bargainActive = true;
            BargainData.sellerName = playerInfoPanels[ownerIndex]?.Data?.playerName ?? $"玩家{ownerIndex}";
            BargainData.buyerName = playerInfoPanels[tokenIndex]?.Data?.playerName ?? $"玩家{tokenIndex}";

            BroadcastMsg("进入Bargain...");
            AudioManager.Instance?.PlayRandomBargainBGM();

            // 保存游戏状态
            SaveGameState(tileIndex, tokenIndex, ownerIndex);

            // 完全切换场景（非叠加）
            UnityEngine.SceneManagement.SceneManager.LoadScene("讨价还价_选卡");
        }

        public void BackToMenu()
        {
            AudioManager.Instance?.PlayMainMenuBGM();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        /// <summary>是否正在忙（掷骰子、等待选择、移动中、Bargain中），外部用于禁用拖拽</summary>
        public bool IsBusy => isBusy || waitingForChoice || isZooming || BargainData.bargainActive;

        private void Update()
        {
            // 检测Bargain是否完成
            if (BargainData.bargainActive && SheNicest.UI.BargainController.BargainResult.completed)
            {
                BargainData.bargainActive = false;
                bool success = SheNicest.UI.BargainController.BargainResult.isSuccess;
                int finalPrice = SheNicest.UI.BargainController.BargainResult.finalPrice;
                SheNicest.UI.BargainController.BargainResult.completed = false;

                Debug.Log($"[Bargain] Result: success={success}, price={finalPrice}");
                StartCoroutine(HandleBargainResult(success, finalPrice));
            }
        }

        /// <summary>
        /// Bargain结束后处理结果
        /// </summary>
        private IEnumerator HandleBargainResult(bool success, int finalPrice)
        {
            // 恢复游戏场景BGM
            AudioManager.Instance?.PlayGameSceneBGM();

            int tileIndex = BargainData.tileIndex;
            int buyerIndex = BargainData.buyerIndex;
            int sellerIndex = BargainData.sellerIndex;

            if (success)
            {
                // 交易成功：房产转移给买方，买方支付成交价
                if (tileIndex >= 0 && tileIndex < buildingData.Length)
                {
                    var data = buildingData[tileIndex];
                    var buyerData = playerInfoPanels[buyerIndex]?.Data;
                    var sellerData = sellerIndex >= 0 ? playerInfoPanels[sellerIndex]?.Data : null;

                    if (buyerData != null && buyerData.cash >= finalPrice)
                    {
                        buyerData.cash -= finalPrice;
                        buyerData.propertyValue += data.TotalValue;

                        if (sellerData != null)
                        {
                            sellerData.cash += finalPrice;
                            sellerData.propertyValue -= data.TotalValue;
                            AudioManager.Instance?.PlayCoin();
                            playerInfoPanels[sellerIndex].UpdateDisplay(); UpdateProsperity();
                        }

                        data.ownerIndex = buyerIndex;
                        UpdateBuildingSprite(tileIndex);
                        playerInfoPanels[buyerIndex].UpdateDisplay(); UpdateProsperity();
                        BroadcastMsg($"Bargain成功！{buyerData.playerName} 以{finalPrice}元购得房产");
                    }
                }
            }
            else
            {
                // 交易失败：支付租金
                if (tileIndex >= 0 && tileIndex < buildingData.Length)
                {
                    var data = buildingData[tileIndex];
                    int rent = data.GetMarketPrice(currentProsperity) / 10;
                    var buyerData = playerInfoPanels[buyerIndex]?.Data;
                    var sellerData = sellerIndex >= 0 ? playerInfoPanels[sellerIndex]?.Data : null;

                    if (buyerData != null)
                    {
                        buyerData.cash -= rent;
                        if (sellerData != null)
                        {
                            sellerData.cash += rent;
                            AudioManager.Instance?.PlayCoin();
                            playerInfoPanels[sellerIndex].UpdateDisplay(); UpdateProsperity();
                        }
                        playerInfoPanels[buyerIndex].UpdateDisplay(); UpdateProsperity();
                        BroadcastMsg($"Bargain失败，{buyerData.playerName} 支付租金{rent}元");
                    }
                }
            }

            yield return new WaitForSeconds(turnDelay);
            yield return NextTurn();
        }

        private bool isZooming = false;

        // 跳过下回合标记（惩罚效果"堵车"使用）
        private bool[] skipNextTurn = new bool[4] { false, false, false, false };

        // 繁荣值系统
        private int currentRound = 1;
        private const int MaxRounds = 15;
        private int currentProsperity = 50; // 初始值
        [SerializeField] private Text prosperityText; // 右下角繁荣值显示
        [SerializeField] private GameObject gameOverPanel; // 游戏结束面板
        [SerializeField] private Text gameOverText; // 游戏结果文字
        [SerializeField] private Button backToMenuButton; // 回首页按钮
        private bool gameEnded = false;

        // 道路修缮：标记每个玩家是否可以再投一次
        private bool[] rerollNextTurn = new bool[4] { false, false, false, false };
    }
}
