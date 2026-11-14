using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Noten;

/// <summary>Empfänger der Benachrichtigung über ein Bulletin (Schüler/Eltern mit Konto oder nur Elternadresse im Profil).</summary>
public sealed record BulletinEmpfaenger(Guid BulletinId, string Email, string Anrede, string Kind, bool MitKonto, bool IstSchueler);

/// <summary>
/// Benachrichtigung der Familien bei Veröffentlichung/Berichtigung (Bauplan §11.12/§11.16): nur an vorhandene Adressen,
/// ohne Noten im Text (nur Hinweis + Link), über die Outbox; ein Fehler bricht die Veröffentlichung nie ab.
/// </summary>
public sealed class NotenMails(MailAusgangService mails, IOptions<AppOptionen> app, ILogger<NotenMails> logger)
{
    /// <summary>Empfänger je Bulletin (Schüler-, Eltern- und Profiladressen, je Bulletin ohne Doppelte).</summary>
    internal static async Task<List<BulletinEmpfaenger>> EmpfaengerAsync(SkoulouDbContext db, IReadOnlyCollection<Guid> bulletinIds, CancellationToken ct)
    {
        var basis = await (from b in db.Bulletins.AsNoTracking()
                           join e in db.Einschreibungen on b.EinschreibungId equals e.Id
                           join s in db.Users on e.SchuelerId equals s.Id
                           where bulletinIds.Contains(b.Id)
                           select new
                           {
                               b.Id, SchuelerId = s.Id, s.Vorname, s.Nachname, s.Email,
                               ProfilMail = db.SchuelerProfile.Where(p => p.KontoId == s.Id).Select(p => p.ElternEmail).FirstOrDefault()
                           }).ToListAsync(ct);
        var schuelerIds = basis.Select(b => b.SchuelerId).ToList();
        var eltern = await (from ek in db.ElternKinder.AsNoTracking()
                            join u in db.Users on ek.ElternId equals u.Id
                            where schuelerIds.Contains(ek.SchuelerId) && u.Email != null && u.Status == Core.Entitaeten.KontoStatus.Actif
                            select new { ek.SchuelerId, u.Email, u.Vorname }).ToListAsync(ct);
        var liste = new List<BulletinEmpfaenger>();
        foreach (var b in basis)
        {
            var kind = $"{b.Vorname} {b.Nachname}".Trim();
            var adressen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Hinzu(string? mail, string anrede, bool konto, bool schueler)
            {
                if (MailAusgangService.IstAdresse(mail) && adressen.Add(mail!.Trim())) liste.Add(new BulletinEmpfaenger(b.Id, mail.Trim(), anrede, kind, konto, schueler));
            }
            Hinzu(b.Email, b.Vorname, true, true);
            foreach (var el in eltern.Where(x => x.SchuelerId == b.SchuelerId)) Hinzu(el.Email, el.Vorname, true, false);
            Hinzu(b.ProfilMail, "Madame, Monsieur", false, false);
        }
        return liste;
    }

    public async Task<int> BulletinVerfuegbarAsync(IEnumerable<BulletinEmpfaenger> empfaenger, string periode, string jahr, string schule, bool rectificatif,
        CancellationToken ct = default)
    {
        var gesendet = 0;
        foreach (var e in empfaenger)
        {
            try
            {
                var ziel = e.IstSchueler ? "/eleve/bulletins" : e.MitKonto ? "/parents/bulletins" : "/auth/connexion";
                var text = rectificatif
                    ? $"Un bulletin rectifié du {periode} ({jahr}) de {e.Kind} est disponible : il remplace le bulletin publié précédemment."
                    : $"Le bulletin du {periode} ({jahr}) de {e.Kind} est disponible.";
                var inhalt = MailLayout.Absatz(e.IstSchueler ? $"Bonjour {e.Anrede}," : $"Bonjour {e.Anrede},")
                    + MailLayout.Absatz($"« {schule} » vous informe : {text}")
                    + MailLayout.Absatz(e.MitKonto
                        ? "Vous pouvez le consulter et le télécharger dans votre espace SKOULOU."
                        : "Vous pouvez le demander à l'établissement ou le consulter dans l'espace SKOULOU de la famille.")
                    + MailLayout.Knopf(app.Value.Url(ziel), e.MitKonto ? "Voir le bulletin" : "Se connecter à SKOULOU");
                var titel = rectificatif ? $"Bulletin rectifié – {e.Kind}" : $"Bulletin disponible – {e.Kind}";
                if (await mails.EinreihenAsync(e.Email, titel, inhalt, rectificatif ? "bulletin-rectifie" : "bulletin-publie", ct: ct) is not null) gesendet++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Benachrichtigung zu Bulletin {Id} nicht eingereiht.", e.BulletinId);
            }
        }
        return gesendet;
    }
}
