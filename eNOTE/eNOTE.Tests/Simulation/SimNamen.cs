namespace ENote.Tests.Simulation;

internal enum LadenArt { Salon, Institut, Barbershop, Boutique, Maquis, Pharmacie, Cabinet, Atelier, Epicerie, Cyber }

/// <summary>Artikel einer Vorlage: Produkt mit Lager, optional Sammel-Artikel (Abzug von einer Basis).</summary>
internal sealed record ArtikelVorlage(string Name, decimal Preis, bool Produkt = false, decimal Lager = 0, decimal? Einkauf = null,
    string? Basis = null, decimal? Abzug = null, decimal? Mindest = null);

internal sealed record KategorieVorlage(string Name, string? Farbe, string? Unter, ArtikelVorlage[] Artikel);

internal sealed record LadenVorlage(LadenArt Art, string Praefix, double VerkaeufeProTag, bool Sonntag, bool Anwesenheit, bool Aufenthalt,
    KategorieVorlage[] Kategorien, string[] Motive, string[] Raeume, string[] Ausgaben);

/// <summary>Erfundene Namen (französisch/togoisch/westafrikanisch) und Ladenvorlagen.</summary>
internal static class SimNamen
{
    public static readonly string[] Vornamen =
    [
        "Afi", "Akossiwa", "Ama", "Kossi", "Koffi", "Kodjo", "Yawa", "Mawuli", "Edem", "Séna", "Komla", "Abla", "Dzifa", "Enyonam",
        "Kafui", "Sélom", "Elom", "Délali", "Fafa", "Kékéli", "Awa", "Fatou", "Moussa", "Aminata", "Ibrahim", "Mariam", "Adjoa",
        "Yao", "Akouvi", "Essi", "Kokou", "Agbéko", "Améyo", "Djifa", "Nadège", "Pélagie", "Rodrigue", "Gisèle", "Aurélie", "Hervé",
    ];

    public static readonly string[] Nachnamen =
    [
        "Agbéko", "Amouzou", "Mensah", "Lawson", "Adjovi", "Kpodar", "Akakpo", "Dossou", "Ahadji", "Sossou", "Gbadamassi", "Tchalla",
        "Agbodjan", "Kouassi", "Diallo", "Ndiaye", "Traoré", "Ouédraogo", "Koné", "Fofana", "Bamba", "Hounkpatin", "Ekué", "Attiogbé",
        "d'Almeida", "de Souza", "Kponton", "Wilson", "Ayivi", "Têtêvi",
    ];

    /// <summary>Ort, Land (null = Selbstregistrierung ohne Land → Lomé) – je Laden einer.</summary>
    public static readonly (string Ort, string Land)[] Orte =
    [
        ("Bè", "Togo"), ("Tokoin", "Togo"), ("Akpakpa", "Bénin"), ("Adidogomé", "Togo"), ("Médina", "Sénégal"), ("Cocody", "Côte d'Ivoire"),
        ("Agoè", "Togo"), ("Gounghin", "Burkina Faso"), ("Hamdallaye", "Mali"), ("Akwa", "Cameroun"), ("Hédzranawoé", "Togo"),
        ("Cadjèhoun", "Bénin"), ("Plateau", "Sénégal"), ("Yopougon", "Côte d'Ivoire"), ("Kpalimé", "Togo"), ("Nyékonakpoè", "Togo"),
        ("Fidjrossè", "Bénin"), ("Treichville", "Côte d'Ivoire"), ("Bonapriso", "Cameroun"), ("Sokodé", "Togo"), ("Kara", "Togo"),
        ("Kodjoviakopé", "Togo"), ("Aného", "Togo"), ("Atakpamé", "Togo"), ("Tsévié", "Togo"), ("Bé-Kpota", "Togo"), ("Avédji", "Togo"),
        ("Djidjolé", "Togo"), ("Dapaong", "Togo"), ("Ouidah", "Bénin"), ("Porto-Novo", "Bénin"), ("Thiès", "Sénégal"),
        ("Bouaké", "Côte d'Ivoire"), ("Bobo-Dioulasso", "Burkina Faso"), ("Ségou", "Mali"), ("Limbé", "Cameroun"),
    ];

    public static string Telefon(int laden, int nr) => $"+228 9{laden % 10}{nr / 10000 % 10} {nr / 100 % 100:00} {nr % 100:00} {(laden * 7 + nr) % 100:00}";

