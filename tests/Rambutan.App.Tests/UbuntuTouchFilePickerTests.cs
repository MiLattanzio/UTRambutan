using System.Text.Json;
using Rambutan.App.Services;
using Rambutan.App.ViewModels;

namespace Rambutan.App.Tests;

public class UbuntuTouchFilePickerTests
{
    [Fact]
    public async Task ATransferredM3uCannotKeepTracksPointingIntoTemporaryStorage()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "#EXTM3U\n../Music/song.mp3\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => LibraryPageViewModel.ValidateTransferredPlaylistAsync(path));
            await File.WriteAllTextAsync(path, "#EXTM3U\n/home/phablet/Music/song.mp3\n");
            await LibraryPageViewModel.ValidateTransferredPlaylistAsync(path);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AcceptsOnlyARegularFileInsideTheCurrentImportDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "rambutan-picker-" + Guid.NewGuid().ToString("N"));
        var incoming = Path.Combine(root, "incoming");
        Directory.CreateDirectory(incoming);
        try
        {
            var selected = Path.Combine(incoming, "playlist ü.csv");
            File.WriteAllText(selected, "Title,Artist\nTest,Artist");
            Assert.Equal(selected, UbuntuTouchFilePicker.ValidateResult(JsonSerializer.Serialize(new { path = selected }), incoming));
            var outside = Path.Combine(root, "private.csv");
            File.WriteAllText(outside, "unrelated");
            Assert.Throws<InvalidDataException>(() => UbuntuTouchFilePicker.ValidateResult(JsonSerializer.Serialize(new { path = outside }), incoming));
            Assert.Throws<InvalidDataException>(() => UbuntuTouchFilePicker.ValidateResult(JsonSerializer.Serialize(new { path = Path.Combine(incoming, "..", "private.csv") }), incoming));
            Assert.Throws<InvalidDataException>(() => UbuntuTouchFilePicker.ValidateResult("{\"path\":\"relative.csv\"}", incoming));
            if (OperatingSystem.IsLinux())
            {
                var link = Path.Combine(incoming, "link.csv");
                File.CreateSymbolicLink(link, outside);
                Assert.Throws<InvalidDataException>(() => UbuntuTouchFilePicker.ValidateResult(JsonSerializer.Serialize(new { path = link }), incoming));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TemporaryFileLeaseIsReleasedOnce()
    {
        var released = 0;
        var picked = new PickedFile("/chosen/file.csv") { Release = () => released++ };
        picked.Dispose();
        picked.Dispose();
        Assert.Equal(1, released);
        new PickedFile("/users/original.csv").Dispose();
    }
}
