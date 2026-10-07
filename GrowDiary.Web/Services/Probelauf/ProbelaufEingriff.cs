using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Fork AI (A-010): Was am Chiller-Regler im Fork für die Dauer eines Laufs aus- und danach wieder eingeschaltet wird.</summary>
public interface IChillerRegler
{
    /// <summary>Schaltet die Fork-Regelung aller Zelte aus, die sie an haben. Liefert deren Ids.</summary>
    IReadOnlyList<int> Ausschalten();

    /// <summary>Schaltet die Fork-Regelung für diese Zelte wieder ein.</summary>
    void Einschalten(IEnumerable<int> zeltIds);
}

/// <summary><see cref="IChillerRegler"/> über <c>Tent.ChillerControlEnabled</c> — der <c>KuehlerWorker</c> liest es jede Minute.</summary>
public sealed class ChillerReglerImZelt : IChillerRegler
{
    private readonly TentRepository _zelte;

    public ChillerReglerImZelt(TentRepository zelte) => _zelte = zelte;

    public IReadOnlyList<int> Ausschalten()
    {
        var betroffen = new List<int>();
        foreach (var zelt in _zelte.GetTents().Where(z => z.ChillerControlEnabled))
        {
            zelt.ChillerControlEnabled = false;
            _zelte.UpdateTent(zelt);
            betroffen.Add(zelt.Id);
        }
        return betroffen;
    }

