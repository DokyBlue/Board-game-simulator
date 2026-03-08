using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BoardGameSimulator.Networking
{
    public class LobbyApiClient : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "http://127.0.0.1:8080";

        public IEnumerator CreateRoom(string gameKey, string token, Action<LobbyApiResult> callback)
        {
            var body = JsonUtility.ToJson(new CreateRoomRequest { gameKey = gameKey });
            yield return Post("/lobby/rooms", body, token, callback);
        }

        public IEnumerator JoinRoom(string code, string token, Action<LobbyApiResult> callback)
        {
            var body = JsonUtility.ToJson(new JoinRoomRequest { code = code });
            yield return Post("/lobby/rooms/join", body, token, callback);
        }

        private IEnumerator Post(string path, string body, string token, Action<LobbyApiResult> callback)
        {
            using (var request = new UnityWebRequest(baseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(new LobbyApiResult(false, request.error, null));
                    yield break;
                }

                if (request.responseCode >= 400)
                {
                    callback?.Invoke(new LobbyApiResult(false, ParseError(request.downloadHandler.text), null));
                    yield break;
                }

                var response = JsonUtility.FromJson<LobbyResponse>(request.downloadHandler.text);
                callback?.Invoke(new LobbyApiResult(true, "ok", response));
            }
        }

        private static string ParseError(string raw)
        {
            try
            {
                var wrapper = JsonUtility.FromJson<ErrorResponse>(raw);
                return string.IsNullOrEmpty(wrapper.message) ? "请求失败" : wrapper.message;
            }
            catch
            {
                return "请求失败";
            }
        }

        [Serializable]
        private class CreateRoomRequest
        {
            public string gameKey;
        }

        [Serializable]
        private class JoinRoomRequest
        {
            public string code;
        }

        [Serializable]
        public class LobbyResponse
        {
            public LobbyRoom room;
        }

        [Serializable]
        public class LobbyRoom
        {
            public long id;
            public string code;
            public string gameKey;
        }

        [Serializable]
        private class ErrorResponse
        {
            public string message;
        }
    }

    public class LobbyApiResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly LobbyApiClient.LobbyResponse Response;

        public LobbyApiResult(bool success, string message, LobbyApiClient.LobbyResponse response)
        {
            Success = success;
            Message = message;
            Response = response;
        }
    }
}
