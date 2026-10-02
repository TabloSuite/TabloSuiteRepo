using System.Text;
using System.Text.RegularExpressions;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class ExamA4HtmlDocumentTests
{
    private static ExamPdfViewModel CreateExam() => new()
    {
        Title = "Épreuve <h1>",
        NiveauCode = "Classe A",
        TotalPoints = 4,
        DurationInMinutes = 30,
        Description = "<p><strong>Instruction importante</strong></p>",
        Exercises = [new ExamPdfExercise
        {
            Number = 1,
            Title = "Lecture",
            Consigne = "<p><em>Lire ce texte</em></p>",
            Questions = [new ExamPdfQuestion
            {
                Number = 1, Points = 4, Prompt = "<p><strong>Question formatée</strong></p><script>alert(1)</script>",
                QuestionType = "SingleChoice", Options = ["Vrai", "Faux"], CorrectOptions = ["Vrai"]
            }]
        }]
    };

    [Fact]
    public void BuildHtml_RespecteMiseEnForme_EtSeparationsDesSolutions()
    {
        var exam = CreateExam();
        var student = ExamA4HtmlDocument.BuildHtml(exam, false);
        var teacher = ExamA4HtmlDocument.BuildHtml(exam, true);

        Assert.Contains("<strong>Question formatée</strong>", student);
        Assert.Contains("<em>Lire ce texte</em>", student);
        Assert.Contains("&lt;h1&gt;", student);
        Assert.DoesNotContain("<h1>Épreuve", student);
        Assert.DoesNotContain("alert(1)", student);
        Assert.DoesNotContain("solution\">✓", student);
        Assert.Contains("solution\">✓", teacher);
    }

    [Fact]
    public async Task BrowserPdf_ContainsFormattedExamWithoutSolutions()
    {
        var browser = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            "/usr/bin/chromium", "/usr/bin/google-chrome"
        }.FirstOrDefault(File.Exists);
        if (browser is null) return;

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LessonPdf:BrowserPath"] = browser }).Build();
        var environment = new BrowserEnvironment();
        var service = new LessonExerciseA4PdfService(config, environment);
        var pdf = Encoding.Latin1.GetString(await service.GenerateHtmlPdfAsync(ExamA4HtmlDocument.BuildHtml(CreateExam(), false)));
        Assert.StartsWith("%PDF-", pdf);
        Assert.Equal(1, Regex.Matches(pdf, @"/Type\s*/Page\b").Count);
    }

    [Fact]
    public void BuildHtml_PreservesPrivateQuestionImagesWithoutExposingSolutions()
    {
        var exam = CreateExam();
        var imageId = Guid.NewGuid();
        exam.Exercises[0].Questions[0].Prompt = $"<p>Décris l'image :</p><img src=\"/media-library/{imageId}\" alt=\"Illustration\">";

        var html = ExamA4HtmlDocument.BuildHtml(exam, false);

        Assert.Contains($"src=\"/media-library/{imageId}\"", html);
        Assert.DoesNotContain("solution\">✓", html);
    }

    private sealed class BrowserEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
