using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

/// <summary>Was der Sync zuletzt geschrieben hat — je Helfer eine Zeile.</summary>
public sealed class WochenplanSyncStand
{
    public string? LetzteSpalte { get; set; }
    public string? LetzterLauf { get; set; }

    /// <summary>Helfer → Wert, den DIESER Dienst zuletzt geschrieben hat.</summary>
    public Dictionary<string, double> Geschrieben { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Helfer, die von Hand verstellt wurden und deshalb in Ruhe gelassen werden.</summary>
    public List<string> VonDir { get; set; } = [];
}

/// <summary>Ein Wert, den der Sync betreut.</summary>
public sealed record WochenplanUebergabe(string Rolle, string EntityId, double Wert, string Zustand);

/// <summary>
/// Fork AI: übergibt die Werte der laufenden Plan-Woche an Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Warum überhaupt.</b> Seit forkai.46 wandern die Wochenwerte im Fork
/// von selbst mit. In Home Assistant standen sie trotzdem fest: Chiller-Ziele,
/// RH-Obergrenze und CO₂-Ziel mussten alle paar Wochen von Hand nachgezogen
/// werden — genau die Arbeit, die der Plan abschaffen soll.</para>
///
/// <para><b>Die Hand gewinnt.</b> Der Dienst merkt sich, welchen Wert er zuletzt
/// in einen Helfer geschrieben hat. Steht dort beim nächsten Lauf etwas anderes,
/// war es ein Mensch — dann wird nicht überschrieben, sondern der Helfer als
/// „von dir gesetzt" vermerkt und ausgelassen, bis er über
/// <see cref="Freigeben"/> wieder freigegeben wird. Ein Automatismus, der eine
/// bewusste Verstellung stillschweigend zurücknimmt, verliert sein Vertrauen —
/// und dann nützt er nichts mehr.</para>
///
/// <para><b>Nichts beim ersten Start.</b> Ohne gespeicherten Stand schreibt der
/// Dienst zunächst nichts, sondern merkt sich die aktuellen Werte als Ausgangslage.
/// Sonst überschriebe der Fork beim ersten Hochfahren Einstellungen, die in HA
/// längst stimmen — dasselbe Vorgehen wie beim CO₂-Modul.</para>
/// </remarks>
public sealed class WochenplanSyncService
{
    public const string Modul = "wochenplan";

    /// <summary>Die vier Werte mit einem Helfer in Home Assistant.</summary>
    /// <remarks>
    /// PPFD, Lichtzeiten und Dosiermengen bleiben bewusst draußen: Licht hängt an
    /// der AC-Infinity-Cloud, die parallele Schreibvorgänge verwirft, und
    /// Dosiermengen sind ein Merkposten fürs Anmischen, kein Stellwert.
    /// </remarks>
    public static class Rollen
    {
        public const string WasserTag = "wasser-tag";
        public const string WasserNacht = "wasser-nacht";
        public const string RhObergrenze = "rh-obergrenze";
        public const string Co2Ziel = "co2-ziel";

        /// <summary>Untere Alarmgrenze der Zelt-Regel „Lufttemperatur".</summary>
        public const string LuftUnten = "luft-unten";

        /// <summary>Obere Alarmgrenze der Zelt-Regel „Lufttemperatur".</summary>
        public const string LuftOben = "luft-oben";

        /// <summary>Untere Alarmgrenze der Zelt-Regel „Lufttemperatur" in der Dunkelphase.</summary>
        public const string LuftNachtUnten = "luft-nacht-unten";

        /// <summary>Obere Alarmgrenze der Zelt-Regel „Lufttemperatur" in der Dunkelphase.</summary>
        public const string LuftNachtOben = "luft-nacht-oben";

        /// <summary>Obere Alarmgrenze der Zelt-Regel „Luftfeuchte".</summary>
        public const string FeuchteOben = "feuchte-oben";
    }

