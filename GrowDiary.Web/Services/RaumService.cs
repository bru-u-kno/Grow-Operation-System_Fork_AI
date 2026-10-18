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
        return new RaumDto(HauptRaumId, Name(), VorgabeName, zelte);
    }

    /// <summary>Prüft und speichert den Namen. Leer setzt auf die Vorgabe. Liefert Feldfehler; leer heißt gespeichert.</summary>
    public Dictionary<string, string> NameSpeichern(string? name)
    {
        var fehler = new Dictionary<string, string>();
        var text = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (text is { Length: > MaxNamenLaenge }) fehler[nameof(RaumAenderung.Name)] = $"Name: höchstens {MaxNamenLaenge} Zeichen.";
        else if (text is not null && text.Any(char.IsControl)) fehler[nameof(RaumAenderung.Name)] = "Name: keine Steuerzeichen.";
        if (fehler.Count > 0) return fehler;

        _repo.SetEinstellungen(Modul, new RaumEinstellungen { Name = text });
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
public sealed record RaumDto(string Id, string Name, string Vorgabe, IReadOnlyList<RaumZeltDto> Zelte);

public sealed class RaumAenderung
{
    public string? Name { get; set; }
}
