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

        [Header("Scene Names")]
        [SerializeField] private string gameSceneName = "CharacterSelectScene";

        [Header("Settings Controls")]
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private Dropdown resolutionDropdown;

        private void Start()
        {
            AudioManager.Instance?.PlayMainMenuBGM();
            BindButtons();
            CloseAllPanels();
            InitSettings();
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
        }

        private void OnStartGame()
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
