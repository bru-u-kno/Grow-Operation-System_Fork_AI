using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Neuling-Test 17.10.2026: Frisch angelegte Helfer bekommen die Werte des Fork.
/// </summary>
/// <remarks>
/// <para><b>Was schiefging.</b> Ein Zahl-Helfer wird ohne Startwert angelegt, Home Assistant nimmt dann sein Minimum
/// (Feuchte EIN/AUS 30 %, Temperatur max. 15 °C, Zeiten 0). Der Fork führt eigene Einstellungen (60 %, 29 °C …) und
/// schrieb sie nur beim Speichern der Einstellungsseite nach Home Assistant. Wer nur den Einrichtungs-Assistenten
/// durchlief, hatte eine Regelung, die nie schaltete — während die Seite des Fork die brauchbaren Werte zeigte.</para>
/// <para><b>Was dieser Dienst tut.</b> Er hält den Stand des Fork <i>vor</i> dem Anlegen fest (sonst übernähme der Fork
/// beim ersten Lesen die eben angelegten Mindestwerte als seine eigenen), legt die fehlenden Helfer an und schreibt danach
/// <b>nur die neu angelegten</b>. Ein Helfer, den es schon gab, behält seinen Wert — auch ein von Hand geänderter.
/// Der Ablauf steht in <see cref="AblaufAsync{T}"/> ohne Home Assistant, damit er prüfbar ist.</para>
/// <para><b>Wo es greift.</b> Bisher nur beim Entfeuchter (der getestete Weg). Die anderen Steuerungen haben
/// dieselbe Lücke möglicherweise; <see cref="Kann"/> sagt, wer einen Weg hat.</para>
/// </remarks>
public sealed class SteuerungStartwerteService
{
    private readonly SteuerungHelferService _helfer;
    private readonly EntfeuchterSteuerungService _entfeuchter;
    private readonly ILogger<SteuerungStartwerteService> _log;

    public SteuerungStartwerteService(SteuerungHelferService helfer, EntfeuchterSteuerungService entfeuchter, ILogger<SteuerungStartwerteService> log)
    {
        _helfer = helfer;
        _entfeuchter = entfeuchter;
        _log = log;
    }

    /// <summary>Welche Steuerungen ihre Startwerte nach dem Anlegen schreiben.</summary>
    public static bool Kann(string modul)
        => string.Equals(modul, EntfeuchterSteuerungService.Modul, StringComparison.OrdinalIgnoreCase);

    /// <summary>Die fehlenden Helfer anlegen und den neuen ihren Startwert geben; die Bilanz sagt, wie viele es waren.</summary>
    public Task<SteuerungHelferService.Bilanz> AnlegenAsync(
        string modul,
        IReadOnlyCollection<string> belegteRollen,
        HomeAssistantSettings settings,
        CancellationToken ct)
    {
        if (!Kann(modul) || !settings.IsConfigured) return _helfer.AnlegenAsync(modul, belegteRollen, settings, ct);

        return AblaufAsync(
            // Zuerst festhalten: Gespeichert ist null, solange nie gespeichert wurde, und EinstellungenAsync übernimmt dann
            // den Stand aus Home Assistant — nach dem Anlegen wären das die Mindestwerte.
            festhalten: () => _entfeuchter.EinstellungenAsync(ct),
            anlegen: () => _helfer.AnlegenAsync(modul, belegteRollen, settings, ct),
            schreiben: (stand, ids) => _entfeuchter.NachHomeAssistantSchreibenAsync(stand, ct, ids),
            _log);
    }

    /// <summary>
    /// Der Ablauf ohne Home Assistant: Stand festhalten, anlegen, danach nur die neu angelegten Helfer schreiben.
    /// Antwortet Home Assistant beim Anlegen nicht, wird nichts geschrieben. Wirft das Schreiben, bleibt die Bilanz erhalten
    /// (die Helfer sind ja angelegt) und meldet die Startwerte als unvollständig — sie lassen sich über die Abweichungs-Anzeige
    /// der Entfeuchtungs-Seite nachholen.
    /// </summary>
    public static async Task<SteuerungHelferService.Bilanz> AblaufAsync<T>(
        Func<Task<T>> festhalten,
        Func<Task<SteuerungHelferService.Bilanz>> anlegen,
        Func<T, IReadOnlySet<string>, Task<bool>> schreiben,
        ILogger? log = null)
    {
        var stand = await festhalten();
        var bilanz = await anlegen();
        if (!bilanz.Erreichbar) return bilanz;

        var gewollt = ZuSchreiben(bilanz.Einzeln);
        if (gewollt.Count == 0) return bilanz;

        try
        {
            var alles = await schreiben(stand, gewollt);
            return bilanz with { Startwerte = alles ? gewollt.Count : 0, StartwerteVollstaendig = alles };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.LogWarning(ex, "Startwerte konnten nicht geschrieben werden — die Helfer stehen auf ihrem Minimum.");
            return bilanz with { Startwerte = 0, StartwerteVollstaendig = false };
        }
    }

    /// <summary>
    /// Welche Helfer den Startwert bekommen: die in diesem Durchgang <b>neu angelegten</b>, soweit der Fork sie schreibt
    /// (<see cref="EntfeuchterSteuerungService.Entitaeten.Startwerte"/>). Ein übersprungener (schon vorhandener) Helfer
    /// ist nie dabei. Die Kennungen in beiden Räumen — der Katalog führt die neuen, ein Bestand kann die alten tragen.
    /// </summary>
    public static IReadOnlySet<string> ZuSchreiben(IEnumerable<SteuerungHelferService.Ergebnis> einzeln)
    {
        var neu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in einzeln.Where(x => x.Angelegt))
        {
            neu.Add(e.EntityId);
            neu.Add(Kennungen.Id(e.EntityId));
        }

        return EntfeuchterSteuerungService.Entitaeten.Startwerte
            .Where(neu.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
