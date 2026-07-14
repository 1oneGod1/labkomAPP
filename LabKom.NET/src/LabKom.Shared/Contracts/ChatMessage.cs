namespace LabKom.Shared.Contracts;

public enum ChatDirection
{
    TeacherToStudent = 0,
    StudentToTeacher = 1,
    Broadcast = 2,
}

public sealed record ChatMessage(
    string Id,
    ChatDirection Direction,
    string? FromPcName,
    string? ToPcName,
    string Body,
    long TimestampUnixMs)
{
    public static ChatMessage NewBroadcast(string fromPcName, string body) => new(
        Guid.NewGuid().ToString("N"),
        ChatDirection.Broadcast,
        fromPcName,
        null,
        body,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
