using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>
/// Was die Sensoren eines Zelts kurz vor einem Zeitpunkt vom Tank zeigten — EC, pH, Wassertemperatur.
/// </summary>
/// <remarks>
/// <para>Eine Quelle für alle, die „was zeigte der Tank damals?" fragen: der Sensor-Endpunkt
/// des Wasserwechsels und Nachfüllens (<c>GET …/wasserwechsel/sensor</c>) und die
/// automatische Nachmessung des Nachfüllens (<see cref="AddbackNachmessungService"/>).
/// Eine zweite Antwort auf dieselbe Frage wäre eine zweite Wahrheit.</para>
/// <para>Die Werte stammen aus den Rohwerten des Sensorverlaufs (dieselbe Tabelle wie
/// <c>/api/tents/{id}/history?resolution=raw</c>): je Messgröße der jüngste Wert im Fenster
/// vor dem Zeitpunkt. Rohwerte bleiben sieben Tage.</para>
/// </remarks>
public sealed class TankSensorService
{
    /// <summary>
    /// So weit sucht „vorher" zurück: 30 Minuten.
    /// </summary>
    /// <remarks>
    /// Annahme, kein Messwert: die Sensoren schreiben alle 5 Minuten
    /// (<see cref="Api.Controllers.SensorHistoryApiController"/>). Sechs Takte Luft decken einen
    /// kurzen Ausfall ab; was älter ist, zeigt nicht mehr den Tank „kurz vor dem Wechsel".
    /// </remarks>
    public const int StandardFensterMinuten = 30;

    private readonly SensorReadingRepository _sensoren;

    public TankSensorService(SensorReadingRepository sensoren)
    {
        _sensoren = sensoren;
    }

    /// <summary>Die jüngsten Tankwerte im Fenster <paramref name="fensterMinuten"/> bis <paramref name="bisUtc"/>.</summary>
    public (SensorWertDto? Ec, SensorWertDto? Ph, SensorWertDto? WasserTemp) Tankwerte(int zeltId, DateTime bisUtc, int fensterMinuten = StandardFensterMinuten)
    {
        var von = bisUtc.AddMinutes(-fensterMinuten);
        SensorWertDto? Juengster(string metrik)
            => _sensoren.GetReadings(zeltId, metrik, von, bisUtc).LastOrDefault() is { } r ? new SensorWertDto(r.Value, r.CapturedAtUtc) : null;

        return (Juengster("reservoir-ec"), Juengster("reservoir-ph"), Juengster("reservoir-temp"));
    }
}
