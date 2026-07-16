using LabKom.Shared.Contracts;

namespace LabKom.Student.Desktop.Services.Capture;

public interface IScreenCaptureSource
{
    IReadOnlyList<MonitorDescriptor> GetMonitors();

    ScreenFrame? CaptureFrame(
        string pcName,
        CaptureProfile profile,
        string? monitorId,
        int targetWidth,
        int targetHeight,
        int jpegQuality,
        long sequenceNumber);
}
