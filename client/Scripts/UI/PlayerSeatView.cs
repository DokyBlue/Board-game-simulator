using BoardGameSimulator.Poker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoardGameSimulator.UI
{
    public class PlayerSeatView : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text chipsText;
        [SerializeField] private TMP_Text actionText;
        [SerializeField] private Image background;
        [SerializeField] private GameObject highlightEffect; // 高亮效果物体（如外边框）

        // 设置高亮的方法
        public void SetHighlight(bool isActive)
        {
            if (highlightEffect != null)
            {
                highlightEffect.SetActive(isActive);
            }
        }

        public void UpdateSeat(PokerPlayer player, string lastAction)
        {
            if (player == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            nameText.text = player.Name;

            // 处理筹码显示
            if (player.IsAllIn)
            {
                chipsText.text = "ALL IN";
                chipsText.color = Color.red;
            }
            else
            {
                chipsText.text = $"筹码: {player.Chips}";
                chipsText.color = Color.white;
            }

            // 处理动作和下注显示
            if (player.IsFolded)
            {
                actionText.text = "已弃牌";
                actionText.color = Color.gray;
                if (background != null) background.color = new Color(0.5f, 0.5f, 0.5f, 0.5f); // 变灰
            }
            else
            {
                actionText.text = $"下注: {player.CurrentBet}\n({lastAction})";
                actionText.color = Color.blue;
                if (background != null) background.color = Color.white; // 恢复正常颜色
            }
        }
    }
}