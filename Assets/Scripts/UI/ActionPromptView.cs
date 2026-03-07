using TMPro;
using UnityEngine;

namespace BoardGameSimulator.UI
{
    public class ActionPromptView : MonoBehaviour
    {
        [SerializeField] private TMP_Text actionText;

        public void Show(string message, Color color)
        {
            actionText.text = message;
            actionText.color = color;
        }
    }
}
