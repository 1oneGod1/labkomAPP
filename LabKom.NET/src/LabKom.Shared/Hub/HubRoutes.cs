namespace LabKom.Shared.Hub;

/// <summary>Konstanta endpoint, method, role, dan audience SignalR LabKom.</summary>
public static class HubRoutes
{
    public const int DefaultPort = 41235;
    public const string TeacherHubPath = "/hubs/teacher";

    public static string BuildClientUrl(string hubUrl, string role, string pcName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubUrl);
        if (!Roles.IsKnown(role)) throw new ArgumentException("Role Hub tidak valid.", nameof(role));
        if (!HubSecurity.IsValidPcName(pcName)) throw new ArgumentException("Nama PC tidak valid.", nameof(pcName));

        var separator = hubUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{hubUrl}{separator}{Roles.Key}={Uri.EscapeDataString(role)}&pc={Uri.EscapeDataString(pcName)}";
    }

    public static class Methods
    {
        // Student -> Hub
        public const string Hello = "Hello";
        public const string Heartbeat = "Heartbeat";
        public const string ReportStatus = "ReportStatus";
        public const string SendChatToTeacher = "SendChatToTeacher";
        public const string PushScreenFrame = "PushScreenFrame";
        public const string PushMonitorInventory = "PushMonitorInventory";
        public const string PushActivityRecord = "PushActivityRecord";
        public const string ReportDeviceKeyRotation = "ReportDeviceKeyRotation";
        public const string PushDeviceTelemetry = "PushDeviceTelemetry";
        public const string ReportFileProgress = "ReportFileProgress";
        public const string ReportCommandResult = "ReportCommandResult";
        public const string ReportRemoteSessionStatus = "ReportRemoteSessionStatus";
        public const string PushFileCollectionChunk = "PushFileCollectionChunk";
        public const string SubmitStudentRegistration = "SubmitStudentRegistration";
        public const string SubmitAssessment = "SubmitAssessment";

        // Hub -> Student Agent
        public const string ReceivePowerCommand = "ReceivePowerCommand";
        public const string ReceiveFileNotice = "ReceiveFileNotice";
        public const string ReceiveWebFilterPolicy = "ReceiveWebFilterPolicy";
        public const string ReceiveAppBlockPolicy = "ReceiveAppBlockPolicy";
        public const string ReceiveDeviceKeyRotation = "ReceiveDeviceKeyRotation";

        // Hub -> Student Desktop
        public const string ReceiveAttention = "ReceiveAttention";
        public const string ReceiveChat = "ReceiveChat";
        public const string ReceiveCaptureProfile = "ReceiveCaptureProfile";
        public const string ReceiveTeacherFrame = "ReceiveTeacherFrame";
        public const string ReceiveTeacherBroadcastSignal = "ReceiveTeacherBroadcastSignal";
        public const string ReceiveClassroomStateSnapshot = "ReceiveClassroomStateSnapshot";
        public const string ReceiveRemoteSession = "ReceiveRemoteSession";
        public const string ReceiveRemoteInput = "ReceiveRemoteInput";
        public const string ReceiveFileCollectionRequest = "ReceiveFileCollectionRequest";
        public const string ReceiveLessonSnapshot = "ReceiveLessonSnapshot";

        // Internal Teacher UI events
        public const string PresenceChanged = "PresenceChanged";
        public const string ChatReceived = "ChatReceived";
        public const string FrameReceived = "FrameReceived";
        public const string ActivityReceived = "ActivityReceived";
        public const string FileProgressReceived = "FileProgressReceived";
    }

    public static class Roles
    {
        public const string Key = "role";
        public const string Agent = "agent";
        public const string Desktop = "desktop";

        public static bool IsKnown(string? role) => role is Agent or Desktop;
    }

    public static class Groups
    {
        public static string ForRole(string role)
        {
            if (!Roles.IsKnown(role)) throw new ArgumentException("Role Hub tidak valid.", nameof(role));
            return $"role:{role}";
        }

        public static string ForPcRole(string pcName, string role)
        {
            if (!HubSecurity.IsValidPcName(pcName)) throw new ArgumentException("Nama PC tidak valid.", nameof(pcName));
            return $"pc:{pcName.ToLowerInvariant()}:{role switch
            {
                Roles.Agent => Roles.Agent,
                Roles.Desktop => Roles.Desktop,
                _ => throw new ArgumentException("Role Hub tidak valid.", nameof(role)),
            }}";
        }
    }
}