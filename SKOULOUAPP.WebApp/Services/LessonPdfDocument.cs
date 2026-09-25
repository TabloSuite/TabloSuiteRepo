using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Net;
using System.Text.RegularExpressions;

namespace HalloApp.WebApp.Services;

public sealed class LessonPdfDocument(TeacherLessonDetailViewModel lesson, bool includeSolutions) : IDocument
{
    private readonly TeacherLessonDetailViewModel _lesson = lesson;
    private readonly bool _includeSolutions = includeSolutions;

    private sealed record StyledText(string Text, float? FontSize, string? FontFamily, string? ColorHex, bool Bold, bool Italic, bool Underline);

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18);
            page.DefaultTextStyle(x => x.FontSize(11));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Element(ComposeContent);
            page.Footer()
                .AlignRight()
                .DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken1))
                .Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span("/");
                    text.TotalPages();
                });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text(_lesson.CourseCode).SemiBold().FontSize(10).FontColor(Colors.Blue.Darken2);
            column.Item().Text(_lesson.Title).Bold().FontSize(22);

            if (!string.IsNullOrWhiteSpace(_lesson.Summary))
            {
                ComposeRichText(column.Item(), _lesson.Summary, Colors.Grey.Darken2);
            }

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Blue.Medium);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            foreach (var lessonPage in _lesson.Pages.OrderBy(item => item.PageNumber))
            {
                var styled = ExtractStyledText(lessonPage.ContentHtml);
                if (string.IsNullOrWhiteSpace(styled.Text))
                {
                    continue;
                }

                column.Item().Column(pageColumn =>
                {
                    pageColumn.Spacing(6);
                    ComposeStyledParagraph(pageColumn.Item(), styled);

                    if (_includeSolutions)
                    {
                        pageColumn.Item().AlignRight().Text($"Page source {lessonPage.PageNumber}")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken1);
                    }
                });
            }
        });
    }

    private static void ComposeRichText(IContainer container, string? html, string fallbackColor)
    {
        var styled = ExtractStyledText(html);
        if (string.IsNullOrWhiteSpace(styled.Text))
        {
            return;
        }

        container.Text(text =>
        {
            var span = text.Span(styled.Text);
            if (styled.FontSize.HasValue)
            {
                span = span.FontSize(styled.FontSize.Value);
            }

            if (!string.IsNullOrWhiteSpace(styled.FontFamily))
            {
                span = span.FontFamily(styled.FontFamily);
            }

            span = span.FontColor(string.IsNullOrWhiteSpace(styled.ColorHex) ? fallbackColor : styled.ColorHex);

            if (styled.Bold)
            {
                span = span.Bold();
            }

            if (styled.Italic)
            {
                span = span.Italic();
            }

            if (styled.Underline)
            {
                span.Underline();
            }
        });
    }

    private static void ComposeStyledParagraph(IContainer container, StyledText styled)
    {
        container.Text(text =>
        {
            var span = text.Span(styled.Text);
            if (styled.FontSize.HasValue)
            {
                span = span.FontSize(styled.FontSize.Value);
            }

            if (!string.IsNullOrWhiteSpace(styled.FontFamily))
            {
                span = span.FontFamily(styled.FontFamily);
            }

            if (!string.IsNullOrWhiteSpace(styled.ColorHex))
            {
                span = span.FontColor(styled.ColorHex);
            }

            if (styled.Bold)
            {
                span = span.Bold();
            }

            if (styled.Italic)
            {
                span = span.Italic();
            }

            if (styled.Underline)
            {
                span.Underline();
            }
        });
    }

    private static StyledText ExtractStyledText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new StyledText(string.Empty, null, null, null, false, false, false);
        }

        var decoded = WebUtility.HtmlDecode(html);
        var sanitized = HtmlSanitizer.SanitizeMitBildern(decoded);
        var textWithBreaks = Regex.Replace(sanitized, "<\\s*br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        textWithBreaks = Regex.Replace(textWithBreaks, "<\\s*/\\s*(p|div|li)\\s*>", "\n", RegexOptions.IgnoreCase);
        var text = HtmlSanitizer.ToPlainText(textWithBreaks);

        float? fontSize = null;
        var sizeClassMatch = Regex.Match(sanitized, "ql-size-(\\d+)px", RegexOptions.IgnoreCase);
        if (sizeClassMatch.Success && float.TryParse(sizeClassMatch.Groups[1].Value, out var parsedSize))
        {
            fontSize = parsedSize;
        }

        var styleSizeMatch = Regex.Match(sanitized, "font-size\\s*:\\s*(\\d+(?:\\.\\d+)?)px", RegexOptions.IgnoreCase);
        if (styleSizeMatch.Success && float.TryParse(styleSizeMatch.Groups[1].Value, out parsedSize))
        {
            fontSize = parsedSize;
        }

        var familyMatch = Regex.Match(sanitized, "font-family\\s*:\\s*([^;\"']+)", RegexOptions.IgnoreCase);
        var fontFamily = familyMatch.Success ? familyMatch.Groups[1].Value.Trim() : null;
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            if (sanitized.Contains("ql-font-arial", StringComparison.OrdinalIgnoreCase))
            {
                fontFamily = "Arial";
            }
            else if (sanitized.Contains("ql-font-serif", StringComparison.OrdinalIgnoreCase))
            {
                fontFamily = "Times New Roman";
            }
            else if (sanitized.Contains("ql-font-monospace", StringComparison.OrdinalIgnoreCase))
            {
                fontFamily = "Courier New";
            }
        }

        var styleColorMatch = Regex.Match(
            sanitized,
            "(?:^|[;\\s\"'])color\\s*:\\s*([^;\"']+)",
            RegexOptions.IgnoreCase);
        var color = styleColorMatch.Success
            ? NormalizeQuestPdfColor(styleColorMatch.Groups[1].Value)
            : null;

        var bold = Regex.IsMatch(sanitized, "<\\s*(strong|b)\\b", RegexOptions.IgnoreCase)
                   || Regex.IsMatch(sanitized, "font-weight\\s*:\\s*(bold|[6-9]00)", RegexOptions.IgnoreCase);
        var italic = Regex.IsMatch(sanitized, "<\\s*(em|i)\\b", RegexOptions.IgnoreCase)
                     || Regex.IsMatch(sanitized, "font-style\\s*:\\s*italic", RegexOptions.IgnoreCase);
        var underline = Regex.IsMatch(sanitized, "<\\s*u\\b", RegexOptions.IgnoreCase)
                        || Regex.IsMatch(sanitized, "text-decoration\\s*:\\s*underline", RegexOptions.IgnoreCase);

        return new StyledText(text, fontSize, fontFamily, color, bold, italic, underline);
    }

    private static string? NormalizeQuestPdfColor(string? rawColor)
    {
        if (string.IsNullOrWhiteSpace(rawColor))
        {
            return null;
        }

        var value = rawColor.Trim().TrimEnd(';').Trim();

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (Regex.IsMatch(hex, "^[0-9a-fA-F]{3}$|^[0-9a-fA-F]{4}$|^[0-9a-fA-F]{6}$|^[0-9a-fA-F]{8}$"))
            {
                return "#" + hex.ToUpperInvariant();
            }

            return null;
        }

        var rgbMatch = Regex.Match(value, "^rgb\\s*\\(\\s*(\\d{1,3})\\s*,\\s*(\\d{1,3})\\s*,\\s*(\\d{1,3})\\s*\\)$", RegexOptions.IgnoreCase);
        if (rgbMatch.Success
            && byte.TryParse(rgbMatch.Groups[1].Value, out var r)
            && byte.TryParse(rgbMatch.Groups[2].Value, out var g)
            && byte.TryParse(rgbMatch.Groups[3].Value, out var b))
        {
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        return value.ToLowerInvariant() switch
        {
            "black" => "#000000",
            "white" => "#FFFFFF",
            "red" => "#FF0000",
            "green" => "#008000",
            "blue" => "#0000FF",
            "yellow" => "#FFFF00",
            "orange" => "#FFA500",
            "purple" => "#800080",
            "gray" or "grey" => "#808080",
            _ => null
        };
    }
}
