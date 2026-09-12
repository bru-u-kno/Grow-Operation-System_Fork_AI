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

    /// <summary>Messgrösse der Zelt-Regeln, die der Plan nachzieht.</summary>
    private static readonly Dictionary<string, string> Zeltregeln = new(StringComparer.OrdinalIgnoreCase)
    {
        [Rollen.LuftUnten] = "temperature",
        [Rollen.LuftOben] = "temperature",
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
            var grenze = rolle == Rollen.LuftUnten ? "min" : "max";
            return $"zelt:{zeltId}/{metrik}/{grenze}";
        }

        return HelferFuer(rolle);
    }

    /// <summary>Welcher Helfer welche Rolle hat — Standard, sofern nichts zugeordnet ist.</summary>
    public string? HelferFuer(string rolle)
    {
        var zugeordnet = _repo.GetGeraete(Modul).FirstOrDefault(g => string.Equals(g.Rolle, rolle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(zugeordnet?.EntityId)) return zugeordnet.EntityId;
        return Standardhelfer.GetValueOrDefault(rolle);
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

        foreach (var (rolle, wert) in Werte(jetzt.Spalte))
        {
            if (ZielFuer(rolle) is not { } entity) continue;

            var zustand = stand.VonDir.Contains(entity, StringComparer.OrdinalIgnoreCase)
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

        foreach (var (rolle, wert) in Werte(spalte))
        {
            if (Zeltregeln.ContainsKey(rolle)) continue; // eigener Durchgang unten
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
            var beruehrt = false;

            foreach (var (rolle, metrikDerRolle) in Zeltregeln)
            {
                if (!string.Equals(metrikDerRolle, metrik, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ziele.TryGetValue(rolle, out var ziel)) continue;

                var unten = rolle == Rollen.LuftUnten;
                var schluessel = $"zelt:{zeltId}/{metrik}/{(unten ? "min" : "max")}";
                var ist = unten ? regel.MinValue : regel.MaxValue;

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

                if (unten) neuMin = ziel; else neuMax = ziel;
                stand.Geschrieben[schluessel] = ziel;
                beruehrt = true;
            }

            if (!beruehrt) continue;

            _regeln.UpdateGrenzen(regel.Id, neuMin, neuMax);
            geschrieben++;
        }

        return geschrieben;
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
        var grows = _grows.GetActiveGrows().Where(g => g.UseFeedChartTargets).ToList();
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
        }

        if (spalte.RhMax is { } rhMax) yield return (Rollen.FeuchteOben, rhMax);
    }

    private static double? Zahl(string? zustand)
        => double.TryParse(zustand, NumberStyles.Float, CultureInfo.InvariantCulture, out var wert) ? wert : null;
}
