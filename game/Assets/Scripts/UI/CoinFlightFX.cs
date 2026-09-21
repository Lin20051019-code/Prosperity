using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 视效v2：金币飞行系统。
    /// 付租/工资/买地/砍价成交时金币从付方弧线飞到收方，金钱终于有实体感。
    /// 贴图：Resources/VFX/coin.png（像素金币）；缺失时静默跳过，不报错不阻塞流程。
    /// 用法：CoinFlightFX.Fly(canvas, fromRect, toRect, amount, onArrive)
    /// </summary>
    public static class CoinFlightFX
    {
        private static Sprite _coin;
        private static FlightHost _host;

        private static Sprite Coin
        {
            get
            {
                if (_coin == null)
                {
                    var tex = Resources.Load<Texture2D>("VFX/coin");
                    if (tex != null)
                        _coin = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                }
                return _coin;
            }
        }

        private class FlightHost : MonoBehaviour { }

        private static FlightHost Host
        {
            get
            {
                if (_host == null)
                {
                    var go = new GameObject("CoinFlightFX_Host");
                    Object.DontDestroyOnLoad(go);
                    _host = go.AddComponent<FlightHost>();
                }
                return _host;
            }
        }

        /// <summary>金额→金币枚数（大额多几枚，3~8封顶，控制画面密度）</summary>
        public static int CoinCount(int amount) => Mathf.Clamp(Mathf.CeilToInt(amount / 150f), 3, 8);

        /// <summary>飞金币。onArrive 在全部金币到达后触发（可用于面板脉冲/数字滚动）。</summary>
        public static void Fly(Canvas canvas, RectTransform from, RectTransform to, int amount, System.Action onArrive = null)
        {
            if (Coin == null || canvas == null || from == null || to == null || amount <= 0)
            {
                onArrive?.Invoke();
                return;
            }
            Host.StartCoroutine(FlyRoutine(canvas, from, to, CoinCount(amount), onArrive));
        }

        private static IEnumerator FlyRoutine(Canvas canvas, RectTransform from, RectTransform to, int count, System.Action onArrive)
        {
            RectTransform uiLayer = canvas.transform as RectTransform;
            Vector2 start, end;
            if (!ToCanvasPoint(canvas, from, out start) || !ToCanvasPoint(canvas, to, out end))
            {
                onArrive?.Invoke();
                yield break;
            }

            // 贝塞尔控制点：中点上抬形成抛物弧
            Vector2 mid = (start + end) * 0.5f;
            mid += new Vector2((end.x - start.x) * 0.08f, 140f + Random.Range(0f, 40f));

            var coins = new List<Image>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("CoinFX", typeof(Image));
                go.transform.SetParent(uiLayer, false);
                var img = go.GetComponent<Image>();
                img.sprite = Coin;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(30, 30);
                img.rectTransform.anchoredPosition = start;
                coins.Add(img);
            }

            for (int i = 0; i < coins.Count; i++)
                Host.StartCoroutine(FlyOne(coins[i], start, end, mid, i * 0.05f));

            yield return new WaitForSeconds(0.45f + (coins.Count - 1) * 0.05f + 0.1f);
            onArrive?.Invoke();
        }

        private static IEnumerator FlyOne(Image coin, Vector2 start, Vector2 end, Vector2 mid, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (coin == null) yield break; // 场景切换（如进Bargain）时画布销毁
            coin.transform.SetAsLastSibling();

            float t = 0f;
            const float dur = 0.45f;
            while (t < dur)
            {
                t += Time.deltaTime;
                if (coin == null) yield break;
                float k = Mathf.Clamp01(t / dur);
                // 二次贝塞尔弧线
                Vector2 a = Vector2.Lerp(start, mid, k);
                Vector2 b = Vector2.Lerp(mid, end, k);
                coin.rectTransform.anchoredPosition = Vector2.Lerp(a, b, k);
                // 宽度余弦压缩模拟翻转自旋
                float spin = Mathf.Abs(Mathf.Cos(k * Mathf.PI * 3f)) * 0.45f + 0.55f;
                coin.rectTransform.localScale = new Vector3(spin, 1f, 1f);
                yield return null;
            }
            if (coin != null) Object.Destroy(coin.gameObject);
        }

        private static bool ToCanvasPoint(Canvas canvas, RectTransform target, out Vector2 local)
        {
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, target.position);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.transform as RectTransform, sp, cam, out local);
        }
    }
}
