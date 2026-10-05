using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services.Tagebuch;

/// <summary>
/// Das Grow-Tagebuch (A-006): alles, was an einem Grow passiert ist, nach Tagen —
/// Messwerte von Hand und vom Sensor, Wasserwechsel, Nachfüllen, Dosierungen,
/// Notizen, Fotos, gebuchter Verbrauch und Sprünge, zu denen nichts eingetragen ist.
/// </summary>
/// <remarks>
/// <para><b>Eine Wahrheit für den Tag.</b> Die Tagesgrenze und die Uhrzeit jeder
/// Zeile rechnet der Server in der Ortszeit der Anlage — dieselbe Uhr, die auch die
/// Tageswerte der Sensoren schneidet (<see cref="SensorReadingRepository.GetReadingsForDay"/>).
/// Rechnete die Oberfläche selbst, könnte ein Browser in einer anderen Zeitzone eine
/// Messung von 00:29 in den Vortag schieben, während die Kurve daneben sie am Tag
/// zeigt. <c>Measurement.TakenAt</c> ist Ortszeit, alles andere UTC
/// (siehe <see cref="Wasserwechsel"/>) — umgerechnet wird genau hier.</para>
///
/// <para><b>Nichts Neues erfunden.</b> Jede Zeile kommt aus einer Tabelle, die es
/// schon gibt; das Tagebuch liest nur. Einzige eigene Speicherung sind die Sprünge
/// (<see cref="TagebuchRepository"/>), weil die Rohwerte nach sieben Tagen gehen.</para>
/// </remarks>
public sealed class TagebuchService
{
    /// <summary>So viele Tage mit Einträgen liefert eine Seite.</summary>
    public const int TageJeSeite = 7;

    /// <summary>Ein Eintrag bis so lange VOR dem Sprung erklärt ihn.</summary>
    /// <remarks>Wer zuerst einträgt und dann nachfüllt, ist selten — aber eine Stunde ist ehrlich.</remarks>
    public static readonly TimeSpan ErklaertVorher = TimeSpan.FromHours(1);

    /// <summary>Ein Eintrag bis so lange NACH dem Sprung erklärt ihn.</summary>
    /// <remarks>
    /// Eingetragen wird nach der Arbeit, nicht währenddessen: Brus Wasserwechsel am
    /// 04.10.2026 zeigt der Sensor um 16:44, eingetragen ist er für 17:30. Drei
    /// Stunden fassen „erst fertig machen, dann aufschreiben", ohne den Abend danach
    /// mitzunehmen (Faustregel Grow OS).
    /// </remarks>
    public static readonly TimeSpan ErklaertNachher = TimeSpan.FromHours(3);

    /// <summary>Eine Kalibrierung so nahe am Sprung erklärt ihn — die Anzeige wurde verschoben, nicht das Wasser.</summary>
    public static readonly TimeSpan KalibrierungNahe = TimeSpan.FromHours(1);

    /// <summary>Wasserwechsel, Lösungswechsel-Messung und Journal so nahe beieinander sind EIN Vorgang.</summary>
    public static readonly TimeSpan VorgangNahe = TimeSpan.FromHours(1);

    /// <summary>Für den Abgleich einer Handmessung: Sensorwerte bis so weit davor und danach.</summary>
    public static readonly TimeSpan AbgleichFenster = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Bis wohin Hand und Sensor „passen" — die Genauigkeit einer Dauersonde.
    /// </summary>
    /// <remarks>
    /// Herstellerangabe Bluelab Guardian Monitor (Datenblatt „Monitor Connect line"):
    /// pH ±0,1, EC ±0,1, Temperatur ±1 °C. Genauer als die Sonde lässt sich nicht
    /// sagen, ob die Handmessung oder der Sensor danebenliegt.
    /// </remarks>
    public static readonly (string Name, string Schluessel, double Toleranz, int Nachkomma)[] AbgleichGroessen =
    [
        ("pH", "reservoir-ph", 0.1, 2),
        ("EC", "reservoir-ec", 0.1, 2),
        ("Wasser", "reservoir-temp", 1.0, 1),
    ];

    /// <summary>Die sechs Kurven je Tag — Kennungen aus <see cref="TentSensorMetricKeyMap"/>.</summary>
    public static readonly (string Schluessel, string Name, int Nachkomma)[] Kurvenreihen =
    [
        ("reservoir-ec", "EC", 2),
        ("reservoir-ph", "pH", 2),
        ("reservoir-temp", "Wasser", 1),
        ("temperature", "Luft", 1),
        ("humidity", "Feuchte", 0),
        ("vpd", "VPD", 2),
    ];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly GrowRepository _grows;
    private readonly JournalRepository _journal;
    private readonly WasserwechselVorgangRepository _vorgaenge;
    private readonly SensorReadingRepository _rohwerte;
    private readonly TagebuchRepository _auffaellig;
    private readonly LightRepository _licht;
    private readonly DosingRepository _dosierung;
    private readonly KostenRepository _kosten;
    private readonly HardwareRepository _hardware;
    private readonly AddbackVorgangRepository? _nachfuellVorgaenge;

    public TagebuchService(
        GrowRepository grows,
        JournalRepository journal,
        SensorReadingRepository rohwerte,
        TagebuchRepository auffaellig,
        LightRepository licht,
        DosingRepository dosierung,
        KostenRepository kosten,
        HardwareRepository hardware,
        WasserwechselVorgangRepository vorgaenge,
        AddbackVorgangRepository? nachfuellVorgaenge = null)
    {
        _nachfuellVorgaenge = nachfuellVorgaenge;
        _grows = grows;
        _journal = journal;
        _rohwerte = rohwerte;
        _auffaellig = auffaellig;
        _licht = licht;
        _dosierung = dosierung;
        _kosten = kosten;
        _hardware = hardware;
        _vorgaenge = vorgaenge;
    }

