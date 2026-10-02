namespace GrowDiary.Web.Models;

/// <summary>
/// Ein eigenes Sollwert-Profil: die Erfahrungswerte des Nutzers.
/// </summary>
/// <remarks>
/// Gespeichert wird nur, was er WIRKLICH geändert hat — nicht der ganze
/// Wertesatz. Wer bloß den pH in der Blüte anpasst, bekommt weiterhin jede
/// spätere Verbesserung an EC, VPD und allem anderen aus der Wissensbasis. Eine
/// Vollkopie hätte ihn beim ersten Speichern von allen Updates abgeschnitten,
/// ohne dass er es gemerkt hätte.
/// </remarks>
public sealed class SetpointProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Worauf es aufbaut — „rdwc-default" oder „dwc-default".</summary>
    public string BaseProfileId { get; set; } = "rdwc-default";

    /// <summary>
    /// Nur die abweichenden Werte, nach Phase und Feld.
    /// Beispiel: <c>{"Veg":{"phMin":5.8,"phMax":6.0}}</c>
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> Overrides { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Wie viele Werte der Nutzer angefasst hat — für die Übersicht.</summary>
    public int ChangedValueCount => Overrides.Values.Sum(stage => stage.Count);

    /// <summary>Die Kennung, unter der Grows und Systeme auf dieses Profil zeigen.</summary>
    public string ReferenceId => Reference(Id);

    /// <summary>„custom:7" — so unterscheidbar von „rdwc-default".</summary>
    public static string Reference(int id) => $"{Prefix}{id}";

    public const string Prefix = "custom:";

    /// <summary>Liest die Id aus „custom:7"; null, wenn es kein eigenes Profil ist.</summary>
    public static int? IdFromReference(string? reference)
        => reference is not null && reference.StartsWith(Prefix, StringComparison.Ordinal)
           && int.TryParse(reference[Prefix.Length..], out var id)
            ? id
            : null;

    /// <summary>
    /// Der Phasenname so, wie <see cref="Services.SetpointProfileResolver.Apply"/> ihn
    /// sucht (<c>Flower</c>) — oder <c>null</c>, wenn es keine Phase ist.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (Fork AI, 02.10.2026).</b> Die Prüfung beim Speichern war
    /// groß/klein-unabhängig, gespeichert wurde der Name roh, und <c>Apply</c>
    /// sucht genau <c>GrowStage.ToString()</c>. „flower" ging also durch, stand in
    /// der Ablage und wirkte nie — die Werte erschienen nicht einmal als
    /// „geändert" in der Tabelle.</para>
    /// <para>Nur Namen, keine Zahlen: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// nähme auch „4" als <c>Flower</c> an.</para>
    /// </remarks>
    public static string? PhaseKanonisch(string? name)
        => name is null
            ? null
            : Enum.GetNames<GrowStage>().FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Die Abweichungen mit kanonischen Phasennamen — beim Speichern und beim Lesen.
    /// </summary>
    /// <remarks>
    /// Stehen „flower" und „Flower" nebeneinander (möglich in Ablagen vor dem
    /// 02.10.2026), werden sie zusammengelegt; bei gleichem Feld gewinnt der schon
    /// richtig geschriebene Eintrag, denn nur der hat bisher gewirkt. Was keine
    /// Phase ist, bleibt unter seinem Namen stehen — die Prüfung beim Speichern
    /// lehnt es ab, und beim Lesen soll nichts still verschwinden.
    /// </remarks>
    public static Dictionary<string, Dictionary<string, double>> PhasenNormalisiert(
        IReadOnlyDictionary<string, Dictionary<string, double>>? roh)
    {
        var ergebnis = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        if (roh is null) return ergebnis;

        // Richtig geschriebene zuerst, damit sie bei Doppelungen gewinnen.
        foreach (var (name, felder) in roh.OrderBy(paar => PhaseKanonisch(paar.Key) == paar.Key ? 0 : 1))
        {
            var schluessel = PhaseKanonisch(name) ?? name;
            if (!ergebnis.TryGetValue(schluessel, out var ziel))
            {
                ziel = new Dictionary<string, double>(StringComparer.Ordinal);
                ergebnis[schluessel] = ziel;
            }

            foreach (var (feld, wert) in felder)
            {
                ziel.TryAdd(feld, wert);
            }
        }

        return ergebnis;
    }

    /// <summary>Die Werte einer Phase unter den Feldnamen der Profiltabelle.</summary>
    /// <remarks>
    /// Eine Stelle für die Zuordnung Feld → Wert: die Tabelle der Oberfläche und
    /// die Paarprüfung gegen das Basisprofil lesen beide hier.
    /// </remarks>
    public static Dictionary<string, double> WerteAus(Services.HydroTargetValues t) => new(StringComparer.Ordinal)
    {
        ["phMin"] = t.PhMin, ["phMax"] = t.PhMax,
        ["ecMin"] = t.EcMin, ["ecMax"] = t.EcMax,
        ["orpMin"] = t.OrpMin, ["orpMax"] = t.OrpMax,
        ["waterTempDayC"] = t.WaterTempDayC, ["waterTempNightC"] = t.WaterTempNightC,
        ["vpdMin"] = t.VpdMin, ["vpdMax"] = t.VpdMax,
        ["ppfdMin"] = t.PpfdMin, ["ppfdMax"] = t.PpfdMax,
        ["co2Min"] = t.Co2Min, ["co2Max"] = t.Co2Max,
    };

    /// <summary>Die Felder, die ein Profil je Phase kennt — Reihenfolge wie in der Tabelle.</summary>
    public static readonly IReadOnlyList<string> Fields =
    [
        "phMin", "phMax",
        "ecMin", "ecMax",
        "orpMin", "orpMax",
        "waterTempDayC", "waterTempNightC",
        "vpdMin", "vpdMax",
        "ppfdMin", "ppfdMax",
        "co2Min", "co2Max",
    ];
}
