using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Fork AI (A-010): Warum ein Start abgelehnt wurde.</summary>
public enum ProbelaufFehler
{
    Keiner,
    ModulUnbekannt,
    DauerUngueltig,
    LaeuftSchon,
    KeineMessung,
    GrenzeVerletzt,
    EingriffFehlgeschlagen,
}

/// <summary>Fork AI (A-010): Der Wunsch, einen Lauf zu starten.</summary>
/// <param name="Grenzen"><c>null</c> = die Voreinstellung aus den Pflanzenzielen.</param>
public sealed record ProbelaufStart(string Modul, int DauerMinuten, ProbelaufGrenzen? Grenzen);

/// <summary>Fork AI (A-010): Antwort auf einen Startwunsch.</summary>
public sealed record ProbelaufStartErgebnis(ProbelaufLauf? Lauf, ProbelaufFehler Fehler, string? Meldung);

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Probelauf — starten, überwachen, zurückstellen, auswerten.
/// </summary>
/// <remarks>
/// <para><b>Die Reihenfolge ist die Sicherheit.</b> Der Lauf steht mit geplantem und hartem Ende in der
/// Datenbank, <i>bevor</i> etwas am Zelt angefasst wird. Stirbt das Add-on danach, findet der nächste Start
/// den Lauf und stellt zurück (<see cref="OffeneBeiStartZurueckstellenAsync"/>).</para>
///
/// <para><b>Abbruch ist der Normalfall der Vorsicht.</b> Eine verletzte Grenze, ein Fühler ohne Wert über
/// eine Minute, ein unbestätigter Eingriff oder das harte Ende beenden den Eingriff sofort. Beendet heißt
/// zurückgestellt — und erst wenn das bestätigt ist, läuft der Nachlauf. Ist es nicht bestätigt, bleibt der
/// Lauf <see cref="ProbelaufStatus.RueckstellungOffen"/>, es wird weiter versucht und der Nutzer benachrichtigt.</para>
///
/// <para><b>Ein Lauf zur Zeit.</b> Solange irgendein Lauf nicht abgeschlossen ist, startet kein anderer.</para>
/// </remarks>
public sealed class ProbelaufService
{
    public const int HoechstDauerMinuten = 60;
    public const int VorgabeDauerMinuten = 20;

    /// <summary>Wie lange vor dem Start als Vergleich aus dem Verlauf geholt wird.</summary>
    public static readonly TimeSpan Vorlauf = TimeSpan.FromMinutes(10);

    /// <summary>Wie lange nach dem Zurückstellen noch aufgezeichnet wird.</summary>
    public static readonly TimeSpan Nachlauf = TimeSpan.FromMinutes(10);

    /// <summary>Spätestens so lange nach dem geplanten Ende wird zurückgestellt, was auch passiert.</summary>
    public static readonly TimeSpan Nachfrist = TimeSpan.FromMinutes(5);

    /// <summary>In diesem Abstand wird ein Messpunkt aufgezeichnet.</summary>
    public static readonly TimeSpan Messtakt = TimeSpan.FromSeconds(30);

    /// <summary>So oft wird ein offenes Zurückstellen wiederholt.</summary>
    public static readonly TimeSpan Wiederholung = TimeSpan.FromSeconds(30);

    /// <summary>Nach so vielen Fehlversuchen kommt die Meldung an den Nutzer.</summary>
    public const int VersucheBisMeldung = 3;

    private static readonly SemaphoreSlim Sperre = new(1, 1);

    /// <summary>
    /// Steht der Kühler gerade <b>absichtlich</b> aus, weil ein Probelauf ihn abgeschaltet hat? Dann ist das
    /// kein Ausfall und der Anlagen-Wächter schweigt.
    /// </summary>
    /// <remarks>
    /// Nur solange der Eingriff läuft. Im Nachlauf ist der Kühler wieder an; steht er dort aus, ist das ein
    /// echter Fehler (oder ein nicht bestätigtes Zurückstellen) und muss gemeldet werden.
    /// </remarks>
    public static bool KuehlerAbsichtlichAus(IEnumerable<ProbelaufLauf> offene)
        => offene.Any(l => l.Status == ProbelaufStatus.Laeuft && string.Equals(l.Modul, "chiller", StringComparison.OrdinalIgnoreCase));

    private readonly ProbelaufRepository _repo;
    private readonly ProbelaufEingriff _eingriff;
    private readonly IProbelaufMessung _messung;
    private readonly IProbelaufMeldung _meldung;
    private readonly TimeProvider _zeit;

