using System.Collections.Concurrent;
using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Die Licht-Steuerung — Livebild, Zeitpläne und das Schalten am
/// AC-Infinity-Controller.
/// </summary>
/// <remarks>
/// <para><b>Warum hier direkt geschaltet wird.</b> Siehe
/// <see cref="LichtEinstellungen"/>: den Zeitplan führt der Controller selbst
/// aus. Eine HA-Automation dazwischen hätte nichts zu regeln, nur einen
/// weiteren Ort, an dem dieselbe Zeit steht.</para>
///
/// <para><b>Die drei Regeln gegen die Cloud.</b> Aus dem Fehler vom 23.08.2026
/// (Preset sprang nach einer Minute zurück): <b>nur Abweichendes</b> schreiben —
/// Veggie und Blüte mit gleicher Ein-Zeit sind ein einziger Aufruf, nicht drei;
/// <b>Abstand</b> zwischen den Aufrufen, weil die Cloud parallele Schreibvorgänge
/// verwirft; <b>später prüfen</b> und wiederholen, weil ein angenommener Aufruf
/// noch lange nicht ein übernommener ist.</para>
///
/// <para><b>Wo die Prüfung läuft.</b> Bei einem einzelnen Befehl (an, aus,
/// Stufe) nicht im Schreib-Aufruf — der wäre eine Minute lang offen. Die offenen
/// Sollwerte stehen im Speicher; jedes Laden des Livebilds prüft sie gegen den
/// Ist-Zustand und schreibt nach, wenn die Frist abgelaufen ist
/// (<see cref="Nachpruefung"/>).</para>
///
/// <para><b>Aber nur im Nachschreibfenster (29.09.2026).</b> Vorher gab es
/// keine Grenze: ein „aus" um 22:00, das die Wolke verwarf, wurde beim nächsten
/// Öffnen der Seite nachgeschrieben — auch Tage später, mitten in der
/// Lichtphase, nachdem das Licht längst in der AC-App eingeschaltet war. Jetzt
/// endet das Nachschreiben nach <see cref="Nachschreibfenster"/>, und sobald
/// die Entität etwas anderes meldet als vor dem Befehl, hat jemand anderes
/// geschaltet und der Befehl gilt als überholt. Zwei offene Tabs schreiben
/// nicht mehr gleichzeitig nach.</para>
///
/// <para><b>Mehrere Schritte gehen durch den <see cref="AcSchreiber"/>.</b> Ein
/// Preset sind drei Aufrufe: Ein-Zeit, Aus-Zeit, Modus. Vorher wurde „Schedule"
/// auch dann gesetzt, wenn die Zeiten nicht ankamen — der Controller schaltete
/// dann nach den alten. Der Schreiber prüft jeden Schritt nach und bricht nach
/// dem ersten nicht bestätigten ab.</para>
/// </remarks>
public sealed class LichtSteuerungService
{
    public const string Modul = "licht";

    /// <summary>Die Modus-Werte des AC-Infinity-Selects.</summary>
    public static class Modi
    {
        public const string Aus = "Off";
        public const string An = "On";
        public const string Zeitplan = "Schedule";
    }

    /// <summary>Die Rollen dieses Moduls — aufgelöst über die Geräteseite.</summary>
    public static class Rollen
    {
        public const string Modus = "licht_modus";
        public const string Stufe = "licht_stufe";
        public const string EinZeit = "licht_ein_zeit";
        public const string AusZeit = "licht_aus_zeit";
        public const string Zustand = "licht_zustand";
        public const string Status = "licht_status";
    }

    /// <summary>
    /// Die Spiegel-Helfer der alten Dashboard-Karte. Sie gehören zur Steuerung,
    /// nicht zum Gerät, und stehen deshalb fest — solange die Karte lebt.
    /// </summary>
    public static class Entitaeten
    {
        public const string VeggieEin = "input_datetime.led_top_zeitplan_veggie_ein";
        public const string VeggieAus = "input_datetime.led_top_zeitplan_veggie_aus";
        public const string BlueteEin = "input_datetime.led_top_zeitplan_blute_ein";
        public const string BlueteAus = "input_datetime.led_top_zeitplan_blute_aus";
    }

