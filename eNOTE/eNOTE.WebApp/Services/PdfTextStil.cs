using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ENote.WebApp.Services;

/// <summary>
/// Gemeinsamer Grundstil der eNOTE-PDFs (Bon, Étiquettes, Rapport financier). Die Standardschrift bildet sonst Ligaturen
/// (« fi », « ffi », « ti »), deren Textebene falsch ist – Kopieren, Suchen und Vorlesen im PDF ergaben « Bénéﬁce » bzw.
/// Fremdzeichen (Klicktest 22.11.2026, gleicher Befund wie in SKOULOU). Ohne Standard-Ligaturen stehen nur echte Buchstaben im PDF.
/// </summary>
public static class PdfTextStil
{
    public static TextStyle OhneLigaturen(TextStyle t) => t.DisableFontFeature(FontFeatures.StandardLigatures);
}
