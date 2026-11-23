using ShootDrop.Core.Background;
using ShootDrop.Core.Settings;
using M = ShootDrop.Core.Background.ShortcutModifiers;

namespace ShootDrop.Core.Tests;

public class ShortcutTests
{
    [Fact]
    public void Default_is_control_option_command_C()
    {
        Assert.Equal("⌃⌥⌘C", Shortcut.Default.ToString());
        Assert.True(Shortcut.Default.IsValid);
        Assert.Equal(new[] { "⌃", "⌥", "⌘", "C" }, Shortcut.Default.Parts());
    }

    [Fact]
    public void Modifiers_are_shown_in_apple_order()
    {
        Assert.Equal("⌃⌥⇧⌘D", new Shortcut(M.Command | M.Shift | M.Option | M.Control, "D").ToString());
    }

    [Theory]
    [InlineData(M.Command | M.Shift, "D")]
    [InlineData(M.Control | M.Option, "7")]
    [InlineData(M.None, "F5")]
    [InlineData(M.Command, "J")]
    public void Valid_shortcuts(M modifiers, string key) => Assert.True(new Shortcut(modifiers, key).IsValid);

    [Theory]
    [InlineData(M.None, "C")]        // plain typing
    [InlineData(M.Shift, "C")]       // capital letter
    [InlineData(M.Command, "Q")]     // quit in every app
    [InlineData(M.Command, "C")]     // copy in every app
    [InlineData(M.Command, "Space")] // not a supported key
    [InlineData(M.Control, "F13")]
    public void Unsafe_or_unsupported_shortcuts_are_refused(M modifiers, string key)
    {
        var s = new Shortcut(modifiers, key);
        Assert.False(s.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(s.Problem));
    }

    [Fact]
    public void A_custom_shortcut_is_saved_and_older_settings_get_the_default()
    {
        using var tmp = new TempDir();
        var store = new SettingsStore(Path.Combine(tmp.Path, "settings.json"));
        Assert.Equal(Shortcut.Default, store.Load().Background.Shortcut);

        var s = store.Load();
        s.Background.Shortcut = new Shortcut(M.Command | M.Shift, "D");
        store.Save(s);

        Assert.Equal("⇧⌘D", store.Load().Background.Shortcut.ToString());
    }
}
