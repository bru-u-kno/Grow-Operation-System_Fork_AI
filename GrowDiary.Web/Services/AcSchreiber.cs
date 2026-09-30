using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Ein einzelner Schreibvorgang an den Controller.</summary>
/// <param name="EntityId">Was geschrieben wird.</param>
/// <param name="Domain">Die Domäne — <c>number</c>, <c>time</c>, <c>select</c>.</param>
/// <param name="Dienst">Der Dienst — <c>set_value</c>, <c>select_option</c>.</param>
/// <param name="Daten">Der Rumpf des Aufrufs.</param>
/// <param name="Soll">
/// Der Zustand, den die Entität hinterher melden muss — als Text, so wie Home
/// Assistant ihn zurückgibt.
/// </param>
public sealed record AcSchreibschritt(
    string EntityId,
    string Domain,
    string Dienst,
    IReadOnlyDictionary<string, object> Daten,
    string Soll);

/// <summary>Der Funkweg zum Controller — genau das, was der Schreiber braucht.</summary>
/// <remarks>
/// <para><b>Warum eine eigene Schnittstelle.</b> Der Sinn dieser Klasse ist der
/// Fall, in dem die Wolke einen Auftrag <i>still verwirft</i>: gesendet ja,
/// angekommen nein. Genau dieser Fall liess sich nicht pruefen, solange der
/// Schreiber am versiegelten <see cref="HomeAssistantService"/> hing — man
/// kann keine Wolke bauen, die verwirft. Ein Test, der nur den guten Weg
/// kennt, prueft nicht die Vorsichtsmassnahme, sondern ihre Abwesenheit.</para>
///
/// <para>Zwei Methoden, keine mehr: die Schnittstelle beschreibt den Bedarf des
/// Schreibers, nicht den Umfang von Home Assistant.</para>
/// </remarks>
public interface IAcFunk
{
    /// <summary>Was meldet diese Entität gerade?</summary>
    Task<HomeAssistantState?> ZustandAsync(
        HomeAssistantSettings einstellungen, string entityId, CancellationToken ct);

    /// <summary>Einen Dienst aufrufen. <c>false</c> heisst: schon das Senden ging schief.</summary>
    Task<bool> SchickenAsync(
        HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
        IReadOnlyDictionary<string, object> daten, CancellationToken ct);
}

/// <summary>Der Funkweg im Betrieb — über Home Assistant.</summary>
public sealed class HomeAssistantFunk : IAcFunk
{
    private readonly HomeAssistantService _homeAssistant;

    public HomeAssistantFunk(HomeAssistantService homeAssistant) => _homeAssistant = homeAssistant;

    public Task<HomeAssistantState?> ZustandAsync(
        HomeAssistantSettings einstellungen, string entityId, CancellationToken ct)
        => _homeAssistant.GetEntityStateAsync(einstellungen, entityId, ct);

    public Task<bool> SchickenAsync(
        HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
        IReadOnlyDictionary<string, object> daten, CancellationToken ct)
        => _homeAssistant.CallEntityServiceAsync(einstellungen, domain, dienst, entityId, ct, daten);
}

/// <summary>Abstand, Prüffrist und Versuche eines Schreibvorgangs.</summary>
/// <remarks>
/// Ohne Angabe gelten die Werte aus der Karte des Testers
/// (<see cref="AcSchreiber.Standard"/>). Die Licht-Steuerung gibt ihre eigenen
/// Einstellungen mit — sonst stünden auf ihrer Seite Felder, die nichts
/// bewirken (Prüfer-Befund 29.09.2026).
/// </remarks>
public sealed record AcTakt(TimeSpan Pause, TimeSpan Wartezeit, int Versuche);

/// <summary>Wie ein Schreibvorgang ausgegangen ist.</summary>
/// <param name="Uebersprungen">Stand schon auf dem Sollwert — nichts gesendet.</param>
/// <param name="Bestaetigt">Der Controller meldet den Sollwert.</param>
/// <param name="Versuche">Wie oft geschrieben wurde.</param>
/// <param name="Angenommen">
/// Hat Home Assistant den Aufruf überhaupt entgegengenommen? <c>false</c>
/// heisst: es wurde nichts gesendet — ein echter Fehler. <c>true</c> ohne
/// <see cref="Bestaetigt"/> ist dagegen nur ein Schwebezustand: gesendet, die
/// Wolke hat den neuen Wert noch nicht zurückgemeldet. Der Unterschied trägt
/// die ganze Antwort an die Oberfläche (<see cref="AcStellAntwort"/>).
/// </param>
public sealed record AcSchrittErgebnis(
    string EntityId, bool Uebersprungen, bool Bestaetigt, int Versuche, string? Ist, string? Fehler,
    bool Angenommen = true);

