using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services.Text;
using HalloApp.Core.Services.Storage;

namespace HalloApp.WebApp.Services;

public sealed class LessonExerciseA4PdfService(IConfiguration configuration, IWebHostEnvironment environment)
{
    private static readonly Regex PrivateImageRegex = new("src=\"/media-library/([0-9a-fA-F-]{36})\"", RegexOptions.Compiled);

    public string BuildHtml(LessonDetailViewModel lesson)
    {
        var pages = lesson.ContentPages.OrderBy(page => page.PageNumber)
            .Select(page => (page.PageNumber, page.ContentHtml)).ToList();
        if (pages.Count == 0)
        {
            pages.Add((1, lesson.Content));
        }
        return BuildHtml(pages, lesson);
    }

    public string BuildHtml(TeacherLessonDetailViewModel lesson)
        => BuildHtml(lesson.Pages.OrderBy(page => page.PageNumber)
            .Select(page => (page.PageNumber, page.ContentHtml)), null);

    private string BuildHtml(IEnumerable<(int PageNumber, string ContentHtml)> pages, LessonDetailViewModel? exerciseLesson)
    {
        var cssPath = Path.Combine(environment.WebRootPath, "css", "lesson-a4.css");
        var css = File.ReadAllText(cssPath);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><style>");
        html.Append("body{margin:0;font-family:Arial,sans-serif}");
        html.Append(css);
        html.Append("</style></head><body><main class=\"lesson-a4-pages\">");

        foreach (var page in pages)
        {
            html.Append("<article class=\"lesson-a4-page ql-snow\"><div class=\"ql-editor\">");
            var sanitizedContent = HtmlSanitizer.SanitizeMitBildern(page.ContentHtml);
            sanitizedContent = Regex.Replace(sanitizedContent, "<img\\b[^>]*>", match => PrivateImageRegex.IsMatch(match.Value) ? match.Value : string.Empty, RegexOptions.IgnoreCase);
            html.Append(sanitizedContent);
            html.Append("</div><p class=\"lesson-a4-page-number\">Page ");
            html.Append(page.PageNumber);
            html.Append("</p></article>");
        }

        if (exerciseLesson is { Exercises.Count: > 0 })
        {
            html.Append("<section class=\"lesson-a4-page lesson-a4-exercises\"><h2>Exercices</h2>");
            foreach (var exercise in exerciseLesson.Exercises.OrderBy(exercise => exercise.Order))
            {
                html.Append("<div class=\"lesson-a4-exercise\"><h3>Exercice ");
                html.Append(exercise.Order);
                html.Append(" : ");
                html.Append(WebUtility.HtmlEncode(exercise.Title));
                html.Append("</h3><p>");
                html.Append(WebUtility.HtmlEncode(HtmlSanitizer.ToPlainText(exercise.Instructions)));
                html.Append("</p><p>");
                html.Append(PrivateImagesOnly(HtmlSanitizer.SanitizeMitBildern(exercise.Question)));
                html.Append("</p>");
                foreach (var option in exercise.Options.OrderBy(option => option.Order))
                {
                    html.Append("<p>□ ");
                    html.Append(WebUtility.HtmlEncode(HtmlSanitizer.ToPlainText(option.Text)));
                    html.Append("</p>");
                }
                if (exercise.ExerciseType == "GapFill")
                {
                    html.Append("<p>");
                    html.Append(string.Join("  ", Enumerable.Repeat("______", Math.Max(exercise.GapCount, 1))));
                    html.Append("</p>");
                }
                else if (exercise.Options.Count == 0)
                {
                    html.Append("<p>________________________________________</p><p>________________________________________</p>");
                }
                html.Append("</div>");
            }
            html.Append("</section>");
        }

        html.Append("</main></body></html>");
        return html.ToString();
    }

    public async Task<byte[]> GeneratePdfAsync(LessonDetailViewModel lesson, CancellationToken cancellationToken = default)
        => await GeneratePdfHtmlAsync(BuildHtml(lesson), cancellationToken);