    /* ------------------------------------------------------------------ */
    /* Sprünge erkennen und merken                                         */
    /* ------------------------------------------------------------------ */

    /// <summary>Sprünge in diesem Zeitraum finden und festhalten.</summary>
    /// <returns>Wie viele neu dazukamen.</returns>
    public int Erkennen(int tentId, DateTime vonUtc, DateTime bisUtc)
    {
        var neu = 0;
        foreach (var schluessel in Sprungerkennung.Messgroessen)
        {
            var werte = _rohwerte.GetReadings(tentId, schluessel, vonUtc, bisUtc)
                .Select(r => new Rohwert(DateTime.SpecifyKind(r.CapturedAtUtc.ToUniversalTime(), DateTimeKind.Utc), r.Value));
            var funde = Sprungerkennung.Finden(schluessel, werte);
            if (funde.Count > 0) neu += _auffaellig.Merken(tentId, funde);
        }

        return neu;
    }

    /// <summary>Alle Zelte, solange ihre Rohwerte noch da sind — jede Nacht vor dem Aufräumen.</summary>
    public int ErkennenFuerAlleZelte(DateTime jetztUtc)
    {
        var neu = 0;
        foreach (var zelt in _grows.GetTents(includeArchived: true))
        {
            neu += Erkennen(zelt.Id, jetztUtc.AddDays(-8), jetztUtc);
        }

        return neu;
    }

    /// <summary>„War nichts" — oder zurücknehmen.</summary>
    public bool Verwerfen(int id, bool verworfen, DateTime jetztUtc)
        => _auffaellig.Verwerfen(id, verworfen ? jetztUtc : null);

    public TagebuchAuffaelligkeit? Auffaelligkeit(int id) => _auffaellig.Get(id);

    /* ------------------------------------------------------------------ */
    /* Die Seite                                                           */
    /* ------------------------------------------------------------------ */

    /// <summary>Eine Seite des Tagebuchs: bis zu <paramref name="tage"/> Tage mit Einträgen, ab <paramref name="bis"/> rückwärts.</summary>
    public TagebuchSeiteDto? Seite(int growId, DateOnly? bis, int tage, DateTime jetztUtc)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return null;

        var (startUtc, endeUtc) = Zeitraum(grow, jetztUtc);

        // Frische Sprünge erkennen, solange die Rohwerte da sind. Nur auf der
        // ersten Seite: ältere Seiten liegen jenseits der sieben Tage.
        if (grow.TentId is { } zelt && bis is null)
        {
            var von = jetztUtc.AddDays(-8) > startUtc ? jetztUtc.AddDays(-8) : startUtc;
            Erkennen(zelt, von, jetztUtc);
        }

        var ereignisse = Sammeln(grow, startUtc, endeUtc);
        var tageMitEintraegen = ereignisse
            .GroupBy(e => OrtsTag(e.Utc))
            .OrderByDescending(g => g.Key)
            .ToList();

        var ab = tageMitEintraegen.Where(g => bis is not { } b || g.Key <= b).ToList();
        var seite = ab.Take(Math.Clamp(tage, 1, 31)).ToList();
        var aelter = ab.Skip(seite.Count).FirstOrDefault();

        var tageDto = seite.Select(g => new TagebuchTagDto(
                g.Key.ToString("yyyy-MM-dd", Invariant),
                AppCulture.German.DateTimeFormat.GetDayName(g.Key.DayOfWeek),
                PhaseAm(grow, g.Key),
                g.OrderByDescending(e => e.Utc).ThenByDescending(e => e.Rang).Select(e => e.Bauen()).ToList(),
                g.Count(e => e.Art == "auffaellig")))
            .ToList();

        string? rohdatenAb = null;
        if (grow.TentId is { } zeltId && _rohwerte.GetOldestReadingUtc(zeltId, "reservoir-ec") is { } aeltester)
        {
            rohdatenAb = OrtsTag(aeltester).ToString("yyyy-MM-dd", Invariant);
        }

