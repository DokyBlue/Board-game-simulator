using TMPro;
using UnityEngine;

namespace BoardGameSimulator.UI
{
    public class ActionPromptView : MonoBehaviour
    {
        [SerializeField] private TMP_Text actionText;
        [SerializeField] private TMP_Text strategyText;
        [TextArea(3, 8)]
        [SerializeField] private string strategyPrefix = "Bot策略面板";

        private string _strategySnapshot = string.Empty;

        public void Show(string message, Color color)
        {
            if (actionText == null)
            {
                return;
            }

            actionText.text = message;
            actionText.color = color;

            if (strategyText == null && !string.IsNullOrWhiteSpace(_strategySnapshot))
            {
                actionText.text = $"{message}\n\n{_strategySnapshot}";
            }
        }

        public void SetStrategySnapshot(string strategyMessage)
        {
            _strategySnapshot = string.IsNullOrWhiteSpace(strategyMessage)
                ? string.Empty
                : $"{strategyPrefix}\n{strategyMessage}";

            if (strategyText != null)
            {
                strategyText.text = _strategySnapshot;
            }
        }
    }
}
