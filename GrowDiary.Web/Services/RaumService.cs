using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.241): Der Raum — der Ort, in dem ein Zelt steht und aus dem es seine Luft zieht („Aufstellraum“,
/// im Anbau „Lung Room“). Er steht zwischen Außen (die echte Außenluft, anlagenweit) und dem Zelt.
/// </summary>
/// <remarks>
/// <b>Eine Stelle.</b> Name und Fühler des Raums stehen genau hier (Name in <c>ForkSteuerungEinstellungen</c>, Fühler als
/// Rollen des Moduls <c>raum</c>); Zuluft und Entfeuchter lesen sie, statt eigene Orte („Keller“) zu führen.
/// Bis zum Mehrraum-Ausbau gibt es einen Raum (<see cref="HauptRaumId"/>); ein Zelt zeigt mit <c>Tent.RaumId</c> darauf
/// oder auf keinen — dann stehen die Pflanzen direkt im Raum und der Raum ist das Zelt.
/// </remarks>
public sealed class RaumService
{
    public const string HauptRaumId = "haupt";
    public const string VorgabeName = "Aufstellraum";
    private const string Modul = "raum";
    private const int MaxNamenLaenge = 60;

    private readonly SteuerungRepository _repo;
    private readonly TentRepository _zelte;

    public RaumService(SteuerungRepository repo, TentRepository zelte)
    {
        _repo = repo;
        _zelte = zelte;
    }

    /// <summary>Aus der Anfrage eines Zeltes: nur der bekannte Raum zählt, alles andere heißt „kein eigener Raum“.</summary>
    public static string? RaumIdNormal(string? roh)
        => string.Equals(roh?.Trim(), HauptRaumId, StringComparison.OrdinalIgnoreCase) ? HauptRaumId : null;

    /// <summary>Der Anzeigename: der gespeicherte, sonst „Aufstellraum“.</summary>
    public string Name() => NameAus(_repo);

    /// <summary>Der Raumname für Dienste, die nur das Repository haben (Zuluft, Entfeuchter-Bremsen).</summary>
    public static string NameAus(SteuerungRepository repo)
    {
        var gespeichert = repo.GetEinstellungen<RaumEinstellungen>(Modul)?.Name;
        return string.IsNullOrWhiteSpace(gespeichert) ? VorgabeName : gespeichert.Trim();
    }

    public RaumDto Stand()
    {
        var zelte = _zelte.GetTents(includeArchived: false)
            .Where(z => z.RaumId == HauptRaumId)
            .Select(z => new RaumZeltDto(z.Id, z.Name))
            .ToList();
        var fuehler = Fuehler();
        return new RaumDto(HauptRaumId, Name(), VorgabeName, zelte, zelte.Count > 0 && fuehler is not null, fuehler);
    }

    // ------------------------------------------------ Raumluft-Werte (cc.263, Stufe 2)

    /// <summary>Die Fühler des Raums (Rollen <c>raum_temp</c> und <c>raum_rh</c>); fehlt eine, gibt es keine Raumwerte.</summary>
    public RaumFuehler? Fuehler()
    {
        var geraete = _repo.GetGeraete(Modul);
        string? Id(string rolle) => geraete.FirstOrDefault(g => string.Equals(g.Rolle, rolle, StringComparison.OrdinalIgnoreCase))?.EntityId;
        var temp = Id("raum_temp");
        var rh = Id("raum_rh");
        return string.IsNullOrWhiteSpace(temp) || string.IsNullOrWhiteSpace(rh) ? null : new RaumFuehler(temp.Trim(), rh.Trim());
    }

    /// <summary>Die Fühler, aus denen dieses Zelt seine Raumluft-Werte bezieht — nur wenn es im Raum steht und der Raum beide hat.</summary>
    public RaumFuehler? FuehlerFuer(Tent zelt)
        => RaumIdNormal(zelt.RaumId) is null ? null : Fuehler();

    /// <summary>
    /// Die Sensor-Zuordnung, mit der das Zelt wirklich gelesen wird: Steht es im Raum und hat der Raum beide Fühler, ersetzen
    /// diese die drei „Außen"-Zeilen des Zelts (Temperatur, Feuchte; die VPD rechnet <see cref="RaumLuft"/>). Sonst die eigene.
    /// </summary>
    public IReadOnlyList<TentSensor> WirksameSensoren(Tent zelt) => RaumLuft.Wirksam(zelt, FuehlerFuer(zelt));

    private bool AussenWerteAktiv(int zeltAnzahl) => zeltAnzahl > 0 && Fuehler() is not null;

    /// <summary>
    /// Der Name für „{Raumname} Temp./RLF/VPD", solange ein Zelt im Raum steht und der Raum beide Fühler hat — sonst <c>null</c>
    /// (dann heißt es wie bisher „Außen"). Kurz gemerkt: Beschriftungen werden je Verlaufszeile gefragt.
    /// </summary>
    public string? AussenBeschriftungsName()
    {
        var jetzt = Environment.TickCount64;
        lock (_beschriftungLock)
        {
            if (jetzt - _beschriftungStand < 5000 && _beschriftungStand != 0) return _beschriftung;
            var anzahl = _zelte.GetTents(includeArchived: false).Count(z => z.RaumId == HauptRaumId);
            _beschriftung = AussenWerteAktiv(anzahl) ? Name() : null;
            _beschriftungStand = jetzt;
            return _beschriftung;
        }
    }

    private readonly object _beschriftungLock = new();
    private string? _beschriftung;
    private long _beschriftungStand;

    /// <summary>Prüft und speichert den Namen. Leer setzt auf die Vorgabe. Liefert Feldfehler; leer heißt gespeichert.</summary>
    public Dictionary<string, string> NameSpeichern(string? name)
    {
        var fehler = new Dictionary<string, string>();
        var text = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (text is { Length: > MaxNamenLaenge }) fehler[nameof(RaumAenderung.Name)] = $"Name: höchstens {MaxNamenLaenge} Zeichen.";
        else if (text is not null && text.Any(char.IsControl)) fehler[nameof(RaumAenderung.Name)] = "Name: keine Steuerzeichen.";
        if (fehler.Count > 0) return fehler;

        _repo.SetEinstellungen(Modul, new RaumEinstellungen { Name = text });
        lock (_beschriftungLock) _beschriftungStand = 0;
        return fehler;
    }
}

/// <summary>Gespeichert in <c>ForkSteuerungEinstellungen</c> (Modul <c>raum</c>). Leer = Vorgabe.</summary>
public sealed class RaumEinstellungen
{
    public string? Name { get; set; }
}

public sealed record RaumZeltDto(int Id, string Name);

/// <param name="Name">Der angezeigte Name.</param>
/// <param name="Vorgabe">Der Name, den ein leeres Feld ergibt.</param>
/// <param name="Zelte">Die Zelte, die in diesem Raum stehen.</param>
/// <param name="AussenWerte">True, wenn „{Raumname} Temp./RLF/VPD" aus den Raum-Fühlern kommt (ein Zelt steht im Raum, beide Fühler sind zugeordnet).</param>
/// <param name="Fuehler">Die beiden Fühler des Raums, oder <c>null</c>, solange einer fehlt.</param>
public sealed record RaumDto(string Id, string Name, string Vorgabe, IReadOnlyList<RaumZeltDto> Zelte, bool AussenWerte = false, RaumFuehler? Fuehler = null);

/// <summary>Die beiden Fühler des Raums (Home-Assistant-Entitäten).</summary>
public sealed record RaumFuehler(string TempEntityId, string RhEntityId);

public sealed class RaumAenderung
{
    public string? Name { get; set; }
}
