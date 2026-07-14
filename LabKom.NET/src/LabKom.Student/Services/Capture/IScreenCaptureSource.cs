using LabKom.Shared.Contracts;

namespace LabKom.Student.Services.Capture;

public interface IScreenCaptureSource
{
    /// <summary>
    /// Capture primary monitor lalu encode ke JPEG dengan ukuran target.
    /// Mengembalikan null jika capture tidak tersedia (mis. service di Session 0).
    /// </summary>
    ScreenFrame? CaptureFrame(string pcName, CaptureProfile profile, int targetWidth, int targetHeight, int jpegQuality);
}