    /// <summary>
    /// Wie weit die Lufttemperatur um den Planwert schwanken darf, bevor die
    /// Zelt-Regel meldet.
    /// </summary>
    /// <remarks>
    /// Der Plan nennt für die Luft EINE Zahl (SKX: 25 °C in der Blüte, 18 °C
    /// zum Schluss), eine Alarmregel braucht aber zwei. ±3 K ist bewusst
    /// grosszuegig: die Regel soll anschlagen, wenn etwas kaputt ist, nicht
    /// wenn die Abluft eine Stufe hinterherhinkt.
    /// </remarks>
    public const double LufttemperaturSpanne = 3.0;

    /// <summary>
    /// Um wie viel Kelvin das Nachtband unter dem Tagband liegt, wenn der Plan
    /// für die Nacht nichts Eigenes nennt.
    /// </summary>
    /// <remarks>
    /// 4 K, abgestimmt am 13.09.2026. Eine Absenkung in dieser Grössenordnung
    /// ist erwünscht — sie hält die Pflanze kompakt. Deutlich mehr wäre es
    /// nicht: dieselbe Wassermenge in kälterer Luft ergibt eine höhere relative
    /// Feuchte, und je tiefer die Nachttemperatur, desto näher rückt der
    /// Taupunkt an das Blatt. Genannt wird die Absenkung in Kelvin, weil es ein
    /// Unterschied ist und kein Messwert.
    /// </remarks>
    public const double Nachtabsenkung = 4.0;

    /// <summary>Messgrösse der Zelt-Regeln, die der Plan nachzieht.</summary>
    private static readonly Dictionary<string, string> Zeltregeln = new(StringComparer.OrdinalIgnoreCase)
    {
        [Rollen.LuftUnten] = "temperature",
        [Rollen.LuftOben] = "temperature",
        [Rollen.LuftNachtUnten] = "temperature",
        [Rollen.LuftNachtOben] = "temperature",
        // Die Feuchte bekommt bewusst KEIN Nachtband: nachts steigt sie von
        // selbst, und gerade dann ist sie gefährlich — Kondensat schlägt sich
        // auf dem kühlsten Punkt nieder, und das ist im Dunkeln das Blatt.
        // Eine nachts gelockerte Grenze wäre eine leisere Anzeige, kein
        // besserer Grow.
        [Rollen.FeuchteOben] = "humidity",
    };

    private static readonly Dictionary<string, string> Standardhelfer = new(StringComparer.OrdinalIgnoreCase)
    {
        [Rollen.WasserTag] = "input_number.chiller_zieltemperatur_tag",
        [Rollen.WasserNacht] = "input_number.chiller_zieltemperatur_nacht",
        [Rollen.RhObergrenze] = "input_number.co2_rh_obergrenze",
        [Rollen.Co2Ziel] = "input_number.co2_zielwert",
    };

    private readonly GrowRepository _grows;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly SteuerungRepository _repo;
    private readonly AlertRuleRepository _regeln;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly ILogger<WochenplanSyncService> _logger;

    public WochenplanSyncService(
        GrowRepository grows,
        KnowledgeBaseLoader wissen,
        SteuerungRepository repo,
        AlertRuleRepository regeln,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        ILogger<WochenplanSyncService> logger)
    {
        _grows = grows;
        _wissen = wissen;
        _repo = repo;
        _regeln = regeln;
        _ha = ha;
        _haSettings = haSettings;
        _logger = logger;
    }

    public WochenplanSyncStand Stand => _repo.GetEinstellungen<WochenplanSyncStand>(Modul) ?? new WochenplanSyncStand();

    /// <summary>
    /// Woran eine Rolle hängt: ein Helfer in Home Assistant oder eine
    /// Zelt-Regel im Fork (<c>zelt:{id}/{metrik}/{grenze}</c>).
    /// </summary>
    public string? ZielFuer(string rolle)
    {
        if (Zeltregeln.TryGetValue(rolle, out var metrik))
        {
            if (Spalte()?.Grow.TentId is not { } zeltId) return null;
            return $"zelt:{zeltId}/{metrik}/{Grenzfeld(rolle)}";
        }

        return HelferFuer(rolle);
    }

