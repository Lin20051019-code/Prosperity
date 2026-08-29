using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 背景滚动：下降一段时间后上升，来回往复，方向切换时缓动过渡。
    /// </summary>
    public class BargainBackgroundScroller : MonoBehaviour
    {
        [SerializeField] private RectTransform backgroundImage;
        [SerializeField] private float scrollSpeed = 50f;
        [SerializeField] private float scrollRange = 400f;
        [SerializeField] private float easeDuration = 0.8f; // 方向切换时的减速/加速时间（秒）

        private float startY;
        private float currentSpeed;
        private int targetDirection = -1; // -1=向下, 1=向上

        private void Start()
        {
            if (backgroundImage != null)
            {
                startY = backgroundImage.anchoredPosition.y;
                currentSpeed = scrollSpeed; // 初始全速向下
            }
        }

        private void Update()
        {
            if (backgroundImage == null) return;

            float pos = backgroundImage.anchoredPosition.y;

            // 到达底部，切换为上升
            if (pos <= startY - scrollRange)
                targetDirection = 1;
            // 到达顶部，切换为下降
            else if (pos >= startY + scrollRange)
                targetDirection = -1;

            // 缓动过渡当前速度到目标速度
            float targetSpeed = scrollSpeed * targetDirection;
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime / easeDuration);

            backgroundImage.anchoredPosition += new Vector2(0, currentSpeed * Time.deltaTime);
        }
    }
}
