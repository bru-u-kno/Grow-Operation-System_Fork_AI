using GrowDiary.Web.Models;
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

    /// <summary>
    /// Ein Tank-EC unter diesem Wert (mS/cm) gilt als kein Messwert: Der Tank enthält Wasser und Dünger, also nie 0,00.
    /// </summary>
    /// <remarks>
    /// Annahme, kein Messwert der Anlage: Am 11.10.2026 meldete der Bluelab Guardian beim Wasserwechsel für rund zwei
    /// Minuten EC 0,0 (11:00:57 bis 11:03:17), dazwischen lag ein 5-Minuten-Messpunkt und der Wechsel zeigte „vorher EC 0,00".
    /// Warum der Sensor 0,0 meldete, ist nicht belegt. Reines Osmosewasser liegt knapp über 0, ein Tank mit Dünger weit darüber.
    /// </remarks>
    public const double EcMindestwert = 0.05;

    private readonly SensorReadingRepository _sensoren;

    public TankSensorService(SensorReadingRepository sensoren)
    {
        _sensoren = sensoren;
    }

    /// <summary>
    /// Die jüngsten Tankwerte im Fenster <paramref name="fensterMinuten"/> bis <paramref name="bisUtc"/>.
    /// </summary>
    /// <returns>
    /// EC, pH und Wassertemperatur sowie <c>UebersprungenEc</c>: der jüngste EC-Wert unter <see cref="EcMindestwert"/>, der
    /// jünger ist als der gelieferte EC (oder bei fehlendem EC der jüngste überhaupt) — damit die Oberfläche sagen kann,
    /// warum der Wert fehlt oder älter ist. Sonst <c>null</c>.
    /// </returns>
    public (SensorWertDto? Ec, SensorWertDto? Ph, SensorWertDto? WasserTemp, SensorWertDto? UebersprungenEc) Tankwerte(int zeltId, DateTime bisUtc, int fensterMinuten = StandardFensterMinuten)
    {
        var von = bisUtc.AddMinutes(-fensterMinuten);
        IReadOnlyList<TentSensorReading> Lesen(string metrik) => _sensoren.GetReadings(zeltId, metrik, von, bisUtc);
        SensorWertDto? Dto(TentSensorReading? r) => r is null ? null : new SensorWertDto(r.Value, r.CapturedAtUtc);

        var ecAlle = Lesen("reservoir-ec");
        var ec = ecAlle.LastOrDefault(r => r.Value >= EcMindestwert);
        var uebersprungen = ecAlle.LastOrDefault(r => r.Value < EcMindestwert && (ec is null || r.CapturedAtUtc > ec.CapturedAtUtc));
        return (Dto(ec), Dto(Lesen("reservoir-ph").LastOrDefault()), Dto(Lesen("reservoir-temp").LastOrDefault()), Dto(uebersprungen));
    }
}
