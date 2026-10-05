namespace GrowDiary.Web.Models;

/// <summary>
/// Ein Wasserwechsel als <b>ein Vorgang</b> (A-006, 05.10.2026).
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Bis hierhin musste Bru einen Wechsel an vier Stellen
/// getrennt erfassen: Messung mit Haken „Lösungswechsel", Eintrag unter
/// Wasserwechsel, Verbrauch unter Kosten, Zeile im Journal. Vier Eingaben für
/// eine Handlung — und beim Löschen blieb von den vier immer etwas stehen.</para>
///
/// <para><b>Was der Vorgang ist.</b> Nur die Klammer: er zeigt auf die Sätze,
/// die er in einer Transaktion angelegt hat, und nimmt sie beim Löschen mit.
/// Die Zahlen selbst stehen weiter dort, wo jeder andere Leser sie sucht —
/// der Wechsel in <c>ChangeoutEntries</c> (daraus rechnet
/// <see cref="Services.Wasserwechsel"/> den letzten Wechsel), die Messwerte in
/// <c>Measurements</c>, die Buchungen in <c>ForkVerbraeuche</c> (über
/// <see cref="Verbrauch.VorgangId"/>), die Zeile in <c>JournalEntries</c>.
/// Eine zweite Kopie der Zahlen gibt es nicht.</para>
///
/// <para>Die Verweise sind <c>null</c>, wenn es den Satz nicht gibt (keine
/// Werte „vorher", Tagebuch abgeschaltet) oder wenn ihn jemand an seiner
/// eigenen Stelle gelöscht hat (<c>ON DELETE SET NULL</c>).</para>
/// </remarks>
public sealed class WasserwechselVorgang
{
    public int Id { get; set; }
    public int GrowId { get; set; }
    public int? ChangeoutId { get; set; }
    public int? MessungVorherId { get; set; }
    public int? MessungNachherId { get; set; }
    public int? JournalId { get; set; }

    /// <summary>Anteil Osmose in Prozent bei Wasserart <see cref="WaterSource.Mixed"/>; sonst <c>null</c>.</summary>
    public double? OsmoseProzent { get; set; }

    /// <summary>Woher die Werte „vorher" stammen: <c>Sensor</c>, <c>Hand</c> oder <c>gemischt</c>; <c>null</c> ohne Werte.</summary>
    public string? VorherHerkunft { get; set; }

    /// <summary>Zeitpunkt der Sensorwerte „vorher" (UTC), falls welche übernommen wurden.</summary>
    public DateTime? VorherSensorZeitUtc { get; set; }

    public DateTime ErstelltAmUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Eine Verbrauchsbuchung, die der Vorgang anlegen soll.</summary>
/// <param name="ArtikelId">Ein bestehender Artikel — oder <c>null</c>, dann gilt <paramref name="WasserArtikelName"/>.</param>
/// <param name="WasserArtikelName">„Leitungswasser" bzw. „Osmosewasser": wird gesucht und beim ersten Mal angelegt (Einheit L).</param>
/// <param name="Menge">In der Einheit des Artikels, größer 0.</param>
public sealed record VorgangBuchungEntwurf(int? ArtikelId, string? WasserArtikelName, double Menge);

/// <summary>Alles, was ein Wasserwechsel-Vorgang in EINER Transaktion anlegt.</summary>
public sealed class WasserwechselVorgangEntwurf
{
    public required int GrowId { get; init; }
    public required ChangeoutEntry Wechsel { get; init; }
    public Measurement? Vorher { get; init; }
    public Measurement? Nachher { get; init; }
    public IReadOnlyList<VorgangBuchungEntwurf> Buchungen { get; init; } = [];
    public JournalEntry? Tagebuch { get; init; }
    public double? OsmoseProzent { get; init; }
    public string? VorherHerkunft { get; init; }
    public DateTime? VorherSensorZeitUtc { get; init; }
}
