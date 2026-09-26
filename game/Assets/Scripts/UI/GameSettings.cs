using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 游戏设置管理器，使用 PlayerPrefs 持久化保存音量和分辨率设置。
    /// </summary>
    public static class GameSettings
    {
        private const string KeyBGMVolume = "BGMVolume";
        private const string KeySFXVolume = "SFXVolume";
        private const string KeyResolutionIndex = "ResolutionIndex";

        private const float DefaultBGMVolume = 1f;
        private const float DefaultSFXVolume = 1f;
        private const int DefaultResolutionIndex = -1; // -1 表示使用默认（最后一个）

        /// <summary>背景音乐音量 (0~1)。</summary>
        public static float BGMVolume
        {
            get => PlayerPrefs.GetFloat(KeyBGMVolume, DefaultBGMVolume);
            set => PlayerPrefs.SetFloat(KeyBGMVolume, Mathf.Clamp01(value));
        }

        /// <summary>游戏音效音量 (0~1)。</summary>
        public static float SFXVolume
        {
            get => PlayerPrefs.GetFloat(KeySFXVolume, DefaultSFXVolume);
            set => PlayerPrefs.SetFloat(KeySFXVolume, Mathf.Clamp01(value));
        }

        private const string KeyLlmDialogs = "LlmDialogsEnabled";

        /// <summary>讨价还价AI台词是否用LLM生成（默认关；关闭/失败时回落CSV台词池）。</summary>
        public static bool LlmDialogsEnabled
        {
            get => PlayerPrefs.GetInt(KeyLlmDialogs, 0) == 1;
            set => PlayerPrefs.SetInt(KeyLlmDialogs, value ? 1 : 0);
        }

        /// <summary>分辨率索引（对应 Screen.resolutions 数组）。</summary>
        public static int ResolutionIndex
        {
            get => PlayerPrefs.GetInt(KeyResolutionIndex, DefaultResolutionIndex);
            set => PlayerPrefs.SetInt(KeyResolutionIndex, value);
        }

        /// <summary>保存所有设置到 PlayerPrefs。</summary>
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        /// <summary>应用分辨率设置（主菜单下拉选择）。</summary>
        public static void ApplyResolution(int index)
        {
            // WebGL: 画布尺寸由浏览器窗口控制，SetResolution 会强制改写 canvas CSS 尺寸
            // 导致画面缩进左下角、输入坐标错位（点击失灵），因此完全跳过。
            if (Application.platform == RuntimePlatform.WebGLPlayer) return;

            var resolutions = Screen.resolutions;
            if (resolutions == null || resolutions.Length == 0)
            {
                // 列表为空（部分笔记本外接屏）：回退当前显示器原生分辨率，不用硬编码16:10
                var def = GetDefaultResolution();
                Screen.SetResolution(def.width, def.height, Screen.fullScreen);
                return;
            }

            if (index < 0 || index >= resolutions.Length)
                index = resolutions.Length - 1;

            var res = resolutions[index];
            Screen.SetResolution(res.width, res.height, Screen.fullScreen);
            ResolutionIndex = index;
        }

        private const int DefaultWidth = 2560;
        private const int DefaultHeight = 1600;

        /// <summary>选取默认分辨率：当前显示器原生桌面分辨率（Display.main，DPI感知=物理像素）。
        /// 不再从 Screen.resolutions 列表挑选——部分笔记本外接屏（如RTX 4060笔记本+2K屏）该列表为空，
        /// 旧逻辑回退到硬编码 16:10 的 2560×1600，在 16:9 屏上被驱动缩放=整幅发糊+左右UI被黑边裁切（2026-09-18实测）。</summary>
        private static Resolution GetDefaultResolution()
        {
            var res = new Resolution();
            // 首选：当前显示器原生桌面分辨率
            var main = Display.main;
            if (main != null && main.systemWidth > 0 && main.systemHeight > 0)
            {
                res.width = main.systemWidth;
                res.height = main.systemHeight;
                return res;
            }
            // 次选：当前实际渲染分辨率
            if (Screen.currentResolution.width > 0 && Screen.currentResolution.height > 0)
                return Screen.currentResolution;
            // 最后兜底：16:9 的 1080p，绝不发明 16:10 模式
            res.width = 1920;
            res.height = 1080;
            return res;
        }

        /// <summary>应用所有设置（在游戏启动时调用）。默认分辨率：当前显示器原生桌面分辨率。</summary>
        public static void ApplyAll()
        {
            // WebGL: 同上，禁止任何分辨率控制
            if (Application.platform == RuntimePlatform.WebGLPlayer) return;

            int idx = ResolutionIndex;
            if (idx < 0)
            {
                // 无保存设置：使用当前显示器原生桌面分辨率，任何比例的屏幕都点对点满屏
                var res = GetDefaultResolution();
                Screen.SetResolution(res.width, res.height, Screen.fullScreen);
            }
            else
            {
                // 用户在下拉菜单选过的分辨率（列表来自显示器支持的模式）；列表失效时回退原生
                var resolutions = Screen.resolutions;
                if (resolutions != null && resolutions.Length > 0 && idx < resolutions.Length)
                {
                    var res = resolutions[idx];
                    Screen.SetResolution(res.width, res.height, Screen.fullScreen);
                }
                else
                {
                    var res = GetDefaultResolution();
                    Screen.SetResolution(res.width, res.height, Screen.fullScreen);
                }
            }
        }
    }
}
