namespace BoardGameSimulator.Core
{
    public static class SessionContext
    {
        public static long UserId { get; set; }
        public static string CurrentUser { get; set; }
        public static string AccessToken { get; set; }
        public static long CurrentRoomId { get; set; }
        public static string CurrentRoomCode { get; set; }
        public static string CurrentRoomGameKey { get; set; }
        public static bool IsRoomOwner { get; set; }
        public static bool AutoStartOnSceneEnter { get; set; }

        public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(AccessToken);

        public static void Clear()
        {
            UserId = 0;
            CurrentUser = string.Empty;
            AccessToken = string.Empty;
            ClearRoom();
        }

        public static void SetRoom(long roomId, string roomCode, string gameKey, bool isOwner)
        {
            CurrentRoomId = roomId;
            CurrentRoomCode = roomCode ?? string.Empty;
            CurrentRoomGameKey = gameKey ?? string.Empty;
            IsRoomOwner = isOwner;
            AutoStartOnSceneEnter = false;
        }

        public static void ClearRoom()
        {
            CurrentRoomId = 0;
            CurrentRoomCode = string.Empty;
            CurrentRoomGameKey = string.Empty;
            IsRoomOwner = false;
            AutoStartOnSceneEnter = false;
        }
    }
}
