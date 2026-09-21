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

        [Header("Turn Indicator")]
        [SerializeField] private TurnIndicator turnIndicator; // 回合提示系统（HUD/横幅/骰子脉动，v4移植）

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
        private bool testMode = false;
        private bool testPlayerDone = false;
        // ===== 测试模式结束 =====

        // ===== 测试模式：Bargain全流程验证 =====
        // 勾选后：开局直接进入Bargain（AI1买玩家房产 → 玩家买AI1房产 → 结束游戏）。取消勾选后恢复原流程。
        [SerializeField] private bool testBargainMode = false;
        // ===== 测试模式：Bargain全流程验证结束 =====
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

            if (gameOverBackButton != null)
                gameOverBackButton.onClick.AddListener(BackToMenu);

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

            // 初始化AI属性 + 性格槽洗牌分配（Bot v2：性格与角色解绑，每局随机，不重复）
            var personaPool = new List<PersonaSlot>(PersonaSlot.Table);
            for (int i = 1; i < 4; i++)
            {
                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    playerInfoPanels[i].Data.InitAIAttributes();
                    if (personaPool.Count > 0)
                    {
                        int pick = Random.Range(0, personaPool.Count);
                        playerInfoPanels[i].Data.ApplyPersona(personaPool[pick]);
                        personaPool.RemoveAt(pick);
                    }
                    Debug.Log($"[Persona] AI{i} {playerInfoPanels[i].Data.playerName} = {playerInfoPanels[i].Data.personaName}");
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

            // 存档读取（主菜单「继续游戏」进入），优先于Bargain临时态恢复
            if (SaveLoadManager.pendingLoadSlot != SaveLoadManager.NoSlot)
            {
                int slot = SaveLoadManager.pendingLoadSlot;
                SaveLoadManager.pendingLoadSlot = SaveLoadManager.NoSlot; // 消费即清，防跨局残留
                var saveData = SaveLoadManager.LoadFromSlot(slot);
                if (saveData != null)
                {
                    // 与新游戏路径一致：清除测试模式残留，防影响正常对局
                    TestBargainState.active = false;
                    TestBargainState.phase = 0;
                    if (dicePanel != null)
                        dicePanel.SetActive(false);
                    RestoreFromSave(saveData);
                    return;
                }
                Debug.LogWarning($"[Save] 存档槽{slot}读取失败，回落到新游戏");
            }

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

            // 初始化回合提示系统（v4移植）
            if (turnIndicator != null)
            {
                var canvas = GetComponentInParent<Canvas>();
                if (canvas == null) canvas = FindObjectOfType<Canvas>();
                turnIndicator.Initialize(canvas);
                turnIndicator.CreateRoundHud(canvas, MaxRounds);
            }

            // ===== 测试模式：开局直接进入Bargain流程 =====
            if (testBargainMode)
            {
                if (SetupTestBargain())
                {
                    TestBargainState.active = true;
                    TestBargainState.phase = 1;
                    if (rollDiceButton != null)
                        rollDiceButton.interactable = false; // 测试期间禁用掷骰，防止回合切换窗口误触
                    if (hintText != null)
                        hintText.text = "[测试] 直接进入Bargain：AI购买玩家房产";
                    BroadcastMsg("[测试] 直接进入Bargain：AI购买玩家房产");
                    var data = buildingData[TestBargainState.playerTileIndex];
                    StartBargain(TestBargainState.playerTileIndex, 1, data); // AI1(罗斯韦尔)作为买方
                    return;
                }
                Debug.LogWarning("[TestMode] 未找到足够的建筑格，测试模式未启动，回落到正常流程");
            }
            else
            {
                // 清除上次测试会话残留的静态状态，防止影响正常对局
                TestBargainState.active = false;
                TestBargainState.phase = 0;
            }
            // ===== 测试模式结束 =====

            // 第1回合开始播报
            BroadcastMsg("第1回合开始");

            // 开局性格暗示播报（Bot v2：不点名，让玩家局内猜谜）
            {
                var hints = new List<string>();
                bool hasRescuer = false, hasVampire = false, hasHoarder = false;
                for (int i = 1; i < 4; i++)
                {
                    if (i >= playerInfoPanels.Count || playerInfoPanels[i] == null) continue;
                    var pn = playerInfoPanels[i].Data.personaName;
                    if (pn == "慈善家") hasRescuer = true;
                    else if (pn == "吸血鬼") hasVampire = true;
                    else if (pn == "囤地豪客") hasHoarder = true;
                }
                if (hasRescuer) hints.Add("有人心怀慈悲，见不得旁人受苦");
                if (hasVampire) hints.Add("有人嗜财如命，谈判桌上寸步不让");
                if (hasHoarder) hints.Add("有人囤地成瘾，见到空地就挪不动脚");
                if (hints.Count > 0)
                    BroadcastMsg($"【传闻】本局暗流涌动——{string.Join("；", hints)}……是谁呢？", BroadcastBar.P1, 4f);
            }

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
            if (gameOverBackButton != null)
                gameOverBackButton.onClick.RemoveListener(BackToMenu);
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

            // 回合横幅 + 骰子脉动（v4移植）
            if (turnIndicator != null)
            {
                turnIndicator.ShowBanner("你的回合 — 掷骰子！", TurnIndicator.PlayerColor, 1f);
                turnIndicator.StartPulse(rollDiceButton);
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
            turnIndicator?.StopPulse();

            // AI回合横幅提示（v4移植）
            var aiData = playerInfoPanels[aiIndex]?.Data;
            if (aiData != null && turnIndicator != null)
                turnIndicator.HighlightAITurn(aiData.playerName);

            // 慈善家救援行为（Bot v2：繁荣意识≥0.9的AI，回合开始时检查最穷玩家）
            TryRescuePoorest(aiIndex);

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

                int chosenSteps = GetDiceSteps();

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
                // 边境封锁倒计时（每整回合-1）
                if (borderClosedRounds > 0)
                {
                    borderClosedRounds--;
                    if (borderClosedRounds == 0)
                        BroadcastMsg("【事件】边境重新开放，双骰恢复！", BroadcastBar.P1);
                }

                // v2.2 M套件：回合末经济收发（危机维持费/通胀吞噬/冲刺红利）
                ApplyEconomyTick();

                // v2.2 状态面板：短板行（回合末刷新，防逐笔闪烁）
                RefreshBottleneckText();

                // 计算繁荣值
                UpdateProsperity();

                // 检查游戏结束条件
                if (CheckGameEnd()) yield break;

                // 回合数+1
                currentRound++;
                if (currentRound > MaxRounds)
                {
                    // 回合上限结束
                    EndGameByRounds();
                    yield break;
                }

                // 新回合播报 + HUD更新（v4移植）
                BroadcastMsg($"第{currentRound}回合开始");
                turnIndicator?.UpdateRoundHud(currentRound, MaxRounds, currentProsperity, (int)currentPhase);
            }

            // 破产出局者跳过回合（v2.1现金破产制；出局即全灭，此为保险）
            if (currentTurn < playerInfoPanels.Count && playerInfoPanels[currentTurn] != null
                && !playerInfoPanels[currentTurn].Data.alive)
            {
                StartCoroutine(SkipAndNextTurn());
                yield break;
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
            if (isBusy || waitingForChoice || BargainData.bargainActive) return;
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

            int steps = GetDiceSteps();

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

        /// <summary>本次掷骰的步数：边境封锁期只取单骰，否则双骰之和（事件"边境封锁"，v3移植）</summary>
        private int GetDiceSteps()
        {
            if (borderClosedRounds > 0)
                return dice1Result;
            return dice1Result + dice2Result;
        }

        /// <summary>
        /// 玩家确认骰子结果（两骰子之和为步数）
        /// </summary>
        private void ChooseDice(int which)
        {
            if (!waitingForChoice) return;
            waitingForChoice = false;

            int steps = GetDiceSteps();
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

                // 经过起点时获得工资（危机/常规300元，冲刺期325元；坐火车不触发）——数值文档v2.1
                if (prevIndex != 0 && tileIndices[tokenIndex] == 0)
                {
                    if (tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null)
                    {
                        var pd = playerInfoPanels[tokenIndex].Data;
                        int salary = GetPhaseSalary();
                        pd.cash += salary;
                        playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                        AudioManager.Instance?.PlayCoin();
                        Debug.Log($"[Salary] Token{tokenIndex} passed start, +{salary} cash → {pd.cash}");
                        BroadcastMsg($"{pd.playerName} 经过起点，获得工资{salary}元");
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
                    // 现金惩罚直接作用于本人（"道德败坏"已按事件定稿删除：惩罚最穷者与济贫引擎对冲，v3移植）
                    ApplyEffectToPlayer(tokenIndex, eventData);
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
                buildingPanel.ShowCharity(400, 12,
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

            // 区间引擎：事件格触发率（危机/常规40%，冲刺区55%更密集——数值文档v2.1 §6.2，v3移植）
            float triggerRate = currentPhase == GamePhase.Sprint ? 0.55f : 0.40f;
            if (Random.value > triggerRate)
            {
                BroadcastMsg("风平浪静，无事发生");
                yield break;
            }

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
                    // 地震：随机3处有人房产降一级（事件定稿：原"整边降级"在新经济下一次蒸发数千财富，限3块可控，v3移植）
                    if (name == "地震")
                    {
                        var candidates = new List<int>();
                        for (int i = 0; i < buildingData.Length; i++)
                        {
                            if (buildingData[i] != null && buildingData[i].level > 0)
                                candidates.Add(i);
                        }
                        int downgraded = 0;
                        var shakeTiles = new List<int>();
                        while (downgraded < 3 && candidates.Count > 0)
                        {
                            int idx = candidates[Random.Range(0, candidates.Count)];
                            candidates.Remove(idx);
                            int oldOwner = buildingData[idx].ownerIndex;
                            buildingData[idx].level--;
                            if (oldOwner >= 0 && oldOwner < playerInfoPanels.Count && playerInfoPanels[oldOwner] != null)
                            {
                                playerInfoPanels[oldOwner].Data.propertyValue -= BuildingData.LevelStep;
                                playerInfoPanels[oldOwner].UpdateDisplay();
                                UpdateProsperity();
                            }
                            UpdateBuildingSprite(idx);
                            StartCoroutine(BuildingDowngradeAnim(idx));
                            shakeTiles.Add(idx);
                            downgraded++;
                            Debug.Log($"[Earthquake] {tilePath[idx].name} downgraded to level {buildingData[idx].level}");
                        }
                        if (shakeTiles.Count > 0)
                            StartCoroutine(EarthquakeShakeAnim(shakeTiles));
                        BroadcastMsg($"【事件】地震：{downgraded}处房产降级", BroadcastBar.P1);
                    }
                    break;

                case PenaltyEffect.ReputationLoss:
                    // 丑闻：财富最高的玩家声誉-10且现金-300（事件定稿：加现金才有劫富档位感，v3移植）
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
                                playerInfoPanels[i].Data.cash -= 300;
                                playerInfoPanels[i].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"【事件】丑闻：{playerInfoPanels[i].Data.playerName} 声誉-10，被\"劝捐\"300元", BroadcastBar.P1);
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
                        BroadcastMsg($"【事件】{playerInfoPanels[randomPlayer].Data.playerName} 因交通事故下回合被跳过", BroadcastBar.P2);
                    }
                    break;

                case PenaltyEffect.CashGain:
                    // 发现金矿：所有玩家现金+200（事件定稿：弃百分比改固定值，v3移植）
                case PenaltyEffect.CashLoss:
                    // 黑帮火拼：所有玩家现金-250（v3移植）
                    for (int i = 0; i < playerInfoPanels.Count; i++)
                        ApplyEffectToPlayer(i, eventData);
                    break;

                case PenaltyEffect.CashGainPercent:
                case PenaltyEffect.CashLossPercent:
                    // 旧百分比事件已弃用（定稿文档第六节），保留兼容
                    for (int i = 0; i < playerInfoPanels.Count; i++)
                        ApplyEffectToPlayer(i, eventData);
                    break;

                case PenaltyEffect.Custom:
                    // 台风：所有玩家被传送到随机位置（事件定稿：新版为随机传送，v3移植）
                    if (name == "台风")
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int randTile = Random.Range(0, tilePath.Count);
                            tileIndices[i] = randTile;
                            Vector2 pos = GetTileAnchorPos(randTile);
                            if (tokens[i] != null) tokens[i].anchoredPosition = pos;
                        }
                        BroadcastMsg("【事件】台风：所有玩家被传送到随机位置", BroadcastBar.P1);
                    }
                    else if (name == "边境封锁")
                    {
                        // 边境封锁：全员单骰4回合（v3移植）
                        borderClosedRounds = 4;
                        BroadcastMsg("【事件】边境封锁：全员只能掷一个骰子，持续4回合", BroadcastBar.P1);
                    }
                    else if (name == "罗宾汉出没")
                    {
                        // 罗宾汉出没：最富玩家-500（劫富降不了繁荣但制造戏剧，v3移植）
                        int maxW = int.MinValue; int richest = -1;
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            if (playerInfoPanels[i].Data.Wealth > maxW) { maxW = playerInfoPanels[i].Data.Wealth; richest = i; }
                        }
                        if (richest >= 0)
                        {
                            playerInfoPanels[richest].Data.cash -= 500;
                            playerInfoPanels[richest].UpdateDisplay(); UpdateProsperity();
                            BroadcastMsg($"【事件】罗宾汉出没：{playerInfoPanels[richest].Data.playerName} 被劫500元", BroadcastBar.P1);
                        }
                    }
                    else if (name == "伸出援手")
                    {
                        // 伸出援手：最富-500，最穷+救济金（危机区+700，其余600）——繁荣"呼吸机"主泵（事件定稿第五节，v3移植）
                        int aid = GetPhaseAidAmount();
                        int maxW2 = int.MinValue, minW2 = int.MaxValue;
                        for (int i = 0; i < playerInfoPanels.Count; i++)
                        {
                            if (playerInfoPanels[i] == null) continue;
                            int w = playerInfoPanels[i].Data.Wealth;
                            if (w > maxW2) maxW2 = w;
                            if (w < minW2) minW2 = w;
                        }
                        // v2.2 P1修复：唯一索引匹配（原"值相等"匹配在并列时放大效果、全员相等时全员白拿）
                        if (maxW2 == minW2)
                        {
                            BroadcastMsg("【事件】市政库充盈，本轮无需救济");
                            UpdateProsperity();
                        }
                        else
                        {
                            int richIdx = -1, poorIdx = -1;
                            for (int i = 0; i < playerInfoPanels.Count; i++)
                            {
                                if (playerInfoPanels[i] == null) continue;
                                int w = playerInfoPanels[i].Data.Wealth;
                                if (w == maxW2 && richIdx < 0) richIdx = i;
                                if (w == minW2 && poorIdx < 0) poorIdx = i;
                            }
                            if (richIdx >= 0)
                            {
                                playerInfoPanels[richIdx].Data.cash -= 500;
                                playerInfoPanels[richIdx].UpdateDisplay();
                                BroadcastMsg($"【事件】伸出援手：{playerInfoPanels[richIdx].Data.playerName} 是最富的，-500");
                            }
                            if (poorIdx >= 0)
                            {
                                playerInfoPanels[poorIdx].Data.cash += aid;
                                playerInfoPanels[poorIdx].UpdateDisplay();
                                BroadcastMsg($"【事件】伸出援手：最困难的{playerInfoPanels[poorIdx].Data.playerName} 获得{aid}元救济金");
                            }
                            UpdateProsperity();
                        }
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
            // 事件定稿：卖地折扣60%市价（与破产制折扣线一致，v3移植）
            int refund = Mathf.RoundToInt(buildingData[target].GetMarketPrice(currentProsperity) * 0.6f);
            playerInfoPanels[tokenIndex].Data.cash += refund;
            playerInfoPanels[tokenIndex].Data.propertyValue -= buildingData[target].TotalValue;
            buildingData[target].level = 0;
            buildingData[target].ownerIndex = -2; // 回归政府所有
            UpdateBuildingSprite(target);
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 卖掉一块房产，获得{refund}元");
        }

        /// <summary>随机选自己的房产降一级（v3移植：允许降到0级有主地）</summary>
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
            playerInfoPanels[tokenIndex].Data.propertyValue -= BuildingData.LevelStep;
            UpdateBuildingSprite(target);
            StartCoroutine(BuildingDowngradeAnim(target));
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{playerInfoPanels[tokenIndex].Data.playerName} 的一处房产降级");
        }

        /// <summary>随机获得一处房产（v3移植：商战=白送政府地 / 人脉=市价85折优先购他人房产）</summary>
        private void AcquireRandomProperty(int tokenIndex, bool payForIt)
        {
            var candidates = new List<int>();
            if (!payForIt)
            {
                // 商战（事件定稿）：只从"政府"地块中白送一块（原版可白抽玩家成熟地块=白送800~1600，过度）
                for (int i = 0; i < buildingData.Length; i++)
                {
                    if (buildingData[i] != null && buildingData[i].IsGovernmentOwned)
                        candidates.Add(i);
                }
                if (candidates.Count == 0)
                {
                    BroadcastMsg("【奖励】市面上已无可获得的政府地块，商战扑空");
                    return;
                }
                int target = candidates[Random.Range(0, candidates.Count)];
                buildingData[target].ownerIndex = tokenIndex;
                buildingData[target].level = 1;
                playerInfoPanels[tokenIndex].Data.propertyValue += buildingData[target].TotalValue;
                UpdateBuildingSprite(target);
                playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                BroadcastMsg($"【奖励】{playerInfoPanels[tokenIndex].Data.playerName} 赢得一处政府地块！", BroadcastBar.P1);
                return;
            }

            // 人脉（事件定稿）：以市价85折优先购得一块他人房产，房款付给原主
            for (int i = 0; i < buildingData.Length; i++)
            {
                if (buildingData[i] != null && buildingData[i].HasOwner && buildingData[i].ownerIndex != tokenIndex)
                    candidates.Add(i);
            }
            if (candidates.Count == 0)
            {
                BroadcastMsg("【奖励】没有他人的房产可谈，人脉扑空");
                return;
            }
            int t = candidates[Random.Range(0, candidates.Count)];
            var tile = buildingData[t];
            int price = Mathf.RoundToInt(tile.GetMarketPrice(currentProsperity) * 0.85f);
            var pd = playerInfoPanels[tokenIndex].Data;
            if (pd.cash < price)
            {
                BroadcastMsg("【奖励】现金不足，人脉机会溜走了");
                return;
            }
            int oldOwner = tile.ownerIndex;
            var od = playerInfoPanels[oldOwner].Data;
            pd.cash -= price;
            pd.propertyValue += tile.TotalValue;
            od.cash += price;
            od.propertyValue -= tile.TotalValue;
            tile.ownerIndex = tokenIndex;
            UpdateBuildingSprite(t);
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            playerInfoPanels[oldOwner].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"【交易】{pd.playerName} 凭人脉以{price}元（市价85折）购得{od.playerName}的{t}号地块", BroadcastBar.P1);
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
            playerInfoPanels[tokenIndex].Data.propertyValue += BuildingData.LevelStep;
            UpdateBuildingSprite(target);
            StartCoroutine(BuildingUpgradeAnim(target));
            playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"【奖励】{playerInfoPanels[tokenIndex].Data.playerName} 的房产免费升级到{buildingData[target].level}级", BroadcastBar.P1);
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

            // 玩家决策期间显示自己的状态栏（购买/升级/付租金/Bargain时资金可见，不隐藏）
            if (playerPanels != null && playerPanels.Count > 0 && playerPanels[0] != null)
                playerPanels[0].SetActive(true);

            bool panelClosed = false;

            if (data.IsGovernmentOwned)
            {
                // 政府所有：可购买（BuyCost=320政府补贴价，v3移植）
                int marketPrice = data.GetMarketPrice(currentProsperity);
                buildingPanel.ShowGovernmentOwned(data, marketPrice,
                    () => { // 购买回调
                        if (tokenIndex < playerInfoPanels.Count && playerInfoPanels[tokenIndex] != null)
                        {
                            var pd = playerInfoPanels[tokenIndex].Data;
                            if (pd.cash >= BuildingData.BuyCost)
                            {
                                pd.cash -= BuildingData.BuyCost;
                                data.level = 1;
                                data.ownerIndex = tokenIndex;
                                pd.propertyValue += data.TotalValue; // 等级已更新为1
                                UpdateBuildingSprite(tileIndex);
                                playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"【交易】{pd.playerName} 购入{tileIndex}号地块（补贴价{BuildingData.BuyCost}元）", BroadcastBar.P1);
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
                            if (data.CanUpgrade && pd.cash >= BuildingData.UpgradeCost)
                            {
                                pd.cash -= BuildingData.UpgradeCost;
                                pd.propertyValue += BuildingData.LevelStep;
                                data.level++;
                                UpdateBuildingSprite(tileIndex);
                                StartCoroutine(BuildingUpgradeAnim(tileIndex));
                                playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                                BroadcastMsg($"【交易】{pd.playerName} 升级{tileIndex}号地块到{data.level}级（-{BuildingData.UpgradeCost}元）", BroadcastBar.P1);
                                AudioManager.Instance?.PlayBuildingUpgrade();
                            }
                        }
                    },
                    () => { panelClosed = true; }
                );
            }
            else
            {
                // 他人的：支付租金（市价16%，L3双倍，v3移植）或Bargain
                int marketPrice = data.GetMarketPrice(currentProsperity);
                int rent = data.GetRent(currentProsperity);
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
        /// AI自动处理建筑格：性格槽驱动（Bot v2，v3移植）——安全线/繁荣意识/谈判狠度全部读人设字段
        /// </summary>
        private IEnumerator AIBuildingAction(int tileIndex, int tokenIndex, BuildingData data)
        {
            yield return new WaitForSeconds(0.5f);

            if (tokenIndex >= playerInfoPanels.Count || playerInfoPanels[tokenIndex] == null) yield break;
            var pd = playerInfoPanels[tokenIndex].Data;

            // 繁荣意识：危机区高意识AI勒紧裤腰带（停买地/停升级，只付租金和必要开销）
            bool crisisRestraint = (currentPhase == GamePhase.Crisis) && pd.WithholdsInCrisis;

            if (data.IsGovernmentOwned)
            {
                // 政府所有：本局人设安全线+买价可负担且不处危机克制则购买
                if (!crisisRestraint && pd.IsSafe && pd.CanAfford(BuildingData.BuyCost))
                {
                    pd.cash -= BuildingData.BuyCost;
                    data.level = 1;
                    pd.propertyValue += data.TotalValue; // v2.2 P0修复：先升级再入账（level==0时TotalValue只有400）
                    data.ownerIndex = tokenIndex;
                    UpdateBuildingSprite(tileIndex);
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"【交易】{pd.playerName} 购入了建筑");
                    AudioManager.Instance?.PlayBuildingUpgrade();
                }
            }
            else if (data.ownerIndex == tokenIndex)
            {
                // 自己的：升级需留足本局人设安全现金
                if (!crisisRestraint && pd.IsSafe && data.CanUpgrade && pd.cash >= BuildingData.UpgradeCost + pd.safeLine)
                {
                    pd.cash -= BuildingData.UpgradeCost;
                    pd.propertyValue += BuildingData.LevelStep;
                    data.level++;
                    UpdateBuildingSprite(tileIndex);
                    StartCoroutine(BuildingUpgradeAnim(tileIndex));
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"【交易】{pd.playerName} 升级建筑到{data.level}级");
                    AudioManager.Instance?.PlayBuildingUpgrade();
                }
            }
            else
            {
                // 他人的：现金够付本局人设心理价(市价×狠度)则Bargain，否则付租金
                int marketPrice = data.GetMarketPrice(currentProsperity);
                int psychologicalPrice = Mathf.RoundToInt(marketPrice * pd.bargainToughness);
                if (pd.alive && data.ownerIndex >= 0 && data.ownerIndex < playerInfoPanels.Count
                    && pd.CanAfford(psychologicalPrice) && Random.value < 0.85f)
                {
                    Debug.Log($"[AI] {pd.playerName}({pd.personaName}) cash={pd.cash} >= 心理价{psychologicalPrice}, chose Bargain");
                    StartBargain(tileIndex, tokenIndex, data);
                    yield break;
                }

                // 现金不足心理价，支付租金
                Debug.Log($"[AI] {pd.playerName}({pd.personaName}) cash={pd.cash} < 心理价{psychologicalPrice}, pay rent");
                if (data.ownerIndex < playerInfoPanels.Count && playerInfoPanels[data.ownerIndex] != null)
                {
                    var ownerData = playerInfoPanels[data.ownerIndex].Data;
                    int rent = data.GetRent(currentProsperity);
                    pd.cash -= rent;
                    ownerData.cash += rent;
                    AudioManager.Instance?.PlayCoin();
                    playerInfoPanels[tokenIndex].UpdateDisplay(); UpdateProsperity();
                    playerInfoPanels[data.ownerIndex].UpdateDisplay(); UpdateProsperity();
                    BroadcastMsg($"【租金】{pd.playerName} 向 {ownerData.playerName} 支付租金{rent}元", BroadcastBar.P2);
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

        /// <summary>带优先级播报（P0告警/P1交易/P2常规，见播报系统设计文档，v3移植）</summary>
        private void BroadcastMsg(string message, int priority, float duration = 2.5f)
        {
            if (broadcastBar != null)
                broadcastBar.Broadcast(message, priority, duration);
        }

        // ==================== v2.2 M套件：萧条经济（批次A移植） ====================

        /// <summary>
        /// 回合末经济收发。危机=维持费（恶名翻倍）+通胀吞噬（超额现金-30%，蒸发不转移）；
        /// 冲刺=繁荣红利+20。收发后由外层 UpdateProsperity 统一刷新繁荣/破产检查。
        /// </summary>
        private void ApplyEconomyTick()
        {
            if (gameEnded) return;
            bool crisis = currentPhase == GamePhase.Crisis;
            bool sprint = currentPhase == GamePhase.Sprint;
            if (!crisis && !sprint) return;

            int eatenTotal = 0;
            var eatenNames = new List<string>();

            for (int i = 0; i < playerInfoPanels.Count; i++)
            {
                if (playerInfoPanels[i] == null) continue;
                var pd = playerInfoPanels[i].Data;
                if (!pd.alive) continue;
                bool infamous = pd.reputation < 45;   // 恶名线：民意抵制对象

                if (crisis)
                {
                    int fee = CrisisFee(infamous);
                    pd.cash -= fee;                                    // 维持费
                    int th = InflationThreshold(infamous);             // 通胀起征点
                    int ex = pd.cash - th;
                    if (ex > 0)
                    {
                        int eaten = Mathf.Min(Mathf.RoundToInt(ex * 0.3f), 250);
                        pd.cash -= eaten;                              // 蒸发，不转移
                        eatenTotal += eaten;
                        eatenNames.Add(pd.playerName);
                    }
                    if (infamous && !surchargeWarned[i])
                    {
                        surchargeWarned[i] = true;
                        BroadcastMsg($"【警告】{pd.playerName} 声望扫地，萧条中被民众抵制，维持费翻倍！", BroadcastBar.P0);
                    }
                }
                else
                {
                    pd.cash += 20;                                     // 冲刺红利
                }
            }

            if (crisis)
            {
                if (eatenTotal > 0)
                    BroadcastMsg($"【危机】市政征收维持费；通胀吞噬现金{eatenTotal}元（{string.Join("、", eatenNames)}）", BroadcastBar.P1);
                else
                    BroadcastMsg("【危机】萧条持续，市政征收维持费", BroadcastBar.P1);
            }
            else if (sprint)
            {
                BroadcastMsg("【繁荣】经济腾飞，全城分红20元/人", BroadcastBar.P1);
            }
        }

        /// <summary>v2.2 状态面板：短板行（最穷玩家），回合末刷新一次（防逐笔闪烁——短板实测每局换手6.3次）</summary>
        private void RefreshBottleneckText()
        {
            if (bottleneckText == null)
            {
                if (prosperityText == null) return;
                var go = new GameObject("BottleneckText");
                go.transform.SetParent(prosperityText.transform.parent, false);
                bottleneckText = go.AddComponent<Text>();
                bottleneckText.font = BargainState.GetSafeFont();
                bottleneckText.fontSize = Mathf.Max(14, prosperityText.fontSize - 4);
                bottleneckText.alignment = TextAnchor.LowerLeft;
                bottleneckText.color = new Color(0.95f, 0.9f, 0.75f);
                bottleneckText.raycastTarget = false;
                var rt = bottleneckText.rectTransform;
                rt.anchorMin = prosperityText.rectTransform.anchorMin;
                rt.anchorMax = prosperityText.rectTransform.anchorMax;
                rt.pivot = prosperityText.rectTransform.pivot;
                rt.anchoredPosition = prosperityText.rectTransform.anchoredPosition + new Vector2(0, -prosperityText.rectTransform.rect.height * 0.7f);
                rt.sizeDelta = new Vector2(prosperityText.rectTransform.sizeDelta.x, prosperityText.rectTransform.sizeDelta.y * 0.6f);
            }
            int minW = int.MaxValue, minIdx = -1;
            for (int i = 0; i < playerInfoPanels.Count; i++)
            {
                if (playerInfoPanels[i] == null) continue;
                int w = playerInfoPanels[i].Data.Wealth;
                if (w < minW) { minW = w; minIdx = i; }
            }
            if (minIdx >= 0)
                bottleneckText.text = $"短板：{playerInfoPanels[minIdx].Data.playerName}（财富{minW}）";
        }

        // ==================== 繁荣值系统 ====================

        /// <summary>
        /// 计算并更新繁荣值 = 最穷玩家财富值 / 26（数值文档v2.1，v3移植）
        /// 每次任何玩家数值变化后调用；附带：变化播报/阈值告警/阶段切换/破产检查
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

            int oldProsperity = currentProsperity;
            currentProsperity = Mathf.RoundToInt((float)minWealth / 26f);

            if (prosperityText != null)
            {
                // v2.2 状态面板：指数+箭头+阶段标签+本阶段规则小字（两行）
                string phaseLabel = currentPhase == GamePhase.Crisis ? "危机区"
                                  : currentPhase == GamePhase.Sprint ? "冲刺区"
                                  : "常规区";
                string rule = currentPhase == GamePhase.Crisis ? "维持费 -30/回合（恶名 -60）"
                            : currentPhase == GamePhase.Sprint ? "分红 +20/回合"
                            : "";
                string arrow = lastProsperityShown >= 0
                    ? (currentProsperity > lastProsperityShown ? " ▲" : currentProsperity < lastProsperityShown ? " ▼" : "")
                    : "";
                string ruleLine = string.IsNullOrEmpty(rule) ? "" : "\n" + rule;
                prosperityText.text = $"繁荣指数 {currentProsperity}{arrow} ▏{phaseLabel}{ruleLine}";
                lastProsperityShown = currentProsperity;
            }

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

            // ===== 播报系统接线（文档A1/A2，v3移植） =====
            if (!processingBankruptcy && !gameEnded)
            {
                BroadcastProsperity(oldProsperity, currentProsperity);
                RefreshPhase();
            }

            // ===== 现金破产制检查（所有资金变动后统一在此收口，v3移植） =====
            if (!processingBankruptcy && !gameEnded)
                CheckAllBankruptcies();
        }

        /// <summary>繁荣变化/阈值播报（P2常规每回合限流B4；跨30向下/90向上告警P0）（v2.2）</summary>
        private void BroadcastProsperity(int oldVal, int newVal)
        {
            if (newVal == oldVal) return;
            int delta = newVal - oldVal;

            // 开局教学（一次性，P1）
            if (!tutorialBroadcastShown)
            {
                tutorialBroadcastShown = true;
                BroadcastMsg("【繁荣】本城繁荣取决于最穷玩家的财富。别让任何人破产——繁荣到110，大家共赢！", BroadcastBar.P1, 4f);
            }

            // 阈值告警（P0）：跨越30向下 / 跨越90向上
            bool crossed30Down = oldVal >= 30 && newVal < 30;
            bool crossed90Up = oldVal < 90 && newVal >= 90;
            if (crossed30Down)
                BroadcastMsg($"【警告】繁荣跌破30！城市衰退边缘——最穷玩家的财富正在拖垮全城！", BroadcastBar.P0);
            else if (crossed90Up)
                BroadcastMsg($"【繁荣】繁荣突破90！距离全员胜利（110）只差一步，别松手！", BroadcastBar.P0);

            // 常规变化（P2，|Δ|≥5）——v2.2 B4：每回合限流一条（繁荣逐笔刷新曾刷屏）
            else if (Mathf.Abs(delta) >= 5 && currentRound != lastProsperityBroadcastRound)
            {
                string arrow = delta > 0 ? $"▲{delta}" : $"▼{Mathf.Abs(delta)}";
                string trend = delta > 0 ? "全城资产升值！" : "地价随之下跌…";
                lastProsperityBroadcastRound = currentRound;
                BroadcastMsg($"【繁荣】城市繁荣 {oldVal} → {newVal}（{arrow}），{trend}", BroadcastBar.P2);
            }
            lastProsperity = newVal;
        }

        /// <summary>阶段切换（危机/常规/冲刺），换挡即播报（v3移植）</summary>
        private void RefreshPhase()
        {
            GamePhase phase =
                currentProsperity < CrisisLine ? GamePhase.Crisis :
                currentProsperity >= SprintLine ? GamePhase.Sprint :
                GamePhase.Normal;

            if (phase != currentPhase && phase != lastBroadcastPhase)
            {
                lastBroadcastPhase = phase;
                switch (phase)
                {
                    case GamePhase.Crisis:
                        BroadcastMsg("【警告】城市进入衰退期！市政启动救市：济贫加码、灾祸减少", BroadcastBar.P0);
                        break;
                    case GamePhase.Sprint:
                        BroadcastMsg("【繁荣】经济腾飞！城市进入冲刺期，机会与风险并存", BroadcastBar.P0);
                        break;
                }
            }
            currentPhase = phase;
        }

        /// <summary>当前阶段工资（危机/常规300，冲刺325）（v3移植）</summary>
        private int GetPhaseSalary() => currentPhase == GamePhase.Sprint ? 325 : 300;

        /// <summary>当前阶段济贫金额（危机700，其余600）（v3移植）</summary>
        private int GetPhaseAidAmount() => currentPhase == GamePhase.Crisis ? 700 : 600;

        // ==================== 现金破产制（数值文档v2.1 第三节，v3移植） ====================

        /// <summary>统一破产检查：任何玩家现金为负 → 强制卖地还债（市价60%）→ 资不抵债则出局→全员失败</summary>
        private void CheckAllBankruptcies()
        {
            if (processingBankruptcy || gameEnded) return;
            for (int i = 0; i < playerInfoPanels.Count; i++)
            {
                if (playerInfoPanels[i] == null) continue;
                if (playerInfoPanels[i].Data.alive && playerInfoPanels[i].Data.cash < 0)
                {
                    processingBankruptcy = true;
                    TryResolveBankruptcy(i);
                    processingBankruptcy = false;
                    if (gameEnded) return;
                }
            }
        }

        /// <summary>慈善家救援（Bot v2，v3移植）：繁荣意识≥0.9的AI，回合开始发现最穷玩家现金<300时，
        /// 以市价90%收购其一块地——变相给穷人输血，播报出来即城市故事</summary>
        private void TryRescuePoorest(int rescuerIndex)
        {
            if (gameEnded || rescuerIndex <= 0 || rescuerIndex >= playerInfoPanels.Count) return;
            var rescuer = playerInfoPanels[rescuerIndex]?.Data;
            if (rescuer == null || !rescuer.alive || !rescuer.IsRescuer || !rescuer.IsSafe) return;

            // 找现金最少的存活他人
            int poorest = -1; int minCash = int.MaxValue;
            for (int i = 0; i < playerInfoPanels.Count; i++)
            {
                if (i == rescuerIndex || playerInfoPanels[i] == null) continue;
                var d = playerInfoPanels[i].Data;
                if (!d.alive || d.cash >= minCash) continue;
                minCash = d.cash; poorest = i;
            }
            if (poorest < 0 || minCash >= 300) return;
            if (playerInfoPanels[poorest].Data.reputation < 35)
            {
                // v2.2 M5：众叛亲离——恶名者无救援（每局每对象只播一次传闻）
                if (!rescueDeniedShown[poorest])
                {
                    rescueDeniedShown[poorest] = true;
                    BroadcastMsg($"【传闻】有人对 {playerInfoPanels[poorest].Data.playerName} 的困境视而不见……", BroadcastBar.P2);
                }
                return;
            }

            // 找穷人名下一块地（按市价从低到高的第一块——从最便宜的开始救）
            int target = -1; int lowestPrice = int.MaxValue;
            for (int i = 0; i < buildingData.Length; i++)
            {
                if (buildingData[i] == null || buildingData[i].ownerIndex != poorest) continue;
                int mp = buildingData[i].GetMarketPrice(currentProsperity);
                if (mp < lowestPrice) { lowestPrice = mp; target = i; }
            }
            if (target < 0) return; // 穷人无地可卖，无能为力

            var tile = buildingData[target];
            int price = Mathf.RoundToInt(tile.GetMarketPrice(currentProsperity) * 0.9f);
            if (rescuer.cash < price) return;

            var poorData = playerInfoPanels[poorest].Data;
            rescuer.cash -= price;
            rescuer.propertyValue += tile.TotalValue;
            poorData.cash += price;
            poorData.propertyValue -= tile.TotalValue;
            tile.ownerIndex = rescuerIndex;
            UpdateBuildingSprite(target);
            playerInfoPanels[rescuerIndex].UpdateDisplay(); UpdateProsperity();
            playerInfoPanels[poorest].UpdateDisplay(); UpdateProsperity();
            AudioManager.Instance?.PlayCoin();
            BroadcastMsg($"【交易】{rescuer.playerName} 以{price}元买下 {poorData.playerName} 的一处地产——「我帮你渡过难关」", BroadcastBar.P1, 4f);
            Debug.Log($"[Rescue] {rescuer.playerName}({rescuer.personaName}) rescued {poorData.playerName} with {price}");
        }

        /// <summary>处理单个玩家的资不抵债：卖地→仍为负则出局并触发全灭（v3移植）</summary>
        private void TryResolveBankruptcy(int pi)
        {
            var pd = playerInfoPanels[pi].Data;
            BroadcastMsg($"【警告】{pd.playerName} 资不抵债！强制拍卖名下地产…", BroadcastBar.P0);

            int sold = 0;
            while (pd.cash < 0)
            {
                int ownedTile = -1;
                for (int i = 0; i < buildingData.Length; i++)
                {
                    if (buildingData[i] != null && buildingData[i].ownerIndex == pi) { ownedTile = i; break; }
                }
                if (ownedTile < 0) break;

                var tile = buildingData[ownedTile];
                int salePrice = Mathf.RoundToInt(tile.GetMarketPrice(currentProsperity) * 0.6f);
                pd.cash += salePrice;
                pd.propertyValue -= tile.TotalValue;
                tile.ownerIndex = -2;
                tile.level = 0;
                UpdateBuildingSprite(ownedTile);
                sold++;
                BroadcastMsg($"【警告】{pd.playerName} 的地产被以 {salePrice}元 强制拍卖");
            }
            playerInfoPanels[pi].UpdateDisplay();

            if (pd.cash < 0)
            {
                // 卖光仍资不抵债 → 破产出局 → 全员失败（最穷的人破产，城市随之崩塌）
                pd.cash = 0;
                pd.propertyValue = 0;
                pd.alive = false;
                if (tokens != null && pi < tokens.Count && tokens[pi] != null)
                    tokens[pi].gameObject.SetActive(false);
                playerInfoPanels[pi].UpdateDisplay();
                // v2.2：败局按声望点名（恶名者"众叛亲离"变体）
                string outMsg = pd.reputation < 35
                    ? $"【警告】{pd.playerName} 破产出局——为富不仁，众叛亲离，城市随之崩塌……"
                    : $"【警告】{pd.playerName} 破产出局——城市失去了最后的经济支柱…";
                BroadcastMsg(outMsg, BroadcastBar.P0);
                string endMsg = pd.reputation < 35
                    ? $"{pd.playerName} 破产出局——为富不仁，众叛亲离，城市随之崩塌……所有玩家失败！"
                    : $"{pd.playerName} 破产出局，城市随之崩塌——所有玩家失败！";
                EndGame(endMsg, false);
            }
            else if (sold > 0)
            {
                BroadcastMsg($"{pd.playerName} 拍卖{sold}处地产后渡过难关，现金 {pd.cash}元", BroadcastBar.P1);
            }
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
            if (currentProsperity >= WinLine)
            {
                EndGame("繁荣值达到110，所有玩家大获全胜！", true);
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
            else if (currentProsperity >= WinLine)
            {
                result = "繁荣值达到110，大获全胜！";
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
            // Bargain往返修复：以下字段此前未保存，往返后AI人设/出局状态会回落默认值
            public static bool[] playerAlive = new bool[4];
            public static string[] personaNames = new string[4];
            public static int[] personaSafeLine = new int[4];
            public static float[] personaProsperityCare = new float[4];
            public static float[] personaBargainToughness = new float[4];
            public static bool[] skipNextTurn = new bool[4];
            public static bool[] rerollNextTurn = new bool[4];
            public static int borderBlockTurns;
            public static int borderClosedRounds;
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
            SavedGameState.borderClosedRounds = borderClosedRounds;
            SavedGameState.gameEnded = gameEnded;

            for (int i = 0; i < 4; i++)
            {
                SavedGameState.tileIndices[i] = tileIndices[i];
                SavedGameState.skipNextTurn[i] = skipNextTurn[i];
                SavedGameState.rerollNextTurn[i] = rerollNextTurn[i];
                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    var pd = playerInfoPanels[i].Data;
                    SavedGameState.playerCash[i] = pd.cash;
                    SavedGameState.playerProperty[i] = pd.propertyValue;
                    SavedGameState.playerRep[i] = pd.reputation;
                    SavedGameState.playerSelfInterest[i] = pd.selfInterest;
                    SavedGameState.playerCashPref[i] = pd.cashPreference;
                    SavedGameState.playerNames[i] = pd.playerName;
                    SavedGameState.playerAlive[i] = pd.alive;
                    SavedGameState.personaNames[i] = pd.personaName;
                    SavedGameState.personaSafeLine[i] = pd.safeLine;
                    SavedGameState.personaProsperityCare[i] = pd.prosperityCare;
                    SavedGameState.personaBargainToughness[i] = pd.bargainToughness;
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
            borderClosedRounds = SavedGameState.borderClosedRounds;
            // borderBlockTurns not used (旧字段保留)
            gameEnded = SavedGameState.gameEnded;

            for (int i = 0; i < 4; i++)
            {
                tileIndices[i] = SavedGameState.tileIndices[i];
                skipNextTurn[i] = SavedGameState.skipNextTurn[i];
                rerollNextTurn[i] = SavedGameState.rerollNextTurn[i];

                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    var pd = playerInfoPanels[i].Data;
                    pd.cash = SavedGameState.playerCash[i];
                    pd.propertyValue = SavedGameState.playerProperty[i];
                    pd.reputation = SavedGameState.playerRep[i];
                    pd.selfInterest = SavedGameState.playerSelfInterest[i];
                    pd.cashPreference = SavedGameState.playerCashPref[i];
                    pd.playerName = SavedGameState.playerNames[i];
                    pd.alive = SavedGameState.playerAlive[i];
                    pd.personaName = SavedGameState.personaNames[i];
                    pd.safeLine = SavedGameState.personaSafeLine[i];
                    pd.prosperityCare = SavedGameState.personaProsperityCare[i];
                    pd.bargainToughness = SavedGameState.personaBargainToughness[i];
                    playerInfoPanels[i].UpdateDisplay();
                }

                // 恢复Token位置（出局者棋子隐藏）
                if (i < tokens.Count && tokens[i] != null && tilePath.Count > 0)
                {
                    tokens[i].gameObject.SetActive(SavedGameState.playerAlive[i]);
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

            // Bargain结果统一由 Update() → HandleBargainResult 单一路径处理。
            // （此前此处与 Update() 存在两条并行孵化回合链的路径，会导致多枚棋子并发移动）
            SavedGameState.hasSavedState = false;
            Debug.Log("[GameState] Restored");
            return true;
        }

        // ==================== 存档系统（主菜单「继续游戏」/游戏内保存，见SaveLoadManager） ====================

        /// <summary>是否允许保存：仅玩家回合且无任何进行中动作（掷骰/移动/缩放/Bargain/已结束），防止协程中间态被存坏。</summary>
        public bool CanSaveNow =>
            !isBusy && !waitingForChoice && !isZooming && !BargainData.bargainActive
            && !gameEnded && currentTurn == 0;

        /// <summary>打包当前局面为存档数据。</summary>
        public SaveData CaptureToSaveData()
        {
            var s = new SaveData();
            s.currentTurn = currentTurn;
            s.currentRound = currentRound;
            s.currentProsperity = currentProsperity;
            s.borderClosedRounds = borderClosedRounds;
            s.gameEnded = gameEnded;

            for (int i = 0; i < 4; i++)
            {
                s.tileIndices[i] = tileIndices[i];
                s.skipNextTurn[i] = skipNextTurn[i];
                s.rerollNextTurn[i] = rerollNextTurn[i];

                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    var pd = playerInfoPanels[i].Data;
                    s.playerNames[i] = pd.playerName;
                    s.playerCash[i] = pd.cash;
                    s.playerProperty[i] = pd.propertyValue;
                    s.playerRep[i] = pd.reputation;
                    s.playerAlive[i] = pd.alive;
                    s.playerSelfInterest[i] = pd.selfInterest;
                    s.playerCashPref[i] = pd.cashPreference;
                    s.personaNames[i] = pd.personaName;
                    s.personaSafeLine[i] = pd.safeLine;
                    s.personaProsperityCare[i] = pd.prosperityCare;
                    s.personaBargainToughness[i] = pd.bargainToughness;
                }
            }

            if (buildingData != null)
            {
                s.buildingLevel = new int[buildingData.Length];
                s.buildingOwner = new int[buildingData.Length];
                s.buildingBaseValue = new int[buildingData.Length];
                for (int i = 0; i < buildingData.Length; i++)
                {
                    s.buildingLevel[i] = buildingData[i].level;
                    s.buildingOwner[i] = buildingData[i].ownerIndex;
                    s.buildingBaseValue[i] = buildingData[i].baseValue;
                }
            }
            return s;
        }

        /// <summary>从存档恢复全部局面并续跑回合循环。</summary>
        public void RestoreFromSave(SaveData s)
        {
            currentTurn = s.currentTurn;
            currentRound = s.currentRound;
            currentProsperity = s.currentProsperity;
            borderClosedRounds = s.borderClosedRounds;
            gameEnded = s.gameEnded;

            for (int i = 0; i < 4; i++)
            {
                tileIndices[i] = s.tileIndices[i];
                skipNextTurn[i] = s.skipNextTurn[i];
                rerollNextTurn[i] = s.rerollNextTurn[i];

                if (i < playerInfoPanels.Count && playerInfoPanels[i] != null)
                {
                    var pd = playerInfoPanels[i].Data;
                    pd.playerName = s.playerNames[i];
                    pd.cash = s.playerCash[i];
                    pd.propertyValue = s.playerProperty[i];
                    pd.reputation = s.playerRep[i];
                    pd.alive = s.playerAlive[i];
                    pd.selfInterest = s.playerSelfInterest[i];
                    pd.cashPreference = s.playerCashPref[i];
                    pd.personaName = s.personaNames[i];
                    pd.safeLine = s.personaSafeLine[i];
                    pd.prosperityCare = s.personaProsperityCare[i];
                    pd.bargainToughness = s.personaBargainToughness[i];
                    playerInfoPanels[i].UpdateDisplay();
                }

                if (i < tokens.Count && tokens[i] != null && tilePath.Count > 0)
                {
                    tokens[i].gameObject.SetActive(s.playerAlive[i]);
                    tokens[i].anchoredPosition = GetTileAnchorPos(tileIndices[i]);
                }
            }

            if (buildingData != null && s.buildingLevel != null)
            {
                for (int i = 0; i < buildingData.Length && i < s.buildingLevel.Length; i++)
                {
                    buildingData[i].level = s.buildingLevel[i];
                    buildingData[i].ownerIndex = s.buildingOwner[i];
                    buildingData[i].baseValue = s.buildingBaseValue[i];
                    UpdateBuildingSprite(i);
                }
            }

            UpdateProsperity();
            BroadcastMsg($"读档成功，从第{currentRound}回合继续");

            // 续跑回合循环
            if (currentTurn == 0)
                StartPlayerTurn();
            else
                StartCoroutine(StartAITurn(currentTurn));
        }

        /// <summary>应用讨价还价人格卡的声望变化（卡面标注的±声望，成交与否均生效）</summary>
        private void ApplyBargainCardReputation(int playerIdx, int cardIdx)
        {
            if (cardIdx < 0 || cardIdx >= BargainState.reputationChange.Length) return;
            if (playerIdx < 0 || playerIdx >= playerInfoPanels.Count || playerInfoPanels[playerIdx] == null) return;

            var pd = playerInfoPanels[playerIdx].Data;
            int rep = BargainState.reputationChange[cardIdx];
            if (rep == 0) return;
            pd.reputation = Mathf.Clamp(pd.reputation + rep, 0, 100);
            playerInfoPanels[playerIdx].UpdateDisplay(); UpdateProsperity();
            BroadcastMsg($"{pd.playerName} ({BargainState.cardNames[cardIdx]}) 声望{(rep >= 0 ? "+" : "")}{rep}");
        }

        // ===== 测试模式：跨场景Bargain测试状态（Bargain为全场景切换，实例字段会销毁，进度需静态保存） =====
        public static class TestBargainState
        {
            public static bool active;          // 测试流程进行中
            public static int phase;            // 1=AI买玩家房产进行中, 2=玩家买AI房产进行中
            public static int playerTileIndex;  // 测试用玩家房产tile索引
            public static int aiTileIndex;      // 测试用AI房产tile索引
        }

        /// <summary>
        /// 测试模式准备：找两处建筑格，玩家与AI1各获得一处1级房产，作为两次Bargain的标的
        /// </summary>
        private bool SetupTestBargain()
        {
            if (buildingData == null || tilePath == null || tilePath.Count == 0) return false;
            if (playerInfoPanels == null || playerInfoPanels.Count < 2 || playerInfoPanels[0] == null || playerInfoPanels[1] == null) return false;

            // 找两处建筑格（房屋块）：第一处给玩家，第二处给AI1
            int playerTile = -1, aiTile = -1;
            for (int i = 0; i < tilePath.Count; i++)
            {
                var img = tilePath[i] != null ? tilePath[i].GetComponent<Image>() : null;
                if (img == null || img.sprite == null || img.sprite.name != "房屋块") continue;
                if (playerTile < 0) { playerTile = i; continue; }
                aiTile = i;
                break;
            }
            if (playerTile < 0 || aiTile < 0)
            {
                Debug.LogWarning("[TestMode] 未找到两处建筑格");
                return false;
            }

            // 玩家获得1级房产
            buildingData[playerTile].level = 1;
            buildingData[playerTile].ownerIndex = 0;
            UpdateBuildingSprite(playerTile);
            playerInfoPanels[0].Data.propertyValue += buildingData[playerTile].TotalValue;
            playerInfoPanels[0].UpdateDisplay();

            // AI1获得1级房产
            buildingData[aiTile].level = 1;
            buildingData[aiTile].ownerIndex = 1;
            UpdateBuildingSprite(aiTile);
            playerInfoPanels[1].Data.propertyValue += buildingData[aiTile].TotalValue;
            playerInfoPanels[1].UpdateDisplay();

            UpdateProsperity(); // 等级变化后刷新市场价格缓存

            TestBargainState.playerTileIndex = playerTile;
            TestBargainState.aiTileIndex = aiTile;
            Debug.Log($"[TestMode] 测试模式就绪：玩家房产={tilePath[playerTile].name}, AI1房产={tilePath[aiTile].name}");
            return true;
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
            public static int sellerCardIndex = -1; // 本局讨价还价卖方所选人格卡（用于结算声望）
            public static int buyerCardIndex = -1;  // 买方所选人格卡
            public static string tileName = "";     // 交易房产名（结果场景显示用）
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
            BargainData.sellerCardIndex = -1;
            BargainData.buyerCardIndex = -1;
            BargainData.tileName = tilePath != null && tileIndex >= 0 && tileIndex < tilePath.Count && tilePath[tileIndex] != null
                ? $"{tilePath[tileIndex].name}（{data.level}级）"
                : "房产";

            BroadcastMsg("进入Bargain...");
            AudioManager.Instance?.PlayRandomBargainBGM();

            // 保存游戏状态
            SaveGameState(tileIndex, tokenIndex, ownerIndex);

            // AIvsAI：在GameScene内直接自动结算并直达结果场景。
            // 不加载选卡场景——同步LoadScene下一帧才切换，选卡会先渲染1~2帧"卡面朝上"的画面（用户报告的闪屏）。
            if (tokenIndex != 0 && ownerIndex != 0)
            {
                BargainSelectController.AutoResolve();
                UnityEngine.SceneManagement.SceneManager.LoadScene("讨价还价_结果");
                return;
            }

            // 完全切换场景（非叠加）
            UnityEngine.SceneManagement.SceneManager.LoadScene("讨价还价_选卡");
        }

        public void BackToMenu()
        {
            // 清理跨场景静态状态，避免影响下一局
            BargainData.bargainActive = false;
            SavedGameState.hasSavedState = false;
            BargainController.BargainResult.completed = false;
            TestBargainState.active = false;
            TestBargainState.phase = 0;
            SaveLoadManager.pendingLoadSlot = SaveLoadManager.NoSlot;
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

                        // 播报B1：成交价 vs 市价，砍价成果可见（v3移植）
                        int marketPrice = data.GetMarketPrice(currentProsperity);
                        string dealNote;
                        if (finalPrice < marketPrice)
                        {
                            int savedPct = Mathf.RoundToInt((marketPrice - finalPrice) * 100f / Mathf.Max(1, marketPrice));
                            dealNote = $"市价{marketPrice}元，砍价省了{savedPct}%";
                        }
                        else
                        {
                            dealNote = $"高于市价{marketPrice}元，高价接盘…";
                        }
                        BroadcastMsg($"【交易】{buyerData.playerName} 以{finalPrice}元购得 {BargainData.tileName}（原属 {sellerData?.playerName ?? "政府"}，{dealNote}）", BroadcastBar.P1);
                    }
                }
            }
            else
            {
                // 交易失败：支付租金（市价16%，L3双倍，v3移植）
                if (tileIndex >= 0 && tileIndex < buildingData.Length)
                {
                    var data = buildingData[tileIndex];
                    int rent = data.GetRent(currentProsperity);
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
                        BroadcastMsg($"Bargain失败，{buyerData.playerName} 向 {sellerData?.playerName ?? "政府"} 支付 {BargainData.tileName} 租金{rent}元");
                    }
                }
            }

            // 应用双方人格卡声望变化（无论成交与否，声望反映讨价还价的态度）
            ApplyBargainCardReputation(sellerIndex, BargainData.sellerCardIndex);
            ApplyBargainCardReputation(buyerIndex, BargainData.buyerCardIndex);

            // ===== 测试模式：串联第二次Bargain或直接结束游戏，不进入正常回合循环 =====
            if (TestBargainState.active)
            {
                if (TestBargainState.phase == 1)
                {
                    // 第一次Bargain（AI买玩家房产）已结算 → 发起第二次（玩家买AI房产）
                    TestBargainState.phase = 2;
                    yield return new WaitForSeconds(turnDelay);
                    BroadcastMsg("[测试] 第二次Bargain：玩家购买AI房产");
                    var aiData = buildingData[TestBargainState.aiTileIndex];
                    StartBargain(TestBargainState.aiTileIndex, 0, aiData); // 玩家作为买方
                    yield break;
                }
                if (TestBargainState.phase == 2)
                {
                    // 两次Bargain完成 → 直接结束游戏
                    TestBargainState.active = false;
                    TestBargainState.phase = 0;
                    yield return new WaitForSeconds(turnDelay);
                    EndGame("测试模式：两次Bargain流程完成，游戏结束", true);
                    yield break;
                }
            }
            // ===== 测试模式结束 =====

            yield return new WaitForSeconds(turnDelay);
            yield return NextTurn();
        }

        private bool isZooming = false;

        // 跳过下回合标记（惩罚效果"堵车"使用）
        private bool[] skipNextTurn = new bool[4] { false, false, false, false };

        // 繁荣值系统（数值设计文档v2.1：div=26，回合上限14，v3移植）
        private int currentRound = 1;
        private const int MaxRounds = 14;
        // v2.2：胜利线常量（100→110，胜利更稀有更有含金量）
        private const int WinLine = 110;
        private int currentProsperity = 50; // 初始值
        [SerializeField] private Text prosperityText; // 右下角繁荣值显示
        [SerializeField] private GameObject gameOverPanel; // 游戏结束面板
        [SerializeField] private Text gameOverText; // 游戏结果文字
        [SerializeField] private Button gameOverBackButton; // 游戏结束面板"回到首页"按钮
        [SerializeField] private Button backToMenuButton; // 回首页按钮
        private bool gameEnded = false;

        // v2.2 M套件状态（批次A移植）
        private readonly bool[] surchargeWarned = new bool[4];   // 恶名加征播报去重（每局每人一次）
        private int lastProsperityBroadcastRound = -1;           // 繁荣播报每回合限流（B4）
        private int lastProsperityShown = -1;                    // 状态面板箭头基准
        private readonly bool[] rescueDeniedShown = new bool[4]; // M5：救援拒绝传闻去重
        private Text bottleneckText;                             // 短板行（回合末刷新）

        /// <summary>危机维持费（恶名者翻倍）：声望<45 民意抵制</summary>
        private int CrisisFee(bool infamous) => infamous ? 60 : 30;
        /// <summary>通胀起征点（恶名者更低）：囤现金者在萧条中被通胀吞噬</summary>
        private int InflationThreshold(bool infamous) => infamous ? 450 : 600;

        // ===== 区间阶段引擎（v2.1 Demo定稿：危机<45 / 冲刺≥60，v3移植） =====
        private const int CrisisLine = 45;
        private const int SprintLine = 60;
        public enum GamePhase { Crisis, Normal, Sprint }
        private GamePhase currentPhase = GamePhase.Normal;
        private GamePhase lastBroadcastPhase = GamePhase.Normal; // 防重复播报
        private int lastProsperity = 50;          // 繁荣变化播报用
        private bool tutorialBroadcastShown = false; // 开局教学只播一次
        private bool processingBankruptcy = false;  // 破产处理重入保护

        // 边境封锁：全员单骰N回合（v3移植）
        private int borderClosedRounds = 0;

        // 道路修缮：标记每个玩家是否可以再投一次
        private bool[] rerollNextTurn = new bool[4] { false, false, false, false };
    }
}
