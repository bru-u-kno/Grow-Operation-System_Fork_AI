using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Die Eingaben eines Vorgangs lesen und prüfen — für Wasserwechsel und Nachfüllen gleich (A-006).
/// </summary>
/// <remarks>
/// <para>Zeitpunkt, Messung vorher/nachher, Buchungen und Tagebuchzeile
/// prüfen beide Abläufe nach denselben Regeln. Stünden sie in jedem Controller
/// einzeln, bekäme beim nächsten Befund nur einer die Reparatur.</para>
/// <para>Fehler landen im <see cref="ModelStateDictionary"/> des Controllers,
/// mit dem Abschnitt vorangestellt („Vorher.ReservoirEc"), damit die
/// Oberfläche weiß, in welchem Schritt sie stehen.</para>
/// </remarks>
internal sealed class VorgangEingabe
{
    /// <summary>Format von <c>ZeitpunktLokal</c>.</summary>
    public const string ZeitFormat = "yyyy-MM-ddTHH:mm";

    private readonly ModelStateDictionary _fehler;
    private readonly MeasurementSanityService _sperre;

    public VorgangEingabe(ModelStateDictionary fehler, MeasurementSanityService sperre)
    {
        _fehler = fehler;
        _sperre = sperre;
    }

    /// <summary>
    /// Der Zeitpunkt des Vorgangs (Ortszeit) — leer heißt jetzt.
    /// </summary>
    /// <param name="zukunftSatz">Der zweite Satz der Meldung, z. B. „Ein Wasserwechsel wird erfasst, nachdem er war."</param>
    public DateTime Zeitpunkt(string? zeitpunktLokal, string feld, string zukunftSatz)
    {
        var zeitpunkt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(zeitpunktLokal)
            && !DateTime.TryParse(zeitpunktLokal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out zeitpunkt))
        {
            _fehler.AddModelError(feld, "Datum oder Uhrzeit konnten nicht gelesen werden.");
            return DateTime.Now;
        }

        // Ein Zeitpunkt in der Zukunft ist ein Plan, keine Erfassung.
        if (zeitpunkt.ToUniversalTime() > DateTime.UtcNow.AddHours(1))
        {
            _fehler.AddModelError(feld, $"Der Zeitpunkt liegt in der Zukunft. {zukunftSatz}");
        }

