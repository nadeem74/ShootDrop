using ShootDrop.Core.Projects;

namespace ShootDrop.Core.Tests;

public class FolderMoveTests
{
    private static readonly FolderTemplate Mine = FolderTemplate.MyTemplate; // Audio/DJI Mic, Video/Pocket 4P, Video/Camera, Video/iPhone, Images

    [Fact]
    public void Dropping_a_folder_on_another_moves_it_inside_with_everything_in_it()
    {
        var t = Mine.Move("Video", "Images", out var newPath);
        Assert.Equal("Images/Video", newPath);
        Assert.Contains("Images/Video/Pocket 4P", t.Folders);
        Assert.Contains("Images/Video/Camera", t.Folders);
        Assert.DoesNotContain(t.AllFolders(), f => f == "Video" || f.StartsWith("Video/"));
    }

    [Fact]
    public void Dropping_on_the_project_moves_a_folder_to_the_top_level()
    {
        var t = Mine.Move("Video/iPhone", null, out var newPath);
        Assert.Equal("iPhone", newPath);
        Assert.Contains("iPhone", t.Folders);
        Assert.DoesNotContain("Video/iPhone", t.Folders);
    }

    [Fact]
    public void A_folder_cant_go_inside_itself_or_its_own_subfolder()
    {
        Assert.Throws<InvalidOperationException>(() => Mine.Move("Video", "Video", out _));
        Assert.Throws<InvalidOperationException>(() => Mine.Move("Video", "Video/Camera", out _));
    }

    [Fact]
    public void A_move_that_would_clash_with_an_existing_folder_is_refused()
    {
        var t = Mine.With("Images/Camera");
        Assert.Throws<InvalidOperationException>(() => t.Move("Video/Camera", "Images", out _));
    }

    [Fact]
    public void Dropping_a_folder_where_it_already_is_changes_nothing()
    {
        Assert.Same(Mine, Mine.Move("Video/Camera", "Video", out var p));
        Assert.Equal("Video/Camera", p);
    }

    [Theory]
    [InlineData("Video/Pocket 4P", "Video", "Footage/Video", "Footage/Video/Pocket 4P")]
    [InlineData("Video", "Video", "Footage/Video", "Footage/Video")]
    [InlineData("Videos/Pocket", "Video", "Footage/Video", "Videos/Pocket")] // same prefix, different folder
    [InlineData("Audio/DJI Mic", "Video", "Footage/Video", "Audio/DJI Mic")]
    public void Routes_follow_a_moved_folder(string path, string from, string to, string expected) =>
        Assert.Equal(expected, FolderTemplate.Rebase(path, from, to));
}