    public ProbelaufService(
        ProbelaufRepository repo, ProbelaufEingriff eingriff, IProbelaufMessung messung, IProbelaufMeldung meldung,
        TimeProvider? zeit = null)
    {
        _repo = repo;
        _eingriff = eingriff;
        _messung = messung;
        _meldung = meldung;
        _zeit = zeit ?? TimeProvider.System;
    }

    private DateTime Jetzt => _zeit.GetUtcNow().UtcDateTime;

    // -------------------------------------------------------------------- Start

    public async Task<ProbelaufStartErgebnis> StartenAsync(ProbelaufStart start, CancellationToken ct)
    {
        if (!ProbelaufEingriff.KenntModul(start.Modul))
            return Abgelehnt(ProbelaufFehler.ModulUnbekannt, $"Für „{start.Modul}\" gibt es keinen Probelauf.");
        if (start.DauerMinuten < 1 || start.DauerMinuten > HoechstDauerMinuten)
            return Abgelehnt(ProbelaufFehler.DauerUngueltig, $"Die Dauer muss zwischen 1 und {HoechstDauerMinuten} Minuten liegen.");

        await Sperre.WaitAsync(ct);
        try
        {
            if (_repo.Offene().Count > 0)
                return Abgelehnt(ProbelaufFehler.LaeuftSchon, "Es läuft schon ein Probelauf. Erst wenn er abgeschlossen ist, kann der nächste starten.");

            var grenzen = start.Grenzen ?? await _messung.VoreinstellungAsync(ct);
            var jetzt = Jetzt;

            var aufnahme = await _messung.JetztAsync(ct);
            if (aufnahme is null)
                return Abgelehnt(ProbelaufFehler.KeineMessung, "Home Assistant liefert gerade keine Messwerte — ohne Sicht aufs Zelt startet kein Probelauf.");
            if (ProbelaufBewertung.Pruefen(grenzen, aufnahme.Werte, TimeSpan.Zero) is { } schonVerletzt)
                return Abgelehnt(ProbelaufFehler.GrenzeVerletzt, "Ein Lauf würde sofort abbrechen: " + schonVerletzt.Grund);

            var vorlauf = (await _messung.VerlaufAsync(jetzt - Vorlauf, jetzt, ct)).ToList();
            var ausgang = await _eingriff.AusgangszustandLesenAsync(start.Modul, ct);

            var geplant = jetzt.AddMinutes(start.DauerMinuten);
            var lauf = new ProbelaufLauf
            {
                Modul = start.Modul,
                Status = ProbelaufStatus.Laeuft,
                StartUtc = jetzt,
                GeplantesEndeUtc = geplant,
                HartesEndeUtc = geplant + Nachfrist,
                Grenzen = grenzen,
                Ausgangszustand = ausgang,
                Messreihe = new ProbelaufMessreihe(vorlauf, [aufnahme.Werte], []),
                TagPhaseBeiStart = aufnahme.TagPhase,
                LetzteMessungUtc = jetzt,
            };
            // Zuerst festhalten, dann eingreifen: stirbt das Add-on dazwischen, steht der Wecker schon da.
            _repo.Anlegen(lauf);

            var (ok, geaendert) = await _eingriff.EingreifenAsync(start.Modul, ausgang, ct);
            lauf.Ausgangszustand = geaendert;
            if (!ok)
            {
                await BeendenAsync(lauf, "Der Eingriff ließ sich nicht bestätigen — es wurde zurückgestellt.", ct);
                return new ProbelaufStartErgebnis(lauf, ProbelaufFehler.EingriffFehlgeschlagen,
                    "Der Eingriff ließ sich nicht bestätigen. Der Lauf wurde abgebrochen und zurückgestellt.");
            }

            _repo.Speichern(lauf);
            return new ProbelaufStartErgebnis(lauf, ProbelaufFehler.Keiner, null);
        }
        finally
        {
            Sperre.Release();
        }
    }

    private static ProbelaufStartErgebnis Abgelehnt(ProbelaufFehler fehler, string meldung) => new(null, fehler, meldung);

    // ------------------------------------------------------------------ Abbruch

    /// <summary>Beendet den Eingriff von Hand: zurückstellen, dann der Nachlauf.</summary>
    public async Task<ProbelaufLauf?> AbbrechenAsync(long id, string grund, CancellationToken ct)
    {
        await Sperre.WaitAsync(ct);
        try
        {
            var lauf = _repo.Holen(id);
            if (lauf is null || lauf.Status != ProbelaufStatus.Laeuft) return lauf;
            await BeendenAsync(lauf, grund, ct);
            return lauf;
        }
        finally
        {
            Sperre.Release();
        }
    }

