using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GrowDiary.Web.Services;

/// <summary>
/// Die automatische Nachmessung nach einem Nachfüllen: liest die Tankwerte der Sensoren zum
/// geplanten Zeitpunkt und trägt sie als Messung „nachher" an den Vorgang ein.
/// </summary>
/// <remarks>
/// <para><b>Gelesen wird zur Fälligkeit, nicht zur Ausführung.</b> Der Fork kann beim Fälligwerden
/// aus gewesen sein (Neustart, Update) oder der Nutzer trägt einen Zeitpunkt in der Vergangenheit
/// ein; dann holt der nächste Takt die Werte aus dem Sensorverlauf nach — mit dem Wert, der damals
/// galt. Der Verlauf hält Rohwerte sieben Tage (<see cref="TankSensorService"/>).</para>
/// <para><b>Vorrang der Hand.</b> Gibt es zur Fälligkeit schon eine Messung „nachher" am Vorgang,
/// wird nichts überschrieben — Werte, die der Nutzer selbst eingetragen hat, gehen vor.</para>
/// <para><b>Ohne Wert kein Eintrag.</b> Liefert in den zehn Minuten vor der Fälligkeit weder der
/// EC- noch der pH-Sensor etwas, wartet der Takt noch zehn Minuten auf nachgelieferte Rohwerte und
/// schließt den Auftrag dann mit „ohneWert" ab. Ein Wert, der die Messsperre
/// (<see cref="MeasurementSanityService"/>) nicht besteht, zählt ebenfalls als „ohneWert" — ein
/// gestörter Sensor soll keine Phantom-Messung in den Verlauf schreiben.</para>
/// </remarks>
public sealed class AddbackNachmessungService
{
    /// <summary>So weit zurück vor der Fälligkeit zählt ein Rohwert noch als „zur Nachmessung".</summary>
    public const int FensterMinuten = 10;

    /// <summary>So lange nach der Fälligkeit wartet der Takt auf nachgelieferte Rohwerte.</summary>
    public const int NachfristMinuten = 10;

    private readonly AddbackVorgangRepository _vorgaenge;
    private readonly GrowRepository _grows;
    private readonly TankSensorService _tank;
    private readonly MeasurementSanityService _sperre;

    public AddbackNachmessungService(AddbackVorgangRepository vorgaenge, GrowRepository grows, TankSensorService tank, MeasurementSanityService sperre)
    {
        _vorgaenge = vorgaenge;
        _grows = grows;
        _tank = tank;
        _sperre = sperre;
    }

    /// <summary>Arbeitet alle bis <paramref name="jetztUtc"/> fälligen Aufträge ab. Gibt zurück, wie viele geschlossen wurden.</summary>
    public int FaelligeAbarbeiten(DateTime jetztUtc)
    {
        var geschlossen = 0;
        foreach (var auftrag in _vorgaenge.FaelligeNachmessungen(jetztUtc))
        {
            if (Ausfuehren(auftrag, jetztUtc)) geschlossen++;
        }

        return geschlossen;
    }

    /// <summary><c>true</c>, wenn der Auftrag abgeschlossen ist (erledigt oder aufgegeben); <c>false</c>, wenn er offen bleibt.</summary>
    public bool Ausfuehren(AddbackNachmessung auftrag, DateTime jetztUtc)
    {
        var vorgang = _vorgaenge.GetOhneGrow(auftrag.VorgangId);
        if (vorgang is null)
        {
            _vorgaenge.NachmessungAbschliessen(auftrag.Id, AddbackNachmessung.Uebersprungen, "Der Vorgang existiert nicht mehr.", jetztUtc);
            return true;
        }

        // Vorbeugend: heute hängt nichts eine Messung „nachher“ nachträglich an einen Vorgang (mit Handwerten beim
        // Speichern wird gar kein Auftrag angelegt). Kommt „Vorgang bearbeiten“ (A-006 V8), greift diese Zeile.
        if (vorgang.MessungNachherId is not null)
        {
            _vorgaenge.NachmessungAbschliessen(auftrag.Id, AddbackNachmessung.Uebersprungen, "Es gibt schon eine Messung „nachher“.", jetztUtc);
            return true;
        }

        var grow = _grows.GetGrow(vorgang.GrowId);
        if (grow is null)
        {
            _vorgaenge.NachmessungAbschliessen(auftrag.Id, AddbackNachmessung.Uebersprungen, "Der Grow existiert nicht mehr.", jetztUtc);
            return true;
        }

        if (grow.TentId is not { } zelt)
        {
            _vorgaenge.NachmessungAbschliessen(auftrag.Id, AddbackNachmessung.OhneWert, "Der Grow steht in keinem Zelt — es gibt keine Sensoren dazu.", jetztUtc);
            return true;
        }

        var (ec, ph, wassertemp) = _tank.Tankwerte(zelt, auftrag.FaelligUtc, FensterMinuten);
        if (ec is null && ph is null)
        {
            if (jetztUtc < auftrag.FaelligUtc.AddMinutes(NachfristMinuten)) return false;
            _vorgaenge.NachmessungAbschliessen(auftrag.Id, AddbackNachmessung.OhneWert,
                $"In den {FensterMinuten} Minuten vor der Nachmessung hat kein Sensor EC oder pH geliefert.", jetztUtc);
            return true;
        }

        var zeit = auftrag.FaelligUtc.ToLocalTime();
        var messung = new Measurement
        {
            GrowId = grow.Id,
            TakenAt = zeit,
            Stage = GrowStageResolver.Resolve(grow, zeit.Date),
            Source = ValueOrigin.HomeAssistant,
            Notes = "Nach dem Nachfüllen — automatisch vom Sensor eingetragen.",
            ReservoirEc = ec?.Wert,
            ReservoirPh = ph?.Wert,
            ReservoirWaterTempC = wassertemp?.Wert,
            SolutionChange = false,
        };

        var fehler = new ModelStateDictionary();
        _sperre.ApplyBlockingValidation(fehler, grow, messung);
        if (!fehler.IsValid)
        {
            var grund = string.Join(" ", fehler.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            _vorgaenge.NachmessungAbschliessen(auftrag.Id, AddbackNachmessung.OhneWert, $"Die Sensorwerte sind nicht plausibel: {grund}", jetztUtc);
            return true;
        }

        _vorgaenge.NachmessungEintragen(auftrag, vorgang, messung, Zeile(messung, zeit), jetztUtc);
        return true;
    }

    /// <summary>Die Zeile, die ans Tagebuch angehängt wird: „Nachmessung (automatisch, 13:34 Uhr): EC 1,35 · pH 5,9."</summary>
    internal static string Zeile(Measurement messung, DateTime ortszeit)
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        var teile = new List<string>();
        if (messung.ReservoirEc is { } ec) teile.Add($"EC {ec.ToString("0.00", de)}");
        if (messung.ReservoirPh is { } ph) teile.Add($"pH {ph.ToString("0.00", de)}");
        if (messung.ReservoirWaterTempC is { } wt) teile.Add($"Wasser {wt.ToString("0.0", de)} °C");
        return $"Nachmessung (automatisch, {ortszeit.ToString("HH:mm", de)} Uhr): {string.Join(" · ", teile)}.";
    }
}
