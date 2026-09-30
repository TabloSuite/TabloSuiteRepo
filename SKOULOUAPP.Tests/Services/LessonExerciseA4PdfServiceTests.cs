using System.Text;
using System.Text.RegularExpressions;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace HalloApp.Tests.Services;

public class LessonExerciseA4PdfServiceTests
{
    private static LessonExerciseA4PdfService CreateService(IConfiguration? configuration = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SKOULOUAPP.WebApp", "wwwroot"));
        var environment = new TestWebHostEnvironment { WebRootPath = root };
        return new LessonExerciseA4PdfService(configuration ?? new ConfigurationBuilder().Build(), environment);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SKOULOUAPP.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void BuildHtml_RendersEachSavedA4PageAndEscapesExercises()
    {
        var lesson = new LessonDetailViewModel
        {
            ContentPages =
            [
                new() { PageNumber = 1, ContentHtml = "<p>Première page</p><script>alert(1)</script>" },
                new() { PageNumber = 2, ContentHtml = "<p>Deuxième page</p>" },
                new() { PageNumber = 3, ContentHtml = "<p>Troisième page</p>" }
            ],
            Media = [new() { MediaType = "video", Url = "https://example.org/video.mp4" }],
            Exercises = [new() { Order = 1, Title = "Question <script>" }]
        };

        var html = CreateService().BuildHtml(lesson);

        Assert.Equal(3, Regex.Matches(html, "class=\\\"lesson-a4-page ql-snow\\\"").Count);
        Assert.Contains("Page 3", html);
        Assert.Contains("Troisième page", html);
        Assert.Contains("Question &lt;script&gt;", html);
        Assert.DoesNotContain("alert(1)", html);
        Assert.DoesNotContain("video.mp4", html);
    }

    [Fact]
    public async Task GeneratePdfAsync_ThreeSavedPages_ProducesThreePdfPagesWithoutBrowserHeaders()
    {
        var browser = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "/usr/bin/chromium", "/usr/bin/google-chrome"
        }.FirstOrDefault(File.Exists);
        if (browser is null)
        {
            return;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LessonPdf:BrowserPath"] = browser
        }).Build();
        var lesson = new LessonDetailViewModel
        {
            ContentPages =
            [
                new() { PageNumber = 1, ContentHtml = "<p>Première page</p>" },
                new() { PageNumber = 2, ContentHtml = "<p>Deuxième page</p>" },
                new() { PageNumber = 3, ContentHtml = "<p>Troisième page</p>" }
            ]
        };

        var bytes = await CreateService(configuration).GeneratePdfAsync(lesson);
        var pdf = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-", pdf);
        Assert.Equal(3, Regex.Matches(pdf, @"/Type\s*/Page\b").Count);
    }

    [Fact]
    public async Task GeneratePdfAsync_ThreeSavedPagesAndExercise_AddsOnlyOneExercisePage()
    {
        var browser = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "/usr/bin/chromium", "/usr/bin/google-chrome"
        }.FirstOrDefault(File.Exists);
        if (browser is null)
        {
            return;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LessonPdf:BrowserPath"] = browser
        }).Build();
        var lesson = new LessonDetailViewModel
        {
            ContentPages = Enumerable.Range(1, 3).Select(number => new LessonContentPageViewModel
            {
                PageNumber = number,
                ContentHtml = $"<p>Page {number}</p>"
            }).ToList(),
            Exercises = [new ExerciseViewModel { Order = 1, Title = "Question", Question = "Réponds à la question" }]
        };

        var pdf = Encoding.Latin1.GetString(await CreateService(configuration).GeneratePdfAsync(lesson));
        Assert.Equal(4, Regex.Matches(pdf, @"/Type\s*/Page\b").Count);
    }

    [Fact]
    public async Task GeneratePdfAsync_TeacherPages_PreservesThreeA4Pages()
    {
        var browser = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "/usr/bin/chromium", "/usr/bin/google-chrome"
        }.FirstOrDefault(File.Exists);
        if (browser is null)
        {
            return;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LessonPdf:BrowserPath"] = browser
        }).Build();
        var lesson = new TeacherLessonDetailViewModel
        {
            Pages = Enumerable.Range(1, 3).Select(number => new TeacherLessonPageViewModel
            {
                PageNumber = number,
                ContentHtml = string.Concat(Enumerable.Repeat($"<p><span class=\"ql-size-20px\">Ligne de contenu {number}</span></p>", 30))
            }).ToList()
        };

        var service = CreateService(configuration);
        var html = service.BuildHtml(lesson);
        Assert.Equal(3, Regex.Matches(html, "class=\"lesson-a4-page ql-snow\"").Count);
        var pdf = Encoding.Latin1.GetString(await service.GeneratePdfAsync(lesson));
        Assert.Equal(3, Regex.Matches(pdf, @"/Type\s*/Page\b").Count);
    }
}