    /// <summary>Offene Sollwerte je Entität. Statisch, weil der Dienst je Anfrage neu entsteht.</summary>
    private static readonly ConcurrentDictionary<string, LichtOffen> Offene = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nur einer prüft und schreibt gleichzeitig nach — zwei Tabs sind zwei parallele Aufträge.</summary>
    private static readonly SemaphoreSlim Pruefsperre = new(1, 1);

    private readonly SteuerungRepository _repo;
    private readonly HomeAssistantService _ha;
    private readonly IAcFunk _funk;
    private readonly AcSchreiber _schreiber;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly SteuerungGeraeteService _geraete;
    private readonly ILogger<LichtSteuerungService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task>? _warten;

    public LichtSteuerungService(
        SteuerungRepository repo,
        HomeAssistantService ha,
        IAcFunk funk,
        AcSchreiber schreiber,
        HomeAssistantSettingsRepository haSettings,
        SteuerungGeraeteService geraete,
        ILogger<LichtSteuerungService> logger,
        Func<TimeSpan, CancellationToken, Task>? warten = null)
    {
        _repo = repo;
        _ha = ha;
        _funk = funk;
        _schreiber = schreiber;
        _haSettings = haSettings;
        _geraete = geraete;
        _logger = logger;
        _warten = warten;
    }

    // ----------------------------------------------------------- Einstellungen

    public LichtEinstellungen Einstellungen => _repo.GetEinstellungen<LichtEinstellungen>(Modul) ?? new LichtEinstellungen();

    public static Dictionary<string, string> Pruefen(LichtEinstellungen e)
    {
        var f = new Dictionary<string, string>();
        foreach (var (name, wert) in new[]
                 {
                     (nameof(e.VeggieEin), e.VeggieEin), (nameof(e.VeggieAus), e.VeggieAus),
                     (nameof(e.BlueteEin), e.BlueteEin), (nameof(e.BlueteAus), e.BlueteAus),
                 })
        {
            if (Zeit(wert) is null) f[name] = "Zeit im Format HH:mm angeben.";
        }

        if (e.Stufe is < 1 or > 10) f[nameof(e.Stufe)] = "Stufe zwischen 1 und 10.";
        if (e.SchreibAbstandMs is < 0 or > 10000) f[nameof(e.SchreibAbstandMs)] = "Abstand zwischen 0 und 10000 ms.";
        if (e.VerifySekunden is < 5 or > 120) f[nameof(e.VerifySekunden)] = "Prüffrist zwischen 5 und 120 Sekunden.";
        if (e.MaxWiederholungen is < 0 or > 5) f[nameof(e.MaxWiederholungen)] = "Höchstens 5 Wiederholungen.";
        return f;
    }

    /// <summary>
    /// Einstellungen prüfen, speichern, Helfer spiegeln — und, wenn das gerade
    /// aktive Preset bearbeitet wurde, die neuen Zeiten sofort an den Controller
    /// geben. Sonst stünde in der Oberfläche eine Zeit, nach der nichts schaltet.
    /// </summary>
    public async Task<(LichtEinstellungen? Gespeichert, Dictionary<string, string> Fehler, bool HaErreicht)> SpeichernAsync(
        LichtEinstellungen e, CancellationToken ct)
    {
        var fehler = Pruefen(e);
        if (fehler.Count > 0) return (null, fehler, false);

        var vorher = Einstellungen;
        var live = await LiveAsync(ct);
        _repo.SetEinstellungen(Modul, e);

        var erreicht = await HelferSpiegelnAsync(e, ct);
        var aktiv = AktivesPreset(vorher, live.EinZeit, live.AusZeit, live.Modus);
        if (aktiv is not null)
        {
            var (ein, aus) = Preset(e, aktiv);
            erreicht &= await SchreibenAsync(new[]
            {
                (Rollen.EinZeit, ein + ":00"),
                (Rollen.AusZeit, aus + ":00"),
            }, e, ct);
        }

        return (e, fehler, erreicht);
    }

