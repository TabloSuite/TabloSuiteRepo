using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Services.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SkoulouApp.WebApp.Services;

public sealed class ExamPdfDocument(ExamPdfViewModel exam, bool includeSolutions) : IDocument
{
    private readonly ExamPdfViewModel _exam = exam;
    private readonly bool _includeSolutions = includeSolutions;

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
            column.Item().Text($"{_exam.NiveauCode} • {_exam.DurationInMinutes} min • {_exam.TotalPoints} pts")
                .SemiBold()
                .FontSize(10)
                .FontColor(Colors.Blue.Darken2);
            column.Item().Text(_exam.Title).Bold().FontSize(22);

            if (!string.IsNullOrWhiteSpace(_exam.Description))
            {
                var plain = HtmlSanitizer.ToPlainText(_exam.Description);
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    column.Item().Text(plain).FontColor(Colors.Grey.Darken2);
                }
            }

            column.Item().PaddingTop(4).Text($"Nom : ____________________    Date : __________    Note : ____ / {_exam.TotalPoints}")
                .FontSize(10)
                .FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Blue.Medium);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            foreach (var block in _exam.Exercises)
            {
                column.Item().Column(blockColumn =>
                {
                    blockColumn.Spacing(8);
                    var exerciseTitle = string.IsNullOrWhiteSpace(block.Title)
                        ? $"Exercice {block.Number}"
                        : $"Exercice {block.Number} — {block.Title}";

                    blockColumn.Item().Text(exerciseTitle).Bold().FontSize(15);

                    if (!string.IsNullOrWhiteSpace(block.Consigne))
                    {
                        var consigne = HtmlSanitizer.ToPlainText(block.Consigne);
                        if (!string.IsNullOrWhiteSpace(consigne))
                        {
                            blockColumn.Item().Text(consigne).Italic().FontColor(Colors.Grey.Darken2);
                        }
                    }

                    foreach (var question in block.Questions)
                    {
                        blockColumn.Item().Element(item => ComposeQuestion(item, question));
                    }
                });
            }
        });
    }

    private void ComposeQuestion(IContainer container, ExamPdfQuestion question)
    {
        container.Column(column =>
        {
            column.Spacing(4);

            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"{question.Number}. {HtmlSanitizer.ToPlainText(question.Prompt)}").SemiBold();
                row.ConstantItem(65).AlignRight().Text($"({question.Points} pt{(question.Points > 1 ? "s" : string.Empty)})")
                    .FontColor(Colors.Grey.Darken2)
                    .FontSize(10);
            });

            var isChoice = question.QuestionType is "Choice" or "SingleChoice" or "MultipleChoice" or "AudioChoice" or "AudioSingleChoice" or "AudioMultipleChoice";
            if (isChoice && question.Options.Count > 0)
            {
                var isSingleChoice = question.QuestionType is "Choice" or "SingleChoice" or "AudioChoice" or "AudioSingleChoice";
                foreach (var option in question.Options)
                {
                    var isCorrect = _includeSolutions && question.CorrectOptions.Contains(option);
                    column.Item().Text(text =>
                    {
                        text.Span(isSingleChoice ? "◯ " : "☐ ").FontSize(18);
                        text.Span(isCorrect ? $"{option} ✓" : option)
                            .FontSize(11)
                            .FontColor(isCorrect ? Colors.Green.Darken2 : Colors.Black);
                    });
                }

                return;
            }

            if (question.QuestionType == "GapFill")
            {
                var count = Math.Max(question.GapCount, 1);
                column.Item().Row(row =>
                {
                    for (var i = 0; i < count; i++)
                    {
                        row.RelativeItem().PaddingRight(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    }
                });

                if (_includeSolutions && question.GapSolutions.Count > 0)
                {
                    column.Item().Text($"Solutions : {string.Join(" · ", question.GapSolutions)}")
                        .FontSize(9)
                        .FontColor(Colors.Green.Darken2);
                }

                return;
            }

            column.Item().Column(answerLines =>
            {
                answerLines.Spacing(8);
                answerLines.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                answerLines.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                answerLines.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
            });

            if (_includeSolutions && !string.IsNullOrWhiteSpace(question.AcceptedAnswers))
            {
                column.Item().Text($"Solution(s) : {question.AcceptedAnswers}")
                    .FontSize(9)
                    .FontColor(Colors.Green.Darken2);
            }
        });
    }
}