        return new TagebuchSeiteDto(growId, grow.TentId, tageDto,
            aelter?.Key.ToString("yyyy-MM-dd", Invariant), rohdatenAb);
    }

    /// <summary>Ein Ereignis, bevor es gebaut wird — gebaut wird nur, was auf der Seite steht.</summary>
    private sealed record Roh(DateTime Utc, string Art, int Rang, Func<TagebuchEreignisDto> Bauen);

    private List<Roh> Sammeln(GrowRun grow, DateTime startUtc, DateTime endeUtc)
    {
        var liste = new List<Roh>();
        var messungen = _grows.GetMeasurementsForGrow(grow.Id);
        var journal = _journal.GetForGrow(grow.Id);
        var wechsel = _grows.GetChangeoutsForGrow(grow.Id);
        var addbacks = _grows.GetAddbackLogsForGrow(grow.Id);
        var fotos = _grows.GetPhotosForGrow(grow.Id);
        var artikel = _kosten.GetArtikel().ToDictionary(a => a.Id);
        var buchungen = _kosten.GetVerbraeuche().Where(v => v.GrowId == grow.Id).ToList();
        var dosen = grow.TentId is { } zelt
            ? _dosierung.GetEvents(tentId: zelt, limit: 5000)
                .Where(d => DosingService.KannGelaufenSein(d) && d.Trigger != DoseTrigger.Calibration && !d.Simulated)
                .Where(d => d.GrowId == grow.Id || (d.GrowId is null && d.OccurredAtUtc >= startUtc && d.OccurredAtUtc < endeUtc))
                .ToList()
            : [];
        var pumpen = grow.TentId is { } z ? _dosierung.GetPumps(z).ToDictionary(p => p.Id) : new Dictionary<int, DosingPump>();

        // Buchungen eines Vorgangs gehören zum Vorgang — nicht noch einmal an eine Messung oder als eigene Zeile.
        var postenJeVorgang = buchungen.Where(b => b.VorgangId is not null)
            .GroupBy(b => b.VorgangId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => Posten(b, artikel)).ToList());
        // A-006 Etappe 3: dasselbe für den Nachfüll-Vorgang (eigener Verweis).
        var postenJeNachfuellen = buchungen.Where(b => b.AddbackVorgangId is not null)
            .GroupBy(b => b.AddbackVorgangId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => Posten(b, artikel)).ToList());
        var postenJeMessung = buchungen.Where(b => b.MessungId is not null && b.VorgangId is null && b.AddbackVorgangId is null)
            .GroupBy(b => b.MessungId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(b => Posten(b, artikel)).ToList());
        var fotosJeMessung = fotos.Where(f => f.MeasurementId is not null)
            .GroupBy(f => f.MeasurementId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ToDto()).ToList());

        // Fotos einer Messung stehen beim Journaleintrag, der auf sie zeigt — wie im Journal-Strom.
        var fotosBeimEintrag = journal.Where(j => j.MeasurementId is not null).Select(j => j.MeasurementId!.Value).ToHashSet();

        // --- Wasserwechsel. Seit forkai.172 legt der Ablauf einen Vorgang an
        // (Changeout, Messung vorher/nachher, Journal, Buchungen) — dann bündelt
        // der Vorgang selbst. Für Altdaten bleibt die Stunde-Nähe (VorgangNahe).
        var vorgaenge = _vorgaenge.FuerGrow(grow.Id).Where(v => v.ChangeoutId is not null)
            .ToDictionary(v => v.ChangeoutId!.Value);
        var messungNachId = messungen.ToDictionary(m => m.Id);
        var journalNachId = journal.ToDictionary(j => j.Id);
        var gebuendelteMessungen = vorgaenge.Values
            .SelectMany(v => new[] { v.MessungVorherId, v.MessungNachherId }).OfType<int>().ToHashSet();
        var gebuendelteEintraege = vorgaenge.Values.Select(v => v.JournalId).OfType<int>().ToHashSet();

        // Nachfüll-Vorgänge bündeln ihre Messungen und ihre Tagebuchzeile genauso.
        var nachfuellVorgaenge = (_nachfuellVorgaenge?.FuerGrow(grow.Id) ?? [])
            .Where(v => v.AddbackLogId is not null)
            .ToDictionary(v => v.AddbackLogId!.Value);
        gebuendelteMessungen.UnionWith(nachfuellVorgaenge.Values.SelectMany(v => new[] { v.MessungVorherId, v.MessungNachherId }).OfType<int>());
        gebuendelteEintraege.UnionWith(nachfuellVorgaenge.Values.Select(v => v.JournalId).OfType<int>());
        foreach (var c in wechsel.OrderBy(c => vorgaenge.ContainsKey(c.Id) ? 0 : 1))
        {
            var utc = Utc(c.PerformedAtUtc);
            Measurement? vorherMessung = null;
            Measurement? messung;
            JournalEntry? eintrag;
            List<TagebuchPostenDto> posten;
            if (vorgaenge.TryGetValue(c.Id, out var vorgang))
            {
                vorherMessung = vorgang.MessungVorherId is { } mv && messungNachId.TryGetValue(mv, out var a) ? a : null;
                messung = vorgang.MessungNachherId is { } mn && messungNachId.TryGetValue(mn, out var b) ? b : null;
                eintrag = vorgang.JournalId is { } jid && journalNachId.TryGetValue(jid, out var e) ? e : null;
                posten = postenJeVorgang.TryGetValue(vorgang.Id, out var pv) ? pv : [];
            }
            else
            {
                messung = messungen
                    .Where(m => m.SolutionChange && !gebuendelteMessungen.Contains(m.Id) && Abstand(MessUtc(m), utc) <= VorgangNahe)
                    .OrderBy(m => Abstand(MessUtc(m), utc))
                    .FirstOrDefault();
                eintrag = journal
                    .Where(j => j.EntryType == JournalEntryType.ReservoirChange && !gebuendelteEintraege.Contains(j.Id) && Abstand(Utc(j.OccurredAtUtc), utc) <= VorgangNahe)
                    .OrderBy(j => Abstand(Utc(j.OccurredAtUtc), utc))
                    .FirstOrDefault();
                if (messung is not null) gebuendelteMessungen.Add(messung.Id);
                if (eintrag is not null) gebuendelteEintraege.Add(eintrag.Id);
                posten = messung is not null && postenJeMessung.TryGetValue(messung.Id, out var p) ? p : [];
            }

            var bilder = new[] { vorherMessung, messung }.OfType<Measurement>()
                .SelectMany(m => fotosJeMessung.TryGetValue(m.Id, out var f) ? f : []).ToList();
            var vorher = vorherMessung is not null
                ? Werte(vorherMessung) with { Ph = vorherMessung.ReservoirPh ?? c.PhBefore, Ec = vorherMessung.ReservoirEc ?? c.EcBefore }
                : new TagebuchWerteDto(c.PhBefore, c.EcBefore, null, null, null, null, null, null, null, null);
            var nachher = messung is not null
                ? Werte(messung) with { Ph = messung.ReservoirPh ?? c.PhAfter, Ec = messung.ReservoirEc ?? c.EcAfter }
                : new TagebuchWerteDto(c.PhAfter, c.EcAfter, null, null, null, null, null, null, null, null);
            var vorgangId = vorgang?.Id;
            liste.Add(new Roh(utc, "wechsel", 3, () => Ereignis(
                $"wechsel-{c.Id}", "wechsel", utc, WechselTitel(c), wasser: true,
                wechsel: new TagebuchWechselDto(
                    c.Id,
                    vorgangId,
                    c.Kind == ChangeoutKind.Full,
                    c.VolumeChangedLiters,
                    c.PercentChanged,
                    c.WaterUsed?.ToString(),
                    c.WaterEcMsCm,
                    vorher,
                    nachher,
                    messung?.Id,
                    Leer(c.Notes),
                    eintrag is not null ? Notiz(eintrag) : null),
                posten: posten, fotos: bilder)));
        }

        // --- Messungen
        foreach (var m in messungen.Where(m => !gebuendelteMessungen.Contains(m.Id)))
        {
            var utc = MessUtc(m);
            var posten = postenJeMessung.TryGetValue(m.Id, out var p) ? p : [];
            var bilder = !fotosBeimEintrag.Contains(m.Id) && fotosJeMessung.TryGetValue(m.Id, out var f) ? f : [];
            var nachfuellen = m.TopOffLiters is not null || m.AddbackEc is not null;
            liste.Add(new Roh(utc, "messung", 1, () => Ereignis(
                $"messung-{m.Id}", "messung", utc, MessTitel(m), wasser: m.SolutionChange || nachfuellen,
                messung: new TagebuchMessungDto(
                    m.Id,
                    m.Source switch { ValueOrigin.HomeAssistant => "sensor", ValueOrigin.Imported => "import", _ => "hand" },
                    m.SolutionChange,
                    Werte(m),
                    m.Source == ValueOrigin.Manual && grow.TentId is { } zelt ? Abgleich(zelt, m, utc) : [],
                    m.Notes is { } n && !n.StartsWith("AutoMeasurement", StringComparison.Ordinal) ? Leer(n) : null),
                posten: posten, fotos: bilder)));
        }

        // --- Journal
        foreach (var j in journal.Where(j => !gebuendelteEintraege.Contains(j.Id)))
        {
            var utc = Utc(j.OccurredAtUtc);
            var meilenstein = IstMeilenstein(j.EntryType);
            var bilder = j.MeasurementId is { } mid && fotosJeMessung.TryGetValue(mid, out var f) ? f : [];
            liste.Add(new Roh(utc, meilenstein ? "meilenstein" : "notiz", 2, () => Ereignis(
                $"notiz-{j.Id}", meilenstein ? "meilenstein" : "notiz", utc, j.Title ?? string.Empty,
                wasser: j.EntryType is JournalEntryType.ReservoirChange or JournalEntryType.Feeding,
                notiz: Notiz(j), fotos: bilder)));
        }

        // --- Nachfüllen / Addback. Seit A-006 Etappe 3 als Vorgang: Messung
        // vorher/nachher, Buchungen und Tagebuchzeile stehen am Eintrag.
        foreach (var a in addbacks)
        {
            var utc = Utc(a.PerformedAtUtc);
            nachfuellVorgaenge.TryGetValue(a.Id, out var nv);
            var vorherMessung = nv?.MessungVorherId is { } mv && messungNachId.TryGetValue(mv, out var m1) ? m1 : null;
            var nachherMessung = nv?.MessungNachherId is { } mn && messungNachId.TryGetValue(mn, out var m2) ? m2 : null;
            var eintrag = nv?.JournalId is { } jid && journalNachId.TryGetValue(jid, out var j1) ? j1 : null;
            var posten = nv is not null && postenJeNachfuellen.TryGetValue(nv.Id, out var pn) ? pn : [];
            var bilder = new[] { vorherMessung, nachherMessung }.OfType<Measurement>()
                .SelectMany(m => fotosJeMessung.TryGetValue(m.Id, out var f) ? f : []).ToList();
            liste.Add(new Roh(utc, "addback", 3, () => Ereignis(
                $"addback-{a.Id}", "addback", utc, AddbackTitel(a), wasser: true,
                addback: new TagebuchAddbackDto(a.Id, a.Kind.ToString(), a.LitersAdded, a.EcBefore, a.EcAfter,
                    a.PhBefore, a.PhAfter, a.WaterUsed?.ToString(), Leer(a.Notes),
                    nv?.Id,
                    nv is null ? null : vorherMessung is not null ? Werte(vorherMessung) : new TagebuchWerteDto(a.PhBefore, a.EcBefore, null, null, null, null, null, null, null, null),
                    nv is null ? null : nachherMessung is not null ? Werte(nachherMessung) : new TagebuchWerteDto(a.PhAfter, a.EcAfter, null, null, null, null, null, null, null, null),
                    eintrag is not null ? Notiz(eintrag) : null),
                posten: posten, fotos: bilder)));
        }

        // --- Dosierpumpe
        foreach (var d in dosen)
        {
            var utc = Utc(d.OccurredAtUtc);
            var pumpe = pumpen.TryGetValue(d.PumpId, out var pp) ? pp.Name : $"Pumpe {d.PumpId}";
            var ml = DosingService.HoechstensGegeben(d);
            liste.Add(new Roh(utc, "dosierung", 3, () => Ereignis(
                $"dosis-{d.Id}", "dosierung", utc, pumpe, wasser: true,
                dosis: new TagebuchDosisDto(d.Id, pumpe, ml,
                    pumpen.TryGetValue(d.PumpId, out var p2) ? p2.MetricKey switch { "reservoir-ph" => "pH", "reservoir-ec" => "EC", _ => null } : null,
                    d.ValueBefore, d.ValueAfter, d.Trigger != DoseTrigger.Manual))));
        }

        // --- Lose Fotos (ohne Messung)
        foreach (var foto in fotos.Where(f => f.MeasurementId is null))
        {
            var utc = Utc(foto.TakenAtUtc);
            liste.Add(new Roh(utc, "foto", 2, () => Ereignis(
                $"foto-{foto.Id}", "foto", utc, Leer(foto.Caption) ?? "Foto", wasser: false, fotos: [foto.ToDto()])));
        }

        // --- Gebuchter Verbrauch ohne Messung (CO₂-Steuerung, Nachträge)
        foreach (var gruppe in buchungen.Where(b => b.MessungId is null && b.VorgangId is null && b.AddbackVorgangId is null).GroupBy(b => Utc(b.ZeitpunktUtc)))
        {
            var utc = gruppe.Key;
            var posten = gruppe.Select(b => Posten(b, artikel)).ToList();
            var erster = gruppe.First();
            liste.Add(new Roh(utc, "verbrauch", 0, () => Ereignis(
                $"verbrauch-{erster.Id}", "verbrauch", utc, "Verbrauch gebucht", wasser: false, posten: posten)));
        }

        // --- Auffällig: Sprünge ohne passenden Eintrag
        if (grow.TentId is { } zeltId)
        {
            // Was einen Sprung erklärt — jede Art Eintrag, die vom Becken erzählt.
            var erklaerungen = new List<DateTime>();
            erklaerungen.AddRange(wechsel.Select(c => Utc(c.PerformedAtUtc)));
            erklaerungen.AddRange(addbacks.Select(a => Utc(a.PerformedAtUtc)));
            // Automatische Einträge (CO₂-Tagesbilanz) sind kein Bericht vom Becken.
            erklaerungen.AddRange(journal.Where(j => j.Source != ValueOrigin.HomeAssistant).Select(j => Utc(j.OccurredAtUtc)));
            // Eine Messung erklärt, wenn an ihr etwas passiert ist: Wechsel-Haken,
            // Nachfüllen, oder gebuchte Zugaben. Eine bloße Messung erklärt nichts.
            erklaerungen.AddRange(messungen
                .Where(m => m.SolutionChange || m.TopOffLiters is not null || m.AddbackEc is not null || postenJeMessung.ContainsKey(m.Id))
                .Select(MessUtc));
            // Buchungen ohne Messung (CO₂-Flasche, Wochenbuchung) sagen nichts
            // darüber, ob etwas ins Becken kam — sie erklären keinen Sprung.

            var kalibrierungen = Kalibrierungen(zeltId);
            var offen = _auffaellig.Lesen(zeltId, startUtc, endeUtc)
                .Where(a => a.VerworfenAmUtc is null)
                .Where(a => !erklaerungen.Any(t => ImFenster(a, t)))
                // Eine Dosis erklärt nur, was sie bewirken kann: pH− einen
                // fallenden pH, Dünger einen steigenden EC. Sonst erklärte jede
                // Säuregabe am Nachmittag ein Nachfüllen mit Wasser.
                .Where(a => !dosen.Any(d => ImFenster(a, Utc(d.OccurredAtUtc)) && DosisPasst(d, a, pumpen)))
                .Where(a => !kalibrierungen.Any(k => k.Messgroesse == a.MetricKey
                    && k.Utc >= a.BeginnUtc - KalibrierungNahe && k.Utc <= a.EndeUtc + KalibrierungNahe))
                .ToList();

            foreach (var gruppe in Gruppieren(offen))
            {
                var erster = gruppe[0];
                var utc = erster.BeginnUtc;
                liste.Add(new Roh(utc, "auffaellig", 4, () => Ereignis(
                    $"auffaellig-{erster.Id}", "auffaellig", utc, SprungTitel(erster), wasser: true,
                    auffaellig: new TagebuchAuffaelligDto(gruppe.Select(SprungDto).ToList()))));
            }
        }

        return liste;
    }

    private static bool ImFenster(TagebuchAuffaelligkeit a, DateTime t)
        => t >= a.BeginnUtc - ErklaertVorher && t <= a.EndeUtc + ErklaertNachher;

    /// <summary>Kann diese Dosis den Sprung bewirkt haben — Messgröße und Richtung?</summary>
    private static bool DosisPasst(DoseEvent dosis, TagebuchAuffaelligkeit a, IReadOnlyDictionary<int, DosingPump> pumpen)
    {
        // Unbekannte Pumpe oder „Eigenes Mittel": was es bewirkt, weiß niemand — dann erklärt sie.
        if (!pumpen.TryGetValue(dosis.PumpId, out var pumpe) || pumpe.MetricKey is not { } wirktAuf) return true;
        if (!string.Equals(wirktAuf, a.MetricKey, StringComparison.Ordinal)) return false;
        return pumpe.LowersValue == (a.Nachher < a.Vorher);
    }

    /// <summary>Sprünge, die sich zeitlich überschneiden, sind EIN Ereignis — der wichtigste zuerst.</summary>
    private static List<List<TagebuchAuffaelligkeit>> Gruppieren(List<TagebuchAuffaelligkeit> funde)
    {
        var gruppen = new List<List<TagebuchAuffaelligkeit>>();
        foreach (var fund in funde.OrderBy(f => f.BeginnUtc))
        {
            var passend = gruppen.FirstOrDefault(g => g.Any(x => fund.BeginnUtc <= x.EndeUtc && x.BeginnUtc <= fund.EndeUtc));
            if (passend is null) gruppen.Add([fund]);
            else passend.Add(fund);
        }

        foreach (var gruppe in gruppen)
        {
            gruppe.Sort((a, b) => Array.IndexOf(Sprungerkennung.Messgroessen, a.MetricKey)
                .CompareTo(Array.IndexOf(Sprungerkennung.Messgroessen, b.MetricKey)));
        }

        return gruppen;
    }

    private readonly record struct Kalibrierung(string Messgroesse, DateTime Utc);

    private List<Kalibrierung> Kalibrierungen(int zeltId)
    {
        var geraete = _hardware.GetHardwareItemsByTent(zeltId).Select(h => h.Id).ToHashSet();
        return _hardware.GetCalibrationEvents()
            .Where(k => geraete.Contains(k.HardwareItemId) && k.Status == CalibrationEventStatus.Completed && k.PerformedAtUtc is not null)
            .Select(k => new Kalibrierung(k.CalibrationType switch
            {
                CalibrationEventType.Ph => "reservoir-ph",
                CalibrationEventType.Ec => "reservoir-ec",
                _ => string.Empty,
            }, Utc(k.PerformedAtUtc!.Value)))
            .Where(k => k.Messgroesse.Length > 0)
            .ToList();
    }

    /// <summary>Handwert gegen den Sensor zur selben Zeit — nur, solange Rohwerte da sind.</summary>
    private List<TagebuchAbgleichDto> Abgleich(int zeltId, Measurement messung, DateTime utc)
    {
        var liste = new List<TagebuchAbgleichDto>();
        foreach (var (name, schluessel, toleranz, nachkomma) in AbgleichGroessen)
        {
            double? hand = schluessel switch
            {
                "reservoir-ph" => messung.ReservoirPh,
                "reservoir-ec" => messung.ReservoirEc,
                "reservoir-temp" => messung.ReservoirWaterTempC,
                _ => null,
            };
            if (hand is not { } h) continue;
            var werte = _rohwerte.GetReadings(zeltId, schluessel, utc - AbgleichFenster, utc + AbgleichFenster)
                .Select(r => r.Value).Order().ToList();
            if (werte.Count == 0) continue;
            var sensor = werte[werte.Count / 2];
            // Gerundet vergleichen — so, wie beide Zahlen auf dem Schirm stehen.
            var abstand = Math.Round(Math.Abs(Math.Round(h, nachkomma) - Math.Round(sensor, nachkomma)), nachkomma);
            liste.Add(new TagebuchAbgleichDto(name, h, Math.Round(sensor, 3), toleranz, nachkomma, abstand <= toleranz));
        }

        return liste;
    }

    /* ------------------------------------------------------------------ */
    /* Die Kurven eines Tages                                              */
    /* ------------------------------------------------------------------ */

    /// <summary>Die sechs Sensorkurven eines Ortstags, mit Lichtphase.</summary>
    public TagebuchKurvenDto? Kurven(int growId, DateOnly tag)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return null;
        var datum = tag.ToString("yyyy-MM-dd", Invariant);
        if (grow.TentId is not { } zelt)
        {
            return new TagebuchKurvenDto(datum, "keine", [], [], "keine");
        }

        var vonUtc = tag.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var bisUtc = tag.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

        var kurven = new List<TagebuchKurveDto>();
        var irgendRoh = false;
        var irgendTag = false;
        foreach (var (schluessel, name, nachkomma) in Kurvenreihen)
        {
            var (_, einheit) = AlertEvaluationService.MetricDisplay(schluessel);
            var einheitKurz = string.IsNullOrWhiteSpace(einheit) ? null : einheit.Trim();
            var roh = _rohwerte.GetReadings(zelt, schluessel, vonUtc, bisUtc.AddTicks(-1));
            if (roh.Count > 0)
            {
                irgendRoh = true;
                var punkte = roh
                    .Select(r => new TagebuchPunktDto(Minute(r.CapturedAtUtc, tag), Math.Round(r.Value, 3)))
                    .ToList();
                var sortiert = roh.Select(r => r.Value).Order().ToList();
                kurven.Add(new TagebuchKurveDto(schluessel, name, einheitKurz, nachkomma,
                    sortiert[0], sortiert[sortiert.Count / 2], sortiert[^1], punkte));
                continue;
            }

            // Ohne eigenen VPD-Sensor rechnet die Live-Seite VPD aus Luft und
            // Feuchte (GrowDashboardComposer → VpdCalculator) — hier genauso,
            // aus den Rohwerten derselben Minute.
            if (schluessel == "vpd" && VpdAusLuftUndFeuchte(zelt, vonUtc, bisUtc, tag) is { Count: > 0 } gerechnet)
            {
                irgendRoh = true;
                var werte = gerechnet.Select(p => p.Wert).Order().ToList();
                kurven.Add(new TagebuchKurveDto(schluessel, name + " (gerechnet)", einheitKurz, nachkomma,
                    werte[0], werte[werte.Count / 2], werte[^1], gerechnet));
                continue;
            }

            var stat = _rohwerte.GetDailyStats(zelt, schluessel, tag, tag).FirstOrDefault();
            if (stat is not null)
            {
                irgendTag = true;
                kurven.Add(new TagebuchKurveDto(schluessel, name, einheitKurz, nachkomma, stat.Min, stat.Median, stat.Max, []));
                continue;
            }

            kurven.Add(new TagebuchKurveDto(schluessel, name, einheitKurz, nachkomma, null, null, null, []));
        }

        var (licht, quelle) = Lichtphasen(zelt, tag, vonUtc, bisUtc);
        return new TagebuchKurvenDto(datum, irgendRoh ? "roh" : irgendTag ? "tag" : "keine", kurven, licht, quelle);
    }

    /// <summary>VPD je Zeitpunkt, an dem Luft und Feuchte beide gemessen wurden (höchstens 2 Minuten auseinander).</summary>
    private List<TagebuchPunktDto> VpdAusLuftUndFeuchte(int zelt, DateTime vonUtc, DateTime bisUtc, DateOnly tag)
    {
        var luft = _rohwerte.GetReadings(zelt, "temperature", vonUtc, bisUtc.AddTicks(-1));
        var feuchte = _rohwerte.GetReadings(zelt, "humidity", vonUtc, bisUtc.AddTicks(-1));
        // Mit dem Blattversatz des Zelts — derselbe Wert wie auf der Live-Kachel.
        var blatt = _grows.GetTent(zelt)?.LeafTempOffsetC ?? 0;
        var punkte = new List<TagebuchPunktDto>();
        foreach (var l in luft)
        {
            var f = feuchte.MinBy(x => (x.CapturedAtUtc - l.CapturedAtUtc).Duration());
            if (f is null || (f.CapturedAtUtc - l.CapturedAtUtc).Duration() > TimeSpan.FromMinutes(2)) continue;
            if (VpdCalculator.Calculate(l.Value, f.Value, blatt) is { } vpd)
            {
                punkte.Add(new TagebuchPunktDto(Minute(l.CapturedAtUtc, tag), Math.Round(vpd, 3)));
            }
        }

        return punkte;
    }

    /// <summary>
    /// Wann an diesem Tag Licht war: zuerst aus den geschalteten Flanken (der
    /// Lichtsensor sieht auch einen Ausfall), sonst aus dem aktiven Lichtplan.
    /// </summary>
    /// <remarks>
    /// Dieselbe Reihenfolge wie <see cref="LightClock.Resolve"/>. Ein Lichtplan
    /// kennt nur seinen heutigen Stand — für einen Tag vor einer Umstellung
    /// (18/6 → 12/12) stimmt er nicht mehr. Deshalb sagt die Quelle, woher die
    /// Spanne kommt, und die Legende schreibt es dazu.
    /// </remarks>
    private (List<TagebuchSpanneDto> Spannen, string Quelle) Lichtphasen(int zelt, DateOnly tag, DateTime vonUtc, DateTime bisUtc)
    {
        var flanken = _licht.GetLightTransitionsByTent(zelt)
            .Select(f => (Utc: Utc(f.OccurredAtUtc), f.Kind))
            .OrderBy(f => f.Utc)
            .ToList();
        var davor = flanken.LastOrDefault(f => f.Utc < vonUtc);
        var amTag = flanken.Where(f => f.Utc >= vonUtc && f.Utc < bisUtc).ToList();
        // Flanken zählen nur, wenn sie den Tag wirklich beschreiben: entweder am
        // Tag selbst, oder die letzte davor liegt nicht länger als einen Tag zurück.
        if (amTag.Count > 0 || (davor != default && vonUtc - davor.Utc < TimeSpan.FromHours(24)))
        {
            var spannen = new List<TagebuchSpanneDto>();
            var an = davor != default && davor.Kind == LightTransitionKind.LightOn ? 0 : (int?)null;
            foreach (var f in amTag)
            {
                var minute = Minute(f.Utc, tag);
                if (f.Kind == LightTransitionKind.LightOn) an ??= minute;
                else if (an is { } a)
                {
                    if (minute > a) spannen.Add(new TagebuchSpanneDto(a, minute));
                    an = null;
                }
            }

            if (an is { } offenAb) spannen.Add(new TagebuchSpanneDto(offenAb, 1440));
            return (spannen, "sensor");
        }

        var plan = _licht.GetActiveLightScheduleForTent(zelt);
        if (plan is null
            || !TimeOnly.TryParse(plan.LightsOnTime, Invariant, out var ein)
            || !TimeOnly.TryParse(plan.LightsOffTime, Invariant, out var aus)
            || ein == aus)
        {
            return ([], "keine");
        }

        var e = ein.Hour * 60 + ein.Minute;
        var o = aus.Hour * 60 + aus.Minute;
        return (e < o
            ? [new TagebuchSpanneDto(e, o)]
            : [new TagebuchSpanneDto(0, o), new TagebuchSpanneDto(e, 1440)], "plan");
    }

    /* ------------------------------------------------------------------ */
    /* Bausteine                                                           */
    /* ------------------------------------------------------------------ */

    private static TagebuchEreignisDto Ereignis(
        string schluessel, string art, DateTime utc, string titel, bool wasser,
        TagebuchMessungDto? messung = null, TagebuchWechselDto? wechsel = null, TagebuchAddbackDto? addback = null,
        TagebuchDosisDto? dosis = null, TagebuchNotizDto? notiz = null, TagebuchAuffaelligDto? auffaellig = null,
        IReadOnlyList<TagebuchPostenDto>? posten = null, IReadOnlyList<PhotoAssetDto>? fotos = null)
    {
        var ort = utc.ToLocalTime();
        return new TagebuchEreignisDto(schluessel, art, utc, ort.ToString("HH:mm", Invariant),
            ort.Hour * 60 + ort.Minute, titel, wasser, messung, wechsel, addback, dosis, notiz, auffaellig,
            posten ?? [], fotos ?? []);
    }

    private static TagebuchNotizDto Notiz(JournalEntry j)
        => new(j.Id, j.EntryType.ToString(), j.Title, j.Body, Utc(j.OccurredAtUtc), j.Source == ValueOrigin.HomeAssistant);

    private static TagebuchWerteDto Werte(Measurement m) => new(
        m.ReservoirPh, m.ReservoirEc, m.ReservoirWaterTempC, m.AirTemperatureC, m.HumidityPercent,
        m.Co2Ppm, m.OrpMv, m.DissolvedOxygenMgL, m.ReservoirLevelLiters, m.PpfdMol);

    private static TagebuchPostenDto Posten(Verbrauch v, IReadOnlyDictionary<int, Verbrauchsartikel> artikel)
        => artikel.TryGetValue(v.ArtikelId, out var a)
            ? new TagebuchPostenDto(a.Name, v.Menge, a.Einheit)
            : new TagebuchPostenDto($"Artikel {v.ArtikelId}", v.Menge, string.Empty);

    private TagebuchSprungDto SprungDto(TagebuchAuffaelligkeit a)
    {
        var (_, einheit) = AlertEvaluationService.MetricDisplay(a.MetricKey);
        return new TagebuchSprungDto(a.Id, a.MetricKey, SprungName(a.MetricKey),
            string.IsNullOrWhiteSpace(einheit) ? null : einheit.Trim(), SprungNachkomma(a.MetricKey),
            a.Vorher, a.Nachher, a.BeginnUtc, a.EndeUtc,
            a.BeginnUtc.ToLocalTime().ToString("HH:mm", Invariant), a.EndeUtc.ToLocalTime().ToString("HH:mm", Invariant),
            a.BeginnUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm", Invariant), a.EndeUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm", Invariant),
            (int)Math.Round((a.EndeUtc - a.BeginnUtc).TotalMinutes), Sprungerkennung.Regel(a.MetricKey));
    }

    private static string SprungName(string schluessel) => schluessel switch
    {
        "reservoir-ec" => "EC",
        "reservoir-ph" => "pH",
        _ => "Wasserstand",
    };

    private static int SprungNachkomma(string schluessel) => schluessel switch
    {
        "reservoir-ec" or "reservoir-ph" => 2,
        "reservoir-level-cm" => 1,
        _ => 0,
    };

    /// <summary>„EC fiel in 25 Minuten um 0,13".</summary>
    public static string SprungTitel(TagebuchAuffaelligkeit a)
    {
        var nachkomma = SprungNachkomma(a.MetricKey);
        var richtung = a.Nachher < a.Vorher ? "fiel" : "stieg";
        var betrag = Math.Abs(a.Nachher - a.Vorher).ToString("N" + nachkomma, AppCulture.German);
        var minuten = (int)Math.Round((a.EndeUtc - a.BeginnUtc).TotalMinutes);
        return $"{SprungName(a.MetricKey)} {richtung} in {minuten} Minuten um {betrag}";
    }

    private static string MessTitel(Measurement m) => m.Source switch
    {
        ValueOrigin.HomeAssistant when m.Notes?.Contains(nameof(AutoMeasurementTriggerKind.LightOnDelay), StringComparison.Ordinal) == true => "Licht an",
        ValueOrigin.HomeAssistant when m.Notes?.Contains(nameof(AutoMeasurementTriggerKind.LightOffDelay), StringComparison.Ordinal) == true => "Licht aus",
        ValueOrigin.HomeAssistant => "Automatische Messung",
        ValueOrigin.Imported => "Importierte Messung",
        ValueOrigin.Derived => "Berechnete Messung",
        _ => "Handmessung",
    };

    private static string WechselTitel(ChangeoutEntry c)
    {
        var art = c.Kind == ChangeoutKind.Full ? "Wasserwechsel" : "Teilwechsel";
        return c.VolumeChangedLiters is { } l ? $"{art} {l.ToString("0.#", AppCulture.German)} L" : art;
    }

    private static string AddbackTitel(AddbackLogEntry a)
    {
        var art = a.Kind switch
        {
            AddbackLogKind.TopOff => "Nachfüllen",
            AddbackLogKind.Correction => "Korrektur",
            _ => "Addback",
        };
        return a.LitersAdded is { } l && l > 0 ? $"{art} {l.ToString("0.#", AppCulture.German)} L" : art;
    }

    private static bool IstMeilenstein(JournalEntryType art) => art is JournalEntryType.GerminationConfirmed
        or JournalEntryType.CloneRooted or JournalEntryType.VegStarted or JournalEntryType.FlipToFlower
        or JournalEntryType.FinishStarted;

    private static string? PhaseAm(GrowRun grow, DateOnly tag)
    {
        var stichtag = tag.ToDateTime(new TimeOnly(12, 0));
        if (stichtag.Date < grow.StartDate.Date) return null;
        var stand = Phasenanker.Fuer(grow, stichtag);
        return $"{Phasenname.Fuer(stand.Stufe)} · Woche {stand.WocheInPhase} · Tag {stand.TagInPhase}";
    }

    /// <summary>Von der Ortsmitternacht des Starttags bis zum Ende (oder jetzt).</summary>
    private static (DateTime StartUtc, DateTime EndeUtc) Zeitraum(GrowRun grow, DateTime jetztUtc)
    {
        var start = DateOnly.FromDateTime(grow.StartDate).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var ende = grow.EndDate is { } e
            ? DateOnly.FromDateTime(e).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime()
            : jetztUtc.AddMinutes(1);
        return (start, ende);
    }

    /// <summary><c>Measurement.TakenAt</c> ist Ortszeit — als UTC.</summary>
    public static DateTime MessUtc(Measurement m) => m.TakenAt.Kind switch
    {
        DateTimeKind.Utc => m.TakenAt,
        _ => DateTime.SpecifyKind(m.TakenAt, DateTimeKind.Local).ToUniversalTime(),
    };

    /// <summary>Ein „…Utc"-Feld sicher als UTC (aus der Datenbank kommt es mal als Utc, mal als Local).</summary>
    private static DateTime Utc(DateTime wert) => wert.Kind switch
    {
        DateTimeKind.Local => wert.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(wert, DateTimeKind.Utc),
        _ => wert,
    };

    public static DateOnly OrtsTag(DateTime utc) => DateOnly.FromDateTime(Utc(utc).ToLocalTime());

    private static int Minute(DateTime utc, DateOnly tag)
    {
        var anfang = tag.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var minuten = (int)Math.Floor((Utc(utc).ToLocalTime() - anfang).TotalMinutes);
        return Math.Clamp(minuten, 0, 1439);
    }

    private static TimeSpan Abstand(DateTime a, DateTime b) => (a - b).Duration();

    private static string? Leer(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
