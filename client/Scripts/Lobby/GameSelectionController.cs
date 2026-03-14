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
        [SerializeField] private GameObject texasHoldemModePanel;

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
            SetTexasModePanel(false);
            SetStatus("请选择游戏", Color.white);
        }

        public void EnterTexasHoldem()
        {
            SetTexasModePanel(true);
            SetStatus("请选择单机模式、创建房间或加入房间", Color.white);
        }

        public void StartTexasHoldemOffline()
        {
            SessionContext.ClearRoom();
            SceneManager.LoadScene(texasHoldemScene);
        }

        public void BackToGameSelectionRoot()
        {
            SetTexasModePanel(false);
            SetStatus("请选择游戏", Color.white);
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
            if (!result.Success || result.Response?.room == null)
            {
                SetStatus(result.Message, Color.red);
                return;
            }

            SessionContext.SetRoom(
                result.Response.room.id,
                result.Response.room.code,
                result.Response.room.gameKey,
                result.Response.room.ownerUserId == SessionContext.UserId);
            SetStatus($"房间已创建，大厅代码：{result.Response.room.code}", Color.green);
            SceneManager.LoadScene(texasHoldemScene);
        }

        private void HandleJoinRoom(LobbyApiResult result)
        {
            if (!result.Success || result.Response?.room == null)
            {
                SetStatus(result.Message, Color.red);
                return;
            }

            SessionContext.SetRoom(
                result.Response.room.id,
                result.Response.room.code,
                result.Response.room.gameKey,
                result.Response.room.ownerUserId == SessionContext.UserId);
            SetStatus($"已加入房间：{result.Response.room.code}", Color.green);
            SceneManager.LoadScene(texasHoldemScene);
        }

        private void SetTexasModePanel(bool visible)
        {
            if (texasHoldemModePanel != null)
            {
                texasHoldemModePanel.SetActive(visible);
            }
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
