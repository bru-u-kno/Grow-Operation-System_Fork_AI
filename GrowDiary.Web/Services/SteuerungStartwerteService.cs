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
    private readonly IReadOnlyDictionary<string, IStartwertDienst> _dienste;
    private readonly ILogger<SteuerungStartwerteService> _log;

    public SteuerungStartwerteService(SteuerungHelferService helfer, IEnumerable<IStartwertDienst> dienste, ILogger<SteuerungStartwerteService> log)
    {
        _helfer = helfer;
        _dienste = dienste.ToDictionary(d => d.StartModul, StringComparer.OrdinalIgnoreCase);
        _log = log;
    }

    /// <summary>Die Steuerungen, die ihre Startwerte nach dem Anlegen schreiben.</summary>
    public static IReadOnlyList<string> Module { get; } =
    [
        EntfeuchterSteuerungService.Modul, ChillerSteuerungService.Modul, ZuluftSteuerungService.Modul, Co2SteuerungService.Modul,
    ];

    /// <summary>Hat diese Steuerung einen Weg für Startwerte? (Der Zusatz-Entfeuchter noch nicht.)</summary>
    public static bool Kann(string modul) => Module.Contains(modul, StringComparer.OrdinalIgnoreCase);

    /// <summary>Die fehlenden Helfer anlegen und den neuen ihren Startwert geben; die Bilanz sagt, wie viele es waren.</summary>
    public Task<SteuerungHelferService.Bilanz> AnlegenAsync(
        string modul,
        IReadOnlyCollection<string> belegteRollen,
        HomeAssistantSettings settings,
        CancellationToken ct)
    {
        var dienst = Waehlen(modul, _dienste);
        if (dienst is null || !settings.IsConfigured) return _helfer.AnlegenAsync(modul, belegteRollen, settings, ct);

        return AblaufAsync(
            // Zuerst festhalten: Der Dienst übernimmt beim ersten Lesen seinen Stand aus Home Assistant — nach dem Anlegen
            // wären das die Mindestwerte der frischen Helfer.
            festhalten: () => dienst.StandFesthaltenAsync(ct),
            anlegen: () => _helfer.AnlegenAsync(modul, belegteRollen, settings, ct),
            schreiben: (stand, ids) => dienst.StartwerteSchreibenAsync(stand, ids, ct),
            _log);
    }

    /// <summary>
    /// Der Dienst für eine Steuerung — <c>null</c>, wenn sie keinen Weg für Startwerte hat (<see cref="Kann"/>). Verspricht die
    /// Liste <see cref="Module"/> einen Weg, den es im Container nicht gibt (Registrierung in <c>Program.cs</c> vergessen), wirft
    /// die Methode: still auf das Anlegen ohne Startwerte zurückzufallen wäre genau der alte Fehler — ein Kühler-Ziel von 4 °C
    /// ohne jede Meldung.
    /// </summary>
    public static IStartwertDienst? Waehlen(string modul, IReadOnlyDictionary<string, IStartwertDienst> dienste)
    {
        if (!Kann(modul)) return null;
        return dienste.TryGetValue(modul, out var dienst)
            ? dienst
            : throw new InvalidOperationException($"Für die Steuerung „{modul}“ ist ein Startwert-Weg vorgesehen, aber kein Dienst registriert (Program.cs).");
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
        Func<T, IReadOnlySet<string>, Task<(int Geschrieben, bool Alles)>> schreiben,
        ILogger? log = null)
    {
        var stand = await festhalten();
        var bilanz = await anlegen();
        if (!bilanz.Erreichbar) return bilanz;

        var neu = ZuSchreiben(bilanz.Einzeln);
        if (neu.Count == 0) return bilanz;

        try
        {
            var (geschrieben, alles) = await schreiben(stand, neu);
            return bilanz with { Startwerte = alles ? geschrieben : 0, StartwerteVollstaendig = alles };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.LogWarning(ex, "Startwerte konnten nicht geschrieben werden — die Helfer stehen auf ihrem Minimum.");
            return bilanz with { Startwerte = 0, StartwerteVollstaendig = false };
        }
    }

    /// <summary>
    /// Die Helfer, die in diesem Durchgang <b>neu angelegt</b> wurden — nur sie bekommen einen Startwert. Ein übersprungener
    /// (schon vorhandener) Helfer ist nie dabei. Welche davon der Dienst wirklich schreibt, entscheidet er selbst
    /// (Plan-geführte und nie gespeicherte bleiben aus). Die Kennungen in beiden Räumen — der Katalog führt die neuen,
    /// ein Bestand kann die alten tragen.
    /// </summary>
    public static IReadOnlySet<string> ZuSchreiben(IEnumerable<SteuerungHelferService.Ergebnis> einzeln)
    {
        var neu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in einzeln.Where(x => x.Angelegt))
        {
            neu.Add(e.EntityId);
            neu.Add(Kennungen.Id(e.EntityId));
        }

        return neu;
    }
}
