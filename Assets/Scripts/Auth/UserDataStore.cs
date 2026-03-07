using System.Collections.Generic;
using System.Linq;
using BoardGameSimulator.Models;
using UnityEngine;

namespace BoardGameSimulator.Auth
{
    // Deprecated: 本地账号存储已由后端 MySQL + Token 登录替代。
    public class UserDataStore : MonoBehaviour
    {
        private const string UserListKey = "BGS_USERS";
        private readonly List<UserAccount> _users = new List<UserAccount>();

        private void Awake()
        {
            Load();
        }

        public bool Register(string username, string password, out string message)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                message = "用户名和密码不能为空";
                return false;
            }

            if (_users.Any(u => u.Username == username))
            {
                message = "用户名已存在";
                return false;
            }

            _users.Add(new UserAccount(username.Trim(), password));
            Save();
            message = "注册成功";
            return true;
        }

        public bool Login(string username, string password, out string message)
        {
            var user = _users.FirstOrDefault(u => u.Username == username.Trim());
            if (user == null)
            {
                message = "用户不存在";
                return false;
            }

            if (user.Password != password)
            {
                message = "密码错误";
                return false;
            }

            message = "登录成功";
            return true;
        }

        private void Save()
        {
            var payload = JsonUtility.ToJson(new UserListPayload(_users));
            PlayerPrefs.SetString(UserListKey, payload);
            PlayerPrefs.Save();
        }

        private void Load()
        {
            if (!PlayerPrefs.HasKey(UserListKey))
            {
                return;
            }

            var payload = JsonUtility.FromJson<UserListPayload>(PlayerPrefs.GetString(UserListKey));
            _users.Clear();
            _users.AddRange(payload.Users);
        }

        [System.Serializable]
        private class UserListPayload
        {
            public List<UserAccount> Users;

            public UserListPayload(List<UserAccount> users)
            {
                Users = users;
            }
        }
    }
}
