using System.Buffers.Binary;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;

namespace Skoulou.Tests.Paedagogik;

/// <summary>Versuchsregel, Lösungsanzeige, Examenszeit, Score, Lückentext und Medienprüfung (rein, ohne DB).</summary>
public class RegelnTests
{
    private static readonly DateTime Jetzt = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Versuchsregel_Anzahl_und_Verbleibend()
    {
        Assert.True(VersuchsRegel.DarfNeuenVersuch(null, 50));
        Assert.True(VersuchsRegel.DarfNeuenVersuch(2, 1));
        Assert.False(VersuchsRegel.DarfNeuenVersuch(2, 2));
        Assert.Null(VersuchsRegel.Verbleibend(null, 3));
        Assert.Equal(0, VersuchsRegel.Verbleibend(1, 4));
        Assert.Equal("1 seul essai", VersuchsRegel.Bezeichnung(1, VersuchsWertung.Premier));
        Assert.Equal("Essais illimités, le meilleur essai compte", VersuchsRegel.Bezeichnung(null, VersuchsWertung.Meilleur));
    }

    [Fact]
    public void Versuchsregel_Wertung_Meilleur_Premier_Dernier()
    {
        var versuche = new[] { new VersuchsWert(2, 8, 10), new VersuchsWert(1, 5, 10), new VersuchsWert(3, 6, 10), new VersuchsWert(4, 8, 10) };
        Assert.Equal(1, VersuchsRegel.Gewertet(versuche, VersuchsWertung.Premier)!.Nr);
        Assert.Equal(4, VersuchsRegel.Gewertet(versuche, VersuchsWertung.Dernier)!.Nr);
        // Bester Anteil, bei Gleichstand der frühere Versuch.
        Assert.Equal(2, VersuchsRegel.Gewertet(versuche, VersuchsWertung.Meilleur)!.Nr);
        // Anteil statt absolute Punkte (Revision mit anderer Punktzahl).
        Assert.Equal(6, VersuchsRegel.Gewertet([new VersuchsWert(5, 9, 20), new VersuchsWert(6, 5, 5)], VersuchsWertung.Meilleur)!.Nr);
        Assert.Null(VersuchsRegel.Gewertet([], VersuchsWertung.Meilleur));
    }