        return zeitpunkt;
    }

    /// <summary>Liter: Pflicht, größer 0.</summary>
    public void LiterPruefen(double? liter, string feld, string frage)
    {
        if (liter is not { } wert || !double.IsFinite(wert) || wert <= 0)
        {
            _fehler.AddModelError(feld, $"{frage} Die Menge muss größer als 0 sein.");
        }
    }

    /// <summary>Wasserart, Anteil Osmose und Wasser-EC.</summary>
    public void WasserPruefen(WaterSource wasser, double? osmoseProzent, double? wasserEcMsCm)
    {
        if (!Enum.IsDefined(wasser)) _fehler.AddModelError("Wasser", "Die Wasserart ist ungültig.");
        if (wasser == WaterSource.Mixed && osmoseProzent is not (>= 0 and <= 100))
        {
            _fehler.AddModelError("OsmoseProzent", "Bei einer Mischung fehlt der Anteil Osmose (0–100 %).");
        }

        MeasurementSanityService.PhysikGrenze(_fehler, "WasserEcMsCm", "ec", wasserEcMsCm, "Der EC des Wassers");
    }

    /// <summary>
    /// Die Messwerte des Ablaufs als Messung — oder <c>null</c>, wenn keiner da ist.
    /// </summary>
    /// <remarks>
    /// Geprüft mit <see cref="MeasurementSanityService.ApplyBlockingValidation"/>,
    /// also mit denselben Grenzen wie jede Messung (<c>MessfelderVollstaendigTests</c>).
    /// </remarks>
    public Measurement? AlsMessung(GrowRun grow, VorgangMessungRequest? werte, string abschnitt, DateTime standardZeit, bool solutionChange, string notiz)
    {
        if (werte is null) return null;
        if (werte.ReservoirEc is null && werte.ReservoirPh is null && werte.ReservoirWaterTempC is null
            && werte.DissolvedOxygenMgL is null && werte.OrpMv is null)
        {
            return null;
        }

        var zeit = standardZeit;
        if (!string.IsNullOrWhiteSpace(werte.ZeitpunktLokal)
            && !DateTime.TryParse(werte.ZeitpunktLokal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out zeit))
        {
            _fehler.AddModelError($"{abschnitt}.ZeitpunktLokal", "Datum oder Uhrzeit konnten nicht gelesen werden.");
            return null;
        }

        var messung = new Measurement
        {
            GrowId = grow.Id,
            TakenAt = zeit,
            Stage = GrowStageResolver.Resolve(grow, zeit.Date),
            Source = NormalisierteHerkunft(werte.Herkunft) == "Sensor" ? ValueOrigin.HomeAssistant : ValueOrigin.Manual,
            Notes = notiz,
            ReservoirEc = werte.ReservoirEc,
            ReservoirPh = werte.ReservoirPh,
            ReservoirWaterTempC = werte.ReservoirWaterTempC,
            DissolvedOxygenMgL = werte.DissolvedOxygenMgL,
            OrpMv = werte.OrpMv,
            SolutionChange = solutionChange,
        };

        var fehler = new ModelStateDictionary();
        _sperre.ApplyBlockingValidation(fehler, grow, messung);
        foreach (var (feld, eintrag) in fehler)
        {
            foreach (var e in eintrag.Errors) _fehler.AddModelError($"{abschnitt}.{feld}", e.ErrorMessage);
        }

        return messung;
    }

    /// <summary>„vorher" darf nicht nach dem Vorgang liegen.</summary>
    public void VorherVorDemVorgang(Measurement? vorher, DateTime zeitpunkt, string was)
    {
        if (vorher is not null && vorher.TakenAt > zeitpunkt)
        {
            _fehler.AddModelError("Vorher.ZeitpunktLokal", $"Die Werte „vorher“ liegen nach dem {was}.");
        }
    }

    /// <summary>Die Buchungen — jede mit einem bestehenden Artikel oder der Wasserart.</summary>
    public List<VorgangBuchungEntwurf> Buchungen(IReadOnlyList<VorgangBuchungRequest> anfrage, IReadOnlyDictionary<int, Verbrauchsartikel> artikel)
    {
        var buchungen = new List<VorgangBuchungEntwurf>();
        for (var i = 0; i < anfrage.Count; i++)
        {
            var b = anfrage[i];
            var feld = $"Buchungen[{i}]";
            if (!double.IsFinite(b.Menge) || b.Menge <= 0)
            {
                _fehler.AddModelError($"{feld}.Menge", "Die Menge muss größer als 0 sein.");
                continue;
            }

            if (b.ArtikelId is { } id)
            {
                if (!artikel.ContainsKey(id)) _fehler.AddModelError($"{feld}.ArtikelId", $"Verbrauchsartikel {id} existiert nicht.");
                else buchungen.Add(new VorgangBuchungEntwurf(id, null, b.Menge));
            }
            else if (b.Wasser is WaterSource.Tap or WaterSource.RO)
            {
                buchungen.Add(new VorgangBuchungEntwurf(null,
                    b.Wasser == WaterSource.RO ? WasserwechselVorgangRepository.OsmosewasserArtikel : WasserwechselVorgangRepository.LeitungswasserArtikel,
                    b.Menge));
            }
            else
            {
                _fehler.AddModelError($"{feld}.ArtikelId", "Jede Buchung braucht einen Artikel oder die Wasserart (Leitung oder Osmose).");
            }
        }

        return buchungen;
    }

    /// <summary>Die Tagebuchzeile — oder <c>null</c>, wenn „Ins Tagebuch" aus ist.</summary>
    /// <param name="art">Seit A-006 setzt nur noch der Ablauf diese Arten; das freie Journal-Formular bietet sie nicht an.</param>
    public JournalEntry? Tagebuch(VorgangTagebuchRequest? anfrage, int growId, JournalEntryType art, DateTime zeitpunkt)
    {
        if (anfrage is null) return null;
        var titel = anfrage.Titel?.Trim();
        var text = anfrage.Text?.Trim();
        if (string.IsNullOrEmpty(titel) && string.IsNullOrEmpty(text))
        {
            _fehler.AddModelError("Tagebuch.Titel", "Für die Tagebuchzeile fehlt Titel oder Text.");
            return null;
        }

        return new JournalEntry
        {
            GrowId = growId,
            Title = string.IsNullOrEmpty(titel) ? null : titel,
            Body = string.IsNullOrEmpty(text) ? null : text,
            EntryType = art,
            Source = ValueOrigin.Manual,
            OccurredAtUtc = zeitpunkt.ToUniversalTime(),
        };
    }

    /// <summary>Die Notiz an der Messung „vorher" — sagt, woher die Werte kommen.</summary>
    public static string VorherNotiz(VorgangMessungRequest? vorher, string vorDem)
        => NormalisierteHerkunft(vorher?.Herkunft) switch
        {
            "Sensor" => $"{vorDem} — Werte vom Sensor übernommen.",
            "gemischt" => $"{vorDem} — teils vom Sensor, teils von Hand.",
            _ => $"{vorDem}.",
        };

    public static string NormalisierteHerkunft(string? herkunft)
        => herkunft?.Trim().ToLowerInvariant() switch
        {
            "sensor" => "Sensor",
            "gemischt" => "gemischt",
            _ => "Hand",
        };

    /// <summary>Was am Vorgang über „vorher" vermerkt wird: Herkunft und Zeit des Sensorwerts.</summary>
    public static (string? Herkunft, DateTime? SensorZeitUtc) VorherVermerk(Measurement? vorher, VorgangMessungRequest? anfrage)
        => vorher is null
            ? (null, null)
            : (NormalisierteHerkunft(anfrage?.Herkunft), anfrage?.SensorZeitUtc is { } sz ? ZuUtc(sz) : null);

    /// <summary>Ein Zeitpunkt aus der Anfrage: ohne Kennzeichnung gilt Ortszeit.</summary>
    public static DateTime ZuUtc(DateTime wert) => wert.Kind switch
    {
        DateTimeKind.Utc => wert,
        DateTimeKind.Local => wert.ToUniversalTime(),
        _ => DateTime.SpecifyKind(wert, DateTimeKind.Local).ToUniversalTime(),
    };

    /// <summary>Die Buchungen als Antwort — mit Artikelname und Einheit.</summary>
    public static List<VorgangBuchungDto> BuchungenAlsDto(IEnumerable<Verbrauch> buchungen, IReadOnlyDictionary<int, Verbrauchsartikel> artikel)
        => buchungen
            .Select(b => new VorgangBuchungDto(b.Id, b.ArtikelId,
                artikel.TryGetValue(b.ArtikelId, out var a) ? a.Name : $"Artikel {b.ArtikelId}",
                artikel.TryGetValue(b.ArtikelId, out var e) ? e.Einheit : string.Empty,
                b.Menge))
            .ToList();
}
