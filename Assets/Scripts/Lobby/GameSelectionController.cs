using BoardGameSimulator.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoardGameSimulator.Lobby
{
    public class GameSelectionController : MonoBehaviour
    {
        [SerializeField] private TMP_Text welcomeText;
        [SerializeField] private string texasHoldemScene = "TexasHoldem";

        private void Start()
        {
            welcomeText.text = $"欢迎，{SessionContext.CurrentUser}";
        }

        public void EnterTexasHoldem()
        {
            SceneManager.LoadScene(texasHoldemScene);
        }
    }
}