    // --------------------------------------------------------------------- Takt

    /// <summary>
    /// Ein Schritt für alle offenen Läufe: Messen, Grenzen prüfen, Ende erkennen, Zurückstellen wiederholen,
    /// Nachlauf aufzeichnen, auswerten. Der Worker ruft das alle fünf Sekunden.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        await Sperre.WaitAsync(ct);
        try
        {
            foreach (var lauf in _repo.Offene())
            {
                try { await EinenSchrittAsync(lauf, ct); }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Ein Fehler bei einem Lauf darf den nächsten Takt nicht verhindern — der Wecker steht in der Datenbank.
                }
            }
        }
        finally
        {
            Sperre.Release();
        }
    }

    private async Task EinenSchrittAsync(ProbelaufLauf lauf, CancellationToken ct)
    {
        var jetzt = Jetzt;
        switch (lauf.Status)
        {
            case ProbelaufStatus.Laeuft:
                await LaufenSchrittAsync(lauf, jetzt, ct);
                break;

            case ProbelaufStatus.RueckstellungOffen:
                if (lauf.LetzteMessungUtc is { } letzter && jetzt - letzter < Wiederholung) return;
                await RueckstellenAsync(lauf, ct);
                break;

            case ProbelaufStatus.Nachlauf:
                await NachlaufSchrittAsync(lauf, jetzt, ct);
                break;
        }
    }

    /// <summary>Messen, ohne dass ein Fehler der Messung den Takt anhält: Fehler heißt „keine Messung".</summary>
    private async Task<ProbelaufMomentaufnahme?> SicherMessenAsync(CancellationToken ct)
    {
        try { return await _messung.JetztAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    private async Task LaufenSchrittAsync(ProbelaufLauf lauf, DateTime jetzt, CancellationToken ct)
    {
        // Das Ende zuerst: auch wenn die Messung hängt oder wirft, wird zum geplanten Ende zurückgestellt.
        if (jetzt >= lauf.GeplantesEndeUtc || jetzt >= lauf.HartesEndeUtc)
        {
            await BeendenAsync(lauf, null, ct);
            return;
        }

        var aufnahme = await SicherMessenAsync(ct);
        var werte = aufnahme?.Werte ?? new ProbelaufMesswerte(jetzt, null, null, null);

        // Fühler ohne Wert: die Uhr startet beim ersten fehlenden Wert und stoppt, wenn alle wieder da sind.
        var fehlt = aufnahme is null || (lauf.Grenzen.FeuchteMax is not null && werte.Feuchte is null)
                    || (lauf.Grenzen.TempMax is not null && werte.Temp is null)
                    || ((lauf.Grenzen.VpdMin is not null || lauf.Grenzen.VpdMax is not null) && werte.Vpd is null);
        lauf.FuehlerLosSeitUtc = fehlt ? lauf.FuehlerLosSeitUtc ?? jetzt : null;
        var fuehlerLosSeit = lauf.FuehlerLosSeitUtc is { } seit ? jetzt - seit : TimeSpan.Zero;

        if (aufnahme is not null && (lauf.LetzteMessungUtc is null || jetzt - lauf.LetzteMessungUtc >= Messtakt))
        {
            lauf.Messreihe.Waehrend.Add(werte with { ZeitUtc = jetzt });
            lauf.LetzteMessungUtc = jetzt;
        }

        if (ProbelaufBewertung.Pruefen(lauf.Grenzen, werte, fuehlerLosSeit) is { } verletzung)
        {
            await BeendenAsync(lauf, verletzung.Grund, ct);
            return;
        }

        _repo.Speichern(lauf);
    }

    private async Task NachlaufSchrittAsync(ProbelaufLauf lauf, DateTime jetzt, CancellationToken ct)
    {
        var aufnahme = await SicherMessenAsync(ct);
        if (aufnahme is not null && (lauf.LetzteMessungUtc is null || jetzt - lauf.LetzteMessungUtc >= Messtakt))
        {
            lauf.Messreihe.Nachlauf.Add(aufnahme.Werte with { ZeitUtc = jetzt });
            lauf.LetzteMessungUtc = jetzt;
        }

        if (jetzt >= (lauf.EingriffEndeUtc ?? jetzt) + Nachlauf)
        {
            Auswerten(lauf, aufnahme?.TagPhase, jetzt);
            lauf.Status = lauf.AbbruchGrund is null ? ProbelaufStatus.Fertig : ProbelaufStatus.Abgebrochen;
            lauf.EndeUtc = jetzt;
        }

        _repo.Speichern(lauf);
    }

    // ---------------------------------------------------- Beenden und Rückstellen

    /// <summary>Beendet den Eingriff: zurückstellen; bei Erfolg beginnt der Nachlauf, sonst bleibt es offen.</summary>
    private async Task BeendenAsync(ProbelaufLauf lauf, string? grund, CancellationToken ct)
    {
        lauf.AbbruchGrund = grund;
        lauf.EingriffEndeUtc = Jetzt;
        await RueckstellenAsync(lauf, ct);
    }

    private async Task RueckstellenAsync(ProbelaufLauf lauf, CancellationToken ct)
    {
        lauf.RueckstellVersuche++;
        lauf.LetzteMessungUtc = Jetzt;
        var ok = await _eingriff.ZurueckstellenAsync(lauf.Modul, lauf.Ausgangszustand, ct);
        lauf.Status = ok ? ProbelaufStatus.Nachlauf : ProbelaufStatus.RueckstellungOffen;
        _repo.Speichern(lauf);

        if (!ok && lauf.RueckstellVersuche >= VersucheBisMeldung && (lauf.RueckstellVersuche - VersucheBisMeldung) % 20 == 0)
        {
            await _meldung.SendenAsync(
                "Probelauf: Zurückstellen nicht bestätigt",
                $"Nach dem Probelauf an „{lauf.Modul}\" ließ sich der frühere Zustand {lauf.RueckstellVersuche}-mal nicht bestätigen. "
                + "Bitte in Home Assistant prüfen, ob Regelung und Gerät wieder laufen.",
                ct);
        }
    }

    /// <summary>
    /// Nach einem Neustart des Add-ons: Was noch im Eingriff steht, wird als Erstes zurückgestellt —
    /// vor dem ersten Takt, vor allem anderem.
    /// </summary>
    public async Task OffeneBeiStartZurueckstellenAsync(CancellationToken ct)
    {
        await Sperre.WaitAsync(ct);
        try
        {
            foreach (var lauf in _repo.Offene().Where(l => l.Status == ProbelaufStatus.Laeuft))
            {
                try { await BeendenAsync(lauf, "Das Add-on wurde während des Laufs neu gestartet — es wurde zurückgestellt.", ct); }
                catch (Exception) when (!ct.IsCancellationRequested) { /* der nächste Takt versucht es über RueckstellungOffen weiter */ }
            }
        }
        finally
        {
            Sperre.Release();
        }
    }

    // ---------------------------------------------------------------- Auswertung

    private static void Auswerten(ProbelaufLauf lauf, bool? tagPhaseJetzt, DateTime jetzt)
    {
        var hinweise = new List<string>();
        if (lauf.AbbruchGrund is { } grund)
        {
            var minuten = Math.Max(0, ((lauf.EingriffEndeUtc ?? jetzt) - lauf.StartUtc).TotalMinutes);
            hinweise.Add($"Abgebrochen nach {minuten.ToString("0.#", CultureInfo.GetCultureInfo("de-DE"))} Min.: {grund}");
        }

        if (lauf.TagPhaseBeiStart is { } vorher && tagPhaseJetzt is { } nachher && vorher != nachher)
            hinweise.Add("Während des Laufs hat die Lichtphase gewechselt — die Werte sind deshalb nur eingeschränkt vergleichbar.");

        if (ProbelaufEingriff.GeraetWarAn(lauf.Ausgangszustand) == false)
            hinweise.Add("Das Gerät war schon aus, als der Lauf begann: Der Lauf zeigt nur, was ohne seine Regelung passiert — nicht, was das Gerät bewirkt. In den Kenntnisstand geht er deshalb nicht als Wirkung ein.");

        hinweise.Add("Ein einzelner Lauf belegt wenig — am besten zu einer anderen Tageszeit wiederholen.");

        var k = ProbelaufBewertung.Kennzahlen(lauf.Messreihe.Vorlauf, lauf.Messreihe.Waehrend, lauf.Messreihe.Nachlauf);
        lauf.Auswertung = new ProbelaufAuswertung(k, hinweise, lauf.TagPhaseBeiStart, tagPhaseJetzt);
    }
}