    private static ArtikelVorlage P(string n, decimal p) => new(n, p);
    private static ArtikelVorlage W(string n, decimal p, decimal lager, decimal einkauf, decimal? mindest = 5) => new(n, p, true, lager, einkauf, Mindest: mindest);

    public static readonly LadenVorlage[] Vorlagen =
    [
        new(LadenArt.Salon, "Salon de coiffure", 7, false, true, false,
        [
            new("Coiffure", "#7c3aed", null, [P("Tresses simples", 5000), P("Tresses africaines", 12000), P("Brushing", 3000), P("Défrisage", 6000), P("Coupe enfant", 2000)]),
            new("Coiffure", null, "Mèches", [P("Pose de mèches", 8000), P("Tissage", 15000)]),
            new("Soins", "#0f8f86", null, [P("Manucure", 2500), P("Pédicure", 3000), P("Soin du visage", 7000)]),
            new("Produits", "#c2410c", null, [W("Mèches Kanekalon", 1500, 120, 900), W("Huile de coco", 2000, 60, 1200), W("Shampoing 500 ml", 3500, 40, 2200)]),
        ], ["Tresses africaines", "Brushing", "Manucure", "Défrisage"], ["Poste 1", "Poste 2", "Bac à shampoing"], ["Loyer", "Électricité", "Produits"]),
        new(LadenArt.Institut, "Institut de beauté", 5, false, true, false,
        [
            new("Soins visage", "#be185d", null, [P("Soin éclat", 10000), P("Nettoyage de peau", 8000), P("Masque hydratant", 6000)]),
            new("Corps", "#0e7490", null, [P("Massage relaxant", 15000), P("Gommage", 9000), P("Épilation jambes", 7000)]),
            new("Ongles", null, null, [P("Pose de vernis", 3000), P("Pose gel", 12000)]),
            new("Boutique", "#b45309", null, [W("Crème karité", 4500, 50, 2500), W("Savon noir", 1500, 80, 700), W("Parfum 50 ml", 15000, 20, 9000, 3)]),
        ], ["Massage relaxant", "Soin éclat", "Pose gel"], ["Cabine 1", "Cabine 2"], ["Loyer", "Produits", "Eau"]),
        new(LadenArt.Barbershop, "Barbershop", 9, true, true, false,
        [
            new("Coupes", "#2547b8", null, [P("Coupe homme", 2000), P("Coupe + barbe", 3000), P("Dégradé", 2500), P("Rasage", 1500)]),
            new("Coupes", null, "Enfants", [P("Coupe enfant", 1000)]),
            new("Produits", "#4d7c0f", null, [W("Gel coiffant", 1500, 60, 800), W("Huile à barbe", 3000, 30, 1700)]),
        ], ["Coupe homme", "Dégradé", "Coupe + barbe"], ["Fauteuil 1", "Fauteuil 2", "Fauteuil 3"], ["Loyer", "Électricité"]),
        new(LadenArt.Boutique, "Boutique", 6, false, false, false,
        [
            new("Tissus", "#7c3aed", null, [W("Pagne wax (6 yards)", 14000, 300, 9000, 30), new("Wax 1 yard", 2500, Basis: "Pagne wax (6 yards)", Abzug: 1), new("Wax 2 yards", 4800, Basis: "Pagne wax (6 yards)", Abzug: 2), new("Pagne complet", 13500, Basis: "Pagne wax (6 yards)", Abzug: 6)]),
            new("Vêtements", "#b91c1c", null, [W("Chemise homme", 7500, 40, 4500), W("Robe en pagne", 12000, 25, 7000), W("Tee-shirt", 3500, 80, 1800)]),
            new("Accessoires", null, null, [W("Sac à main", 9000, 15, 5500, 3), W("Foulard", 2500, 50, 1200)]),
        ], [], [], ["Loyer", "Transport", "Marchandises"]),
        new(LadenArt.Maquis, "Maquis", 16, true, false, false,
        [
            new("Plats", "#b91c1c", null, [P("Riz sauce arachide", 1500), P("Fufu sauce graine", 2000), P("Poulet braisé", 3500), P("Poisson braisé", 4000), P("Akoumé sauce gboma", 1500)]),
            new("Boissons", "#0369a1", null, [W("Bière 65 cl", 800, 400, 550, 48), W("Sucrerie", 500, 500, 300, 48), W("Eau minérale", 300, 600, 150, 48), W("Tchoukoutou", 300, 200, 120, 20)]),
            new("Boissons", null, "Jus locaux", [W("Bissap", 500, 150, 200, 20), W("Jus de gingembre", 500, 150, 200, 20)]),
        ], [], [], ["Marché", "Gaz", "Loyer", "Électricité"]),
        new(LadenArt.Pharmacie, "Dépôt pharmaceutique", 11, true, false, false,
        [
            new("Médicaments", "#4d7c0f", null, [W("Paracétamol 500 mg", 500, 600, 250, 50), W("Sirop contre la toux", 2500, 120, 1500, 15), W("Antipaludique (boîte)", 3500, 150, 2200, 20), W("Sels de réhydratation", 300, 300, 120, 30)]),
            new("Parapharmacie", "#0f8f86", null, [W("Gel hydroalcoolique", 1500, 100, 800), W("Moustiquaire", 4000, 40, 2500, 5), W("Thermomètre", 3000, 25, 1800, 3)]),
            new("Services", null, null, [P("Prise de tension", 500), P("Test de glycémie", 1500)]),
        ], ["Prise de tension"], [], ["Grossiste", "Loyer", "Électricité"]),
        new(LadenArt.Cabinet, "Cabinet médical", 4, false, true, true,
        [
            new("Consultations", "#2547b8", null, [P("Consultation", 5000), P("Consultation de suivi", 3000), P("Visite à domicile", 10000)]),
            new("Soins", "#0f8f86", null, [P("Pansement", 2000), P("Injection", 1500), P("Perfusion", 6000), P("Journée d'hospitalisation", 15000)]),
            new("Pharmacie", "#4d7c0f", null, [W("Antibiotique (boîte)", 4500, 120, 2800, 10), W("Soluté 500 ml", 2500, 200, 1400, 20), W("Kit de perfusion", 1500, 200, 700, 20)]),
        ], ["Consultation", "Perfusion", "Pansement", "Journée d'hospitalisation"], ["Salle d'attente", "Chambre 1", "Chambre 2", "Box de soins"], ["Matériel médical", "Loyer", "Salaires"]),
        new(LadenArt.Atelier, "Atelier de couture", 3, false, true, false,
        [
            new("Confection", "#7c3aed", null, [P("Robe sur mesure", 15000), P("Ensemble homme", 18000), P("Retouche", 2000), P("Ourlet", 1000)]),
            new("Fournitures", null, null, [W("Fil (bobine)", 500, 100, 250), W("Fermeture éclair", 300, 150, 120)]),
        ], ["Robe sur mesure", "Retouche"], ["Cabine d'essayage"], ["Fournitures", "Électricité"]),
        new(LadenArt.Epicerie, "Superette", 13, true, false, false,
        [
            new("Alimentation", "#c2410c", null, [W("Riz 5 kg", 4500, 120, 3600, 10), W("Huile 1 L", 1500, 150, 1150, 15), W("Sucre 1 kg", 900, 200, 700, 20), W("Lait en poudre", 2500, 80, 1900, 10), W("Pâtes 500 g", 500, 250, 350, 25)]),
            new("Hygiène", "#0e7490", null, [W("Savon", 400, 300, 250, 30), W("Lessive 1 kg", 1800, 90, 1300, 10)]),
            new("Recharges", null, null, [P("Recharge téléphone", 1000)]),
        ], [], [], ["Grossiste", "Transport", "Loyer"]),
        new(LadenArt.Cyber, "Cyber & Impression", 10, false, false, false,
        [
            new("Impression", "#475569", null, [P("Impression N/B (page)", 50), P("Impression couleur (page)", 200), P("Photocopie", 25), P("Reliure", 1000)]),
            new("Services", "#2547b8", null, [P("Saisie de document", 1500), P("Scan", 200), P("Heure d'internet", 500)]),
            new("Fournitures", null, null, [W("Clé USB 16 Go", 4000, 30, 2500, 3), W("Rame de papier", 3500, 40, 2600, 5)]),
        ], [], [], ["Papier et encre", "Électricité", "Internet"]),
    ];

    public static readonly string[] Notizen =
    [
        "Préfère les rendez-vous le matin.", "Allergique à certains produits, vérifier avant.", "Client fidèle, toujours ponctuel.",
        "A demandé un rappel la semaine prochaine.", "Paiement souvent par Mobile Money.", "Accompagnée de sa fille.",
        "Tension un peu élevée, à surveiller.", "Souhaite la même coupe que la dernière fois.",
    ];
}
