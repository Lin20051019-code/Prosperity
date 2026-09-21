using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 主菜单控制器，管理开始游戏、游戏设置、制作人员和退出游戏按钮。
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button creditsButton;
        [SerializeField] private Button exitGameButton;

        [Header("Panel References")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject creditsPanel;
        [SerializeField] private Button panelClickCatcher;

        [Header("Start Sub Panel")]
        [SerializeField] private GameObject startPanel;      // 「开始游戏」子面板（新游戏/继续游戏）
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button continueGameButton;  // 无任何存档时置灰
        [SerializeField] private Button startPanelCloseButton;

        [Header("Load Save Panel")]
        [SerializeField] private GameObject loadSavePanel;   // 「继续游戏」存档选择框
        [SerializeField] private Button loadSaveCloseButton;
        [SerializeField] private Button[] loadSlotButtons = new Button[SaveLoadManager.SlotCount];
        [SerializeField] private Text[] loadSlotTexts = new Text[SaveLoadManager.SlotCount];

        [Header("Scene Names")]
        [SerializeField] private string gameSceneName = "CharacterSelectScene";

        [Header("Settings Controls")]
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private Dropdown resolutionDropdown;

        private void Start()
        {
            // v4.2 i18n：首启按系统语言自动检测（主菜单有手动切换按钮）
            if (Application.systemLanguage == SystemLanguage.English)
                BargainState.language = "en";

            AudioManager.Instance?.PlayMainMenuBGM();
            BindButtons();
            CloseAllPanels();
            InitSettings();
            CreateLanguageToggle();

            // 继续游戏：无任何存档时置灰不可点
            if (continueGameButton != null)
                continueGameButton.interactable = SaveLoadManager.HasAnySave();
        }

        /// <summary>v4.2 i18n：右上角语言切换按钮（EN/中文）</summary>
        private void CreateLanguageToggle()
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            var btnObj = new GameObject("LanguageToggle", typeof(Image), typeof(Button));
            btnObj.transform.SetParent(canvas.transform, false);
            var img = btnObj.GetComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
            var rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-24, -24);
            rt.sizeDelta = new Vector2(150, 44);

            var txtObj = new GameObject("LangText", typeof(Text));
            txtObj.transform.SetParent(btnObj.transform, false);
            var txt = txtObj.AddComponent<Text>();
            txt.font = BargainState.GetSafeFont();
            txt.fontSize = 22;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.rectTransform.sizeDelta = new Vector2(150, 44);
            txt.text = BargainState.language == "en" ? "中文" : "EN";

            btnObj.GetComponent<Button>().onClick.AddListener(() =>
            {
                BargainState.language = BargainState.language == "en" ? "zh" : "en";
                I18n.Reset();
                txt.text = BargainState.language == "en" ? "中文" : "EN";
            });
        }

        private void BindButtons()
        {
            if (startGameButton != null)
                startGameButton.onClick.AddListener(OnStartGame);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(OnSettings);

            if (creditsButton != null)
                creditsButton.onClick.AddListener(OnCredits);

            if (exitGameButton != null)
                exitGameButton.onClick.AddListener(OnExitGame);

            if (panelClickCatcher != null)
                panelClickCatcher.onClick.AddListener(CloseAllPanels);

            if (newGameButton != null)
                newGameButton.onClick.AddListener(OnNewGame);

            if (continueGameButton != null)
                continueGameButton.onClick.AddListener(OnContinueGame);

            if (startPanelCloseButton != null)
                startPanelCloseButton.onClick.AddListener(CloseAllPanels);

            if (loadSaveCloseButton != null)
                loadSaveCloseButton.onClick.AddListener(CloseAllPanels);

            for (int i = 0; i < loadSlotButtons.Length; i++)
            {
                int slot = i + 1; // 闭包捕获槽位号
                if (loadSlotButtons[i] != null)
                    loadSlotButtons[i].onClick.AddListener(() => OnLoadSlotClick(slot));
            }
        }

        private void OnDestroy()
        {
            if (startGameButton != null)
                startGameButton.onClick.RemoveListener(OnStartGame);

            if (settingsButton != null)
                settingsButton.onClick.RemoveListener(OnSettings);

            if (creditsButton != null)
                creditsButton.onClick.RemoveListener(OnCredits);

            if (exitGameButton != null)
                exitGameButton.onClick.RemoveListener(OnExitGame);

            if (bgmVolumeSlider != null)
                bgmVolumeSlider.onValueChanged.RemoveListener(OnBGMVolumeChanged);

            if (sfxVolumeSlider != null)
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXVolumeChanged);

            if (resolutionDropdown != null)
                resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);

            if (panelClickCatcher != null)
                panelClickCatcher.onClick.RemoveListener(CloseAllPanels);

            if (newGameButton != null)
                newGameButton.onClick.RemoveListener(OnNewGame);

            if (continueGameButton != null)
                continueGameButton.onClick.RemoveListener(OnContinueGame);

            if (startPanelCloseButton != null)
                startPanelCloseButton.onClick.RemoveListener(CloseAllPanels);

            if (loadSaveCloseButton != null)
                loadSaveCloseButton.onClick.RemoveListener(CloseAllPanels);

            // 槽位按钮用lambda绑定，只能整组清除（本组件为唯一接线方，无持久化调用）
            for (int i = 0; i < loadSlotButtons.Length; i++)
            {
                if (loadSlotButtons[i] != null)
                    loadSlotButtons[i].onClick.RemoveAllListeners();
            }
        }

        private void OnStartGame()
        {
            // 不再直接进游戏：弹出「新游戏/继续游戏」子选项面板
            CloseAllPanels();
            if (startPanel != null)
                startPanel.SetActive(true);
            if (panelClickCatcher != null)
                panelClickCatcher.gameObject.SetActive(true);
        }

        private void OnNewGame()
        {
            SaveLoadManager.pendingLoadSlot = SaveLoadManager.NoSlot;
            LoadGameScene();
        }

        private void OnContinueGame()
        {
            if (!SaveLoadManager.HasAnySave()) return; // 双保险（Start已置灰）
            CloseAllPanels();
            RefreshLoadSlots();
            if (loadSavePanel != null)
                loadSavePanel.SetActive(true);
            if (panelClickCatcher != null)
                panelClickCatcher.gameObject.SetActive(true);
        }

        private void OnLoadSlotClick(int slot)
        {
            if (!SaveLoadManager.HasSave(slot)) return; // 空槽不可点
            SaveLoadManager.pendingLoadSlot = slot;
            LoadGameScene();
        }

        private void LoadGameScene()
        {
            if (!string.IsNullOrEmpty(gameSceneName) && Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                SceneManager.LoadScene(gameSceneName);
            }
            else
            {
                Debug.LogWarning($"[MainMenu] Game scene '{gameSceneName}' not found or not in Build Settings.");
            }
        }

        /// <summary>刷新「继续游戏」存档框：3个槽位的文字与可点状态（空槽置灰）。</summary>
        private void RefreshLoadSlots()
        {
            for (int i = 0; i < loadSlotTexts.Length; i++)
            {
                int slot = i + 1;
                bool hasSave = SaveLoadManager.HasSave(slot);
                if (loadSlotTexts[i] != null)
                    loadSlotTexts[i].text = $"存档{slot}：{SaveLoadManager.GetSlotDisplay(slot)}";
                if (loadSlotButtons[i] != null)
                    loadSlotButtons[i].interactable = hasSave;
            }
        }

        private void OnSettings()
        {
            CloseAllPanels();
            if (settingsPanel != null)
                settingsPanel.SetActive(true);
            if (panelClickCatcher != null)
                panelClickCatcher.gameObject.SetActive(true);
        }

        private void OnCredits()
        {
            CloseAllPanels();
            if (creditsPanel != null)
                creditsPanel.SetActive(true);
            if (panelClickCatcher != null)
                panelClickCatcher.gameObject.SetActive(true);
        }

        private void OnExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// 关闭所有弹出面板（供面板上的"返回"按钮调用）。
        /// </summary>
        public void CloseAllPanels()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (creditsPanel != null)
                creditsPanel.SetActive(false);

            if (startPanel != null)
                startPanel.SetActive(false);

            if (loadSavePanel != null)
                loadSavePanel.SetActive(false);

            if (panelClickCatcher != null)
                panelClickCatcher.gameObject.SetActive(false);
        }

        private void InitSettings()
        {
            // 初始化滑条值并绑定回调
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

            // 初始化分辨率下拉菜单
            if (resolutionDropdown != null)
            {
                resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            }

            // 应用已保存的设置
            GameSettings.ApplyAll();
        }

        private void OnBGMVolumeChanged(float value)
        {
            GameSettings.BGMVolume = value;
            GameSettings.Save();
            AudioManager.Instance?.SetBGMVolume(value);
        }

        private void OnSFXVolumeChanged(float value)
        {
            GameSettings.SFXVolume = value;
            GameSettings.Save();
            AudioManager.Instance?.SetSFXVolume(value);
        }

        private void OnResolutionChanged(int index)
        {
            GameSettings.ApplyResolution(index);
            GameSettings.Save();
        }
    }
}
