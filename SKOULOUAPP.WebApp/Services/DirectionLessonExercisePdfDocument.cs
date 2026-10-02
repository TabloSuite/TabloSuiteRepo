using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Net;
using System.Text.RegularExpressions;

namespace SkoulouApp.WebApp.Services;

public sealed class DirectionLessonExercisePdfDocument(LessonDetailViewModel lesson) : IDocument
{
    private readonly LessonDetailViewModel _lesson = lesson;
    private readonly record struct TextSegment(string Text, bool IsLink);
    private readonly record struct StyledLine(List<TextSegment> Segments);

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(11).LineHeight(1.35f).FontColor(Colors.Grey.Darken4));

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
                column.Item().Text(HtmlSanitizer.ToPlainText(_lesson.Summary)).FontColor(Colors.Grey.Darken2).LineHeight(1.45f);
            }

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Blue.Medium);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            var contentPages = _lesson.ContentPages
                .OrderBy(p => p.PageNumber)
                .ToList();

            if (contentPages.Count == 0 && !string.IsNullOrWhiteSpace(_lesson.Content))
            {
                contentPages.Add(new LessonContentPageViewModel
                {
                    PageNumber = 1,
                    ContentHtml = _lesson.Content
                });
            }

            for (var index = 0; index < contentPages.Count; index++)
            {
                var contentPage = contentPages[index];
                if (index > 0)
                {
                    column.Item().PageBreak();
                }

                ComposeCompactContentBlock(column.Item(), contentPage.ContentHtml);
                column.Item().AlignRight().Text($"Page source {contentPage.PageNumber}")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken1);
            }

            if (_lesson.Exercises.Count == 0)
            {
                return;
            }

            column.Item().PaddingTop(4).Text("Exercices").Bold().FontSize(13).FontColor(Colors.Green.Darken2);

            foreach (var exercise in _lesson.Exercises.OrderBy(x => x.Order))
            {
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(exerciseColumn =>
                {
                    exerciseColumn.Spacing(6);
                    exerciseColumn.Item().Row(row =>
                    {
                        row.AutoItem().Text($"Exercice {exercise.Order}").SemiBold().FontSize(11).FontColor(Colors.Blue.Darken2);
                        row.AutoItem().PaddingLeft(8).Text(exercise.ExerciseType).FontSize(10).FontColor(Colors.Grey.Darken2);
                    });

                    if (!string.IsNullOrWhiteSpace(exercise.Title))
                    {
                        exerciseColumn.Item().Text(HtmlSanitizer.ToPlainText(exercise.Title)).SemiBold().LineHeight(1.4f);
                    }

                    if (!string.IsNullOrWhiteSpace(exercise.Instructions))
                    {
                        exerciseColumn.Item().Text(HtmlSanitizer.ToPlainText(exercise.Instructions)).FontColor(Colors.Grey.Darken2).LineHeight(1.45f);
                    }

                    if (!string.IsNullOrWhiteSpace(exercise.Question))
                    {
                        exerciseColumn.Item().Text(HtmlSanitizer.ToPlainText(exercise.Question)).LineHeight(1.45f);
                    }

                    if (exercise.ExerciseType is "SingleChoice" or "Choice" or "AudioChoice" or "AudioSingleChoice")
                    {
                        foreach (var option in exercise.Options.OrderBy(o => o.Order))
                        {
                            var optionText = HtmlSanitizer.ToPlainText(option.Text);
                            exerciseColumn.Item().Text(text =>
                            {
                                text.Span("◯ ").FontSize(12);
                                text.Span(optionText).FontSize(11);
                            });
                        }
                    }
                    else if (exercise.ExerciseType is "MultipleChoice" or "AudioMultipleChoice")
                    {
                        foreach (var option in exercise.Options.OrderBy(o => o.Order))
                        {
                            var optionText = HtmlSanitizer.ToPlainText(option.Text);
                            exerciseColumn.Item().Text(text =>
                            {
                                text.Span("☐ ").FontSize(12);
                                text.Span(optionText).FontSize(11);
                            });
                        }
                    }
                    else if (exercise.ExerciseType == "GapFill")
                    {
                        var count = Math.Max(exercise.GapCount, 1);
                        exerciseColumn.Item().Text(string.Join("   ", Enumerable.Repeat("______", count))).FontColor(Colors.Grey.Darken2);
                    }
                    else
                    {
                        exerciseColumn.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        exerciseColumn.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                        exerciseColumn.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    }
                });
            }
        });
    }

    private static void ComposeCompactContentBlock(IContainer container, string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        var decoded = WebUtility.HtmlDecode(html);
        var sanitized = HtmlSanitizer.SanitizeMitBildern(decoded);
        var textWithBreaks = Regex.Replace(sanitized, "<\\s*br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        textWithBreaks = Regex.Replace(textWithBreaks, "<\\s*/\\s*(p|div|li)\\s*>", "\n", RegexOptions.IgnoreCase);
        var plainText = HtmlSanitizer.ToPlainText(textWithBreaks);

        if (string.IsNullOrWhiteSpace(plainText))
        {
            return;
        }

        container.Text(text =>
        {
            text.Span(plainText)
                .FontSize(11)
                .LineHeight(1.35f)
                .FontColor(Colors.Grey.Darken4);
        });
    }

    private static void ComposeContentWithColorAccents(IContainer container, string? html)
    {
        var segments = ExtractSegments(html);
        if (segments.Count == 0)
        {
            return;
        }

        var lines = BuildLines(segments);
        if (lines.Count == 0)
        {
            return;
        }

        container.Column(column =>
        {
            column.Spacing(4);

            foreach (var line in lines)
            {
                column.Item().Text(text =>
                {
                    foreach (var segment in line.Segments)
                    {
                        if (string.IsNullOrWhiteSpace(segment.Text))
                        {
                            continue;
                        }

                        var span = text.Span(segment.Text).FontSize(11).LineHeight(1.55f);
                        span = segment.IsLink
                            ? span.FontColor(Colors.Blue.Darken1)
                            : span.FontColor(Colors.Grey.Darken4);
                    }
                });
            }
        });
    }

    private static List<StyledLine> BuildLines(List<TextSegment> segments)
    {
        var lines = new List<StyledLine>();
        var current = new List<TextSegment>();

        foreach (var segment in segments)
        {
            var parts = segment.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (!string.IsNullOrWhiteSpace(part))
                {
                    current.Add(new TextSegment(part, segment.IsLink));
                }

                var isLineBreak = i < parts.Length - 1;
                if (!isLineBreak)
                {
                    continue;
                }

                if (current.Count > 0)
                {
                    lines.Add(new StyledLine([.. current]));
                    current.Clear();
                }
            }
        }

        if (current.Count > 0)
        {
            lines.Add(new StyledLine([.. current]));
        }

        return lines;
    }

    private static List<TextSegment> ExtractSegments(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var sanitized = HtmlSanitizer.SanitizeMitBildern(WebUtility.HtmlDecode(html));
        var withLineBreaks = Regex.Replace(sanitized, "<\\s*br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        withLineBreaks = Regex.Replace(withLineBreaks, "<\\s*/\\s*(p|div|li|h1|h2|h3|h4|h5|h6)\\s*>", "\n", RegexOptions.IgnoreCase);

        var results = new List<TextSegment>();
        var anchorRegex = new Regex("<a\\b[^>]*>(?<inner>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var startIndex = 0;
        foreach (Match match in anchorRegex.Matches(withLineBreaks))
        {
            var before = withLineBreaks[startIndex..match.Index];
            AppendSegment(results, before, isLink: false);

            var linkInner = match.Groups["inner"].Value;
            AppendSegment(results, linkInner, isLink: true);

            startIndex = match.Index + match.Length;
        }

        if (startIndex < withLineBreaks.Length)
        {
            AppendSegment(results, withLineBreaks[startIndex..], isLink: false);
        }

        return results;
    }

    private static void AppendSegment(List<TextSegment> segments, string rawFragment, bool isLink)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
        {
            return;
        }

        var noTags = Regex.Replace(rawFragment, "<[^>]+>", string.Empty, RegexOptions.Singleline);
        var decoded = WebUtility.HtmlDecode(noTags)
            .Replace("\r", string.Empty)
            .Replace("\u00A0", " ");

        decoded = Regex.Replace(decoded, "[ \t\f\v]+", " ");
        decoded = Regex.Replace(decoded, "\n{3,}", "\n\n");

        if (string.IsNullOrWhiteSpace(decoded))
        {
            return;
        }

        segments.Add(new TextSegment(decoded, isLink));
    }
}
