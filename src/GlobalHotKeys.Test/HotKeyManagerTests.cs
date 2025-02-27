namespace GlobalHotKeys.Test;

public class HotKeyManagerTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void create_and_dispose_HotKeyManager()
    {
        // Create and dispose HotKeyManager
        using (var manager = new HotKeyManager())
        {
            // No assert is necessary as per original code
        }
    }

    [Test]
    public void register_2_keys()
    {
        // arrange
        using var manager = new HotKeyManager();
        // act
        using var s1 = manager.Register(VirtualKeyCode.KEY_0, Modifiers.Shift);
        using var s2 = manager.Register(VirtualKeyCode.KEY_1, Modifiers.Shift);
        // assert
        Assert.AreEqual(0, s1.Id);
        Assert.AreEqual(1, s2.Id);
    }

    [Test]
    public void register_reuses_ids()
    {
        // arrange
        using var manager = new HotKeyManager();
        // act
        var s1 = manager.Register(VirtualKeyCode.KEY_0, Modifiers.Shift);
        s1.Dispose();
        using var s2 = manager.Register(VirtualKeyCode.KEY_1, Modifiers.Shift);
        // assert
        Assert.AreEqual(0, s1.Id);
        Assert.AreEqual(0, s2.Id);
    }
}