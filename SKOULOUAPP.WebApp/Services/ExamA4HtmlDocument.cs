using System.Net;
using System.Text;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Services.Text;

namespace SkoulouApp.WebApp.Services;

public static class ExamA4HtmlDocument
{
    public static string BuildHtml(ExamPdfViewModel exam, bool includeSolutions)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><style>");
        html.Append("@page{size:A4;margin:16mm}body{font:13px/1.5 Arial,sans-serif;color:#1e293b;margin:0}header{border-bottom:1px solid #0891b2;padding-bottom:12px;margin-bottom:22px}header h1{font-size:22px;margin:6px 0}h2{font-size:16px;margin:0}h3{font-size:14px;margin:0}.intro{color:#475569}.block{margin:22px 0;break-inside:avoid-page}.question{margin:12px 0;break-inside:avoid-page}.prompt{font-weight:600}.option{margin:6px 0}.solution{color:#047857}.answer{border-bottom:1px solid #cbd5e1;height:22px}.ql-editor{white-space:pre-wrap;overflow-wrap:break-word}.ql-editor p{margin:0}.ql-align-center{text-align:center}.ql-align-right{text-align:right}.ql-size-small{font-size:10px}.ql-size-large{font-size:18px}.ql-size-huge{font-size:24px}@media print{body{print-color-adjust:exact}});");
        html.Append("</style></head><body><header><p>");
        html.Append(Encode(exam.NiveauCode));
        html.Append(" · ").Append(exam.DurationInMinutes).Append(" min · ").Append(exam.TotalPoints).Append(" pts</p><h1>").Append(Encode(exam.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(exam.Description))
        {
            html.Append("<div class=\"ql-editor intro\">").Append(HtmlSanitizer.Sanitize(exam.Description)).Append("</div>");
        }
        html.Append("<p>Nom : ____________________ &nbsp; Date : __________ &nbsp; Note : ____ / ").Append(exam.TotalPoints).Append("</p></header>");
        foreach (var block in exam.Exercises.OrderBy(block => block.Number))
        {
            html.Append("<section class=\"block\"><h2>Exercice ").Append(block.Number);
            if (!string.IsNullOrWhiteSpace(block.Title)) html.Append(" — ").Append(Encode(block.Title));
            html.Append("</h2>");
            if (!string.IsNullOrWhiteSpace(block.Consigne)) html.Append("<div class=\"ql-editor intro\">").Append(HtmlSanitizer.Sanitize(block.Consigne)).Append("</div>");
            foreach (var question in block.Questions.OrderBy(question => question.Number))
            {
                html.Append("<div class=\"question\"><div class=\"prompt\">").Append(question.Number).Append(". <div class=\"ql-editor\">").Append(HtmlSanitizer.SanitizeMitBildern(question.Prompt)).Append("</div> (").Append(question.Points).Append(" pts)</div>");
                var isChoice = question.QuestionType is "Choice" or "SingleChoice" or "MultipleChoice" or "AudioChoice" or "AudioSingleChoice" or "AudioMultipleChoice";
                if (isChoice && question.Options.Count > 0)
                {
                    var symbol = question.QuestionType is "Choice" or "SingleChoice" or "AudioChoice" or "AudioSingleChoice"
                        ? "<span style=\"display:inline-block;width:14px;height:14px;border:1.5px solid #1e293b;border-radius:50%;vertical-align:middle;margin-right:8px;\"></span>"
                        : "<span style=\"display:inline-block;width:14px;height:14px;border:1.5px solid #1e293b;border-radius:2px;vertical-align:middle;margin-right:8px;\"></span>";
                    foreach (var option in question.Options)
                    {
                        var correct = includeSolutions && question.CorrectOptions.Contains(option);
                        html.Append("<div class=\"option\">").Append(symbol).Append(Encode(HtmlSanitizer.ToPlainText(option)));
                        if (correct) html.Append(" <span class=\"solution\">✓</span>");
                        html.Append("</div>");
                    }
                }
                else if (question.QuestionType == "GapFill")
                {
                    for (var i = 0; i < Math.Max(question.GapCount, 1); i++) html.Append("<span>__________ &nbsp;</span>");
                    if (includeSolutions && question.GapSolutions.Count > 0) html.Append("<p class=\"solution\">Solutions : ").Append(Encode(string.Join(" · ", question.GapSolutions))).Append("</p>");
                }
                else
                {
                    html.Append("<div class=\"answer\"></div><div class=\"answer\"></div><div class=\"answer\"></div>");
                    if (includeSolutions && !string.IsNullOrWhiteSpace(question.AcceptedAnswers)) html.Append("<p class=\"solution\">Solutions : ").Append(Encode(question.AcceptedAnswers)).Append("</p>");
                }
                html.Append("</div>");
            }
            html.Append("</section>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
