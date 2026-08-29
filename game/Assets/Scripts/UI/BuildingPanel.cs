using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SheNicest.UI
{
    /// <summary>
    /// 建筑面板：显示房屋信息，支持购买/升级/支付租金/Bargain。
    /// </summary>
    public class BuildingPanel : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Text infoText;
        [SerializeField] private Button actionButton;        // 购买/升级/支付租金
        [SerializeField] private Text actionButtonText;
        [SerializeField] private Button bargainButton;         // Bargain按钮
        [SerializeField] private Text bargainButtonText;
        [SerializeField] private Button skipButton;

        private System.Action onClosed;
        private System.Action _onAction;
        private System.Action _onBargain;

        private void Start()
        {
            if (actionButton != null)
                actionButton.onClick.AddListener(OnAction);
            if (skipButton != null)
                skipButton.onClick.AddListener(OnSkip);
            if (bargainButton != null)
                bargainButton.onClick.AddListener(OnBargain);
        }

        /// <summary>政府所有（可购买）</summary>
        public void ShowGovernmentOwned(BuildingData data, int marketPrice, System.Action onBuy, System.Action onClose)
        {
            onClosed = onClose;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
                infoText.text = $"空地（0级）\n市场价格: {marketPrice}元\n购买费用: {data.UpgradeCost}元";

            if (actionButtonText != null)
                actionButtonText.text = $"购买 ({data.UpgradeCost}元)";
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onBuy;
        }

        /// <summary>火车站（乘坐火车）</summary>
        public void ShowTrainStation(int cost, System.Action onRide, System.Action onClose)
        {
            onClosed = onClose;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
                infoText.text = $"🚂 火车站\n乘坐火车前往另一个火车站\n费用: {cost}元";

            if (actionButtonText != null)
                actionButtonText.text = $"乘坐 ({cost}元)";
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onRide;
        }

        /// <summary>自己的房产（可升级）</summary>
        public void ShowOwned(BuildingData data, int marketPrice, string ownerName, System.Action onUpgrade, System.Action onClose)
        {
            onClosed = onClose;
            if (panel != null) panel.SetActive(true);

            string nextInfo = data.CanUpgrade
                ? $"\n升级费用: {data.UpgradeCost}元 → {data.level + 1}级"
                : "\n已达最高等级（3级）";

            if (infoText != null)
                infoText.text = $"{data.level}级建筑（{ownerName}）\n市场价格: {marketPrice}元{nextInfo}";

            if (actionButtonText != null)
                actionButtonText.text = data.CanUpgrade ? $"升级 ({data.UpgradeCost}元)" : "已满级";
            if (actionButton != null) actionButton.gameObject.SetActive(data.CanUpgrade);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onUpgrade;
        }

        /// <summary>他人的房产（支付租金或Bargain）</summary>
        public void ShowRented(BuildingData data, int marketPrice, string ownerName, System.Action onPayRent, System.Action onBargain, System.Action onClose)
        {
            onClosed = onClose;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
                infoText.text = $"{data.level}级建筑（{ownerName}）\n市场价格: {marketPrice}元\n租金: {marketPrice / 10}元";

            if (actionButtonText != null)
                actionButtonText.text = $"支付租金 ({marketPrice / 10}元)";
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (bargainButtonText != null)
                bargainButtonText.text = "Bargain";
            if (bargainButton != null) bargainButton.gameObject.SetActive(true);
            if (skipButton != null) skipButton.gameObject.SetActive(false);

            _onAction = onPayRent;
            _onBargain = onBargain;
        }

        private void OnAction()
        {
            _onAction?.Invoke();
            Close();
        }

        private void OnBargain()
        {
            _onBargain?.Invoke();
            Close();
        }

        private void OnSkip()
        {
            Close();
        }

        private void Close()
        {
            if (panel != null) panel.SetActive(false);
            onClosed?.Invoke();
        }
    }
}