    /// <summary>
    /// Welches Feld der Zelt-Regel eine Rolle beschreibt: <c>min</c>, <c>max</c>,
    /// <c>nacht-min</c> oder <c>nacht-max</c>.
    /// </summary>
    /// <remarks>
    /// Der Name landet im Schlüssel, unter dem sich der Dienst merkt, was er
    /// zuletzt geschrieben hat. <c>min</c> und <c>max</c> heissen deshalb weiter
    /// so wie vorher — ein anderer Name wäre für den gemerkten Stand eine neue
    /// Grenze, und der erste Lauf danach hielte die alte für von Hand gesetzt.
    /// </remarks>
    public static string Grenzfeld(string rolle) => rolle switch
    {
        Rollen.LuftUnten => "min",
        Rollen.LuftNachtUnten => "nacht-min",
        Rollen.LuftNachtOben => "nacht-max",
        _ => "max",
    };

    /// <summary>Welcher Helfer welche Rolle hat — Standard, sofern nichts zugeordnet ist.</summary>
    public string? HelferFuer(string rolle)
    {
        var zugeordnet = _repo.GetGeraete(Modul).FirstOrDefault(g => string.Equals(g.Rolle, rolle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(zugeordnet?.EntityId)) return zugeordnet.EntityId;
        return Standardhelfer.GetValueOrDefault(rolle);
    }

    /// <summary>Zustand einer Rolle, die nicht der Sync, sondern die CO₂-Steuerung schreibt.</summary>
    public const string ZustandCo2Steuerung = "über CO₂-Steuerung";

    /// <summary>
    /// Fork AI (forkai.115, F-013): Gibt der Sync diese Rolle an die CO₂-Steuerung ab?
    /// </summary>
    /// <remarks>
    /// <para><b>Nur eine schreibende Stelle je Helfer.</b> Ist die CO₂-Steuerung
    /// einmal gespeichert, schreibt ihr Worker stündlich die drei Zielstufen —
    /// und die folgen mit Ziel-Quelle „Plan" ohnehin schon der Woche, nur als
    /// Prozentstaffel je Canopy-Bereich. Schriebe der Sync zusätzlich den
    /// rohen Planwert (1200) in die Warm-Stufe, setzte der Worker ihn binnen
    /// einer Stunde auf seine Staffel (960) zurück, und der Sync hielte das
    /// für eine Handänderung. Vor der gespeicherten CO₂-Seite bleibt es beim
    /// alten Weg: dann ist der Sync der einzige, der das Ziel nachzieht.</para>
    /// </remarks>
    public static bool RolleBeiCo2Steuerung(string rolle, bool co2Gespeichert)
        => co2Gespeichert && string.Equals(rolle, Rollen.Co2Ziel, StringComparison.OrdinalIgnoreCase);

    private bool Co2Gespeichert() => _repo.GetEinstellungen<Co2Einstellungen>(Co2SteuerungService.Modul) is not null;

    /// <summary>
    /// Fork AI (forkai.115, F-013): Die HA-Helfer, die der Wochenplan gerade führt,
    /// mit dem Wert der laufenden Woche.
    /// </summary>
    /// <remarks>
    /// Andere Module (CO₂, Kühler) fragen hier nach, bevor sie schreiben, und
    /// lassen diese Helfer aus. Auch ein von Hand verstellter Helfer bleibt
    /// „geführt": der Plan schreibt ihn dann nicht, aber ein anderes Modul
    /// darf die Handänderung ebenso wenig zurücknehmen. Gepflegt wird der Wert
    /// an einer Stelle — im Wochenplan (Festlegung vom 15.09.2026).
    /// Leer, solange kein einzelner Durchgang mit Wochen-Zielen läuft.
    /// </remarks>
    public IReadOnlyDictionary<string, double> GefuehrteHelfer()
    {
        var liste = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (Spalte() is not { } jetzt) return liste;

        var co2Gespeichert = Co2Gespeichert();
        foreach (var (rolle, wert) in Werte(jetzt.Spalte))
        {
            if (Zeltregeln.ContainsKey(rolle)) continue;
            if (RolleBeiCo2Steuerung(rolle, co2Gespeichert)) continue;
            if (HelferFuer(rolle) is { } entity) liste[entity] = wert;
        }

        return liste;
    }

    /// <summary>Gibt einen von Hand verstellten Helfer wieder für den Plan frei.</summary>
    public void Freigeben(string rolle)
    {
        var stand = Stand;
        if (ZielFuer(rolle) is not { } entity) return;

        stand.VonDir.RemoveAll(x => string.Equals(x, entity, StringComparison.OrdinalIgnoreCase));
        stand.Geschrieben.Remove(entity);
        _repo.SetEinstellungen(Modul, stand);
    }

    /// <summary>Die Werte der laufenden Woche, unabhängig davon, ob geschrieben wird.</summary>
    public IReadOnlyList<WochenplanUebergabe> Sollwerte()
    {
        var stand = Stand;
        var liste = new List<WochenplanUebergabe>();
        if (Spalte() is not { } jetzt) return liste;

        var co2Gespeichert = Co2Gespeichert();
        foreach (var (rolle, wert) in Werte(jetzt.Spalte))
        {
            if (ZielFuer(rolle) is not { } entity) continue;

            var zustand = RolleBeiCo2Steuerung(rolle, co2Gespeichert)
                ? ZustandCo2Steuerung
                : stand.VonDir.Contains(entity, StringComparer.OrdinalIgnoreCase)
                    ? "von dir gesetzt"
                    : "folgt dem Plan";

            liste.Add(new WochenplanUebergabe(rolle, entity, wert, zustand));
        }

        return liste;
    }

    /// <summary>Ein Durchlauf: vergleichen, schreiben, Stand fortschreiben.</summary>
    public async Task<int> UebergebenAsync(CancellationToken ct)
    {
        if (Spalte() is not { } laufend) return 0;
        var (grow, spalte) = laufend;

        var stand = Stand;
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var ersterLauf = stand.LetzterLauf is null;
        var geschrieben = 0;
        var co2Gespeichert = Co2Gespeichert();

        foreach (var (rolle, wert) in Werte(spalte))
        {
            if (Zeltregeln.ContainsKey(rolle)) continue; // eigener Durchgang unten
            if (RolleBeiCo2Steuerung(rolle, co2Gespeichert)) continue; // schreibt die CO₂-Steuerung
            if (HelferFuer(rolle) is not { } entity) continue;
            if (stand.VonDir.Contains(entity, StringComparer.OrdinalIgnoreCase)) continue;

            // Einzelabfrage je Helfer statt GetStatesAsync: dessen Wörterbuch
            // ist nach METRIK-Kennungen geschlüsselt (chiller, reservoir-temp),
            // nicht nach Entitäts-Kennungen — wer dort `input_number.…`
            // nachschlägt, findet grundsätzlich nichts.
            var istWert = Zahl((await _ha.GetEntityStateAsync(settings, entity, ct))?.State);

            // Beim ersten Lauf nur merken, nicht schreiben: was in HA steht, ist
            // die Ausgangslage, nicht ein Fremdeingriff.
            if (ersterLauf)
            {
                if (istWert is { } vorhanden) stand.Geschrieben[entity] = vorhanden;
                continue;
            }

            if (stand.Geschrieben.TryGetValue(entity, out var zuletzt)
                && istWert is { } jetzt
                && Math.Abs(jetzt - zuletzt) > 0.001)
            {
                stand.VonDir.Add(entity);
                _logger.LogInformation(
                    "Wochenplan: {Entity} wurde von Hand auf {Wert} gestellt — der Plan lässt ihn in Ruhe.",
                    entity, jetzt);
                continue;
            }

            if (istWert is { } unveraendert && Math.Abs(unveraendert - wert) < 0.001) continue;

            var ok = await _ha.CallEntityServiceAsync(settings, "input_number", "set_value", entity, ct,
                new Dictionary<string, object> { ["value"] = wert });

            if (!ok)
            {
                _logger.LogWarning("Wochenplan: {Entity} hat den Wert {Wert} nicht angenommen.", entity, wert);
                continue;
            }

            stand.Geschrieben[entity] = wert;
            geschrieben++;
        }

        geschrieben += Zeltgrenzen(grow, spalte, stand, ersterLauf);

        stand.LetzteSpalte = spalte.Id;
        stand.LetzterLauf = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _repo.SetEinstellungen(Modul, stand);
        return geschrieben;
    }

    /// <summary>
    /// Zieht die festen Zelt-Regeln für Lufttemperatur und Luftfeuchte nach.
    /// </summary>
    /// <remarks>
    /// Gleiche Regel wie bei den Helfern: was der Dienst zuletzt geschrieben
    /// hat, merkt er sich; steht beim naechsten Lauf etwas anderes drin, war es
    /// ein Mensch und die Grenze bleibt in Ruhe. Regeln auf „Plan" werden nicht
    /// angefasst — die holen ihre Grenzen ohnehin selbst.
    /// </remarks>
    private int Zeltgrenzen(GrowRun grow, FeedChartColumn spalte, WochenplanSyncStand stand, bool ersterLauf)
    {
        if (grow.TentId is not { } zeltId) return 0;

        var ziele = Werte(spalte)
            .Where(x => Zeltregeln.ContainsKey(x.Rolle))
            .ToDictionary(x => x.Rolle, x => x.Wert, StringComparer.OrdinalIgnoreCase);
        if (ziele.Count == 0) return 0;

        var regeln = _regeln.GetForTent(zeltId);
        var geschrieben = 0;

        foreach (var metrik in Zeltregeln.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (regeln.FirstOrDefault(r => string.Equals(r.MetricKey, metrik, StringComparison.OrdinalIgnoreCase)) is not { } regel)
            {
                continue;
            }

            if (regel.Quelle != Grenzwertquelle.Fest) continue;

            var neuMin = regel.MinValue;
            var neuMax = regel.MaxValue;
            var neuNachtMin = regel.NightMinValue;
            var neuNachtMax = regel.NightMaxValue;
            var beruehrt = false;

            foreach (var (rolle, metrikDerRolle) in Zeltregeln)
            {
                if (!string.Equals(metrikDerRolle, metrik, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ziele.TryGetValue(rolle, out var ziel)) continue;

                var feld = Grenzfeld(rolle);
                var schluessel = $"zelt:{zeltId}/{metrik}/{feld}";
                var ist = feld switch
                {
                    "min" => regel.MinValue,
                    "max" => regel.MaxValue,
                    "nacht-min" => regel.NightMinValue,
                    _ => regel.NightMaxValue,
                };

                if (stand.VonDir.Contains(schluessel, StringComparer.OrdinalIgnoreCase)) continue;

                if (ersterLauf)
                {
                    if (ist is { } vorhanden) stand.Geschrieben[schluessel] = vorhanden;
                    continue;
                }

                if (stand.Geschrieben.TryGetValue(schluessel, out var zuletzt)
                    && ist is { } jetzt
                    && Math.Abs(jetzt - zuletzt) > 0.001)
                {
                    stand.VonDir.Add(schluessel);
                    _logger.LogInformation(
                        "Wochenplan: Zelt-Grenze {Schluessel} wurde von Hand auf {Wert} gestellt — der Plan lässt sie in Ruhe.",
                        schluessel, jetzt);
                    continue;
                }

                if (ist is { } unveraendert && Math.Abs(unveraendert - ziel) < 0.001) continue;

                switch (feld)
                {
                    case "min": neuMin = ziel; break;
                    case "max": neuMax = ziel; break;
                    case "nacht-min": neuNachtMin = ziel; break;
                    default: neuNachtMax = ziel; break;
                }
                stand.Geschrieben[schluessel] = ziel;
                beruehrt = true;
            }

            if (!beruehrt) continue;

            _regeln.UpdateGrenzen(regel.Id, neuMin, neuMax, neuNachtMin, neuNachtMax);
            geschrieben++;
        }

        return geschrieben;
    }

    /// <summary>
    /// Fork AI (forkai.129): Lufttemperatur der laufenden Plan-Woche, für Module,
    /// die daraus einen eigenen Wert ableiten (Entfeuchter: Temperatur max. = Plan + Abstand).
    /// </summary>
    /// <remarks>
    /// Nacht wie bei den Zelt-Regeln: der Nachtwert des Plans, sonst Tag minus
    /// <see cref="Nachtabsenkung"/>. null, solange kein einzelner Durchgang mit
    /// Wochen-Zielen läuft oder der Plan keine Lufttemperatur nennt.
    /// </remarks>
    public (string Woche, double LuftTagC, double LuftNachtC)? PlanLuft()
    {
        if (Spalte() is not { } jetzt || jetzt.Spalte.AirTempC is not { } tag) return null;
        var woche = string.IsNullOrWhiteSpace(jetzt.Spalte.Label) ? jetzt.Spalte.Id : jetzt.Spalte.Label;
        return (woche, tag, jetzt.Spalte.AirTempNightC ?? tag - Nachtabsenkung);
    }

    /// <summary>Hat die Woche gewechselt, seit zuletzt übergeben wurde?</summary>
    public bool Wochenwechsel()
        => Spalte() is { } jetzt && !string.Equals(jetzt.Spalte.Id, Stand.LetzteSpalte, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Die Spalte des ersten laufenden Durchgangs mit aktiven Wochen-Zielen.
    /// </summary>
    /// <remarks>
    /// Bewusst nur einer: die Helfer in HA gehören dem Zelt, nicht dem Grow.
    /// Liefen zwei Durchgänge mit verschiedenen Programmen, würden sie sich um
    /// dieselben Helfer streiten — dann lieber nichts schreiben.
    /// </remarks>
    private (GrowRun Grow, FeedChartColumn Spalte)? Spalte()
    {
        var grows = _grows.GetActiveGrows().Where(MischplanService.NutztWochenziele).ToList();
        if (grows.Count != 1) return null;

        if (MischplanService.ZielSpalteFuerGrow(grows[0], _wissen.NutrientPrograms)?.Spalte is not { } spalte)
        {
            return null;
        }

        return (grows[0], spalte);
    }

    /// <summary>
    /// Was die Woche vorgibt, je Rolle. Öffentlich, damit die Zuordnung ohne
    /// Datenbank und ohne Home Assistant geprüft werden kann.
    /// </summary>
    public static IEnumerable<(string Rolle, double Wert)> Werte(FeedChartColumn spalte)
    {
        if (spalte.WaterTempDayC is { } tag) yield return (Rollen.WasserTag, tag);
        if (spalte.WaterTempNightC is { } nacht) yield return (Rollen.WasserNacht, nacht);
        if (spalte.RhMax is { } rh) yield return (Rollen.RhObergrenze, rh);

        // Beim CO₂ nennt der Plan eine Spanne; geregelt wird auf einen Wert.
        // Die Untergrenze ist die sichere Wahl: sie ist das, was der Plan
        // mindestens sehen will, und überfordert die Anlage an warmen Tagen nicht.
        if (spalte.Co2Min is { } co2) yield return (Rollen.Co2Ziel, co2);

        // Luft und Feuchte haben keinen Helfer, sondern eine Zelt-Regel: das
        // Zielband kennt beide nicht (HydroTargetValues fuehrt sie nicht), sie
        // koennen also nicht auf „Plan" stehen. Statt die Regeln alle paar
        // Wochen von Hand nachzuziehen, tut es der Plan hier — die Regeln
        // bleiben „Fest", nur ihre Zahlen wandern mit.
        if (spalte.AirTempC is { } luft)
        {
            yield return (Rollen.LuftUnten, luft - LufttemperaturSpanne);
            yield return (Rollen.LuftOben, luft + LufttemperaturSpanne);

            // Nennt der Plan keine Nachttemperatur, gilt der Tagwert minus der
            // üblichen Absenkung — mit derselben Spanne, damit Tag und Nacht
            // gleich streng sind und nur der Mittelpunkt wandert.
            var nachtLuft = spalte.AirTempNightC ?? luft - Nachtabsenkung;
            yield return (Rollen.LuftNachtUnten, nachtLuft - LufttemperaturSpanne);
            yield return (Rollen.LuftNachtOben, nachtLuft + LufttemperaturSpanne);
        }

        if (spalte.RhMax is { } rhMax) yield return (Rollen.FeuchteOben, rhMax);
    }

    private static double? Zahl(string? zustand)
        => double.TryParse(zustand, NumberStyles.Float, CultureInfo.InvariantCulture, out var wert) ? wert : null;
}
