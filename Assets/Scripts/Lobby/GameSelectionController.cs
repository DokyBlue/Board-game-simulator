using BoardGameSimulator.Core;
using BoardGameSimulator.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoardGameSimulator.Lobby
{
    public class GameSelectionController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text welcomeText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_InputField joinRoomCodeInput;

        [Header("API")]
        [SerializeField] private LobbyApiClient lobbyApiClient;

        [Header("Scene")]
        [SerializeField] private string texasHoldemScene = "TexasHoldem";
        [SerializeField] private string loginScene = "Login";

        private const string TexasHoldemGameKey = "texas_holdem";

        private void Start()
        {
            if (!SessionContext.IsLoggedIn)
            {
                SceneManager.LoadScene(loginScene);
                return;
            }

            welcomeText.text = $"欢迎，{SessionContext.CurrentUser}";
            SetStatus("请选择德州扑克并创建或加入房间", Color.white);
        }

        public void EnterTexasHoldem()
        {
            SceneManager.LoadScene(texasHoldemScene);
        }

        public void CreateTexasHoldemRoom()
        {
            if (lobbyApiClient == null)
            {
                SetStatus("LobbyApiClient 未绑定", Color.red);
                return;
            }

            SetStatus("正在创建房间...", Color.yellow);
            StartCoroutine(lobbyApiClient.CreateRoom(TexasHoldemGameKey, SessionContext.AccessToken, HandleCreateRoom));
        }

        public void JoinTexasHoldemRoom()
        {
            if (lobbyApiClient == null)
            {
                SetStatus("LobbyApiClient 未绑定", Color.red);
                return;
            }

            var roomCode = joinRoomCodeInput == null ? string.Empty : joinRoomCodeInput.text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                SetStatus("请输入房间号", Color.red);
                return;
            }

            SetStatus("正在加入房间...", Color.yellow);
            StartCoroutine(lobbyApiClient.JoinRoom(roomCode, SessionContext.AccessToken, HandleJoinRoom));
        }

        private void HandleCreateRoom(LobbyApiResult result)
        {
            if (!result.Success)
            {
                SetStatus(result.Message, Color.red);
                return;
            }

            SetStatus($"房间已创建：{result.Response.room.code}", Color.green);
            SceneManager.LoadScene(texasHoldemScene);
        }

        private void HandleJoinRoom(LobbyApiResult result)
        {
            if (!result.Success)
            {
                SetStatus(result.Message, Color.red);
                return;
            }

            SetStatus($"已加入房间：{result.Response.room.code}", Color.green);
            SceneManager.LoadScene(texasHoldemScene);
        }

        private void SetStatus(string message, Color color)
        {
            if (statusText == null)
            {
                return;
            }

            statusText.text = message;
            statusText.color = color;
        }
    }
}
