using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 像素锐利渲染：动态字体图集重建后强制 Point 采样。
    /// 项目字体 VonwaonBitmap 为像素位图字体，UGUI Text 的字形图集默认 Bilinear 采样，
    /// 在非 1.0 缩放的屏幕（如 2560×1440 上的 1.333 倍 Canvas 缩放）上字形边缘被插值发糊；
    /// Point 采样让像素字形保持锐利。配合贴图导入的 Point 过滤一并解决 2K/4K 屏发糊问题（2026-09-18）。
    /// </summary>
    public static class PixelFontSharpener
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            // 用 -=/+= 防重复注册（域重载后静态类重新初始化，事件在引擎侧持久）
            Font.textureRebuilt -= OnFontTextureRebuilt;
            Font.textureRebuilt += OnFontTextureRebuilt;
            // 进程内已存在的字体图集也立即处理一次
            OnFontTextureRebuilt(null);
        }

        private static void OnFontTextureRebuilt(Font font)
        {
            // font==null 时处理当前所有动态字体；否则只处理重建的那个
            if (font != null)
            {
                SetPoint(font);
                return;
            }
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var f in fonts)
                SetPoint(f);
        }

        private static void SetPoint(Font font)
        {
            var tex = font?.material?.mainTexture;
            if (tex != null)
                tex.filterMode = FilterMode.Point;
        }
    }
}
