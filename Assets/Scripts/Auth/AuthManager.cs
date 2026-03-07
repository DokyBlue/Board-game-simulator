using BoardGameSimulator.Core;
using BoardGameSimulator.Networking;
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
        [SerializeField] private AuthApiClient authApiClient;

        [Header("Scene")]
        [SerializeField] private string gameSelectionScene = "GameSelection";

        public void Register()
        {
            SetFeedback("请求中...", Color.yellow);
            StartCoroutine(authApiClient.Register(usernameInput.text.Trim(), passwordInput.text, HandleAuthResult));
        }

        public void Login()
        {
            SetFeedback("请求中...", Color.yellow);
            StartCoroutine(authApiClient.Login(usernameInput.text.Trim(), passwordInput.text, HandleAuthResult));
        }

        private void HandleAuthResult(AuthApiResult result)
        {
            if (!result.Success)
            {
                SetFeedback(result.Message, Color.red);
                return;
            }

            SessionContext.UserId = result.Response.user.id;
            SessionContext.CurrentUser = result.Response.user.username;
            SessionContext.AccessToken = result.Response.token;
            SceneManager.LoadScene(gameSelectionScene);
        }

        private void SetFeedback(string message, Color color)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }
    }
}
