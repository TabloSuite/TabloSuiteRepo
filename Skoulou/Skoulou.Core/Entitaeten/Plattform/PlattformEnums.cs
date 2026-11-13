namespace Skoulou.Core.Entitaeten;

/// <summary>Geschlecht eines Kontos (Werte wie in der Alt-App).</summary>
public enum Geschlecht
{
    Divers = 0,
    Maennlich = 1,
    Weiblich = 2
}

/// <summary>Zustand eines Kontos; nur <see cref="Actif"/> darf sich anmelden.</summary>
public enum KontoStatus
{
    EnAttente = 0,
    Actif = 1,
    Bloque = 2,
    Archive = 3
}

/// <summary>Zustand einer Schule (Mandant).</summary>
public enum SchulStatus
{
    EnAttente = 0,
    Active = 1,
    Refusee = 2,
    Suspendue = 3
}

/// <summary>Fortlaufende Nummernkreise.</summary>
public enum NummernArt
{
    Quittung = 0,
    Ecriture = 1,
    Bulletin = 2,
    Plattformrechnung = 3,
    Matrikel = 4
}

/// <summary>Gewicht einer Annonce.</summary>
public enum AnkuendigungsStufe
{
    Info = 0,
    Wichtig = 1
}

/// <summary>Zielgruppen einer Annonce; « Visiteurs » = nicht angemeldete Besucher.</summary>
[Flags]
public enum ZielRollen
{
    Keine = 0,
    Visiteurs = 1,
    SuperAdmin = 2,
    Responsable = 4,
    Direktor = 8,
    Koordinator = 16,
    Lehrer = 32,
    Schueler = 64,
    Eltern = 128,
    Alle = Visiteurs | SuperAdmin | Responsable | Direktor | Koordinator | Lehrer | Schueler | Eltern
}

/// <summary>Richtung einer Nachricht der Messagerie.</summary>
public enum NachrichtRichtung
{
    Eingang = 0,
    Ausgang = 1
}

/// <summary>Versandzustand einer Mail in der Outbox.</summary>
public enum MailStatus
{
    Offen = 0,
    Gesendet = 1,
    Fehlgeschlagen = 2,
    Verworfen = 3
}

/// <summary>Art eines gespeicherten Mediums.</summary>
public enum MedienArt
{
    Bild = 0,
    Video = 1,
    Dokument = 2
}

/// <summary>Verwendungszweck eines Mediums (bestimmt Pfad und Grenzen).</summary>
public enum MedienZweck
{
    Inhalt = 0,
    Logo = 1,
    Foto = 2,
    Beleg = 3,
    Video = 4
}
