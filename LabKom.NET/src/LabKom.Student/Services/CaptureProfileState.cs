using LabKom.Shared.Contracts;

namespace LabKom.Student.Services;

/// <summary>
/// State profile capture saat ini, di-update saat Hub kirim ReceiveCaptureProfile.
/// </summary>
public class CaptureProfileState
{
    private CaptureProfile _profile = CaptureProfile.Thumbnail;
    public CaptureProfile Current
    {
        get => _profile;
        set => _profile = value;
    }
}