/// <summary>
/// Schreibt an einen AC-Infinity-Controller — nacheinander und mit Nachkontrolle.
/// </summary>
/// <remarks>
/// <para><b>Warum das nicht einfach drei Aufrufe sind.</b> Der Tester hat es
/// teuer gelernt und in seiner Karte dokumentiert: <b>die AC-Infinity-Cloud
/// verwirft parallele Updates</b> („Unable to update device controls"). Wer
/// Ein-Zeit, Aus-Zeit und Modus gleichzeitig schickt, bekommt im besten Fall
/// eines davon.</para>
///
/// <para><b>Und ein einzelner Aufruf ist kein Beleg.</b> Home Assistant meldet
/// „gesendet", nicht „angekommen" — die Cloud kann still verwerfen. Deshalb
/// wird nach jedem Schritt <b>nachgelesen</b>, ob die Entität den Sollwert
/// meldet, und bis zu zweimal wiederholt. Ohne das steht auf der Seite
/// „gestellt", während am Gerät nichts passiert ist. Genau diese Sorte
/// Erfolgsmeldung ist in diesem Projekt schon mehrfach teuer geworden.</para>
///
/// <para><b>Was schon stimmt, wird nicht geschrieben.</b> Jeder Aufruf ist eine
/// Gelegenheit zu scheitern; der Tester prüft aus demselben Grund vorher.</para>
/// </remarks>
public sealed class AcSchreiber
{
    /// <summary>Pause zwischen zwei Schreibvorgängen.</summary>
    /// <remarks>
    /// Zwei Sekunden — der Wert aus der Karte des Testers
    /// (<c>write_gap_ms</c>), dort als Standard nach seinen Versuchen mit der
    /// Cloud. Faustregel, keine dokumentierte Herstellerangabe.
    /// </remarks>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

    /// <summary>Wie lange längstens auf die Bestätigung gewartet wird.</summary>
    /// <remarks>Ebenfalls aus der Karte (<c>verify_seconds</c>, dort 20 s).</remarks>
    public static readonly TimeSpan Wartezeit = TimeSpan.FromSeconds(20);

    /// <summary>In diesem Takt wird nachgefragt, bis die Wartezeit um ist.</summary>
    /// <remarks>
    /// <para><b>Nachfragen statt schlafen.</b> Die 20 Sekunden sind eine
    /// Obergrenze für den schlechten Fall, keine Wartepflicht. Wer sie stumpf
    /// abwartet, sperrt die Oberfläche eine halbe Minute für eine Zahl, die
    /// meistens nach zwei Sekunden steht — und bei drei Schritten wäre das eine
    /// Minute.</para>
    ///
    /// <para><b>Eine Sekunde und nicht zwei.</b> Nachfragen ist ein <i>Lesen</i>;
    /// der Abstand aus <see cref="Pause"/> gilt dem <i>Schreiben</i>, weil die
    /// Wolke dicht aufeinanderfolgende Auftraege verwirft. Zwei verschiedene
    /// Dinge brauchen zwei verschiedene Zahlen — solange beide 2 s waren, konnte
    /// kein Test die Pause von einer Nachfrage unterscheiden, und genau das ist
    /// beim ersten Anlauf passiert: die Pause liess sich ersatzlos streichen,
    /// ohne dass ein einziger Test rot wurde.</para>
    /// </remarks>
    public static readonly TimeSpan Nachfragetakt = TimeSpan.FromSeconds(1);

    /// <summary>Wie oft ein Schritt wiederholt wird, bevor er als gescheitert gilt.</summary>
    public const int Versuche = 3;

    /// <summary>Die Vorgabe: <see cref="Pause"/>, <see cref="Wartezeit"/>, <see cref="Versuche"/>.</summary>
    public static readonly AcTakt Standard = new(Pause, Wartezeit, Versuche);

    private readonly IAcFunk _funk;
    private readonly ILogger<AcSchreiber> _logger;

    public AcSchreiber(IAcFunk funk, ILogger<AcSchreiber> logger)
    {
        _funk = funk;
        _logger = logger;
    }

