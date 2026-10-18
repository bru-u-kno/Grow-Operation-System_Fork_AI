using System.Text.RegularExpressions;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.247): Die Messgrößen eines Zelts mit deutschem Etikett und den Home-Assistant-Domains, die zu ihnen passen.
/// </summary>
/// <remarks>
/// <para><b>Warum hier.</b> Bis forkai.245 standen Etikett und Domains nur in der Oberfläche
/// (<c>GrowDiary.React/src/features/home-assistant/messgroessen.ts</c>: <c>definitions</c> und <c>suggestionFilters</c>), und
/// dort sortieren sie nur die Auswahl. Der Fork-MCP ordnet jetzt über
/// <c>PUT api/settings/tents/{id}/sensors/{metricType}</c> zu und braucht dieselbe Regel als Prüfung. Abgetippt wäre sie eine
/// zweite Wahrheit; deshalb hält <c>src/sensor-messgroessen-gleich.test.ts</c> diese Tabelle gegen die Oberfläche
/// (gleiches Etikett; jede Domain, die die Oberfläche vorschlägt, ist hier zulässig), und <c>SensorMessgroessenTests</c>
/// verlangt für jede Messgröße einen Eintrag.</para>
/// <para><b>Zulässig, nicht vorgeschlagen.</b> Die Oberfläche sortiert mit ihren Domains die Auswahl; hier steht, was der
/// Fork tatsächlich lesen kann. Beides ist gleich bis auf „Licht" (siehe dort).</para>
/// <para><b>Nur am Ende ergänzen, nichts umbenennen:</b> der Fork-MCP findet Messgrößen über das Etikett.</para>
/// <para>Drei Messgrößen haben in der Oberfläche keine Zeile (<c>OHNE_FORMULARZEILE</c> in
/// <c>messgroessen-vollstaendig.test.ts</c>: USV-Akku und die beiden Pumpen-Leistungen). Ihr Etikett und ihre Domain stehen nur
/// hier; beides sind Zahlenwerte, also <c>sensor</c>.</para>
/// </remarks>
public static class SensorMessgroessen
{
    /// <summary>Ein Eintrag: Etikett wie in der App, und welche Domains eine zugeordnete Entität haben darf.</summary>
    public sealed record Eintrag(string Etikett, IReadOnlyList<string> Domains);

    public static IReadOnlyDictionary<SensorMetricType, Eintrag> Alle { get; } = new Dictionary<SensorMetricType, Eintrag>
    {
        [SensorMetricType.AirTemperature] = new("Lufttemp", ["sensor"]),
        [SensorMetricType.Humidity] = new("Luftfeuchte", ["sensor"]),
        [SensorMetricType.Vpd] = new("VPD", ["sensor"]),
        [SensorMetricType.Ppfd] = new("PPFD", ["sensor"]),
        [SensorMetricType.Co2] = new("CO₂", ["sensor"]),
        // Hier steht mehr als in der Oberfläche: LightStateNormalizer liest auch Zahlen (Helligkeit in %, Lux, Leistung der
        // Lampe — „ein gemappter Sensor mit 100.0 %"), und der Demobestand ordnet genau so zu. Die Oberfläche schlägt nur
        // Schalter vor; der Test hält ihre Domains als Teilmenge dieser.
        [SensorMetricType.LightStatus] = new("Licht", ["switch", "light", "binary_sensor", "input_boolean", "sensor"]),
        [SensorMetricType.OutsideTemperature] = new("Außen Temp.", ["sensor"]),
        [SensorMetricType.OutsideHumidity] = new("Außen RLF", ["sensor"]),
        [SensorMetricType.OutsideVpd] = new("Außen VPD", ["sensor"]),
        [SensorMetricType.ReservoirPh] = new("pH", ["sensor"]),
        [SensorMetricType.ReservoirEc] = new("EC", ["sensor"]),
        [SensorMetricType.ReservoirWaterTemp] = new("Wassertemp", ["sensor"]),
        [SensorMetricType.ReservoirLevel] = new("Wasserstand (L)", ["sensor"]),
        [SensorMetricType.ReservoirLevelCm] = new("Wasserstand (cm)", ["sensor"]),
        [SensorMetricType.ReservoirOrp] = new("ORP", ["sensor"]),
        [SensorMetricType.ReservoirDissolvedOxygen] = new("DO", ["sensor"]),
        [SensorMetricType.PumpCirculation] = new("Umwälzpumpe", ["switch", "input_boolean"]),
        [SensorMetricType.PumpAir] = new("Luftpumpe", ["switch", "input_boolean"]),
        [SensorMetricType.Chiller] = new("Chiller", ["climate", "switch"]),
        [SensorMetricType.UpsStatus] = new("USV", ["sensor", "binary_sensor"]),
        [SensorMetricType.UpsBattery] = new("USV-Akku", ["sensor"]),
        [SensorMetricType.PumpCirculationPower] = new("Umwälzpumpe Leistung", ["sensor"]),
        [SensorMetricType.PumpAirPower] = new("Luftpumpe Leistung", ["sensor"]),
    };

