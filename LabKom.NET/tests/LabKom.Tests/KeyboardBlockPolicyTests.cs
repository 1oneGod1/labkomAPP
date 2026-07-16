using LabKom.Student.Desktop.Services;

namespace LabKom.Tests;

public sealed class KeyboardBlockPolicyTests
{
    [Theory]
    [InlineData(KeyboardBlockPolicy.LeftWindows, false, false)]
    [InlineData(KeyboardBlockPolicy.RightWindows, false, false)]
    [InlineData(KeyboardBlockPolicy.Applications, false, false)]
    [InlineData(KeyboardBlockPolicy.Tab, true, false)]
    [InlineData(KeyboardBlockPolicy.F4, true, false)]
    [InlineData(KeyboardBlockPolicy.Escape, true, false)]
    [InlineData(KeyboardBlockPolicy.Escape, false, true)]
    [InlineData(KeyboardBlockPolicy.F11, false, false)]
    public void ManagedGesturesAreBlocked(int virtualKey, bool altDown, bool controlDown)
    {
        Assert.True(KeyboardBlockPolicy.ShouldBlock(virtualKey, altDown, controlDown));
    }

    [Theory]
    [InlineData(KeyboardBlockPolicy.Tab, false, false)]
    [InlineData(0x51, true, true)]
    [InlineData(0x41, false, true)]
    public void OrdinaryKeysRemainAvailable(int virtualKey, bool altDown, bool controlDown)
    {
        Assert.False(KeyboardBlockPolicy.ShouldBlock(virtualKey, altDown, controlDown));
    }
}
