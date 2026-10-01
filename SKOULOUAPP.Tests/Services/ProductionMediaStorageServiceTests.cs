using HalloApp.Core.Services.Storage;
using HalloApp.WebApp.Services;
using Xunit;

namespace HalloApp.Tests.Services;

public class ProductionMediaStorageServiceTests
{
    [Fact]
    public async Task LegacyUpload_SpeichertNichtLokal()
    {
        var service = new ProductionMediaStorageService();
        using var content = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new MediaSaveRequest
        {
            Content = content,
            FileName = "test.png",
            MediaType = "image",
            AltText = "Test",
            LessonFolder = "test"
        }));
    }
}