    public async Task<byte[]> GeneratePdfAsync(TeacherLessonDetailViewModel lesson, CancellationToken cancellationToken = default)
        => await GeneratePdfHtmlAsync(BuildHtml(lesson), cancellationToken);

    public Task<byte[]> GenerateHtmlPdfAsync(string html, CancellationToken cancellationToken = default)
        => GeneratePdfHtmlAsync(html, cancellationToken);

    public async Task<byte[]> GenerateHtmlPdfAsync(string html, IBlobMediaLibraryService mediaLibrary, Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
        => await GeneratePdfHtmlAsync(await EmbedPrivateImagesAsync(PrivateImagesOnly(html), mediaLibrary, userId, roles, cancellationToken), cancellationToken);

    private static string PrivateImagesOnly(string html)
        => Regex.Replace(html, "<img\\b[^>]*>", match => PrivateImageRegex.IsMatch(match.Value) ? match.Value : string.Empty, RegexOptions.IgnoreCase);

    public async Task<byte[]> GeneratePdfAsync(LessonDetailViewModel lesson, IBlobMediaLibraryService mediaLibrary, Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
        => await GeneratePdfHtmlAsync(await EmbedPrivateImagesAsync(BuildHtml(lesson), mediaLibrary, userId, roles, cancellationToken), cancellationToken);

    public async Task<byte[]> GeneratePdfAsync(TeacherLessonDetailViewModel lesson, IBlobMediaLibraryService mediaLibrary, Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
        => await GeneratePdfHtmlAsync(await EmbedPrivateImagesAsync(BuildHtml(lesson), mediaLibrary, userId, roles, cancellationToken), cancellationToken);

    private static async Task<string> EmbedPrivateImagesAsync(string html, IBlobMediaLibraryService mediaLibrary, Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        foreach (var match in PrivateImageRegex.Matches(html).Cast<Match>().Select(match => match.Groups[1].Value).Distinct())
        {
            if (!Guid.TryParse(match, out var assetId))
            {
                continue;
            }
            var image = await mediaLibrary.OpenImageAsync(assetId, userId, roles, cancellationToken);
            if (image is null || !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Une image de la leçon n'est plus disponible pour cet utilisateur.");
            }
            await using var content = image.Content;
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            html = html.Replace($"src=\"/media-library/{match}\"", $"src=\"data:{image.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}\"", StringComparison.Ordinal);
        }
        return html;
    }

    private async Task<byte[]> GeneratePdfHtmlAsync(string html, CancellationToken cancellationToken)
    {
        var browser = ResolveBrowser();
        if (browser is null)
        {
            throw new InvalidOperationException("Le moteur PDF n'est pas disponible sur ce serveur.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "skoulou-a4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var htmlPath = Path.Combine(directory, "lesson.html");
            var pdfPath = Path.Combine(directory, "lesson.pdf");
            await File.WriteAllTextAsync(htmlPath, html, Encoding.UTF8, cancellationToken);

            using var process = new Process();
            process.StartInfo.FileName = browser;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ArgumentList.Add("--headless");
            process.StartInfo.ArgumentList.Add("--disable-gpu");
            process.StartInfo.ArgumentList.Add("--no-first-run");
            process.StartInfo.ArgumentList.Add("--no-default-browser-check");
            process.StartInfo.ArgumentList.Add("--allow-file-access-from-files");
            process.StartInfo.ArgumentList.Add("--no-pdf-header-footer");
            process.StartInfo.ArgumentList.Add("--print-to-pdf=" + pdfPath);
            process.StartInfo.ArgumentList.Add("--user-data-dir=" + Path.Combine(directory, "profile"));
            process.StartInfo.ArgumentList.Add(new Uri(htmlPath).AbsoluteUri);
            process.Start();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                throw;
            }

            if (process.ExitCode != 0 || !File.Exists(pdfPath))
            {
                throw new InvalidOperationException("La génération du PDF A4 a échoué.");
            }

            return await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string? ResolveBrowser()
    {
        var configured = configuration["LessonPdf:BrowserPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe")
            }
            : new[] { "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome" };
        return candidates.FirstOrDefault(File.Exists);
    }
}
