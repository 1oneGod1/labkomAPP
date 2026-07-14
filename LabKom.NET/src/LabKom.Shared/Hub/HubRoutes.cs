namespace LabKom.Shared.Hub;

/// <summary>
/// Konstanta endpoint SignalR. Teacher Console host hub di TeacherHubPath,
/// Student Agent dan Teacher UI sama-sama connect ke path ini.
/// </summary>
public static class HubRoutes
{
    public const int DefaultPort = 41235;
    public const string TeacherHubPath = "/hubs/teacher";

    public static class Methods
    {
        // Student → Hub
        public const string Hello = "Hello";
        public const string Heartbeat = "Heartbeat";
        public const string ReportStatus = "ReportStatus";
        public const string SendChatToTeacher = "SendChatToTeacher";
        public const string PushScreenFrame = "PushScreenFrame";
        public const string PushActivityRecord = "PushActivityRecord";
        public const string ReportFileProgress = "ReportFileProgress";

        // Hub → Student
        public const string ReceiveAttention = "ReceiveAttention";
        public const string ReceiveChat = "ReceiveChat";
        public const string ReceivePowerCommand = "ReceivePowerCommand";
        public const string ReceiveCaptureProfile = "ReceiveCaptureProfile";
        public const string ReceiveFileNotice = "ReceiveFileNotice";
        public const string ReceiveWebFilterPolicy = "ReceiveWebFilterPolicy";
        public const string ReceiveAppBlockPolicy = "ReceiveAppBlockPolicy";

        // Hub → Overlay
        public const string ReceiveTeacherFrame = "ReceiveTeacherFrame";
        public const string ReceiveTeacherBroadcastSignal = "ReceiveTeacherBroadcastSignal";

        // Hub → Teacher UI
        public const string PresenceChanged = "PresenceChanged";
        public const string ChatReceived = "ChatReceived";
        public const string FrameReceived = "FrameReceived";
        public const string ActivityReceived = "ActivityReceived";
        public const string FileProgressReceived = "FileProgressReceived";
    }

    /// <summary>
    /// Query string parameter pada URL koneksi untuk membedakan
    /// Agent (full presence) dari Overlay (UI-only listener).
    /// </summary>
    public static class Roles
    {
        public const string Key = "role";
        public const string Agent = "agent";
        public const string Overlay = "overlay";
    }
}
