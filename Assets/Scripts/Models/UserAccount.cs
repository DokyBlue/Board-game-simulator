using System;

namespace BoardGameSimulator.Models
{
    [Serializable]
    public class UserAccount
    {
        public string Username;
        public string Password;

        public UserAccount(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }
}
