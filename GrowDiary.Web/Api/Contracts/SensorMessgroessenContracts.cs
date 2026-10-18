namespace GrowDiary.Web.Api.Contracts;

/// <summary>
/// Fork AI (forkai.247): Eine Messgröße, wie sie einem Zelt zugeordnet wird — für den Fork-MCP (<c>GET api/messgroessen</c>).
/// </summary>
/// <param name="MetricType">Name in <c>SensorMetricType</c>, etwa <c>OutsideTemperature</c>.</param>
/// <param name="MetrikSchluessel">Schlüssel in Verlauf und Meldegrenzen, etwa <c>outside-temperature</c>.</param>
/// <param name="Etikett">Wie die Zeile in der App heißt, etwa „Außen Temp.".</param>
/// <param name="MeldeEtikett">Wie die Meldegrenzen sie nennen, falls anders; sonst <c>null</c>.</param>
/// <param name="Domains">Welche Home-Assistant-Domains zu ihr passen.</param>
/// <param name="Einheit">Die Einheit, wie die Meldegrenzen sie schreiben (etwa „°C"); <c>null</c>, wo es keine gibt.</param>
public sealed record SensorMessgroesseDto(
    string MetricType,
    string MetrikSchluessel,
    string Etikett,
    string? MeldeEtikett,
    IReadOnlyList<string> Domains,
    string? Einheit);

/// <summary>
/// Fork AI (forkai.247): Genau eine Messgröße eines Zelts zuordnen (<c>PUT api/settings/tents/{id}/sensors/{metricType}</c>).
/// </summary>
/// <remarks>Leer oder <c>null</c> löst die Zuordnung — die Zeile bleibt mit ihrer Id stehen, inaktiv und ohne Entität.</remarks>
public sealed class SetTentSensorRequest
{
    public string? HaEntityId { get; set; }
}