    // --------------------------------------------------------------- Livebild

    public async Task<LichtLive> LiveAsync(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);
        var e = Einstellungen;
        var geraete = _geraete.EntitiesFuerModul(Modul);

        string? Text(string? id) => id is not null && nachId.TryGetValue(id, out var s) ? s.State : null;
        string? Rolle(string rolle) => geraete.TryGetValue(rolle, out var id) && !string.IsNullOrWhiteSpace(id) ? Text(id) : null;

        var modus = Rolle(Rollen.Modus);
        var einZeit = Kurz(Rolle(Rollen.EinZeit));
        var ausZeit = Kurz(Rolle(Rollen.AusZeit));
        var stufe = int.TryParse(Rolle(Rollen.Stufe), NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : (int?)null;
        var zustand = Rolle(Rollen.Zustand) is { } z ? z is "on" or "On" : (bool?)null;
        var online = Rolle(Rollen.Status) is { } s2 ? s2 is "on" or "On" : (bool?)null;

        var offen = await OffenePruefenAsync(nachId, geraete, e, ct);

        return new LichtLive(
            HaErreichbar: entities.Count > 0,
            Modus: modus,
            Stufe: stufe,
            EinZeit: einZeit,
            AusZeit: ausZeit,
            LichtAn: zustand,
            ControllerOnline: online,
            AktivesPreset: AktivesPreset(e, einZeit, ausZeit, modus),
            NaechsterWechsel: NaechsterWechsel(modus, einZeit, ausZeit, zustand),
            Unbestaetigt: offen.Unbestaetigt,
            Fehlgeschlagen: offen.Fehlgeschlagen,
            StandUtc: DateTime.UtcNow);
    }

    // ----------------------------------------------------------------- Befehle

