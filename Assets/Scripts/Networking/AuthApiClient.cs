using BoardGameSimulator.Core;
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BoardGameSimulator.Networking
{
    public class AuthApiClient : MonoBehaviour
    {
        //[SerializeField] private string baseUrl = "http://127.0.0.1:8080";

        public IEnumerator Register(string username, string password, Action<AuthApiResult> callback)
        {
            yield return PostAuth("/auth/register", username, password, callback);
        }

        public IEnumerator Login(string username, string password, Action<AuthApiResult> callback)
        {
            yield return PostAuth("/auth/login", username, password, callback);
        }

        private IEnumerator PostAuth(string path, string username, string password, Action<AuthApiResult> callback)
        {
            var payload = JsonUtility.ToJson(new AuthRequest
            {
                username = username,
                password = password
            });

            using (var request = new UnityWebRequest(SessionContext.ServerBaseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(new AuthApiResult(false, request.error, null));
                    yield break;
                }

                if (request.responseCode >= 400)
                {
                    callback?.Invoke(new AuthApiResult(false, ParseError(request.downloadHandler.text), null));
                    yield break;
                }

                var response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);
                callback?.Invoke(new AuthApiResult(true, "ok", response));
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
        private class AuthRequest
        {
            public string username;
            public string password;
        }

        [Serializable]
        public class AuthResponse
        {
            public string token;
            public AuthUser user;
        }

        [Serializable]
        public class AuthUser
        {
            public long id;
            public string username;
        }

        [Serializable]
        private class ErrorResponse
        {
            public string message;
        }
    }

    public class AuthApiResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly AuthApiClient.AuthResponse Response;

        public AuthApiResult(bool success, string message, AuthApiClient.AuthResponse response)
        {
            Success = success;
            Message = message;
            Response = response;
        }
    }
}
