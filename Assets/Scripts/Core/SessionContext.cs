namespace BoardGameSimulator.Core
{
    public static class SessionContext
    {
        public static long UserId { get; set; }
        public static string CurrentUser { get; set; }
        public static string AccessToken { get; set; }

        public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(AccessToken);

        public static void Clear()
        {
            UserId = 0;
            CurrentUser = string.Empty;
            AccessToken = string.Empty;
        }
    }
}
