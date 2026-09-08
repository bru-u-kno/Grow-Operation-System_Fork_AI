namespace GrowDiary.Web.Models;

// Fork AI (forkai.6): Modelle der Kosten-Seite. Eigene Datei, damit der
// Abgleich mit dem Original konfliktfrei bleibt.

/// <summary>
/// Etwas, das aufgebraucht und nachgekauft wird — CO₂-Flasche, Dünger,
/// pH-Down. Kein Hardware-Artikel: der geht kaputt, das hier geht leer.
/// </summary>
public sealed class Verbrauchsartikel
{
    public int Id { get; set; }

    /// <summary>Anzeigename — so heißt der Artikel in Listen, Karten und im Journal.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Wer es herstellt (forkai.8), z. B. „Linde", „Canna".</summary>
    public string? Hersteller { get; set; }

    /// <summary>Die Produktbezeichnung des Herstellers (forkai.8), z. B. „Aqua Vega A".</summary>
    public string? Produkt { get; set; }

    /// <summary>Preis eines vollen Gebindes (forkai.8) — belegt die Kosten beim Erfassen vor.</summary>
    public double? PreisEur { get; set; }

    /// <summary>Einheit der Menge, wie der Nutzer sie nennt: „kg", „L", „ml".</summary>
    public string Einheit { get; set; } = "kg";

    /// <summary>Menge eines vollen Gebindes, damit die Erfassung vorbelegt werden kann.</summary>
    public double? Gebinde { get; set; }
    public int? TentId { get; set; }
    public string? Notiz { get; set; }
    public bool Aktiv { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Eine Füllung / ein Nachkauf eines Verbrauchsartikels.</summary>
public sealed class Nachfuellung
{
    public int Id { get; set; }
    public int ArtikelId { get; set; }
    public DateTime ZeitpunktUtc { get; set; } = DateTime.UtcNow;
    public double Menge { get; set; }
    public double? KostenEur { get; set; }
    public int? GrowId { get; set; }
    public string? Notiz { get; set; }

    /// <summary>Wann diese Füllung aufgebraucht war. Null = läuft noch.</summary>
    public DateTime? LeerAmUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Warum ein Zählerstand festgehalten wurde.</summary>
public enum ZaehlerAnlass
{
    /// <summary>Täglicher Takt des Workers.</summary>
    Tag,
    /// <summary>Ein neuer Grow ist der laufende geworden.</summary>
    GrowStart,
    /// <summary>Die Phase des laufenden Grows hat gewechselt.</summary>
    Phase,
    /// <summary>Von Hand oder nach dem Speichern der Strom-Quelle.</summary>
    Manuell,
}

/// <summary>
/// Ein Stand des kWh-Zählers aus Home Assistant — samt dem Grow und der Phase,
/// die zu dem Zeitpunkt liefen. Aus den Differenzen entstehen Strom je Grow
/// und je Phase, unabhängig davon, ob jemand in HA einen Helfer zurücksetzt.
/// </summary>
public sealed class Zaehlerstand
{
    public int Id { get; set; }
    public DateTime ZeitpunktUtc { get; set; } = DateTime.UtcNow;
    public double Kwh { get; set; }
    public ZaehlerAnlass Anlass { get; set; } = ZaehlerAnlass.Tag;
    public int? GrowId { get; set; }

    /// <summary>Phasenname (GrowStage) zum Zeitpunkt des Stands; null ohne laufenden Grow.</summary>
    public string? Phase { get; set; }
}

/// <summary>Welche HA-Entitäten den Strom liefern. Der Preis liegt weiter in den Kosten-Einstellungen.</summary>
public sealed class StromQuelle
{
    /// <summary>kWh-Gesamtzähler (state_class total_increasing), z. B. der DECT-Steckdose.</summary>
    public string? ZaehlerEntityId { get; set; }

    /// <summary>Aktuelle Leistung in W — nur für die Anzeige.</summary>
    public string? LeistungEntityId { get; set; }
}