    /// <summary>
    /// Die Schritte der Reihe nach ausführen, jeden mit Nachkontrolle.
    /// </summary>
    /// <param name="warten">
    /// Die Wartefunktion — im Betrieb <see cref="Task.Delay(TimeSpan, CancellationToken)"/>,
    /// im Test etwas, das sofort zurückkehrt. Ohne diesen Haken dauerte ein
    /// Testlauf über eine Minute, und ein langsamer Test wird abgeschaltet.
    /// </param>
    public async Task<IReadOnlyList<AcSchrittErgebnis>> SchreibenAsync(
        HomeAssistantSettings einstellungen,
        IReadOnlyList<AcSchreibschritt> schritte,
        Func<TimeSpan, CancellationToken, Task>? warten = null,
        CancellationToken ct = default,
        AcTakt? takt = null)
    {
        warten ??= (dauer, token) => Task.Delay(dauer, token);
        takt ??= Standard;
        var ergebnisse = new List<AcSchrittErgebnis>();
        var erster = true;

        foreach (var schritt in schritte)
        {
            // Steht es schon so? Dann nichts senden — jeder Aufruf ist eine
            // Gelegenheit zu scheitern.
            var vorher = await _funk.ZustandAsync(einstellungen, schritt.EntityId, ct);
            if (Passt(vorher?.State, schritt.Soll))
            {
                ergebnisse.Add(new AcSchrittErgebnis(schritt.EntityId, true, true, 0, vorher?.State, null));
                continue;
            }

            if (!erster && takt.Pause > TimeSpan.Zero) await warten(takt.Pause, ct);
            erster = false;

            var ergebnis = await EinSchrittAsync(einstellungen, schritt, warten, takt, ct);
            ergebnisse.Add(ergebnis);

            // Nach einem gescheiterten Schritt die folgenden gar nicht erst
            // versuchen: der Modus 'Schedule" ohne die Zeiten waere schlimmer
            // als gar nichts.
            if (!ergebnis.Bestaetigt) break;
        }

        return ergebnisse;
    }

    private async Task<AcSchrittErgebnis> EinSchrittAsync(
        HomeAssistantSettings einstellungen,
        AcSchreibschritt schritt,
        Func<TimeSpan, CancellationToken, Task> warten,
        AcTakt takt,
        CancellationToken ct)
    {
        string? ist = null;

        for (var versuch = 1; versuch <= takt.Versuche; versuch++)
        {
            var gesendet = await _funk.SchickenAsync(
                einstellungen, schritt.Domain, schritt.Dienst, schritt.EntityId, schritt.Daten, ct);

            if (!gesendet)
            {
                return new AcSchrittErgebnis(schritt.EntityId, false, false, versuch, null,
                    "Home Assistant hat den Aufruf nicht angenommen.", Angenommen: false);
            }

            // So lange nachfragen, bis es steht — längstens die Wartezeit.
            var versucheJeRunde = Math.Max(1, (int)(takt.Wartezeit.Ticks / Nachfragetakt.Ticks));
            for (var frage = 0; frage < versucheJeRunde; frage++)
            {
                await warten(Nachfragetakt, ct);

                var nachher = await _funk.ZustandAsync(einstellungen, schritt.EntityId, ct);
                ist = nachher?.State;

                if (Passt(ist, schritt.Soll))
                {
                    return new AcSchrittErgebnis(schritt.EntityId, false, true, versuch, ist, null);
                }
            }

            _logger.LogInformation(
                "AC-Test: {Entity} meldet nach Versuch {Versuch} {Ist} statt {Soll}.",
                schritt.EntityId, versuch, ist, schritt.Soll);
        }

        return new AcSchrittErgebnis(schritt.EntityId, false, false, takt.Versuche, ist,
            $"Der Controller meldet weiterhin {ist ?? "nichts"} statt {schritt.Soll}. "
            + $"Die AC-Infinity-Cloud verwirft Aufträge gelegentlich — nach {takt.Versuche} Versuchen "
            + "gebe ich auf, statt weiter zu senden.");
    }

    /// <summary>
    /// Meldet die Entität den Sollwert?
    /// </summary>
    /// <remarks>
    /// Zahlen werden als Zahlen verglichen: Home Assistant meldet für eine
    /// gesetzte 7 je nach Entität „7", „7.0" oder „7,0". Ein Textvergleich
    /// hielte das für einen Fehlschlag und schriebe endlos nach. Zeiten kommen
    /// als „18:00:00", gesetzt wird „18:00" — deshalb der Vergleich auf die
    /// ersten fünf Zeichen.
    /// </remarks>
    public static bool Passt(string? ist, string soll)
    {
        if (ist is null) return false;
        if (string.Equals(ist, soll, StringComparison.OrdinalIgnoreCase)) return true;

        if (double.TryParse(ist, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var istZahl)
            && double.TryParse(soll, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var sollZahl))
        {
            return Math.Abs(istZahl - sollZahl) < 0.001;
        }

        // '18:00:00" gegen '18:00"
        if (ist.Length >= 5 && soll.Length >= 5
            && ist.Contains(':') && soll.Contains(':')
            && ist[..5] == soll[..5])
        {
            return true;
        }

        return false;
    }
}
