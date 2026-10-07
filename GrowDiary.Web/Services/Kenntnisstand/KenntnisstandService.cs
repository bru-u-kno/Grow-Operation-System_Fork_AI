using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-010, Etappe 2, 07.10.2026): Holt Verlauf, Ziele und Läufe und rechnet daraus den Kenntnisstand.
/// </summary>
/// <remarks>
/// <para>Sieben Tage Verlauf von vier Fühlern sind ein paar Megabyte aus Home Assistant — deshalb wird das Ergebnis fünf Minuten
/// gehalten. Ein Lauf ändert die Läufe, nicht den Verlauf; wer gerade einen Lauf beendet hat, wartet also höchstens fünf Minuten auf
/// die neue Wirkung. Die Läufe selbst lesen wir bei jedem Aufruf frisch aus der Datenbank (billig) — nur der teure Zielabgleich
/// kommt aus dem Zwischenspeicher.</para>
/// </remarks>
public sealed class KenntnisstandService
{
    public const int Tage = 7;
    private static readonly TimeSpan Haltedauer = TimeSpan.FromMinutes(5);
    private static readonly object Sperre = new();
    private static (DateTime Stand, IReadOnlyList<ZielZeile> Zeilen)? _zielabgleich;

    private readonly IProbelaufMessung _messung;
    private readonly ProbelaufRepository _repo;
    private readonly TimeProvider _zeit;

    public KenntnisstandService(IProbelaufMessung messung, ProbelaufRepository repo, TimeProvider? zeit = null)
    {
        _messung = messung;
        _repo = repo;
        _zeit = zeit ?? TimeProvider.System;
    }

    /// <summary>Für Tests: den Zwischenspeicher leeren.</summary>
    public static void Vergessen()
    {
        lock (Sperre) _zielabgleich = null;
    }

    public async Task<Kenntnisstand> BerechnenAsync(CancellationToken ct)
    {
        var jetzt = _zeit.GetUtcNow().UtcDateTime;
        var zielabgleich = await ZielabgleichAsync(jetzt, ct);
        var laeufe = _repo.Liste(500);

        var titel = SteuerungApiController.ModulTitel;
        var wirkung = KenntnisstandRechner.Wirkung(laeufe, titel);
        var abdeckung = KenntnisstandRechner.Abdeckung(laeufe, ProbelaufEingriff.Module, titel);
        return new Kenntnisstand(
            jetzt, Tage, zielabgleich, wirkung, abdeckung,
            KenntnisstandRechner.Naechster(abdeckung, zielabgleich),
            KenntnisstandRechner.Hinweise(zielabgleich, wirkung));
    }

    private async Task<IReadOnlyList<ZielZeile>> ZielabgleichAsync(DateTime jetzt, CancellationToken ct)
    {
        lock (Sperre)
        {
            if (_zielabgleich is { } z && jetzt - z.Stand < Haltedauer) return z.Zeilen;
        }

        var serie = await _messung.ZeltverlaufAsync(jetzt.AddDays(-Tage), jetzt, ct);
        var zeilen = KenntnisstandRechner.Zielabgleich(serie, await _messung.ZielbaenderAsync(ct));
        lock (Sperre) _zielabgleich = (jetzt, zeilen);
        return zeilen;
    }
}
