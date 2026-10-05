using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>
/// Ein Wasserwechsel als ein Vorgang (A-006): Wechsel, Messung vorher und
/// nachher, Verbrauch und Tagebuchzeile mit einem Speichern.
/// </summary>
public sealed class WasserwechselVorgangRequest
{
    /// <summary>Wann der Wechsel war, Ortszeit <c>yyyy-MM-ddTHH:mm</c>. Leer = jetzt.</summary>
    public string? ZeitpunktLokal { get; set; }

    /// <summary>Komplett- oder Teilwechsel.</summary>
    public ChangeoutKind Art { get; set; } = ChangeoutKind.Full;

    /// <summary>Neu angesetzte Liter — Pflicht, größer 0.</summary>
    public double? Liter { get; set; }

    /// <summary>Womit angesetzt wurde.</summary>
    public WaterSource Wasser { get; set; } = WaterSource.Tap;

    /// <summary>Nur bei Mischung: Anteil Osmose 0–100 %.</summary>
    public double? OsmoseProzent { get; set; }

    /// <summary>EC des Ausgangswassers, mS/cm (Vorschlag oder selbst gemessen).</summary>
    public double? WasserEcMsCm { get; set; }

    /// <summary>Werte vor dem Wechsel — vom Sensor übernommen oder von Hand. Leer = keine Messung „vorher".</summary>
    public VorgangMessungRequest? Vorher { get; set; }

    /// <summary>Werte nach dem Ansetzen. Leer = keine Messung „nachher".</summary>
    public VorgangMessungRequest? Nachher { get; set; }

    /// <summary>Was als Verbrauch gebucht wird.</summary>
    public List<VorgangBuchungRequest> Buchungen { get; set; } = [];

    /// <summary>Ob der Wechsel die Wasserwechsel-Erinnerung neu startet. Standard ja.</summary>
    public bool ErinnerungNeuStarten { get; set; } = true;

    /// <summary>Notiz zum Wechsel; steht am Wechsel.</summary>
    public string? Notiz { get; set; }

    /// <summary>Die Tagebuchzeile — oder <c>null</c>, wenn „Ins Tagebuch" aus ist.</summary>
    public VorgangTagebuchRequest? Tagebuch { get; set; }
}

/// <summary>Die Messwerte rund um den Wechsel — dieselben Felder und Sperren wie die Messung.</summary>
public sealed class VorgangMessungRequest
{
    /// <summary>Zeitpunkt, Ortszeit <c>yyyy-MM-ddTHH:mm</c>. Leer = Zeitpunkt des Wechsels (vorher: eine Minute davor).</summary>
    public string? ZeitpunktLokal { get; set; }

    /// <summary><c>Sensor</c>, <c>Hand</c> oder <c>gemischt</c> — woher die Werte stammen.</summary>
    public string? Herkunft { get; set; }

    /// <summary>Zeit des übernommenen Sensorwerts (UTC), falls einer übernommen wurde.</summary>
    public DateTime? SensorZeitUtc { get; set; }

    public double? ReservoirEc { get; set; }
    public double? ReservoirPh { get; set; }
    public double? ReservoirWaterTempC { get; set; }
    public double? DissolvedOxygenMgL { get; set; }
    public double? OrpMv { get; set; }
}

/// <summary>Eine Buchung: entweder ein Artikel, oder das Wasser (dann sucht bzw. legt der Vorgang den Artikel an).</summary>
public sealed class VorgangBuchungRequest
{
    public int? ArtikelId { get; set; }

    /// <summary><c>Tap</c> = Leitungswasser, <c>RO</c> = Osmosewasser. Nur ohne <see cref="ArtikelId"/>.</summary>
    public WaterSource? Wasser { get; set; }

    /// <summary>In der Einheit des Artikels, größer 0. Wasser in Litern.</summary>
    public double Menge { get; set; }
}

/// <summary>Die Zeile fürs Tagebuch, so wie die Vorschau sie gezeigt hat.</summary>
public sealed class VorgangTagebuchRequest
{
    public string? Titel { get; set; }
    public string? Text { get; set; }
}

/// <summary>Eine gebuchte Zeile des Vorgangs.</summary>
public sealed record VorgangBuchungDto(int Id, int ArtikelId, string ArtikelName, string Einheit, double Menge);

/// <summary>Ein gespeicherter Vorgang mit allem, was er angelegt hat.</summary>
public sealed record WasserwechselVorgangDto(
    int Id,
    int GrowId,
    DateTime ErstelltAmUtc,
    ChangeoutDto? Wechsel,
    MeasurementDto? Vorher,
    MeasurementDto? Nachher,
    IReadOnlyList<VorgangBuchungDto> Buchungen,
    JournalEntryDto? Tagebuch,
    double? OsmoseProzent,
    string? VorherHerkunft,
    DateTime? VorherSensorZeitUtc);

/// <summary>Ein Sensorwert mit seiner Zeit.</summary>
public sealed record SensorWertDto(double Wert, DateTime ZeitUtc);

/// <summary>
/// Was die Sensoren kurz vor einem Zeitpunkt zeigten — für „vorher" im Ablauf.
/// </summary>
/// <param name="FensterMinuten">Wie weit zurück gesucht wurde.</param>
/// <param name="Hinweis">Warum nichts da ist (kein Zelt, keine Werte im Fenster …), sonst <c>null</c>.</param>
public sealed record WasserwechselSensorDto(
    DateTime ZeitpunktUtc,
    int FensterMinuten,
    SensorWertDto? Ec,
    SensorWertDto? Ph,
    SensorWertDto? WasserTemp,
    string? Hinweis);
