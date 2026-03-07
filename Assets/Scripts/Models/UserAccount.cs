using System;

namespace BoardGameSimulator.Models
{
    // Deprecated: 用户信息来自后端接口返回。
    public class UserAccount
    {
        public long Id;
        public string Username;
        public string Password;

        public UserAccount(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }
}
