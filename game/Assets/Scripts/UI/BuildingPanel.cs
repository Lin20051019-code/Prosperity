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
        private bool _closed; // 防连点：面板关闭后忽略后续点击

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
            _closed = false;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
                infoText.text = $"空地（0级）\n市场价格: {marketPrice}元\n购买费用: {BuildingData.BuyCost}元（政府补贴价）";

            if (actionButtonText != null)
                actionButtonText.text = I18n.T("panel_buy_btn", $"购买 ({BuildingData.BuyCost}元)", ("price", BuildingData.BuyCost));
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onBuy;
        }

        /// <summary>公益中心（捐款换声望）</summary>
        public void ShowCharity(int cost, int repGain, System.Action onDonate, System.Action onClose)
        {
            onClosed = onClose;
            _closed = false;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
                infoText.text = I18n.T("panel_charity_info", $"公益中心\n捐款 {cost} 元\n可获得 {repGain} 点声望", ("cost", cost), ("rep", repGain));

            if (actionButtonText != null)
                actionButtonText.text = $"捐款 ({cost}元)";
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onDonate;
        }

        /// <summary>火车站（乘坐火车）</summary>
        public void ShowTrainStation(int cost, System.Action onRide, System.Action onClose)
        {
            onClosed = onClose;
            _closed = false;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
                infoText.text = $"🚂 火车站\n乘坐火车前往另一个火车站\n费用: {cost}元";

            if (actionButtonText != null)
                actionButtonText.text = I18n.T("panel_train_btn", $"乘坐 ({cost}元)", ("price", cost));
            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onRide;
        }

        /// <summary>自己的房产（可升级）</summary>
        public void ShowOwned(BuildingData data, int marketPrice, string ownerName, System.Action onUpgrade, System.Action onClose)
        {
            onClosed = onClose;
            _closed = false;
            if (panel != null) panel.SetActive(true);

            string nextInfo = data.CanUpgrade
                ? $"\n升级费用: {BuildingData.UpgradeCost}元 → {data.level + 1}级"
                : "\n已达最高等级（3级）";

            if (infoText != null)
                infoText.text = $"{data.level}级建筑（{ownerName}）\n市场价格: {marketPrice}元{nextInfo}";

            if (actionButtonText != null)
                actionButtonText.text = data.CanUpgrade ? I18n.T("panel_upgrade_btn", $"升级 ({BuildingData.UpgradeCost}元)", ("price", BuildingData.UpgradeCost)) : I18n.T("panel_maxed", "已满级");
            if (actionButton != null) actionButton.gameObject.SetActive(data.CanUpgrade);
            if (bargainButton != null) bargainButton.gameObject.SetActive(false);
            if (skipButton != null) skipButton.gameObject.SetActive(true);

            _onAction = onUpgrade;
        }

        /// <summary>他人的房产（支付租金或Bargain）</summary>
        public void ShowRented(BuildingData data, int marketPrice, string ownerName, System.Action onPayRent, System.Action onBargain, System.Action onClose)
        {
            onClosed = onClose;
            _closed = false;
            if (panel != null) panel.SetActive(true);

            if (infoText != null)
            {
                int rent = data.GetRent();
                infoText.text = I18n.T("panel_owned_building", $"{data.level}级建筑（{ownerName}）\n市场价格: {marketPrice}元\n租金: {rent}元{(data.level >= 3 ? "（3级翻倍）" : "")}", ("level", data.level), ("owner", ownerName), ("market", marketPrice), ("rent", rent), ("l3note", data.level >= 3 ? I18n.T("panel_l3_note", "（3级翻倍）") : ""));
            }

            if (actionButtonText != null)
                actionButtonText.text = I18n.T("panel_rent_btn", $"支付租金 ({data.GetRent()}元)", ("price", data.GetRent()));
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
            if (_closed) return;
            _closed = true;
            _onAction?.Invoke();
            Close();
        }

        private void OnBargain()
        {
            if (_closed) return;
            _closed = true;
            _onBargain?.Invoke();
            Close();
        }

        private void OnSkip()
        {
            if (_closed) return;
            Close();
        }

        private void Close()
        {
            if (panel != null) panel.SetActive(false);
            _closed = true;
            onClosed?.Invoke();
        }
    }
}
