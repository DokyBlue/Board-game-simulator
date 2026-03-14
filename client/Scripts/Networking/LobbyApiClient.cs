using BoardGameSimulator.Core;
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BoardGameSimulator.Networking
{
    public class LobbyApiClient : MonoBehaviour
    {
        //[SerializeField] private string baseUrl = "http://127.0.0.1:8080";

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

        public IEnumerator LeaveRoom(long roomId, string token, Action<LobbyApiResult> callback)
        {
            var body = JsonUtility.ToJson(new LeaveRoomRequest { roomId = roomId });
            yield return Post("/lobby/rooms/leave", body, token, callback);
        }

        public IEnumerator GetRoomState(long roomId, string token, Action<LobbyStateApiResult> callback)
        {
            using (var request = UnityWebRequest.Get($"{SessionContext.ServerBaseUrl}/lobby/rooms/{roomId}/state"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(new LobbyStateApiResult(false, request.error, null));
                    yield break;
                }

                if (request.responseCode >= 400)
                {
                    callback?.Invoke(new LobbyStateApiResult(false, ParseError(request.downloadHandler.text), null));
                    yield break;
                }

                var response = JsonUtility.FromJson<LobbyStateResponse>(request.downloadHandler.text);
                callback?.Invoke(new LobbyStateApiResult(true, "ok", response));
            }
        }

        public IEnumerator StartGame(long roomId, string token, Action<LobbyStartApiResult> callback)
        {
            var body = "{}";
            using (var request = new UnityWebRequest($"{SessionContext.ServerBaseUrl}/lobby/rooms/{roomId}/start", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(new LobbyStartApiResult(false, request.error, null));
                    yield break;
                }

                if (request.responseCode >= 400)
                {
                    callback?.Invoke(new LobbyStartApiResult(false, ParseError(request.downloadHandler.text), null));
                    yield break;
                }

                var response = JsonUtility.FromJson<LobbyStartResponse>(request.downloadHandler.text);
                callback?.Invoke(new LobbyStartApiResult(true, "ok", response));
            }
        }

        public IEnumerator GetRoomMembers(long roomId, string token, Action<LobbyMembersApiResult> callback)
        {
            using (var request = UnityWebRequest.Get($"{SessionContext.ServerBaseUrl}/lobby/rooms/{roomId}/members"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(new LobbyMembersApiResult(false, request.error, null));
                    yield break;
                }

                if (request.responseCode >= 400)
                {
                    callback?.Invoke(new LobbyMembersApiResult(false, ParseError(request.downloadHandler.text), null));
                    yield break;
                }

                var response = JsonUtility.FromJson<LobbyMembersResponse>(request.downloadHandler.text);
                callback?.Invoke(new LobbyMembersApiResult(true, "ok", response));
            }
        }

        private IEnumerator Post(string path, string body, string token, Action<LobbyApiResult> callback)
        {
            using (var request = new UnityWebRequest(SessionContext.ServerBaseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                request.certificateHandler = new BypassCertificate();
                request.SetRequestHeader("ngrok-skip-browser-warning", "true");

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

                LobbyResponse response = null;
                var raw = request.downloadHandler.text;
                if (!string.IsNullOrWhiteSpace(raw) && raw.Contains("\"room\""))
                {
                    response = JsonUtility.FromJson<LobbyResponse>(raw);
                }

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
        private class LeaveRoomRequest
        {
            public long roomId;
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
            public long ownerUserId;
        }

        [Serializable]
        public class LobbyMembersResponse
        {
            public long roomId;
            public LobbyMember[] members;
        }

        [Serializable]
        public class LobbyMember
        {
            public long userId;
            public string username;
            public int isOwner;
        }

        [Serializable]
        public class LobbyStateResponse
        {
            public long roomId;
            public LobbyRoom room;
            public LobbyMember[] members;
            public bool gameStarted;
            public string startedAt;
        }

        [Serializable]
        public class LobbyStartResponse
        {
            public string message;
            public long roomId;
            public string startedAt;
        }

        [Serializable]
        private class ErrorResponse
        {
            public string message;
        }
    }

    public class BypassCertificate : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            return true;
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

    public class LobbyMembersApiResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly LobbyApiClient.LobbyMembersResponse Response;

        public LobbyMembersApiResult(bool success, string message, LobbyApiClient.LobbyMembersResponse response)
        {
            Success = success;
            Message = message;
            Response = response;
        }
    }


    public class LobbyStateApiResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly LobbyApiClient.LobbyStateResponse Response;

        public LobbyStateApiResult(bool success, string message, LobbyApiClient.LobbyStateResponse response)
        {
            Success = success;
            Message = message;
            Response = response;
        }
    }

    public class LobbyStartApiResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly LobbyApiClient.LobbyStartResponse Response;

        public LobbyStartApiResult(bool success, string message, LobbyApiClient.LobbyStartResponse response)
        {
            Success = success;
            Message = message;
            Response = response;
        }
    }

}
