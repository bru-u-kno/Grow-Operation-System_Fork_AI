using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.21): Übersetzt eine Rolle in die Entität, die sie gerade erfüllt.
/// </summary>
/// <remarks>
/// <para><b>Eine Wahrheit je Gerät.</b> Jede Steuerung fragt hier nach
/// („co2" / „licht"), statt eine Entity-ID im eigenen Code zu halten. Ein
/// getauschter Ventilator ist damit eine Zeile in der Oberfläche, nicht ein
/// Commit.</para>
///
/// <para><b>Verweise.</b> Steht statt einer Entity-ID ein <c>@Name</c>, zeigt die
/// Rolle auf ein eigenes Gerät derselben Tabelle. Geräte, die mehrere Regelungen
/// nutzen (Zuluft, Außenluft), hängen so an genau einer Zeile. Ein Verweis, der
/// ins Leere zeigt, gilt als nicht zugeordnet — er wird NICHT still durch die
/// Vorgabe ersetzt, sonst schaltete eine Regelung heimlich auf ein anderes
/// Gerät um.</para>
/// </remarks>
public sealed class SteuerungGeraeteService
{
    private readonly SteuerungRepository _repo;

    public SteuerungGeraeteService(SteuerungRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Die Entität einer Rolle — die gespeicherte. Null, wenn keine zugeordnet ist,
    /// eine optionale Rolle bewusst leer steht oder ein Verweis ins Leere zeigt.
    /// </summary>
    public string? Entity(string modul, string schluessel)
    {
        var gespeichert = _repo.GetGeraete(modul)
            .FirstOrDefault(g => string.Equals(g.Rolle, schluessel, StringComparison.OrdinalIgnoreCase))?.EntityId;

        return Aufloesen(gespeichert);
    }

    /// <summary>Alle Rollen eines Moduls auf einmal — spart je Rolle eine Abfrage.</summary>
    public IReadOnlyDictionary<string, string?> EntitiesFuerModul(string modul)
    {
        var gespeichert = _repo.GetGeraete(modul)
            .ToDictionary(g => g.Rolle, g => g.EntityId, StringComparer.OrdinalIgnoreCase);

        var ergebnis = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rolle in SteuerungGeraeteRollen.FuerModul(modul))
        {
            ergebnis[rolle.Schluessel] = Aufloesen(
                gespeichert.TryGetValue(rolle.Schluessel, out var eigen) && !string.IsNullOrWhiteSpace(eigen) ? eigen : null);
        }

        // Fork AI (A-009): Rollen, die dieses Modul von einem anderen mitbenutzt
        // (Zelt-Fühler und Licht des Zusatz-Entfeuchters). Gelesen werden die
        // Zuordnungen des Quell-Moduls — es gibt nur eine Wahrheit je Gerät.
        foreach (var (quelle, schluessel) in SteuerungGeraeteRollen.MitbenutztVon(modul))
        {
            var eigen = _repo.GetGeraete(quelle)
                .FirstOrDefault(g => string.Equals(g.Rolle, schluessel, StringComparison.OrdinalIgnoreCase))?.EntityId;
            ergebnis[schluessel] = Aufloesen(eigen);
        }
        return ergebnis;
    }

