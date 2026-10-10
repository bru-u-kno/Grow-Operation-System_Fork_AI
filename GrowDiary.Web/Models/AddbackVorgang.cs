namespace GrowDiary.Web.Models;

/// <summary>
/// Ein Nachfüllen (Addback) als <b>ein Vorgang</b> — A-006, Etappe 3 (05.10.2026).
/// </summary>
/// <remarks>
/// <para>Dasselbe Muster wie <see cref="WasserwechselVorgang"/>: nur die
/// Klammer. Die Zahlen stehen dort, wo jeder andere Leser sie sucht — der
/// Eintrag in <c>AddbackLogs</c> (daraus liest die Addback-Übersicht und das
/// Tagebuch), die Messwerte in <c>Measurements</c>, die Buchungen in
/// <c>ForkVerbraeuche</c> (über <see cref="Verbrauch.AddbackVorgangId"/>), die
/// Zeile in <c>JournalEntries</c> (Art „Fütterung").</para>
///
/// <para>Die Verweise sind <c>null</c>, wenn es den Satz nicht gibt oder ihn
/// jemand an seiner eigenen Stelle gelöscht hat (<c>ON DELETE SET NULL</c>).</para>
/// </remarks>
public sealed class AddbackVorgang
{
    public int Id { get; set; }
    public int GrowId { get; set; }
    public int? AddbackLogId { get; set; }
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

/// <summary>
/// Die automatische Nachmessung eines Nachfüllens: nach einer eingestellten Zeit liest der Fork
/// EC und pH aus den Sensoren und hängt sie als Messung „nachher" an den Vorgang.
/// </summary>
public sealed class AddbackNachmessung
{
    public const string Offen = "offen";
    public const string Erledigt = "erledigt";
    /// <summary>Kein (plausibler) Sensorwert zur Zeit der Nachmessung — es bleibt bei „nachher" von Hand.</summary>
    public const string OhneWert = "ohneWert";
    /// <summary>Es gab inzwischen eine Messung „nachher" von Hand — sie hat Vorrang.</summary>
    public const string Uebersprungen = "uebersprungen";

    public int Id { get; set; }
    public int VorgangId { get; set; }
    public int GrowId { get; set; }
    public DateTime FaelligUtc { get; set; }
    public string Status { get; set; } = Offen;
    public DateTime? ErledigtUtc { get; set; }
    public string? Hinweis { get; set; }
}

/// <summary>Alles, was ein Nachfüll-Vorgang in EINER Transaktion anlegt.</summary>
public sealed class AddbackVorgangEntwurf
{
    public required int GrowId { get; init; }
    public required AddbackLogEntry Eintrag { get; init; }
    public Measurement? Vorher { get; init; }
    public Measurement? Nachher { get; init; }
    public IReadOnlyList<VorgangBuchungEntwurf> Buchungen { get; init; } = [];
    public JournalEntry? Tagebuch { get; init; }
    public double? OsmoseProzent { get; init; }
    public string? VorherHerkunft { get; init; }
    public DateTime? VorherSensorZeitUtc { get; init; }

    /// <summary>Wann die automatische Nachmessung fällig ist (UTC) — <c>null</c> ohne Nachmessung.</summary>
    public DateTime? NachmessungFaelligUtc { get; init; }
}
