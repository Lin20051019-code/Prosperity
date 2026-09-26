using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 游戏内设置面板：背景音乐/音效音量调整，退出到主菜单。
    /// </summary>
    public class GameSettingsPanel : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button openSettingsButton;
        [SerializeField] private Button closeButton;

        [Header("Sliders")]
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;

        [Header("Buttons")]
        [SerializeField] private Button backToMenuButton;

        [Header("Save System")]
        [SerializeField] private DiceRollController diceRollController;  // 场景接线：取存档数据/判断可存状态
        [SerializeField] private Button saveGameButton;                  // 设置面板上的「保存游戏」
        [SerializeField] private GameObject saveSlotPanel;                // 存档面板（3槽+提示+覆盖确认）
        [SerializeField] private Button savePanelCloseButton;
        [SerializeField] private Button[] slotButtons = new Button[SaveLoadManager.SlotCount];
        [SerializeField] private Text[] slotTexts = new Text[SaveLoadManager.SlotCount];
        [SerializeField] private Text saveHintText;                       // 保存结果/不可存提示
        [SerializeField] private GameObject overwriteConfirmPanel;         // 覆盖已有存档的二次确认框
        [SerializeField] private Text overwriteConfirmText;
        [SerializeField] private Button confirmOverwriteButton;
        [SerializeField] private Button cancelOverwriteButton;

        // 待覆盖确认的槽位（0=无）
        private int pendingOverwriteSlot = 0;

        private void Start()
        {
            if (openSettingsButton != null)
                openSettingsButton.onClick.AddListener(OpenPanel);

            if (closeButton != null)
                closeButton.onClick.AddListener(ClosePanel);

            if (backToMenuButton != null)
                backToMenuButton.onClick.AddListener(OnBackToMenu);

            if (saveGameButton != null)
                saveGameButton.onClick.AddListener(OnSaveGame);

            if (savePanelCloseButton != null)
                savePanelCloseButton.onClick.AddListener(CloseSavePanel);

            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i + 1; // 闭包捕获槽位号
                if (slotButtons[i] != null)
                    slotButtons[i].onClick.AddListener(() => OnSlotClick(slot));
            }

            if (confirmOverwriteButton != null)
                confirmOverwriteButton.onClick.AddListener(OnConfirmOverwrite);

            if (cancelOverwriteButton != null)
                cancelOverwriteButton.onClick.AddListener(OnCancelOverwrite);

            if (saveSlotPanel != null)
                saveSlotPanel.SetActive(false);
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.value = GameSettings.BGMVolume;
                bgmVolumeSlider.onValueChanged.AddListener(OnBGMVolumeChanged);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.value = GameSettings.SFXVolume;
                sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
            }

            CreateLlmToggle();

            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (openSettingsButton != null) openSettingsButton.onClick.RemoveListener(OpenPanel);
            if (closeButton != null) closeButton.onClick.RemoveListener(ClosePanel);
            if (backToMenuButton != null) backToMenuButton.onClick.RemoveListener(OnBackToMenu);
            if (saveGameButton != null) saveGameButton.onClick.RemoveListener(OnSaveGame);
            if (savePanelCloseButton != null) savePanelCloseButton.onClick.RemoveListener(CloseSavePanel);
            // 槽位按钮用lambda绑定，只能整组清除（本组件为唯一接线方，无持久化调用）
            for (int i = 0; i < slotButtons.Length; i++)
            {
                if (slotButtons[i] != null) slotButtons[i].onClick.RemoveAllListeners();
            }
            if (confirmOverwriteButton != null) confirmOverwriteButton.onClick.RemoveListener(OnConfirmOverwrite);
            if (cancelOverwriteButton != null) cancelOverwriteButton.onClick.RemoveListener(OnCancelOverwrite);
            if (bgmVolumeSlider != null) bgmVolumeSlider.onValueChanged.RemoveListener(OnBGMVolumeChanged);
            if (sfxVolumeSlider != null) sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXVolumeChanged);
            if (llmToggleButton != null) llmToggleButton.onClick.RemoveListener(OnLlmToggle);
        }

        private void OpenPanel()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(true);
        }

        private void ClosePanel()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
            CloseSavePanel();
            GameSettings.Save();
        }

        private void OnBGMVolumeChanged(float value)
        {
            GameSettings.BGMVolume = value;
            AudioManager.Instance?.SetBGMVolume(value);
        }

        private void OnSFXVolumeChanged(float value)
        {
            GameSettings.SFXVolume = value;
            AudioManager.Instance?.SetSFXVolume(value);
        }

        private void OnBackToMenu()
        {
            GameSettings.Save();
            AudioManager.Instance?.PlayMainMenuBGM();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        // ==================== 存档系统（保存到3个槽位，覆盖已有存档需二次确认） ====================

        /// <summary>当前是否允许保存（仅玩家回合空闲时，见DiceRollController.CanSaveNow）。</summary>
        private bool CanSave => diceRollController != null && diceRollController.CanSaveNow;

        private void OnSaveGame()
        {
            // 面板总是弹出：不可存时槽位置灰并在面板内显示原因。
            // （此前提示文字藏在未打开的面板里，AI回合点保存毫无反馈=玩家以为按钮失灵）
            bool can = CanSave;
            RefreshSaveSlots();
            SetSlotsInteractable(can);
            ShowHint(can ? "" : "回合进行中，暂时无法保存");
            pendingOverwriteSlot = 0;
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
            if (saveSlotPanel != null)
                saveSlotPanel.SetActive(true);
        }

        private bool lastSlotsInteractable = true;

        /// <summary>统一设置3个槽位按钮可点状态（记录状态供Update侦测忙碌↔空闲切换）。</summary>
        private void SetSlotsInteractable(bool value)
        {
            for (int i = 0; i < slotButtons.Length; i++)
            {
                if (slotButtons[i] != null)
                    slotButtons[i].interactable = value;
            }
            lastSlotsInteractable = value;
        }

        private void Update()
        {
            // 存档面板打开期间，忙碌↔玩家回合空闲切换时自动同步槽位可点状态（免关面板重开）
            if (saveSlotPanel == null || !saveSlotPanel.activeSelf) return;
            if (lastSlotsInteractable && !CanSave)
            {
                SetSlotsInteractable(false);
                ShowHint("回合进行中，暂时无法保存");
                if (overwriteConfirmPanel != null)
                    overwriteConfirmPanel.SetActive(false);
                pendingOverwriteSlot = 0;
            }
            else if (!lastSlotsInteractable && CanSave)
            {
                SetSlotsInteractable(true);
                ShowHint("");
            }
        }

        private void CloseSavePanel()
        {
            if (saveSlotPanel != null)
                saveSlotPanel.SetActive(false);
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
            pendingOverwriteSlot = 0;
        }

        /// <summary>刷新3个槽位的显示文字（空槽显示"空"，有档显示回合+时间）。</summary>
        private void RefreshSaveSlots()
        {
            for (int i = 0; i < slotTexts.Length; i++)
            {
                if (slotTexts[i] != null)
                    slotTexts[i].text = $"存档{i + 1}：{SaveLoadManager.GetSlotDisplay(i + 1)}";
            }
        }

        private void OnSlotClick(int slot)
        {
            if (!CanSave)
            {
                ShowHint("回合进行中，暂时无法保存");
                return;
            }
            if (SaveLoadManager.HasSave(slot))
            {
                // 覆盖已有存档：先弹二次确认
                pendingOverwriteSlot = slot;
                if (overwriteConfirmText != null)
                    overwriteConfirmText.text = $"存档{slot}已有进度，确定覆盖？";
                if (overwriteConfirmPanel != null)
                    overwriteConfirmPanel.SetActive(true);
                return;
            }
            DoSave(slot);
        }

        private void OnConfirmOverwrite()
        {
            if (pendingOverwriteSlot > 0)
                DoSave(pendingOverwriteSlot);
        }

        private void OnCancelOverwrite()
        {
            pendingOverwriteSlot = 0;
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
        }

        private void DoSave(int slot)
        {
            if (diceRollController == null) return;
            SaveLoadManager.SaveToSlot(slot, diceRollController.CaptureToSaveData());
            pendingOverwriteSlot = 0;
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
            RefreshSaveSlots();
            ShowHint($"已保存到存档{slot}");
        }

        private void ShowHint(string message)
        {
            if (saveHintText != null)
                saveHintText.text = message;
        }

        // ==================== LLM台词试点：AI台词开关（运行时创建按钮，场景零改动）====================

        private Button llmToggleButton;
        private Text llmToggleText;

        /// <summary>设置面板内动态创建「AI台词」开关（WebGL不支持LLM，不创建）。</summary>
        private void CreateLlmToggle()
        {
            if (settingsPanel == null) return;
            if (Application.platform == RuntimePlatform.WebGLPlayer) return;

            var btnObj = new GameObject("LlmDialogToggle", typeof(Image), typeof(Button));
            btnObj.transform.SetParent(settingsPanel.transform, false);
            var rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -330f);
            rt.sizeDelta = new Vector2(240f, 60f);

            // #7E4A30 棕底白字，与面板内其它按钮同款
            var img = btnObj.GetComponent<Image>();
            img.color = new Color(126f / 255f, 74f / 255f, 48f / 255f);

            var txtObj = new GameObject("Text");
            txtObj.transform.SetParent(btnObj.transform, false);
            llmToggleText = txtObj.AddComponent<Text>();
            llmToggleText.font = BargainState.GetSafeFont();
            llmToggleText.fontSize = 28;
            llmToggleText.alignment = TextAnchor.MiddleCenter;
            llmToggleText.color = Color.white;
            var trt = llmToggleText.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            llmToggleButton = btnObj.GetComponent<Button>();
            llmToggleButton.onClick.AddListener(OnLlmToggle);
            RefreshLlmToggleLabel();
        }

        private void OnLlmToggle()
        {
            GameSettings.LlmDialogsEnabled = !GameSettings.LlmDialogsEnabled;
            GameSettings.Save();
            RefreshLlmToggleLabel();
        }

        private void RefreshLlmToggleLabel()
        {
            if (llmToggleText != null)
                llmToggleText.text = GameSettings.LlmDialogsEnabled ? "AI台词：开" : "AI台词：关";
        }
    }
}
