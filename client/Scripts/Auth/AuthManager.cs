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
        //[SerializeField] private TMP_InputField serverAddressInput;
        [SerializeField] private TMP_Text feedbackText;

        [Header("Dependency")]
        [SerializeField] private AuthApiClient authApiClient;

        [Header("Scene")]
        [SerializeField] private string gameSelectionScene = "GameSelection";

        private string serverAddress = "http://8.147.64.150:8080";

        public void Register()
        {
            SessionContext.ServerBaseUrl = serverAddress; // 设置默认服务器地址
            //if (serverAddressInput != null && !string.IsNullOrWhiteSpace(serverAddressInput.text))
            //{
            //    SessionContext.ServerBaseUrl = serverAddressInput.text.Trim().TrimEnd('/');
            //}
            SetFeedback("请求中...", Color.yellow);
            StartCoroutine(authApiClient.Register(usernameInput.text.Trim(), passwordInput.text, HandleAuthResult));
        }

        public void Login()
        {
            SessionContext.ServerBaseUrl = serverAddress; // 设置默认服务器地址
            //// 如果输入框不为空，就更新全局服务器地址（同时去掉两端多余空格和末尾的斜杠）
            //if (serverAddressInput != null && !string.IsNullOrWhiteSpace(serverAddressInput.text))
            //{
            //    SessionContext.ServerBaseUrl = serverAddressInput.text.Trim().TrimEnd('/');
            //}
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
            SessionContext.ClearRoom();
            SceneManager.LoadScene(gameSelectionScene);
        }

        private void SetFeedback(string message, Color color)
        {
            feedbackText.text = message;
            feedbackText.color = color;
        }
    }
}
