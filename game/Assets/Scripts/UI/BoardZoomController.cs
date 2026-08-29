using UnityEngine;

namespace SheNicest.UI
{
    /// <summary>
    /// 棋盘拖拽控制器，左键拖拽移动地图位置。
    /// 在DiceRollController控制缩放/移动期间自动禁用拖拽。
    /// </summary>
    public class BoardZoomController : MonoBehaviour
    {
        [SerializeField] private RectTransform board;
        [SerializeField] private DiceRollController diceRollController;

        private Vector2 dragStartMouse;
        private Vector2 dragStartPos;
        private bool isDragging;

        /// <summary>外部可调用：临时禁用拖拽（如玩家移动期间）</summary>
        private bool dragDisabled = false;

        public bool DragDisabled
        {
            get => dragDisabled;
            set => dragDisabled = value;
        }

        private void Update()
        {
            if (board == null) return;

            // 如果DiceRollController正在忙（掷骰子或移动中），不允许拖拽
            if (diceRollController != null && diceRollController.IsBusy)
            {
                isDragging = false;
                return;
            }

            if (dragDisabled) return;

            // 左键拖拽 — 但不能在点击UI按钮时触发
            if (Input.GetMouseButtonDown(0))
            {
                if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                {
                    isDragging = true;
                    dragStartMouse = Input.mousePosition;
                    dragStartPos = board.anchoredPosition;
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
            }

            if (isDragging)
            {
                Vector2 delta = (Vector2)Input.mousePosition - dragStartMouse;
                board.anchoredPosition = dragStartPos + delta;
            }
        }
    }
}