    public void Einschalten(IEnumerable<int> zeltIds)
    {
        foreach (var id in zeltIds)
        {
            if (_zelte.GetTent(id) is not { } zelt) continue;
            zelt.ChillerControlEnabled = true;
            _zelte.UpdateTent(zelt);
        }
    }
}

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Eingriff eines Probelaufs — Regelung pausieren, Gerät ausschalten,
/// und danach alles zurückstellen.
/// </summary>
/// <remarks>
/// <para><b>Reihenfolge.</b> Eingreifen: erst die Regelung aus (sonst schaltet sie das Gerät sofort wieder ein),
/// dann das Gerät. Zurückstellen: erst das Gerät in seinen früheren Zustand, dann die Regelung wieder an.
/// Ohne diese Reihenfolge gäbe es einen Moment, in dem die Regelung gegen ein falsch stehendes Gerät arbeitet.</para>
///
/// <para><b>Gespeicherte Ids.</b> Der Ausgangszustand trägt die beim Start aufgelösten Entity-IDs. Tauscht
/// jemand mitten im Lauf ein Gerät unter „Geräte &amp; Entitäten", stellt das Zurückstellen trotzdem das
/// Gerät zurück, das tatsächlich ausgeschaltet wurde.</para>
///
/// <para><b>Nur was lief.</b> Eine Automation, die vor dem Lauf schon aus war, wird nicht ein-, ein
/// nicht erreichbares Gerät nicht umgeschaltet. Zurückgestellt wird, was der Lauf verändert hat.</para>
///
/// <para><b>Wächter bleiben an.</b> Welche Automation pausiert wird, steht ausdrücklich am Bauteil
/// (<see cref="ProbelaufRolle"/>).</para>
/// </remarks>
public sealed class ProbelaufEingriff
{
    /// <summary>Welche Geräte-Rollen ein Modul für den Lauf ausschaltet — dieselben Rollen wie auf „Geräte &amp; Entitäten".</summary>
    private static readonly Dictionary<string, string[]> GeraeteRollen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["entfeuchter"] = ["port_schalter"],
        [SteuerungGeraeteRollen.ZusatzModul] = ["zusatz_schalter"],
        ["zuluft"] = ["port_schalter"],
        ["co2"] = ["port_schalter"],
        ["chiller"] = ["steckdose"],
    };

    /// <summary>Die Module, an denen ein Probelauf möglich ist.</summary>
    public static IReadOnlyCollection<string> Module => GeraeteRollen.Keys;

    public static bool KenntModul(string modul) => GeraeteRollen.ContainsKey(modul);

    private readonly IProbelaufHa _ha;
    private readonly IChillerRegler _chiller;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ProbelaufEingriff(IProbelaufHa ha, IChillerRegler chiller)
    {
        _ha = ha;
        _chiller = chiller;
    }

    // ----------------------------------------------------------------- Zustand

    private sealed record AutomationZustand(string Id, string Zustand);

    private sealed record GeraetZustand(string Rolle, string Id, string Zustand);

    private sealed record Ausgang(
        List<AutomationZustand> Automationen, List<GeraetZustand> Geraete, List<int> ChillerZelte);

    /// <summary>Liest, was der Lauf später verändern wird — vor dem Eingriff.</summary>
    public async Task<string> AusgangszustandLesenAsync(string modul, CancellationToken ct)
    {
        var rollen = RollenFuer(modul);

        var automationen = new List<AutomationZustand>();
        foreach (var id in await _ha.AutomationenAsync(modul, ProbelaufRolle.Pausieren, ct))
        {
            var zustand = await _ha.ZustandAsync(id, ct);
            if (zustand is not null) automationen.Add(new AutomationZustand(id, zustand));
        }

        var geraete = new List<GeraetZustand>();
        foreach (var rolle in rollen)
        {
            if (_ha.Entity(modul, rolle) is not { } id) continue;
            if (await _ha.ZustandAsync(id, ct) is { } zustand) geraete.Add(new GeraetZustand(rolle, id, zustand));
        }

        return JsonSerializer.Serialize(new Ausgang(automationen, geraete, []), Json);
    }

    /// <summary>
    /// Lief das Gerät, als der Lauf begann? <c>false</c>, wenn alle Geräte des Laufs schon aus waren — dann zeigt der Lauf nur, was ohne
    /// die Regelung passiert, nicht, was das Gerät bewirkt. <c>null</c>, wenn der Zustand unbekannt ist.
    /// </summary>
    public static bool? GeraetWarAn(string ausgangszustand)
    {
        try
        {
            var z = JsonSerializer.Deserialize<Ausgang>(ausgangszustand, Json);
            if (z?.Geraete is null || z.Geraete.Count == 0) return null; // auch „{}" (ältere Läufe, Tests) heißt: unbekannt
            var bekannte = z.Geraete.Where(g => NichtUnbekannt(g.Zustand)).ToList();
            return bekannte.Count == 0 ? null : bekannte.Any(g => !Ausschalter.IstAus(g.Zustand));
        }
        catch (JsonException) { return null; }
    }

    // --------------------------------------------------------------- Eingreifen

    /// <summary>
    /// Pausiert die Regelung und schaltet das Gerät aus. <c>true</c>, wenn alles bestätigt ist;
    /// bei <c>false</c> bleibt der Aufrufer in der Pflicht, <see cref="ZurueckstellenAsync"/> aufzurufen.
    /// </summary>
    /// <param name="ausgang">Das Ergebnis von <see cref="AusgangszustandLesenAsync"/>; wird um die Chiller-Zelte ergänzt zurückgegeben.</param>
    public async Task<(bool Ok, string Ausgang)> EingreifenAsync(string modul, string ausgang, CancellationToken ct)
    {
        _ = RollenFuer(modul);
        var zustand = JsonSerializer.Deserialize<Ausgang>(ausgang, Json)!;
        var ok = true;

        // Chiller: der Fork-eigene Regler zuerst — er schaltet die Steckdose sonst innerhalb einer Minute wieder an.
        if (string.Equals(modul, "chiller", StringComparison.OrdinalIgnoreCase))
            zustand.ChillerZelte.AddRange(_chiller.Ausschalten());

        foreach (var automation in zustand.Automationen.Where(a => IstAn(a.Zustand)))
            ok &= await _ha.AutomationSetzenAsync(automation.Id, false, ct);

        // Nur wenn die Regelung wirklich aus ist, wird das Gerät ausgeschaltet — sonst schaltet sie es zurück.
        if (ok)
        {
            foreach (var geraet in zustand.Geraete.Where(g => !Ausschalter.IstAus(g.Zustand) && NichtUnbekannt(g.Zustand)))
                ok &= await _ha.AusschaltenAsync(geraet.Id, ct);
        }

        return (ok, JsonSerializer.Serialize(zustand, Json));
    }

    // ----------------------------------------------------------- Zurückstellen

    /// <summary>
    /// Stellt alles zurück, was der Lauf verändert hat. <c>true</c> nur, wenn jeder Schritt bestätigt ist.
    /// Wirft nie: ein Fehler macht das Ergebnis zu <c>false</c>, damit es weiter versucht wird.
    /// </summary>
    public async Task<bool> ZurueckstellenAsync(string modul, string ausgang, CancellationToken ct)
    {
        Ausgang zustand;
        try { zustand = JsonSerializer.Deserialize<Ausgang>(ausgang, Json)!; }
        catch { return false; }

        var ok = true;
        try
        {
            foreach (var geraet in zustand.Geraete)
                ok &= await _ha.ZustandHerstellenAsync(geraet.Id, geraet.Zustand, ct);

            foreach (var automation in zustand.Automationen.Where(a => IstAn(a.Zustand)))
                ok &= await _ha.AutomationSetzenAsync(automation.Id, true, ct);

            if (zustand.ChillerZelte.Count > 0) _chiller.Einschalten(zustand.ChillerZelte);
        }
        catch
        {
            return false;
        }
        return ok;
    }

    private static bool IstAn(string zustand) => string.Equals(zustand, "on", StringComparison.OrdinalIgnoreCase);

    private static bool NichtUnbekannt(string zustand)
        => !string.Equals(zustand, "unavailable", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(zustand, "unknown", StringComparison.OrdinalIgnoreCase);

    private static string[] RollenFuer(string modul)
        => GeraeteRollen.TryGetValue(modul, out var rollen)
            ? rollen
            : throw new ArgumentException($"Für „{modul}\" gibt es keinen Probelauf.", nameof(modul));
}