    /// <summary>Die frei benannten Geräte, nach Namen.</summary>
    public IReadOnlyList<SteuerungGeraet> EigeneGeraete()
        => _repo.GetGeraete(SteuerungGeraeteRollen.EigenesModul)
            .OrderBy(g => g.Rolle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Was in der Datenbank steht — ohne Vorgabe, ohne Auflösung. Für die Oberfläche.</summary>
    public IReadOnlyDictionary<string, string> Gespeichert(string modul)
        => _repo.GetGeraete(modul).ToDictionary(g => g.Rolle, g => g.EntityId, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ Ändern

    /// <summary>
    /// Zuordnungen eines Moduls setzen. Liefert die Feldfehler je Rolle; ist die
    /// Liste leer, wurde gespeichert. Geprüft wird gegen die Domains der Rolle —
    /// ein <c>sensor.</c> in einer Schaltrolle ist ein Tippfehler, kein Wunsch.
    /// </summary>
    public IReadOnlyDictionary<string, string> Speichern(string modul, IReadOnlyDictionary<string, string?> zuordnungen)
    {
        var fehler = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var namen = EigeneGeraete().Select(g => g.Rolle).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        foreach (var (schluessel, roh) in zuordnungen)
        {
            var rolle = SteuerungGeraeteRollen.Finden(modul, schluessel);
            if (rolle is null)
            {
                fehler[schluessel] = "Diese Rolle gibt es in dieser Steuerung nicht.";
                continue;
            }

            var wert = roh?.Trim() ?? string.Empty;
            if (wert.Length == 0 || wert == SteuerungGeraeteRollen.BewusstLeer)
            {
                if (rolle.Pflicht) fehler[schluessel] = "Diese Rolle braucht ein Gerät.";
                continue;
            }

            if (wert[0] == SteuerungGeraeteRollen.VerweisZeichen)
            {
                if (!namen.Contains(wert[1..].Trim()))
                    fehler[schluessel] = "Dieses eigene Gerät gibt es nicht.";
                continue;
            }

            if (Domain(wert) is not { } domain)
            {
                fehler[schluessel] = "Das ist keine Entity-ID (erwartet wird z. B. sensor.zelt_co2).";
                continue;
            }

            if (rolle.Domains.Count > 0 && !rolle.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                fehler[schluessel] = $"Diese Rolle erwartet {string.Join(" oder ", rolle.Domains)}.";
        }

        if (fehler.Count > 0) return fehler;

        foreach (var (schluessel, roh) in zuordnungen)
        {
            var rolle = SteuerungGeraeteRollen.Finden(modul, schluessel)!;
            var wert = roh?.Trim() ?? string.Empty;
            // Leer löscht die Zuordnung. Die Marke „bewusst leer" wird nicht mehr geschrieben (es gibt keine
            // Vorgabe, die zurückkäme), aber noch gelesen: in älteren Datenbanken steht sie.
            _repo.SetGeraet(modul, schluessel,
                wert.Length == 0 || wert == SteuerungGeraeteRollen.BewusstLeer ? null : wert);
        }
        return fehler;
    }

    /// <summary>Ein eigenes Gerät anlegen oder ändern. Leerer Name oder leere Entität = Fehler.</summary>
    public string? EigenesGeraetSpeichern(string name, string entityId, string? alterName = null)
    {
        name = name.Trim();
        entityId = entityId.Trim();
        if (name.Length == 0) return "Das Gerät braucht einen Namen.";
        if (name[0] == SteuerungGeraeteRollen.VerweisZeichen) return "Der Name darf nicht mit @ beginnen.";
        if (Domain(entityId) is null) return "Das ist keine Entity-ID (erwartet wird z. B. fan.zuluft_keller).";

        if (!string.IsNullOrWhiteSpace(alterName) && !string.Equals(alterName, name, StringComparison.Ordinal))
            _repo.RemoveGeraet(SteuerungGeraeteRollen.EigenesModul, alterName.Trim());

        _repo.SetGeraet(SteuerungGeraeteRollen.EigenesModul, name, entityId);
        return null;
    }

    /// <summary>
    /// Ein eigenes Gerät löschen — nur, wenn keine Rolle darauf verweist. Sonst
    /// stünde eine Regelung ohne Gerät da, ohne dass es jemand gemerkt hätte.
    /// </summary>
    public string? EigenesGeraetLoeschen(string name)
    {
        var verweis = SteuerungGeraeteRollen.VerweisZeichen + name.Trim();
        var benutzt = _repo.GetGeraete()
            .Where(g => !string.Equals(g.Modul, SteuerungGeraeteRollen.EigenesModul, StringComparison.OrdinalIgnoreCase))
            .Where(g => string.Equals(g.EntityId, verweis, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.Modul)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (benutzt.Count > 0)
            return $"Wird noch genutzt von: {string.Join(", ", benutzt)}.";

        _repo.RemoveGeraet(SteuerungGeraeteRollen.EigenesModul, name.Trim());
        return null;
    }

    // ------------------------------------------------------------------ Intern

    private string? Aufloesen(string? wert)
    {
        if (string.IsNullOrWhiteSpace(wert)) return null;
        wert = wert.Trim();
        if (wert == SteuerungGeraeteRollen.BewusstLeer) return null;
        if (wert[0] != SteuerungGeraeteRollen.VerweisZeichen) return wert;

        var name = wert[1..].Trim();
        var ziel = _repo.GetGeraete(SteuerungGeraeteRollen.EigenesModul)
            .FirstOrDefault(g => string.Equals(g.Rolle, name, StringComparison.CurrentCultureIgnoreCase));
        return string.IsNullOrWhiteSpace(ziel?.EntityId) ? null : ziel.EntityId.Trim();
    }

    /// <summary>Die Domain einer Entity-ID, oder null, wenn es keine ist.</summary>
    public static string? Domain(string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        var wert = entityId.Trim();
        // Streng nach HA-Format, nicht nur „ein Punkt": der Wert landet roh in
        // erzeugten Automationen (siehe SteuerungBauteile.VorlageFuellen).
        return SteuerungBauteile.IstEntityId(wert) ? wert[..wert.IndexOf('.')] : null;
    }
}
