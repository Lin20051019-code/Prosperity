using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 屏幕顶部播报系统 v2：队列 + 优先级（设计见《播报系统设计文档》）。
    /// P0=阈值告警/破产（可打断当前条目），P1=交易/开局教学，P2=常规变化。
    /// 队列上限4条，超出丢弃最低优先级。
    /// </summary>
    public class BroadcastBar : MonoBehaviour
    {
        public const int P0 = 0;  // 阈值告警：立即展示，可打断
        public const int P1 = 1;  // 交易/教学
        public const int P2 = 2;  // 常规

        [SerializeField] private GameObject panel;
        [SerializeField] private Text messageText;
        [SerializeField] private float displayDuration = 2.5f;
        [SerializeField] private float fadeDuration = 0.5f;

        private struct Item { public string msg; public int priority; public float duration; }
        private readonly Queue<Item> queue = new Queue<Item>();
        private CanvasGroup canvasGroup;
        private Coroutine currentCoroutine;
        private bool showing;
        private int lastShownPriority = P2;

        // v2.0 5.3：卷轴播报条样式
        private Image priorityStrip;                        // 左侧优先级竖条（8px）
        private static readonly Color TextInk = new Color(0.165f, 0.122f, 0.078f);   // #2A1F14 深墨
        private static readonly Color TextAlert = new Color(0.549f, 0.118f, 0.071f); // #8C1E12 P0深红
        private static readonly Color StripP0 = new Color(0.651f, 0.118f, 0.098f);   // #A61E19
        private static readonly Color StripP1 = new Color(0.612f, 0.478f, 0.180f);   // #9C7A2E
        private static readonly Color StripP2 = new Color(0.290f, 0.365f, 0.227f);   // #4A5D3A

        private void Start()
        {
            if (panel != null)
            {
                canvasGroup = panel.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = panel.AddComponent<CanvasGroup>();
                panel.SetActive(false);
                EnsurePriorityStrip();
            }
            // 字体走项目安全字体（团结引擎内置字体坑）
            if (messageText != null && messageText.font == null)
                messageText.font = BargainState.GetSafeFont();
        }

        /// <summary>创建左侧优先级竖条（叠在卷轴左端轴杆内侧，8px宽）</summary>
        private void EnsurePriorityStrip()
        {
            if (panel == null) return;
            var existing = panel.transform.Find("PriorityStrip");
            if (existing != null) { priorityStrip = existing.GetComponent<Image>(); return; }
            var go = new GameObject("PriorityStrip", typeof(Image));
            go.transform.SetParent(panel.transform, false);
            priorityStrip = go.GetComponent<Image>();
            priorityStrip.raycastTarget = false;
            priorityStrip.color = StripP2;
            var rt = priorityStrip.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(68f, 0f); // 纸面左缘内侧（纸面约从65px起）
            rt.sizeDelta = new Vector2(8f, 90f);        // 竖条高度覆盖纸面文字带
        }

        /// <summary>按优先级设置竖条与文字颜色（v2.0 5.3）</summary>
        private void ApplyPriorityStyle(int priority)
        {
            if (priorityStrip != null)
                priorityStrip.color = priority == P0 ? StripP0 : priority == P1 ? StripP1 : StripP2;
            if (messageText != null)
                messageText.color = priority == P0 ? TextAlert : TextInk;
        }

        /// <summary>播报一条消息（默认P2常规优先级）</summary>
        public void Broadcast(string message) => Broadcast(message, P2, displayDuration);

        /// <summary>播报一条消息（带优先级与时长）</summary>
        public void Broadcast(string message, int priority, float duration = 2.5f)
        {
            if (panel == null || messageText == null || string.IsNullOrEmpty(message)) return;

            // 队列上限：超出丢弃本条（最低优先级先丢）
            if (queue.Count >= 4)
            {
                if (priority >= P2) return; // 常规消息直接丢弃
                DropLowest();
            }

            if (!showing)
            {
                ShowNow(message, priority, duration);
            }
            else if (priority == P0)
            {
                // v2.0 B1修复：P0告警打断当前条目——被杀的条塞回队首，不再丢消息
                if (currentCoroutine != null) StopCoroutine(currentCoroutine);
                showing = false;
                if (queue.Count > 0)
                {
                    var arr = queue.ToArray();
                    queue.Clear();
                    queue.Enqueue(new Item { msg = messageText.text, priority = lastShownPriority, duration = 2.5f });
                    foreach (var it in arr) queue.Enqueue(it);
                }
                ShowNow(message, priority, duration);
            }
            else
            {
                queue.Enqueue(new Item { msg = message, priority = priority, duration = duration });
            }
        }

        private void DropLowest()
        {
            // v2.0 B2修复：优先丢队列中最后一个P2（常规可弃），没有P2才丢队首
            var arr = queue.ToArray();
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                if (arr[i].priority >= P2)
                {
                    var keep = new System.Collections.Generic.List<Item>();
                    for (int j = 0; j < arr.Length; j++)
                        if (j != i) keep.Add(arr[j]);
                    queue.Clear();
                    foreach (var it in keep) queue.Enqueue(it);
                    return;
                }
            }
            if (queue.Count > 0) queue.Dequeue();
        }

        private void ShowNow(string message, int priority, float duration)
        {
            messageText.text = message;
            lastShownPriority = priority;
            ApplyPriorityStyle(priority); // v2.0 5.3：竖条+文字颜色随优先级
            panel.SetActive(true);
            showing = true;
            currentCoroutine = StartCoroutine(ShowAndFade(priority == P0 ? Mathf.Max(duration, 4f) : duration));
        }

        private IEnumerator ShowAndFade(float duration)
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            yield return new WaitForSeconds(duration);

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                if (canvasGroup != null) canvasGroup.alpha = 1f - elapsed / fadeDuration;
                yield return null;
            }

            if (panel != null) panel.SetActive(false);
            showing = false;

            // 取下一条
            if (queue.Count > 0)
            {
                var next = queue.Dequeue();
                ShowNow(next.msg, next.priority, next.duration);
            }
        }
    }
}
