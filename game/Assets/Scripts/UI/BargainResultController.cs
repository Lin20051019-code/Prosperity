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
                if (BargainState.resultSuccess)
                    resultText.text = $"交易成功！\n成交价: {BargainState.resultFinalPrice}元";
                else
                    resultText.text = "交易失败\n将支付租金";
            }
        }

        private void ConfirmResult()
        {
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
