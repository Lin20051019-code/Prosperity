using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 氛围层（播报与城市状态文档v2.0第四节）：危机=雨幕+冷蓝暗角；冲刺=阳光光束+暖金暗角+光点漂浮；常规=无。
    /// 常驻Canvas子对象（Board之后、面板之前，全部raycastTarget=false防挡点击）；
    /// 档位切换用alpha/颜色渐变（约1s），不做Instantiate/Destroy往返GC。
    /// 雨的实现：GameScene Canvas为ScreenSpaceOverlay，世界空间Rain2D粒子必被Canvas整层遮挡（实测确认），
    /// 故按文档预留降级方案：程序化雨幕贴图（Texture2D像素风短竖线）+ RawImage uvRect滚动（wrapMode=Repeat），
    /// 双层不同速度/粒度叠纵深。素材从Resources/Atmosphere/加载（暗角/阳光光束/冲刺光点，
    /// Texture2D+运行时Sprite.Create，项目expr同款），缺失时程序化兜底，不崩不挡流程。
    /// </summary>
    public class AtmosphereController : MonoBehaviour
    {
        public const float VignetteIntensity = 0.2f; // 文档：初始0.2，试玩范围0.15~0.35（试玩后改此常量定值）

        private enum Mood { None, Crisis, Sprint }

        private static AtmosphereController instance;

        private static readonly Color CrisisTint = new Color(120f / 255f, 140f / 255f, 180f / 255f); // 冷蓝
        private static readonly Color SprintTint = new Color(180f / 255f, 160f / 255f, 120f / 255f); // 暖金

        private Image vignetteImg;
        private Image sunbeamImg;
        private RectTransform sunbeamRt;
        private RawImage rainRawA, rainRawB; // RawImage才有uvRect滚动
        private Image[] spotPool;

        private Mood target = Mood.None;
        private float vignetteA, rainA, beamA;     // 各层当前alpha（渐变量）
        private float breathT, swayT;              // 光束呼吸/摆动相位
        private float scrollA, scrollB;            // 雨幕uv滚动
        private readonly Vector2[] spotPos = new Vector2[SpotCount];
        private readonly Vector2[] spotBaseSize = new Vector2[SpotCount];
        private readonly float[] spotSpeed = new float[SpotCount];
        private readonly float[] spotPhase = new float[SpotCount];
        private const int SpotCount = 10;

        /// <summary>幂等创建：DiceRollController.Start每次进GameScene都调用（存档读取/Bargain往返路径同样覆盖）</summary>
        public static void EnsureCreated()
        {
            if (instance != null) return; // Unity假null：场景卸载后自动失效
            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null) return;
            var go = new GameObject("AtmosphereRoot", typeof(RectTransform), typeof(AtmosphereController));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            // 层级：Board之后、按钮/面板之前（文档：地图 → 氛围 → UI）
            var board = canvas.transform.Find("Board");
            rt.SetSiblingIndex(board != null ? board.GetSiblingIndex() + 1 : 1);
            instance = go.GetComponent<AtmosphereController>();
            instance.Build();
        }

        /// <summary>档位设置（DiceRollController.RefreshPhase换挡点调用）</summary>
        public static void SetPhase(bool crisis, bool sprint)
        {
            if (instance == null) return;
            instance.target = crisis ? Mood.Crisis : (sprint ? Mood.Sprint : Mood.None);
        }

        private void Build()
        {
            var root = GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;

            // 1) 暗角（危机冷蓝/冲刺暖金，tint乘灰度暗角贴图）
            // 素材暗角.png经VLM自检判废（RGB均匀暗蓝黑、径向alpha梯度缺失→叠加无边缘晕影），改用程序化径向暗角
            var vigTex = FallbackVignette();
            vignetteImg = MakeImageLayer("Vignette", root, Sprite.Create(vigTex,
                new Rect(0, 0, vigTex.width, vigTex.height), new Vector2(0.5f, 0.5f), 100f));

            // 2) 阳光光束（冲刺：呼吸+微摆；UI Image无Screen混合材质，透明PNG低alpha叠加近似发光）
            sunbeamImg = MakeImageLayer("Sunbeam", root, LoadSprite("Atmosphere/阳光光束", FallbackSunbeam));
            sunbeamRt = sunbeamImg.rectTransform;

            // 3) 雨（危机：程序化雨幕贴图双层滚动，远景慢细/近景快粗）
            var rainTex = MakeRainTexture();
            rainRawA = MakeRainLayer("RainA", root, rainTex);
            rainRawB = MakeRainLayer("RainB", root, rainTex);

            // 4) 冲刺光点池（漂浮粒子，仅冲刺激活）
            spotPool = new Image[SpotCount];
            var spotSprites = new Sprite[4];
            for (int i = 0; i < 4; i++)
                spotSprites[i] = LoadSprite("Atmosphere/冲刺光点_" + (i + 1), FallbackSpot);
            for (int i = 0; i < SpotCount; i++)
            {
                var spot = MakeImageLayer("Spot" + i, root, spotSprites[Random.Range(0, spotSprites.Length)]);
                // 光点不是全屏层：改锚点为自由定位
                var srt = spot.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
                spotPool[i] = spot;
                RespawnSpot(i, firstSpawn: true);
                spot.gameObject.SetActive(false);
            }

            ApplyAlphas();
        }

        // ==================== 素材 ====================

        /// <summary>加载Texture2D并运行时建Sprite；失败返回兜底贴图（不崩）</summary>
        private static Sprite LoadSprite(string path, System.Func<Texture2D> fallback)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogWarning($"[Atmosphere] 素材缺失: {path}（使用程序化兜底）");
                tex = fallback();
            }
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>程序化雨幕贴图：64x64透明底+白色短竖线（像素风），wrapMode=Repeat供uv滚动</summary>
        private static Texture2D MakeRainTexture()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            var cols = new Color[64 * 64];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.clear;
            for (int n = 0; n < 14; n++)
            {
                int x = Random.Range(0, 64);
                int y0 = Random.Range(0, 64);
                int len = Random.Range(6, 16);
                var c = new Color(0.82f, 0.88f, 1f, 0.55f + Random.Range(0f, 0.25f));
                for (int y = 0; y < len; y++)
                {
                    int yy = (y0 + y) & 63; // 环绕，保证贴图边缘无缝
                    cols[(yy * 64) + x] = c;
                }
            }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        // —— 程序化兜底贴图（素材丢失时不崩，风格统一）——

        private static Texture2D FallbackVignette()
        {
            int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var cols = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x / (float)(s - 1) - 0.5f) * 2f, dy = (y / (float)(s - 1) - 0.5f) * 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy); // 中心0 → 角落约1.41
                    float a = Mathf.Clamp01((d - 0.55f) / 0.85f);
                    cols[y * s + x] = new Color(1f, 1f, 1f, a); // 白色RGB+alpha梯度：让Image.color的冷蓝/暖金tint能乘进颜色（黑RGB会吞掉tint）
                }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D FallbackSunbeam()
        {
            int w = 256, h = 144;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var cols = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 两条斜向亮带（左上→右下），带内alpha渐隐
                    float band1 = Mathf.Exp(-Mathf.Pow(x - y * 1.1f - 20f, 2f) / 900f);
                    float band2 = Mathf.Exp(-Mathf.Pow(x - y * 1.1f - 110f, 2f) / 600f) * 0.7f;
                    float a = Mathf.Clamp01(band1 * 0.5f + band2 * 0.4f) * Mathf.Lerp(1f, 0.35f, y / (float)h);
                    cols[y * w + x] = new Color(1f, 0.92f, 0.72f, a);
                }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        private static Texture2D FallbackSpot()
        {
            int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var cols = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Max(Mathf.Abs(x - 7.5f), Mathf.Abs(y - 7.5f)); // 像素方块光点
                    cols[y * s + x] = d < 4f ? new Color(1f, 0.9f, 0.6f, d < 2f ? 0.9f : 0.45f) : Color.clear;
                }
            tex.SetPixels(cols);
            tex.Apply();
            return tex;
        }

        // ==================== 层构建 ====================

        /// <summary>全屏stretch Image层：raycastTarget=false（氛围层不挡点击），初始透明</summary>
        private static Image MakeImageLayer(string name, RectTransform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        /// <summary>全屏stretch RawImage层（雨）：uvRect滚动需要RawImage</summary>
        private static RawImage MakeRainLayer(string name, RectTransform parent, Texture2D tex)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            raw.uvRect = new Rect(0f, 0f, 16f, 9f);
            raw.color = new Color(1f, 1f, 1f, 0f);
            return raw;
        }

        // ==================== 动画与档位渐变 ====================

        private void Update()
        {
            float tRain = target == Mood.Crisis ? 1f : 0f;
            float tBeam = target == Mood.Sprint ? 1f : 0f;
            float tVig = target == Mood.None ? 0f : VignetteIntensity * 1.5f; // 两态统一0.3：0.2在浅米底/GIF量化下实测不可见，文档范围0.15~0.35内取高值
            const float speed = 1f; // 约1s完成换挡（文档0.5s淡入时序的近似）
            rainA = Mathf.MoveTowards(rainA, tRain, speed * Time.deltaTime);
            beamA = Mathf.MoveTowards(beamA, tBeam, speed * Time.deltaTime);
            vignetteA = Mathf.MoveTowards(vignetteA, tVig, speed * Time.deltaTime * VignetteIntensity);
            ApplyAlphas();

            if (rainA > 0.001f)
            {
                // 雨幕滚动：uv窗口上移=雨向下落（Repeat环绕）；A远景、B近景更快更粗
                scrollA += 0.30f * Time.deltaTime;
                scrollB += 0.55f * Time.deltaTime;
                var rect = GetComponent<RectTransform>().rect;
                SetRainUV(rainRawA, scrollA, rect, 1f);   // 远景：64px粒度
                SetRainUV(rainRawB, scrollB, rect, 0.5f); // 近景：128px粒度（uv窗口减半=贴图放大）
            }

            if (beamA > 0.001f)
            {
                // 光束：alpha呼吸0.75~1.0（周期约6s）+ 缓慢±2°摆动（周期约10s）
                breathT += Time.deltaTime * (Mathf.PI * 2f / 6f);
                swayT += Time.deltaTime * (Mathf.PI * 2f / 10f);
                float breath = 0.75f + 0.25f * (0.5f + 0.5f * Mathf.Sin(breathT));
                sunbeamImg.color = new Color(1f, 1f, 1f, beamA * breath * 0.14f); // 素材右上高亮集中，0.32实测洗白画面；按素材建议8~18%取0.14
                sunbeamRt.localEulerAngles = new Vector3(0, 0, 2f * Mathf.Sin(swayT));
                AnimateSpots();
            }
            else
            {
                sunbeamImg.color = new Color(1f, 1f, 1f, 0f);
                for (int i = 0; i < spotPool.Length; i++)
                    if (spotPool[i] != null && spotPool[i].gameObject.activeSelf)
                        spotPool[i].gameObject.SetActive(false);
            }
        }

        private void ApplyAlphas()
        {
            // 暗角颜色随档位过渡：危机冷蓝 → 冲刺暖金（beamA 0→1 即切换占比）。
            // tint须压暗×0.4：白色贴图×中亮tint=亮色罩（实测洗白画面），压暗后叠加层才是"边缘变暗"的真暗角
            var tint = Color.Lerp(CrisisTint, SprintTint, beamA);
            vignetteImg.color = new Color(tint.r * 0.4f, tint.g * 0.4f, tint.b * 0.4f, vignetteA);
            rainRawA.color = new Color(1f, 1f, 1f, rainA * 0.40f);
            rainRawB.color = new Color(1f, 1f, 1f, rainA * 0.26f);
        }

        private static void SetRainUV(RawImage raw, float scroll, Rect rect, float texScale)
        {
            var uv = raw.uvRect;
            uv.width = Mathf.Max(1f, rect.width / 64f * texScale);
            uv.height = Mathf.Max(1f, rect.height / 64f * texScale);
            uv.y = scroll; // 窗口上移=贴图内容相对向下落（Repeat环绕）
            raw.uvRect = uv;
        }

        // ==================== 光点池（漂浮粒子，仅冲刺） ====================

        private void RespawnSpot(int i, bool firstSpawn)
        {
            var rect = GetComponent<RectTransform>().rect;
            float size = Random.Range(64f, 128f); // 素材与暖米背景同色系，24~64px实测不可见；按素材建议64~128px+高alpha
            spotBaseSize[i] = new Vector2(size, size);
            spotPool[i].rectTransform.sizeDelta = spotBaseSize[i];
            spotPos[i] = new Vector2(
                Random.Range(rect.width * -0.48f, rect.width * 0.48f),
                firstSpawn ? Random.Range(rect.height * -0.5f, rect.height * 0.5f)
                           : rect.height * -0.5f - size);
            spotSpeed[i] = Random.Range(28f, 70f); // 上浮速度
            spotPhase[i] = Random.Range(0f, Mathf.PI * 2f);
            spotPool[i].rectTransform.anchoredPosition = spotPos[i];
        }

        private void AnimateSpots()
        {
            var rect = GetComponent<RectTransform>().rect;
            for (int i = 0; i < spotPool.Length; i++)
            {
                var spot = spotPool[i];
                if (spot == null) continue;
                if (!spot.gameObject.activeSelf) spot.gameObject.SetActive(true);
                spotPos[i].y += spotSpeed[i] * Time.deltaTime;
                spotPos[i].x += Mathf.Sin(spotPhase[i] + Time.time * 0.8f + i) * 6f * Time.deltaTime;
                // 缩放脉冲0.85~1.15 + alpha闪烁
                float pulse = 1f + 0.15f * Mathf.Sin(spotPhase[i] + Time.time * 2.2f);
                spot.rectTransform.sizeDelta = spotBaseSize[i] * pulse;
                float flick = 0.75f + 0.25f * (0.5f + 0.5f * Mathf.Sin(spotPhase[i] + Time.time * 2.6f));
                spot.color = new Color(1f, 1f, 1f, beamA * flick);
                spot.rectTransform.anchoredPosition = spotPos[i];
                if (spotPos[i].y > rect.height * 0.5f + 80f) RespawnSpot(i, false); // 出屏回收
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null; // 场景卸载（Bargain往返）后允许重建
        }
    }
}
