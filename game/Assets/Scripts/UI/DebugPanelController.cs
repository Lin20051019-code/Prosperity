using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 调试面板：同时按住 A+S+D 呼出，在设置按钮上方出现「调试面板」按钮。
    /// 功能：1.调整四名角色现金/声望 2.立即触发惩罚/事件/奖励卡 3.选择卖方/买方直接进入砍价。
    /// 全部UI运行时创建（场景零改动）；由 DiceRollController.Start 创建、随场景切换销毁；
    /// 热键状态用静态标记保留，砍价场景往返后按钮仍可见，无需重按。
    /// 所有操作仅在玩家回合空闲时可用（与存档同判定），AI回合/掷骰中自动置灰并提示。
    /// </summary>
    public class DebugPanelController : MonoBehaviour
    {
        // ---------- 依赖 ----------
        private DiceRollController _dice;
        private PenaltyCardPanel _cardPanel;
        private Canvas _canvas;

        // ---------- 热键 ----------
        private static bool sHotkeyOn = false;   // 跨场景保留（退出Play/重启进程自然清零）
        private bool _comboWasHeld = false;      // 边沿检测：按住期间只切换一次

        // ---------- 主面板 ----------
        private GameObject _hotkeyButton;
        private GameObject _mainPanel;
        private readonly InputField[] _cashInputs = new InputField[4];
        private readonly InputField[] _repInputs = new InputField[4];
        private readonly Text[] _rowNameTexts = new Text[4];
        private Text _mainHintText;
        private readonly List<Button> _idleGatedButtons = new List<Button>(); // 忙碌时置灰的主面板动作按钮
        private bool _lastIdle = true;

        // ---------- 卡牌子面板 ----------
        private GameObject _cardSubPanel;
        private Text _cardTitleText;
        private GameObject _cardTargetRow;
        private readonly List<Button> _cardTargetButtons = new List<Button>();
        private Transform _cardListRoot;
        private int _cardTarget = 0;
        private PenaltyCardPanel.CardType _cardKind;

        // ---------- 砍价子面板 ----------
        private GameObject _bargainSubPanel;
        private readonly List<Button> _sellerButtons = new List<Button>();
        private readonly List<Button> _buyerButtons = new List<Button>();
        private Transform _propListRoot;
        private Text _propHeaderText;
        private Text _bargainHintText;
        private Button _startBargainButton;
        private int _sellerIdx = -1;
        private int _buyerIdx = -1;
        private int _propTileIdx = -1;
        private bool _bargainValid = false;

        // ---------- 样式 ----------
        private static readonly Color BtnBrown = new Color(126f / 255f, 74f / 255f, 48f / 255f);  // #7E4A30 与设置按钮同款
        private static readonly Color OptionOff = new Color(84f / 255f, 50f / 255f, 30f / 255f);   // 选项未选中
        private static readonly Color OptionOn = new Color(178f / 255f, 116f / 255f, 74f / 255f);  // 选项选中
        private static readonly Color PanelBg = new Color(0.09f, 0.09f, 0.12f, 0.96f);
        private static readonly Color InputBg = new Color(0.16f, 0.16f, 0.19f);
        private static readonly Color HintColor = new Color(0.95f, 0.85f, 0.4f);
        private const int MaxPropRows = 8; // 房产列表最多显示条数

        // ==================== 创建与入口 ====================

        /// <summary>由 DiceRollController.Start 调用：创建（或复用）调试控制器并注入依赖。</summary>
        public static DebugPanelController EnsureCreated(DiceRollController dice, PenaltyCardPanel cardPanel)
        {
            var existing = FindObjectOfType<DebugPanelController>();
            if (existing != null)
            {
                existing.Initialize(dice, cardPanel);
                return existing;
            }

            var go = new GameObject("DebugPanelController");
            var ctrl = go.AddComponent<DebugPanelController>();
            ctrl.Initialize(dice, cardPanel);
            return ctrl;
        }

        /// <summary>构建全部UI（重复调用仅更新依赖引用）。</summary>
        public void Initialize(DiceRollController dice, PenaltyCardPanel cardPanel)
        {
            _dice = dice;
            _cardPanel = cardPanel;

            if (_canvas != null) return; // UI已建，仅刷新依赖

            _canvas = dice != null ? dice.GetComponentInParent<Canvas>() : null;
            if (_canvas == null) _canvas = FindObjectOfType<Canvas>();
            if (_canvas == null) return;

            BuildHotkeyButton();
            BuildMainPanel();
            BuildCardSubPanel();
            BuildBargainSubPanel();

            // 热键状态跨场景保留：砍价往返GameScene重建后，之前按过A+S+D则按钮直接可见
            _hotkeyButton.SetActive(sHotkeyOn);

            _lastIdle = IsIdle;
            ApplyIdleToButtons();
        }

        private bool IsIdle => _dice != null && _dice.DebugIsIdle;

        private void Update()
        {
            // 热键：A/S/D 三键同按切换（输入框聚焦时不响应，防打字误触）
            bool inputFocused = EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            bool held = !inputFocused
                && Input.GetKey(KeyCode.A) && Input.GetKey(KeyCode.S) && Input.GetKey(KeyCode.D);
            if (held && !_comboWasHeld) ToggleHotkey();
            _comboWasHeld = held;

            // 空闲状态同步：面板打开期间忙碌→置灰、空闲→恢复（与存档面板同款体验）
            SyncIdleState();
        }

        /// <summary>切换调试按钮可见性（public：允许程序化/测试调用）。</summary>
        public void ToggleHotkey()
        {
            sHotkeyOn = !sHotkeyOn;
            if (_hotkeyButton != null) _hotkeyButton.SetActive(sHotkeyOn);
            if (!sHotkeyOn) CloseAllPanels();
            Debug.Log($"[DebugPanel] hotkey -> {(sHotkeyOn ? "ON" : "OFF")}");
        }

        // ==================== 热键按钮 ====================

        private void BuildHotkeyButton()
        {
            var btn = MakeButton(_canvas.transform, "调试面板", new Vector2(30f, 100f), new Vector2(180f, 60f), 28);
            var rt = btn.transform as RectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero; // 左下角对齐：设置按钮(30,30)正上方
            _hotkeyButton = btn.gameObject;
            _hotkeyButton.SetActive(false);
            btn.onClick.AddListener(OpenMainPanel);
        }

        // ==================== 主面板 ====================

        private void BuildMainPanel()
        {
            _mainPanel = MakePanel("DebugMainPanel", new Vector2(820f, 760f));
            var t = _mainPanel.transform;

            MakeText(t, "调试面板", new Vector2(0, 340), new Vector2(400, 50), 36, Color.white, TextAnchor.MiddleCenter);
            MakeText(t, "1. 数值调整", new Vector2(0, 292), new Vector2(400, 40), 30, Color.white, TextAnchor.MiddleCenter);
            MakeText(t, "现金", new Vector2(-90, 256), new Vector2(200, 32), 24, Color.gray, TextAnchor.MiddleCenter);
            MakeText(t, "声望", new Vector2(150, 256), new Vector2(200, 32), 24, Color.gray, TextAnchor.MiddleCenter);

            float[] rowY = { 218f, 164f, 110f, 56f };
            for (int i = 0; i < 4; i++)
            {
                _rowNameTexts[i] = MakeText(t, "玩家" + i, new Vector2(-300, rowY[i]), new Vector2(180, 44), 24, Color.white, TextAnchor.MiddleCenter);
                _cashInputs[i] = MakeInput(t, new Vector2(-90, rowY[i]), new Vector2(200, 44));
                _repInputs[i] = MakeInput(t, new Vector2(150, rowY[i]), new Vector2(200, 44));
            }

            var applyBtn = MakeButton(t, "应用数值", new Vector2(0, 6), new Vector2(240, 52), 26);
            applyBtn.onClick.AddListener(OnApplyValues);
            _idleGatedButtons.Add(applyBtn);

            MakeText(t, "2. 触发卡牌", new Vector2(0, -46), new Vector2(400, 40), 30, Color.white, TextAnchor.MiddleCenter);
            var penaltyBtn = MakeButton(t, "惩罚", new Vector2(-250, -100), new Vector2(220, 50), 26);
            penaltyBtn.onClick.AddListener(() => OpenCardSubPanel(PenaltyCardPanel.CardType.Penalty));
            var eventBtn = MakeButton(t, "事件", new Vector2(0, -100), new Vector2(220, 50), 26);
            eventBtn.onClick.AddListener(() => OpenCardSubPanel(PenaltyCardPanel.CardType.Event));
            var rewardBtn = MakeButton(t, "奖励", new Vector2(250, -100), new Vector2(220, 50), 26);
            rewardBtn.onClick.AddListener(() => OpenCardSubPanel(PenaltyCardPanel.CardType.Reward));
            _idleGatedButtons.Add(penaltyBtn);
            _idleGatedButtons.Add(eventBtn);
            _idleGatedButtons.Add(rewardBtn);

            MakeText(t, "3. 直接砍价", new Vector2(0, -152), new Vector2(400, 40), 30, Color.white, TextAnchor.MiddleCenter);
            var bargainEntryBtn = MakeButton(t, "选择卖方买方开始砍价", new Vector2(0, -202), new Vector2(480, 52), 26);
            bargainEntryBtn.onClick.AddListener(OpenBargainSubPanel);
            _idleGatedButtons.Add(bargainEntryBtn);

            _mainHintText = MakeText(t, "", new Vector2(0, -300), new Vector2(740, 70), 24, HintColor, TextAnchor.MiddleCenter);

            var closeBtn = MakeButton(t, "关闭", new Vector2(0, -350), new Vector2(200, 50), 26);
            closeBtn.onClick.AddListener(CloseAllPanels);
        }

        private void OpenMainPanel()
        {
            if (_mainPanel == null) return;
            _mainPanel.SetActive(true);
            _mainPanel.transform.SetAsLastSibling();
            if (_cardSubPanel != null) _cardSubPanel.SetActive(false);
            if (_bargainSubPanel != null) _bargainSubPanel.SetActive(false);
            RefreshValueInputs();
            _lastIdle = IsIdle;
            ApplyIdleToButtons();
            ShowMainHint(_lastIdle ? "" : "回合进行中，暂时无法使用调试功能");
        }

        private void CloseAllPanels()
        {
            if (_mainPanel != null) _mainPanel.SetActive(false);
            if (_cardSubPanel != null) _cardSubPanel.SetActive(false);
            if (_bargainSubPanel != null) _bargainSubPanel.SetActive(false);
        }

        private void RefreshValueInputs()
        {
            if (_dice == null) return;
            for (int i = 0; i < 4; i++)
            {
                var pd = _dice.DebugGetPlayerData(i);
                if (pd == null) continue;
                _rowNameTexts[i].text = pd.playerName;
                if (!_cashInputs[i].isFocused) _cashInputs[i].text = pd.cash.ToString();
                if (!_repInputs[i].isFocused) _repInputs[i].text = pd.reputation.ToString();
            }
        }

        private void OnApplyValues()
        {
            if (!GuardIdle()) return;

            var cash = new int[4];
            var rep = new int[4];
            for (int i = 0; i < 4; i++)
            {
                var pd = _dice.DebugGetPlayerData(i);
                if (pd == null) { cash[i] = 0; rep[i] = 0; continue; }
                cash[i] = int.TryParse(_cashInputs[i].text, out var c) ? c : pd.cash;
                rep[i] = int.TryParse(_repInputs[i].text, out var r) ? r : pd.reputation;
            }

            _dice.DebugSetPlayerValues(cash, rep);
            RefreshValueInputs();
            ShowMainHint("数值已应用（声望自动限制在0~100）");
        }

        private void ShowMainHint(string msg)
        {
            if (_mainHintText != null) _mainHintText.text = msg;
        }

        // ==================== 卡牌子面板 ====================

        private void BuildCardSubPanel()
        {
            _cardSubPanel = MakePanel("DebugCardPanel", new Vector2(900f, 920f));
            var t = _cardSubPanel.transform;

            _cardTitleText = MakeText(t, "触发卡牌", new Vector2(0, 415), new Vector2(840, 44), 30, Color.white, TextAnchor.MiddleCenter);

            // 作用对象行（事件卡为全局效果，整行隐藏）
            _cardTargetRow = new GameObject("CardTargetRow", typeof(RectTransform));
            _cardTargetRow.transform.SetParent(t, false);
            var rowRt = _cardTargetRow.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(0, 316);
            rowRt.sizeDelta = new Vector2(900, 46);

            MakeText(_cardTargetRow.transform, "作用对象", new Vector2(-330, 0), new Vector2(120, 44), 24, Color.gray, TextAnchor.MiddleCenter);
            float[] x = { -195f, -65f, 65f, 195f };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = MakeButton(_cardTargetRow.transform, "Target_" + i, new Vector2(x[i], 0), new Vector2(130, 46), 22);
                b.onClick.AddListener(() => SelectCardTarget(idx));
                _cardTargetButtons.Add(b);
            }

            _cardListRoot = new GameObject("CardList", typeof(RectTransform)).transform;
            _cardListRoot.SetParent(t, false);
            var listRt = _cardListRoot.GetComponent<RectTransform>();
            listRt.anchorMin = listRt.anchorMax = listRt.pivot = new Vector2(0.5f, 0.5f);
            listRt.anchoredPosition = Vector2.zero;
            listRt.sizeDelta = new Vector2(900, 920);

            var backBtn = MakeButton(t, "返回", new Vector2(0, -390), new Vector2(200, 50), 26);
            backBtn.onClick.AddListener(() => _cardSubPanel.SetActive(false));
        }

        private void OpenCardSubPanel(PenaltyCardPanel.CardType kind)
        {
            if (!GuardIdle()) return;
            _cardKind = kind;
            _cardSubPanel.SetActive(true);
            _cardSubPanel.transform.SetAsLastSibling();

            switch (kind)
            {
                case PenaltyCardPanel.CardType.Penalty:
                    _cardTitleText.text = "触发惩罚（选择作用对象，点击卡名立即生效）";
                    _cardTargetRow.SetActive(true);
                    break;
                case PenaltyCardPanel.CardType.Event:
                    _cardTitleText.text = "触发事件（全局效果，点击卡名立即生效）";
                    _cardTargetRow.SetActive(false);
                    break;
                default:
                    _cardTitleText.text = "触发奖励（选择作用对象，点击卡名立即生效）";
                    _cardTargetRow.SetActive(true);
                    break;
            }

            RefreshCardTargetButtons();
            RebuildCardList();
        }

        private void RefreshCardTargetButtons()
        {
            for (int i = 0; i < _cardTargetButtons.Count; i++)
            {
                var pd = _dice != null ? _dice.DebugGetPlayerData(i) : null;
                var label = pd != null ? pd.playerName : "玩家" + i;
                SetOptionColor(_cardTargetButtons[i], i == _cardTarget);
                SetButtonLabel(_cardTargetButtons[i], label);
            }
        }

        private void SelectCardTarget(int idx)
        {
            _cardTarget = idx;
            RefreshCardTargetButtons();
        }

        /// <summary>重建卡名列表（按逻辑键去重——CSV重复行只是随机抽卡的权重）。</summary>
        private void RebuildCardList()
        {
            for (int i = _cardListRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(_cardListRoot.GetChild(i).gameObject); // 立即销毁：同帧内重复打开时旧项不残留

            List<PenaltyEventData> pool = _cardKind switch
            {
                PenaltyCardPanel.CardType.Penalty => _cardPanel != null ? _cardPanel.PenaltyPool : null,
                PenaltyCardPanel.CardType.Event => _cardPanel != null ? _cardPanel.EventPool : null,
                _ => _cardPanel != null ? _cardPanel.rewardPool : null,
            };
            if (pool == null || pool.Count == 0) return;

            var seen = new HashSet<string>();
            float y = 252f;
            foreach (var evt in pool)
            {
                if (evt == null) continue;
                if (!seen.Add(evt.EventKey)) continue;

                var captured = evt;
                var btn = MakeButton(_cardListRoot, "Card_" + evt.EventKey, new Vector2(0, y), new Vector2(800, 50), 24);
                SetButtonLabel(btn, $"{evt.eventName}（{evt.effectDescription}）");
                btn.onClick.AddListener(() => OnCardClicked(captured));
                y -= 56f;
            }
        }

        private void OnCardClicked(PenaltyEventData evt)
        {
            if (!GuardIdle()) return;
            _dice.DebugApplyCard(_cardKind, evt, _cardTarget);
            RefreshValueInputs(); // 数值可能已被卡牌效果改变
        }

        // ==================== 砍价子面板 ====================

        private void BuildBargainSubPanel()
        {
            _bargainSubPanel = MakePanel("DebugBargainPanel", new Vector2(940f, 880f));
            var t = _bargainSubPanel.transform;

            MakeText(t, "直接砍价（选择卖方与买方）", new Vector2(0, 395), new Vector2(800, 48), 32, Color.white, TextAnchor.MiddleCenter);

            float[] bx = { -210f, -70f, 70f, 210f };
            MakeText(t, "卖方", new Vector2(-350, 330), new Vector2(100, 44), 26, Color.gray, TextAnchor.MiddleCenter);
            MakeText(t, "买方", new Vector2(-350, 270), new Vector2(100, 44), 26, Color.gray, TextAnchor.MiddleCenter);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var sb = MakeButton(t, "Seller_" + i, new Vector2(bx[i], 330), new Vector2(130, 46), 22);
                sb.onClick.AddListener(() => SelectSeller(idx));
                _sellerButtons.Add(sb);
                var bb = MakeButton(t, "Buyer_" + i, new Vector2(bx[i], 270), new Vector2(130, 46), 22);
                bb.onClick.AddListener(() => SelectBuyer(idx));
                _buyerButtons.Add(bb);
            }

            _propHeaderText = MakeText(t, "选择卖方房产：", new Vector2(0, 215), new Vector2(700, 36), 26, Color.white, TextAnchor.MiddleLeft);
            _propListRoot = new GameObject("PropList", typeof(RectTransform)).transform;
            _propListRoot.SetParent(t, false);
            var listRt = _propListRoot.GetComponent<RectTransform>();
            listRt.anchorMin = listRt.anchorMax = listRt.pivot = new Vector2(0.5f, 0.5f);
            listRt.anchoredPosition = Vector2.zero;
            listRt.sizeDelta = new Vector2(940, 880);

            _bargainHintText = MakeText(t, "", new Vector2(0, -185), new Vector2(860, 40), 24, HintColor, TextAnchor.MiddleCenter);

            _startBargainButton = MakeButton(t, "开始砍价", new Vector2(0, -245), new Vector2(260, 54), 28);
            _startBargainButton.onClick.AddListener(OnStartBargain);
            _startBargainButton.interactable = false;

            var backBtn = MakeButton(t, "返回", new Vector2(0, -320), new Vector2(200, 50), 26);
            backBtn.onClick.AddListener(() => _bargainSubPanel.SetActive(false));
        }

        private void OpenBargainSubPanel()
        {
            if (!GuardIdle()) return;
            _bargainSubPanel.SetActive(true);
            _bargainSubPanel.transform.SetAsLastSibling();

            if (_sellerIdx < 0) _sellerIdx = 0;  // 默认：我卖
            if (_buyerIdx < 0) _buyerIdx = 1;    // 默认：AI1买
            _propTileIdx = -1;

            RefreshSellerBuyerButtons();
            RebuildPropertyList();
            ValidateBargainSelection();
        }

        private void RefreshSellerBuyerButtons()
        {
            for (int i = 0; i < 4; i++)
            {
                var pd = _dice != null ? _dice.DebugGetPlayerData(i) : null;
                string label = pd != null ? pd.playerName : "玩家" + i;
                bool alive = pd == null || pd.alive;

                SetOptionColor(_sellerButtons[i], i == _sellerIdx);
                SetOptionColor(_buyerButtons[i], i == _buyerIdx);
                SetButtonLabel(_sellerButtons[i], alive ? label : label + "(出局)");
                SetButtonLabel(_buyerButtons[i], alive ? label : label + "(出局)");
                _sellerButtons[i].interactable = alive;  // 出局者不可选
                _buyerButtons[i].interactable = alive;
            }
        }

        private void SelectSeller(int idx)
        {
            _sellerIdx = idx;
            _propTileIdx = -1;
            RefreshSellerBuyerButtons();
            RebuildPropertyList();
            ValidateBargainSelection();
        }

        private void SelectBuyer(int idx)
        {
            _buyerIdx = idx;
            RefreshSellerBuyerButtons();
            ValidateBargainSelection();
        }

        private void RebuildPropertyList()
        {
            for (int i = _propListRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(_propListRoot.GetChild(i).gameObject); // 立即销毁：同帧内重复刷新时旧项不残留

            var props = _dice != null ? _dice.DebugGetOwnedTiles(_sellerIdx) : null;
            int count = props != null ? props.Count : 0;
            bool truncated = count > MaxPropRows;
            string shownCount = truncated ? $"共{count}处，仅显示前{MaxPropRows}处" : $"共{count}处";
            _propHeaderText.text = count == 0
                ? "选择卖方房产：（该角色名下没有房产）"
                : $"选择卖方房产：{shownCount}";

            float y = 160f;
            int shown = 0;
            foreach (var p in props)
            {
                if (shown >= MaxPropRows) break;
                int tileIdx = p.tileIndex;
                var btn = MakeButton(_propListRoot, "Prop_" + p.tileIndex, new Vector2(0, y), new Vector2(700, 48), 22);
                SetButtonLabel(btn, $"{p.tileName}（{p.level}级 · 市价{p.marketPrice}元）");
                btn.onClick.AddListener(() => SelectProperty(tileIdx));
                y -= 56f;
                shown++;
            }
        }

        private void SelectProperty(int tileIdx)
        {
            _propTileIdx = tileIdx;
            for (int i = 0; i < _propListRoot.childCount; i++)
            {
                var b = _propListRoot.GetChild(i).GetComponent<Button>();
                if (b == null) continue;
                int idx;
                SetOptionColor(b, int.TryParse(b.name.Substring("Prop_".Length), out idx) && idx == tileIdx);
            }
            ValidateBargainSelection();
        }

        private void ValidateBargainSelection()
        {
            string hint;
            var sellerData = _dice != null && _sellerIdx >= 0 ? _dice.DebugGetPlayerData(_sellerIdx) : null;
            var buyerData = _dice != null && _buyerIdx >= 0 ? _dice.DebugGetPlayerData(_buyerIdx) : null;

            if (_sellerIdx < 0 || _buyerIdx < 0)
                hint = "请选择卖方与买方";
            else if (_sellerIdx == _buyerIdx)
                hint = "卖方与买方不能是同一个人";
            else if (sellerData != null && _dice.DebugGetOwnedTiles(_sellerIdx).Count == 0)
                hint = $"{sellerData.playerName} 名下没有房产，无法砍价";
            else if (_propTileIdx < 0)
                hint = "请选择要交易的房产";
            else
                hint = "";

            _bargainValid = string.IsNullOrEmpty(hint);
            _bargainHintText.text = hint;
            _startBargainButton.interactable = _bargainValid;
        }

        private void OnStartBargain()
        {
            if (!GuardIdle()) return;
            if (!_bargainValid)
            {
                ValidateBargainSelection();
                return;
            }

            if (_dice.DebugStartBargain(_propTileIdx, _buyerIdx))
            {
                CloseAllPanels(); // 场景即将切换，收起全部面板
            }
            else
            {
                _bargainHintText.text = "当前状态无法开始砍价（回合进行中）";
                _startBargainButton.interactable = false;
            }
        }

        // ==================== 忙碌状态同步 ====================

        private void SyncIdleState()
        {
            bool idle = IsIdle;
            if (idle == _lastIdle) return;
            _lastIdle = idle;
            ApplyIdleToButtons();
            if (_mainPanel != null && _mainPanel.activeSelf)
                ShowMainHint(idle ? "" : "回合进行中，暂时无法使用调试功能");
        }

        private void ApplyIdleToButtons()
        {
            foreach (var b in _idleGatedButtons)
                if (b != null) b.interactable = _lastIdle;
        }

        private bool GuardIdle()
        {
            if (IsIdle) return true;
            ShowMainHint("回合进行中，暂时无法使用调试功能");
            Debug.Log("[DebugPanel] blocked: game busy");
            return false;
        }

        // ==================== UI构建辅助 ====================

        private GameObject MakePanel(string name, Vector2 size)
        {
            var obj = new GameObject(name, typeof(Image));
            obj.transform.SetParent(_canvas.transform, false);
            var rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            obj.GetComponent<Image>().color = PanelBg;
            obj.SetActive(false);
            return obj;
        }

        private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, int fontSize)
        {
            var obj = new GameObject(label, typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            var rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            obj.GetComponent<Image>().color = BtnBrown;

            var txt = MakeText(obj.transform, label, Vector2.zero, size, fontSize, Color.white, TextAnchor.MiddleCenter);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 0f);
            trt.offsetMax = new Vector2(-6f, 0f);

            return obj.GetComponent<Button>();
        }

        private Text MakeText(Transform parent, string content, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align)
        {
            var obj = new GameObject("Text");
            obj.transform.SetParent(parent, false);
            var txt = obj.AddComponent<Text>();
            txt.font = BargainState.GetSafeFont();
            txt.text = content;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.raycastTarget = false;
            var rt = txt.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return txt;
        }

        private InputField MakeInput(Transform parent, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject("Input", typeof(Image));
            obj.transform.SetParent(parent, false);
            var rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            obj.GetComponent<Image>().color = InputBg;

            var input = obj.AddComponent<InputField>();
            input.contentType = InputField.ContentType.IntegerNumber; // 只允许整数（含负号）
            input.caretColor = Color.white;

            var txt = MakeText(obj.transform, "", Vector2.zero, size, 26, Color.white, TextAnchor.MiddleCenter);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 2f);
            trt.offsetMax = new Vector2(-10f, -2f);
            input.textComponent = txt;

            var ph = MakeText(obj.transform, "0", Vector2.zero, size, 22, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter);
            var prt = ph.rectTransform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(10f, 2f);
            prt.offsetMax = new Vector2(-10f, -2f);
            input.placeholder = ph;

            return input;
        }

        private void SetOptionColor(Button b, bool selected)
        {
            var img = b != null ? b.GetComponent<Image>() : null;
            if (img != null) img.color = selected ? OptionOn : OptionOff;
        }

        private void SetButtonLabel(Button b, string label)
        {
            var txt = b != null ? b.GetComponentInChildren<Text>() : null;
            if (txt != null) txt.text = label;
        }
    }
}
