namespace LabKom.Student.Desktop.Services;

/// <summary>Pure policy for keyboard gestures blocked during managed classroom modes.</summary>
public static class KeyboardBlockPolicy
{
    public const int Tab = 0x09;
    public const int Escape = 0x1B;
    public const int LeftWindows = 0x5B;
    public const int RightWindows = 0x5C;
    public const int Applications = 0x5D;
    public const int F4 = 0x73;
    public const int F11 = 0x7A;

    public static bool ShouldBlock(int virtualKey, bool altDown, bool controlDown)
    {
        if (virtualKey is LeftWindows or RightWindows or Applications or F11) return true;
        if (altDown && virtualKey is Tab or F4 or Escape) return true;
        if (controlDown && virtualKey == Escape) return true;
        return false;
    }
}