    /// <summary>Aus, An, ein Preset anwenden oder die Stufe setzen.</summary>
    public async Task<bool> BefehlAsync(string art, string? preset, int? stufe, CancellationToken ct)
    {
        var e = Einstellungen;
        switch (art)
        {
            case "aus":
                return await SchreibenAsync(new[] { (Rollen.Modus, Modi.Aus) }, e, ct);

            case "an":
                return await SchreibenAsync(new[] { (Rollen.Modus, Modi.An) }, e, ct);

            case "stufe":
            {
                if (stufe is not { } wert || wert is < 1 or > 10) return false;
                e.Stufe = wert;
                _repo.SetEinstellungen(Modul, e);
                return await SchreibenAsync(new[] { (Rollen.Stufe, wert.ToString(CultureInfo.InvariantCulture)) }, e, ct);
            }

            case "preset":
            {
                if (preset is not ("veggie" or "bluete")) return false;
                var (ein, aus) = Preset(e, preset);
                // Reihenfolge: erst die Zeiten, dann der Modus. Wer zuerst auf
                // Zeitplan schaltet, laesst den Controller kurz nach den alten
                // Zeiten schalten.
                return await SchreibenAsync(new[]
                {
                    (Rollen.EinZeit, ein + ":00"),
                    (Rollen.AusZeit, aus + ":00"),
                    (Rollen.Modus, Modi.Zeitplan),
                }, e, ct);
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Schreibt, was vom Soll abweicht. Ein einzelner Auftrag wird gesendet und
    /// zur späteren Prüfung gemerkt; mehrere gehen der Reihe nach durch den
    /// <see cref="AcSchreiber"/>, der jeden Schritt nachprüft und nach dem ersten
    /// nicht bestätigten abbricht.
    /// </summary>
    private async Task<bool> SchreibenAsync(IReadOnlyList<(string Rolle, string Soll)> auftraege, LichtEinstellungen e, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var geraete = _geraete.EntitiesFuerModul(Modul);
        var ziele = new List<(string EntityId, string Soll)>();
        foreach (var (rolle, soll) in auftraege)
        {
            // Fehlt eine Rolle, wird gar nichts geschrieben: ein Preset ohne
            // Aus-Zeit waere derselbe halbe Zustand wie ein verworfener Schritt.
            if (!geraete.TryGetValue(rolle, out var id) || string.IsNullOrWhiteSpace(id)) return false;
            ziele.Add((id, soll));
        }

        // Ein neuer Befehl ersetzt jeden offenen für dieselbe Entität — auch
        // dann, wenn gleich unten nichts gesendet wird, weil der Controller
        // schon auf dem neuen Soll steht. Vorher blieb in genau diesem Fall der
        // alte Eintrag liegen: „aus" verworfen, dann „an" (stand schon) — und
        // das nächste Öffnen der Seite schrieb das veraltete „aus" nach.
        foreach (var (entity, _) in ziele) Offene.TryRemove(entity, out _);

        if (ziele.Count > 1)
        {
            var schritte = ziele
                .Select(z => Schritt(z.EntityId, z.Soll))
                .OfType<AcSchreibschritt>()
                .ToList();
            if (schritte.Count != ziele.Count) return false;

            var ergebnisse = await _schreiber.SchreibenAsync(settings, schritte, _warten, ct, Takt(e));
            var bestaetigt = ergebnisse.Count == schritte.Count && ergebnisse.All(r => r.Bestaetigt);
            if (!bestaetigt)
            {
                var haengt = ergebnisse.FirstOrDefault(r => !r.Bestaetigt);
                _logger.LogWarning(
                    "Licht: {Entity} nicht bestätigt ({Fehler}) — die folgenden Schritte wurden nicht geschrieben.",
                    haengt?.EntityId, haengt?.Fehler);
            }
            return bestaetigt;
        }

        var (entityId, sollWert) = ziele[0];
        var vorher = (await _funk.ZustandAsync(settings, entityId, ct))?.State;
        if (vorher is not null && Gleich(vorher, sollWert)) return true;

        var antwort = await SendenAsync(settings, entityId, sollWert, ct);
        var jetzt = DateTime.UtcNow;
        Offene[entityId] = new LichtOffen(entityId, sollWert, jetzt, 1, jetzt, vorher);
        // Blieb nur die Antwort aus, war der Auftrag unterwegs — ob er wirkte,
        // zeigt die Nachprüfung als „unbestätigt". Abgelehnt ist allein,
        // was Home Assistant gar nicht angenommen hat.
        return antwort != HaDienstAntwort.Abgelehnt;
    }

    /// <summary>
    /// Abstand, Prüffrist und Versuche aus den Einstellungen der Seite — der
    /// erste Versuch plus <see cref="LichtEinstellungen.MaxWiederholungen"/>.
    /// </summary>
    public static AcTakt Takt(LichtEinstellungen e) => new(
        TimeSpan.FromMilliseconds(e.SchreibAbstandMs),
        TimeSpan.FromSeconds(e.VerifySekunden),
        e.MaxWiederholungen + 1);

    /// <summary>Der Aufruf, der diese Entität auf den Sollwert stellt.</summary>
    internal static AcSchreibschritt? Schritt(string entityId, string soll)
    {
        var domaene = entityId.Split('.', 2)[0];
        return domaene switch
        {
            "select" => new AcSchreibschritt(entityId, "select", "select_option",
                new Dictionary<string, object> { ["option"] = soll }, soll),
            "time" => new AcSchreibschritt(entityId, "time", "set_value",
                new Dictionary<string, object> { ["time"] = soll }, soll),
            "number" => new AcSchreibschritt(entityId, "number", "set_value",
                new Dictionary<string, object>
                {
                    ["value"] = double.TryParse(soll, NumberStyles.Float, CultureInfo.InvariantCulture, out var zahl) ? (object)zahl : soll,
                }, soll),
            "input_datetime" => new AcSchreibschritt(entityId, "input_datetime", "set_datetime",
                new Dictionary<string, object> { ["time"] = soll }, soll),
            _ => null,
        };
    }

    private Task<HaDienstAntwort> SendenAsync(HomeAssistantSettings settings, string entityId, string soll, CancellationToken ct)
        => Schritt(entityId, soll) is { } s
            ? _funk.SchickenMitAntwortAsync(settings, s.Domain, s.Dienst, s.EntityId, s.Daten, ct)
            : Task.FromResult(HaDienstAntwort.Abgelehnt);

    /// <summary>Die vier Helfer der alten Dashboard-Karte nachziehen.</summary>
    private async Task<bool> HelferSpiegelnAsync(LichtEinstellungen e, CancellationToken ct)
    {
        if (!e.HelferSpiegeln) return true;
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var alles = true;
        foreach (var (id, wert) in new[]
                 {
                     (Entitaeten.VeggieEin, e.VeggieEin), (Entitaeten.VeggieAus, e.VeggieAus),
                     (Entitaeten.BlueteEin, e.BlueteEin), (Entitaeten.BlueteAus, e.BlueteAus),
                 })
        {
            alles &= await SendenAsync(settings, id, wert + ":00", ct) == HaDienstAntwort.Angenommen;
        }

        return alles;
    }

    /// <summary>Was mit einem offenen Sollwert geschieht.</summary>
    public enum Nachpruefschritt
    {
        /// <summary>Übernommen — vergessen.</summary>
        Erledigt,
        /// <summary>Jemand anderes hat geschaltet — vergessen, nicht nachschreiben.</summary>
        Ueberholt,
        /// <summary>Prüffrist läuft noch.</summary>
        Warten,
        /// <summary>Frist abgelaufen, noch Versuche und Zeit übrig — noch einmal schreiben.</summary>
        Nachschreiben,
        /// <summary>Versuche aufgebraucht oder Fenster vorbei — melden, nie mehr schreiben.</summary>
        Aufgegeben,
    }

    /// <summary>
    /// Wie lange nach dem Befehl höchstens nachgeschrieben wird: jede Wiederholung
    /// bekommt ihre Prüffrist, und das Ganze einmal Luft obendrauf.
    /// </summary>
    /// <remarks>Mit den Vorgaben (20 s, 2 Wiederholungen) sind das zwei Minuten.</remarks>
    public static TimeSpan Nachschreibfenster(LichtEinstellungen e)
        => TimeSpan.FromSeconds(e.VerifySekunden * (e.MaxWiederholungen + 1) * 2);

    /// <summary>
    /// Die Entscheidung über einen offenen Sollwert — rein, damit jeder Fall
    /// seinen eigenen Test hat.
    /// </summary>
    public static Nachpruefschritt Nachpruefung(LichtOffen offen, string? ist, DateTime jetztUtc, LichtEinstellungen e)
    {
        if (ist is not null && Gleich(ist, offen.Soll)) return Nachpruefschritt.Erledigt;

        // Meldet die Entität etwas Drittes — weder den alten Stand noch den
        // gewollten —, hat jemand anderes geschaltet. Unbekannt (unavailable,
        // nicht lesbar) ist kein Drittes: da bleibt die Frage offen.
        var lesbar = ist is not null && ist is not ("unavailable" or "unknown" or "");
        if (lesbar && offen.Vorher is { } vorher && !Gleich(ist!, vorher)) return Nachpruefschritt.Ueberholt;

        if (jetztUtc - offen.SeitUtc < TimeSpan.FromSeconds(e.VerifySekunden)) return Nachpruefschritt.Warten;

        // Ohne Befehlszeit (alter Eintrag) zählt der letzte Versuch als Anfang.
        var anfang = offen.ErstUtc ?? offen.SeitUtc;
        if (jetztUtc - anfang > Nachschreibfenster(e)) return Nachpruefschritt.Aufgegeben;
        if (offen.Versuche > e.MaxWiederholungen) return Nachpruefschritt.Aufgegeben;

        return Nachpruefschritt.Nachschreiben;
    }

    /// <summary>
    /// Die offenen Sollwerte gegen den Ist-Zustand halten (<see cref="Nachpruefung"/>).
    /// </summary>
    private async Task<(IReadOnlyList<string> Unbestaetigt, IReadOnlyList<string> Fehlgeschlagen)> OffenePruefenAsync(
        IReadOnlyDictionary<string, HomeAssistantEntity> nachId,
        IReadOnlyDictionary<string, string?> geraete,
        LichtEinstellungen e,
        CancellationToken ct)
    {
        if (Offene.IsEmpty) return (Array.Empty<string>(), Array.Empty<string>());

        // Prüft gerade ein anderer Aufruf (zweiter Tab), wird hier nur gelesen.
        var darfSchreiben = await Pruefsperre.WaitAsync(0, ct);
        try
        {
            var settings = _haSettings.GetEffectiveHomeAssistantSettings();
            var unbestaetigt = new List<string>();
            var fehlgeschlagen = new List<string>();

            foreach (var offen in Offene.Values.ToList())
            {
                // Entfernt und ersetzt wird nur der Eintrag, der hier gelesen
                // wurde. Kam in der Zwischenzeit ein neuer Befehl für dieselbe
                // Entität (zweiter Tab, Klick während des Nachschreibens), gehört
                // dessen Eintrag nicht dieser Prüfung.
                var gelesen = new KeyValuePair<string, LichtOffen>(offen.EntityId, offen);

                if (!geraete.Values.Any(id => string.Equals(id, offen.EntityId, StringComparison.OrdinalIgnoreCase)))
                {
                    Offene.TryRemove(gelesen);
                    continue;
                }

                var ist = nachId.TryGetValue(offen.EntityId, out var s) ? s.State : null;
                switch (Nachpruefung(offen, ist, DateTime.UtcNow, e))
                {
                    case Nachpruefschritt.Erledigt:
                        Offene.TryRemove(gelesen);
                        break;

                    case Nachpruefschritt.Ueberholt:
                        _logger.LogInformation(
                            "Licht: {Entity} steht jetzt auf {Ist} — anderswo geschaltet, {Soll} wird nicht nachgeschrieben.",
                            offen.EntityId, ist, offen.Soll);
                        Offene.TryRemove(gelesen);
                        break;

                    case Nachpruefschritt.Warten:
                        unbestaetigt.Add(offen.EntityId);
                        break;

                    case Nachpruefschritt.Aufgegeben:
                        fehlgeschlagen.Add(offen.EntityId);
                        break;

                    case Nachpruefschritt.Nachschreiben when darfSchreiben:
                        // Steht noch derselbe Eintrag da? Sonst hat ein neuer
                        // Befehl ihn ersetzt oder entfernt — dann ist dieser Soll
                        // veraltet und wird nicht mehr gesendet.
                        if (!Offene.TryGetValue(offen.EntityId, out var aktuell) || aktuell != offen)
                        {
                            break;
                        }

                        _logger.LogWarning("Licht: {Entity} steht auf {Ist}, gewollt war {Soll} — Versuch {Nummer}.",
                            offen.EntityId, ist, offen.Soll, offen.Versuche + 1);
                        await SendenAsync(settings, offen.EntityId, offen.Soll, ct);

                        // Nur fortschreiben, was noch derselbe Eintrag ist: ein
                        // während des Sendens gesetzter neuer Befehl gewinnt.
                        Offene.TryUpdate(offen.EntityId,
                            offen with { SeitUtc = DateTime.UtcNow, Versuche = offen.Versuche + 1 }, offen);
                        unbestaetigt.Add(offen.EntityId);
                        break;

                    case Nachpruefschritt.Nachschreiben:
                        unbestaetigt.Add(offen.EntityId);
                        break;
                }
            }

            return (unbestaetigt, fehlgeschlagen);
        }
        finally
        {
            if (darfSchreiben) Pruefsperre.Release();
        }
    }

    /// <summary>Einen gemeldeten Fehlschlag wegräumen, wenn der Nutzer ihn zur Kenntnis genommen hat.</summary>
    public static void OffeneVergessen() => Offene.Clear();

    // ----------------------------------------------------------------- Rechnen

    internal static (string Ein, string Aus) Preset(LichtEinstellungen e, string preset)
        => preset == "bluete" ? (e.BlueteEin, e.BlueteAus) : (e.VeggieEin, e.VeggieAus);

    /// <summary>
    /// Welches Preset gerade gilt: Modus Zeitplan UND beide Controller-Zeiten
    /// gleich den gespeicherten. Bei gleichen Zeitpaaren gewinnt Veggie — dann
    /// sind die Presets ohnehin nicht unterscheidbar.
    /// </summary>
    internal static string? AktivesPreset(LichtEinstellungen e, string? ein, string? aus, string? modus)
    {
        if (modus != Modi.Zeitplan || ein is null || aus is null) return null;
        if (ein == e.VeggieEin && aus == e.VeggieAus) return "veggie";
        if (ein == e.BlueteEin && aus == e.BlueteAus) return "bluete";
        return null;
    }

    /// <summary>
    /// Der nächste Wechsel, lokal gerechnet — inklusive der Fälle über
    /// Mitternacht. Der Controller sagt es nicht, und ein „–" wäre die Zahl,
    /// nach der man am häufigsten schaut.
    /// </summary>
    internal static string? NaechsterWechsel(string? modus, string? ein, string? aus, bool? an)
    {
        if (modus != Modi.Zeitplan) return null;
        if (Zeit(ein) is not { } einZeit || Zeit(aus) is not { } ausZeit) return null;
        if (einZeit == ausZeit) return null;

        var jetzt = DateTime.Now.TimeOfDay;
        var lichtphase = an ?? InFenster(jetzt, einZeit, ausZeit);
        var ziel = lichtphase ? ausZeit : einZeit;
        var stunden = (ziel - jetzt + TimeSpan.FromDays(1)).TotalHours % 24;
        var rest = TimeSpan.FromHours(stunden);
        /* Nur die Restzeit (13.09.2026).
         *
         * Vorher stand hier „an 05:00 · in 7 h 5 min". Die Uhrzeit steht eine
         * Zeile darueber im Zeitplan und die Richtung gross daneben (AUS) —
         * dreimal dasselbe in zwei Zeilen. Uebrig bleibt die Zahl, nach der man
         * tatsaechlich schaut. */
        return $"in {(int)rest.TotalHours} h {rest.Minutes} min";
    }

    private static bool InFenster(TimeSpan jetzt, TimeSpan ein, TimeSpan aus)
        => ein <= aus ? jetzt >= ein && jetzt < aus : jetzt >= ein || jetzt < aus;

    /// <summary>„05:00", „05:00:00" und „5:00" gelten als dieselbe Zeit.</summary>
    internal static bool Gleich(string ist, string soll)
    {
        if (string.Equals(ist, soll, StringComparison.OrdinalIgnoreCase)) return true;
        if (Zeit(ist) is { } a && Zeit(soll) is { } b) return a == b;
        return double.TryParse(ist, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(soll, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && Math.Abs(x - y) < 0.001;
    }

    internal static TimeSpan? Zeit(string? wert)
        => TimeSpan.TryParseExact(wert, new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss", @"h\:mm\:ss" },
            CultureInfo.InvariantCulture, out var t) ? t : null;

    private static string? Kurz(string? wert) => Zeit(wert) is { } t ? $"{t:hh\\:mm}" : null;
}

/// <summary>Das Livebild der Licht-Steuerung.</summary>
/// <param name="Modus">Der Modus des Controllers: <c>Off</c>, <c>On</c>, <c>Schedule</c>, …</param>
/// <param name="AktivesPreset"><c>veggie</c>, <c>bluete</c> oder null, wenn die Zeiten zu keinem passen.</param>
/// <param name="Unbestaetigt">Entitäten, deren Befehl der Controller noch nicht übernommen hat.</param>
/// <param name="Fehlgeschlagen">Entitäten, bei denen auch die Wiederholungen nichts gebracht haben.</param>
public sealed record LichtLive(
    bool HaErreichbar,
    string? Modus,
    int? Stufe,
    string? EinZeit,
    string? AusZeit,
    bool? LichtAn,
    bool? ControllerOnline,
    string? AktivesPreset,
    string? NaechsterWechsel,
    IReadOnlyList<string> Unbestaetigt,
    IReadOnlyList<string> Fehlgeschlagen,
    DateTime StandUtc);
