using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 屏幕顶部播报系统，显示当前操作信息，自动淡出。
    /// </summary>
    public class BroadcastBar : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text messageText;
        [SerializeField] private float displayDuration = 2.5f;
        [SerializeField] private float fadeDuration = 0.5f;

        private CanvasGroup canvasGroup;
        private Coroutine currentCoroutine;

        private void Start()
        {
            if (panel != null)
            {
                canvasGroup = panel.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = panel.AddComponent<CanvasGroup>();
                panel.SetActive(false);
            }
        }

        /// <summary>
        /// 播报一条消息
        /// </summary>
        public void Broadcast(string message)
        {
            if (panel == null || messageText == null) return;

            if (currentCoroutine != null)
                StopCoroutine(currentCoroutine);

            messageText.text = message;
            panel.SetActive(true);

            currentCoroutine = StartCoroutine(ShowAndFade());
        }

        private IEnumerator ShowAndFade()
        {
            // 显示阶段
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            yield return new WaitForSeconds(displayDuration);

            // 淡出阶段
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                if (canvasGroup != null)
                    canvasGroup.alpha = 1f - t;
                yield return null;
            }

            if (panel != null) panel.SetActive(false);
        }
    }
}