    /// <summary>Genau eine Entität: <c>domain.objekt</c>, beides aus <c>[a-z0-9_]</c> — wie <c>KiHomeAssistantApiController</c>.</summary>
    private static readonly Regex EineEntitaet = new(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Die Messgröße zum Namen im Weg — genau ein Name des Enums, Groß-/Kleinschreibung egal.
    /// </summary>
    /// <remarks>
    /// Nicht über <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>: das nimmt Zahlen („3") und verknüpft Listen
    /// („Humidity,Vpd" = 1 | 2 = Co2) — beides landete bei einer anderen Messgröße (Prüfer forkai.247, Leitplanke 6).
    /// </remarks>
    public static SensorMetricType? Lesen(string? name)
    {
        var text = name?.Trim();
        if (string.IsNullOrEmpty(text) || !NurBuchstaben.IsMatch(text)) return null;
        foreach (var wert in Enum.GetValues<SensorMetricType>())
        {
            if (string.Equals(wert.ToString(), text, StringComparison.OrdinalIgnoreCase)) return wert;
        }
        return null;
    }

    /// <summary>Ein Bezeichner: Buchstabe vorn, dann Buchstaben und Ziffern („Co2") — kein Komma, kein Minus, keine Zahl allein.</summary>
    private static readonly Regex NurBuchstaben = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Die Zeile, die Grow OS für eine Messgröße liest: die letzte aktive mit Entität (wie <c>HomeAssistantService.GetStatesAsync</c>:
    /// <c>GroupBy(…).Last()</c>), sonst die älteste. <c>null</c>, wenn es keine gibt.
    /// </summary>
    public static TentSensor? WirksameZeile(IEnumerable<TentSensor> zeilen, SensorMetricType messgroesse)
    {
        var eigene = zeilen.Where(s => s.MetricType == messgroesse).OrderBy(s => s.Id).ToList();
        return eigene.LastOrDefault(s => s.IsActive && !string.IsNullOrWhiteSpace(s.HaEntityId)) ?? eigene.FirstOrDefault();
    }

    public static bool IstEineEntitaet(string entityId) => EineEntitaet.IsMatch(entityId);

    /// <summary>Die Domain einer Entität (<c>sensor</c> bei <c>sensor.zelt</c>).</summary>
    public static string Domain(string entityId) => entityId.Split('.', 2)[0];

    /// <summary>Passt die Entität zur Messgröße? Ohne Eintrag passt nichts (Vergessen sperrt).</summary>
    public static bool DomainPasst(SensorMetricType messgroesse, string entityId)
        => Alle.TryGetValue(messgroesse, out var eintrag) && eintrag.Domains.Contains(Domain(entityId), StringComparer.Ordinal);
}
