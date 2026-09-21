using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SheNicest.UI
{
    /// <summary>
    /// 结果场景控制器：显示交易结果，确认后设置BargainResult并卸载场景。
    /// </summary>
    public class BargainResultController : MonoBehaviour
    {
        [Header("Background")]
        [SerializeField] private BargainBackgroundScroller backgroundScroller;

        [Header("Result UI")]
        [SerializeField] private Text resultText;
        [SerializeField] private Button confirmResultButton;

        private void Start()
        {
            if (confirmResultButton != null)
                confirmResultButton.onClick.AddListener(ConfirmResult);

            if (resultText != null)
            {
                string tileName = DiceRollController.BargainData.tileName;
                if (string.IsNullOrEmpty(tileName)) tileName = I18n.T("ui_property", "房产");

                string role = BargainState.isAIVsAI
                    ? ""
                    : (BargainState.isPlayerBuyer ? I18n.T("ui_role_buyer", "（你是买方）") : I18n.T("ui_role_seller", "（你是卖方）"));

                // v3移植：收尾台词（成交/失败的现场感），无台词时退回纯结果文案
                string quote = string.IsNullOrEmpty(BargainState.resultLine) ? "" : $"「{BargainState.resultLine}」\n";

                if (BargainState.resultSuccess)
                    resultText.text = I18n.T("result_success_line", $"{quote}交易成功！\n{BargainState.buyerName} 以 {BargainState.resultFinalPrice} 元\n购得 {tileName}（原属 {BargainState.sellerName}）{role}", ("quote", quote), ("buyer", BargainState.buyerName), ("price", BargainState.resultFinalPrice), ("tile", tileName), ("seller", BargainState.sellerName), ("role", role));
                else
                    resultText.text = I18n.T("result_fail_line", $"{quote}交易失败\n{BargainState.buyerName} 需向 {BargainState.sellerName}\n支付 {tileName} 的租金{role}", ("quote", quote), ("buyer", BargainState.buyerName), ("seller", BargainState.sellerName), ("tile", tileName), ("role", role));
            }
        }

        private bool confirmed; // 防连点：防止重复设置结果/重复加载GameScene

        private void ConfirmResult()
        {
            if (confirmed) return;
            confirmed = true;
            if (confirmResultButton != null) confirmResultButton.interactable = false;

            // 设置BargainResult供DiceRollController读取
            BargainController.BargainResult.isSuccess = BargainState.resultSuccess;
            BargainController.BargainResult.finalPrice = BargainState.resultFinalPrice;
            BargainController.BargainResult.completed = true;

            // 完全切回GameScene
            AudioManager.Instance?.PlayGameSceneBGM();
            SceneManager.LoadScene("GameScene");
        }
    }
}
