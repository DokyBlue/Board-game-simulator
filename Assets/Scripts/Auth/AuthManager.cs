using BoardGameSimulator.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoardGameSimulator.Auth
{
    public class AuthManager : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_Text feedbackText;

        [Header("Dependency")]
        [SerializeField] private UserDataStore dataStore;

        [Header("Scene")]
        [SerializeField] private string gameSelectionScene = "GameSelection";

        public void Register()
        {
            if (dataStore.Register(usernameInput.text, passwordInput.text, out var message))
            {
                feedbackText.text = message;
                feedbackText.color = Color.green;
                return;
            }

            feedbackText.text = message;
            feedbackText.color = Color.red;
        }

        public void Login()
        {
            if (dataStore.Login(usernameInput.text, passwordInput.text, out var message))
            {
                SessionContext.CurrentUser = usernameInput.text.Trim();
                SceneManager.LoadScene(gameSelectionScene);
                return;
            }

            feedbackText.text = message;
            feedbackText.color = Color.red;
        }
    }
}
