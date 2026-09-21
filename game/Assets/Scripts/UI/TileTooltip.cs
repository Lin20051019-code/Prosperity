using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SheNicest.UI
{
    /// <summary>
    /// 格子悬停提示：鼠标移到格子上显示格子属性小窗口。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TileTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private DiceRollController diceRollController;
        
        private static GameObject tooltipPanel;
        private static Text tooltipText;

        public void OnPointerEnter(PointerEventData eventData)
        {
            ShowTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideTooltip();
        }

        private void ShowTooltip()
        {
            if (diceRollController == null) return;

            var tile = GetComponent<RectTransform>();
            var img = GetComponent<Image>();
            if (img == null || img.sprite == null) return;

            string spriteName = img.sprite.name;
            string info = GetTileInfo(spriteName);

            if (string.IsNullOrEmpty(info)) return;

            EnsureTooltipExists();

            if (tooltipText != null)
                tooltipText.text = info;

            if (tooltipPanel != null)
            {
                tooltipPanel.SetActive(true);
                // Position near the tile
                var tooltipRt = tooltipPanel.GetComponent<RectTransform>();
                Vector2 tilePos = tile.position;
                tooltipRt.position = new Vector3(tilePos.x + 80, tilePos.y + 40, 0);
            }
        }

        private void HideTooltip()
        {
            if (tooltipPanel != null)
                tooltipPanel.SetActive(false);
        }

        private string GetTileInfo(string spriteName)
        {
            switch (spriteName)
            {
                case "房屋块":
                    // Find this tile's index and building data
                    var tile = GetComponent<RectTransform>();
                    var board = tile.parent;
                    int tileIndex = -1;
                    for (int i = 0; i < board.childCount; i++)
                    {
                        if (board.GetChild(i) == tile) { tileIndex = i; break; }
                    }
                    
                    if (diceRollController != null && tileIndex >= 0)
                    {
                        int prosperity = diceRollController.CurrentProsperity;
                        var bd = diceRollController.GetBuildingData(tileIndex);
                        if (bd != null)
                        {
                            int marketPrice = bd.GetMarketPrice(prosperity);
                            string owner = bd.IsGovernmentOwned ? I18n.T("tile_gov_owned", "政府所有") :
                                bd.HasOwner ? I18n.T("tile_owner_player", $"玩家{bd.ownerIndex}", ("n", bd.ownerIndex)) : I18n.T("tile_unowned", "无主");
                            return I18n.T("tile_building_info", $"建筑格 (Lv.{bd.level})\n所有者: {owner}\n市场价格: {marketPrice}元", ("level", bd.level), ("owner", owner), ("market", marketPrice));
                        }
                    }
                    return I18n.T("tile_building", "建筑格");
                
                case "惩罚块":
                    return I18n.T("tile_penalty", "惩罚格\n触发惩罚事件");
                case "奖励块":
                    return I18n.T("tile_reward", "奖励格\n获得随机奖励");
                case "事件块":
                    return I18n.T("tile_event", "事件格\n触发全局事件");
                case "基金会":
                    return I18n.T("tile_foundation", "公益中心\n可花费400获得12声誉");
                case "火车块(1)":
                    return I18n.T("tile_train", "火车站\n花费50元传送到另一火车站");
                case "起点":
                    return I18n.T("tile_start", "起点\n经过获得200元工资");
                default:
                    return null;
            }
        }

        private void EnsureTooltipExists()
        {
            if (tooltipPanel != null) return;

            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            Sprite bgSprite = null;
            Font font = null;
            // WebGL 注意: AssetDatabase 仅编辑器可用，改用 Resources.Load 保证打包后可用
            bgSprite = Resources.Load<Sprite>("文本框");
            font = Resources.Load<Font>("VonwaonBitmap-16px");

            tooltipPanel = new GameObject("TileTooltip", typeof(RectTransform), typeof(Image));
            tooltipPanel.transform.SetParent(canvas.transform, false);
            var rt = tooltipPanel.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(280, 110);
            rt.pivot = new Vector2(0, 1);
            var img = tooltipPanel.GetComponent<Image>();
            if (bgSprite != null)
            {
                img.sprite = bgSprite;
                img.type = Image.Type.Sliced;
            }
            img.color = Color.white;
            img.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(tooltipPanel.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(30, 15);
            textRt.offsetMax = new Vector2(-30, -15);
            tooltipText = textGo.GetComponent<Text>();
            tooltipText.fontSize = 16;
            tooltipText.alignment = TextAnchor.MiddleLeft;
            tooltipText.color = new Color(0.35f, 0.16f, 0.06f); // 深棕色
            tooltipText.lineSpacing = 1.2f;
            if (font != null) tooltipText.font = font;
            tooltipText.raycastTarget = false;

            tooltipPanel.SetActive(false);
        }

        /// <summary>为所有格子添加悬停提示组件</summary>
        public static void SetupTooltips(DiceRollController controller)
        {
            if (controller == null) return;
            var board = GameObject.Find("Canvas/Board");
            if (board == null) return;

            for (int i = 0; i < board.transform.childCount; i++)
            {
                var tile = board.transform.GetChild(i);
                if (!tile.name.StartsWith("Tile_")) continue;

                var tooltip = tile.GetComponent<TileTooltip>();
                if (tooltip == null)
                    tooltip = tile.gameObject.AddComponent<TileTooltip>();
                
                // Set private field via reflection
                var field = typeof(TileTooltip).GetField("diceRollController", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                    field.SetValue(tooltip, controller);
            }
        }
    }
}