    [Fact]
    public void Loesungen_Immediat_ApresFrist_Jamais()
    {
        Assert.True(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.Immediat, Jetzt, null, frageBeantwortet: true, versuchAbgeschlossen: false));
        Assert.False(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.Immediat, Jetzt, null, false, false));
        Assert.False(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.Jamais, Jetzt.AddYears(1), Jetzt, true, true));
        Assert.False(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.ApresFrist, Jetzt, Jetzt.AddHours(1), true, true));
        Assert.True(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.ApresFrist, Jetzt, Jetzt, false, false));
        // Ohne Frist: nach der eigenen Abgabe.
        Assert.False(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.ApresFrist, Jetzt, null, true, false));
        Assert.True(LoesungsRegel.Sichtbar(LoesungsSichtbarkeit.ApresFrist, Jetzt, null, true, true));
    }

    [Fact]
    public void Examen_Deadline_Fenster_Karenz()
    {
        var frist = Jetzt.AddMinutes(30);
        Assert.Equal(Jetzt.AddMinutes(30), PruefungsZeit.Deadline(Jetzt, 60, frist));
        Assert.Equal(Jetzt.AddMinutes(45), PruefungsZeit.Deadline(Jetzt, 45, frist.AddHours(2)));
        Assert.Equal(Jetzt.AddMinutes(45), PruefungsZeit.Deadline(Jetzt, 45, null));
        Assert.False(PruefungsZeit.Startbar(Jetzt, Jetzt.AddMinutes(1), null));
        Assert.True(PruefungsZeit.Startbar(Jetzt, Jetzt, frist));
        Assert.False(PruefungsZeit.Startbar(frist, Jetzt, frist));

        var deadline = Jetzt;
        Assert.True(PruefungsZeit.AnnahmeOffen(Jetzt.AddSeconds(30), deadline));
        Assert.False(PruefungsZeit.AnnahmeOffen(Jetzt.AddSeconds(31), deadline));
        Assert.False(PruefungsZeit.AutoAbgabeFaellig(Jetzt.AddSeconds(30), deadline));
        Assert.True(PruefungsZeit.AutoAbgabeFaellig(Jetzt.AddSeconds(31), deadline));
        Assert.False(PruefungsZeit.AutoAbgabeFaellig(Jetzt.AddDays(9), null));
        Assert.Equal(TimeSpan.Zero, PruefungsZeit.Restzeit(Jetzt.AddMinutes(1), Jetzt));
        Assert.Equal("1 h 05 min", PruefungsZeit.DauerText(65));
        Assert.Equal("45 min", PruefungsZeit.DauerText(45));
    }

    [Fact]
    public void Score_punktgewichtet_und_Farbstufen()
    {
        Assert.Equal(60m, ScoreRechner.Gesamt([(10m, 10m), (0m, 10m), (8m, 10m)]));
        // Punktgewichtet: großes Examen wiegt mehr als eine kleine Übung.
        Assert.Equal(25m, ScoreRechner.Gesamt([(0m, 30m), (10m, 10m)]));
        // Über- und Unterläufe werden begrenzt, Einträge ohne Höchstpunkte ignoriert.
        Assert.Equal(50m, ScoreRechner.Gesamt([(15m, 10m), (-3m, 10m), (5m, 0m)]));
        Assert.Null(ScoreRechner.Gesamt([]));
        Assert.Equal(33.33m, ScoreRechner.Prozent(1, 3));
        Assert.Equal("rot", ScoreRechner.Stufe(49.99m));
        Assert.Equal("hellrot", ScoreRechner.Stufe(50m));
        Assert.Equal("hellgruen", ScoreRechner.Stufe(75m));
        Assert.Equal("gruen", ScoreRechner.Stufe(75.01m));
        Assert.Equal("neutral", ScoreRechner.Stufe(null));
    }

    [Fact]
    public void Lueckentext_Marker_werden_beim_Speichern_geprueft()
    {
        Assert.Null(LueckenText.Pruefe("Le [[1]] mange la [[2]].", 2));
        Assert.Contains("[[1]]", LueckenText.Pruefe("Pas de trou.", 1));
        Assert.Contains("plusieurs fois", LueckenText.Pruefe("[[1]] et [[1]]", 2));
        Assert.Contains("sans en sauter", LueckenText.Pruefe("[[1]] et [[3]]", 2));
        Assert.Contains("2 trou(s)", LueckenText.Pruefe("[[1]] et [[2]]", 3));
        var teile = LueckenText.Zerlege("A [[1]] B [[2]]");
        Assert.Equal(["A ", null, " B ", null], teile.Select(t => t.Html));
        Assert.Equal([null, 1, null, 2], teile.Select(t => t.Nr));
        Assert.Equal("A ___ B ___", LueckenText.MitStrichen("A [[1]] B [[ 2 ]]", "___"));
    }

    [Fact]
    public void Medien_Typ_ueber_Magic_Bytes_und_Groesse()
    {
        Assert.Equal("image/png", MedienPruefung.Erkenne([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0])!.ContentType);
        Assert.Equal("image/jpeg", MedienPruefung.Erkenne([0xFF, 0xD8, 0xFF, 0xE0])!.ContentType);
        Assert.Equal("image/gif", MedienPruefung.Erkenne("GIF89a......"u8)!.ContentType);
        Assert.Equal("image/webp", MedienPruefung.Erkenne("RIFF\0\0\0\0WEBPVP8 "u8)!.ContentType);
        Assert.Equal(MedienArt.Video, MedienPruefung.Erkenne("\0\0\0\u0018ftypmp42"u8)!.Art);
        Assert.Equal("video/quicktime", MedienPruefung.Erkenne("\0\0\0\u0014ftypqt  "u8)!.ContentType);
        Assert.Null(MedienPruefung.Erkenne("<script>alert"u8));
        Assert.Null(MedienPruefung.PruefeGroesse(MedienArt.Bild, MedienPruefung.MaxBildBytes));
        Assert.NotNull(MedienPruefung.PruefeGroesse(MedienArt.Bild, MedienPruefung.MaxBildBytes + 1));
        Assert.NotNull(MedienPruefung.PruefeGroesse(MedienArt.Video, 0));
        Assert.Null(MedienPruefung.PruefeGroesse(MedienArt.Video, 200L * 1024 * 1024));
    }

    [Fact]
    public void Videodauer_aus_mvhd_auch_wenn_moov_am_Ende()
    {
        using var strom = Mp4(zeitskala: 1000, dauer: 95_500);
        Assert.Equal(95.5, MedienPruefung.Mp4Dauer(strom));
        using var kaputt = new MemoryStream("\0\0\0\u0018ftypmp42xxxxxxxx"u8.ToArray());
        Assert.Null(MedienPruefung.Mp4Dauer(kaputt));
    }

    /// <summary>Minimales MP4: ftyp, mdat, moov(mvhd v0).</summary>
    internal static MemoryStream Mp4(uint zeitskala, uint dauer)
    {
        var ms = new MemoryStream();
        void Box(string typ, byte[] inhalt)
        {
            var kopf = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(kopf, (uint)(8 + inhalt.Length));
            System.Text.Encoding.ASCII.GetBytes(typ).CopyTo(kopf, 4);
            ms.Write(kopf);
            ms.Write(inhalt);
        }
        Box("ftyp", "mp42\0\0\0\0isom"u8.ToArray());
        Box("mdat", new byte[64]);
        var mvhd = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12), zeitskala);
        BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16), dauer);
        var mvhdBox = new MemoryStream();
        var k = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(k, (uint)(8 + mvhd.Length));
        "mvhd"u8.CopyTo(k.AsSpan(4));
        mvhdBox.Write(k);
        mvhdBox.Write(mvhd);
        Box("moov", mvhdBox.ToArray());
        ms.Position = 0;
        return ms;
    }
}
