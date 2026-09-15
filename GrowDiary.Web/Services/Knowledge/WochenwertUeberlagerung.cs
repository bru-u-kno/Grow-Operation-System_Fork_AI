using System.Runtime.CompilerServices;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.Knowledge;

/// <summary>
/// Fork AI (F-004, forkai.112): legt die vom Nutzer gesetzten Wochenwerte auf
/// die geladenen Düngeprogramme.
/// </summary>
/// <remarks>
/// <para><b>Warum an dieser Stelle.</b> Jeder Leser der Wochenspalten —
/// Mischplan, Zielband, Alarme, CO₂, Wochenplan-Sync, Wochenplan-Seite — geht
/// über <see cref="KnowledgeBaseLoader.NutrientPrograms"/>. Wird die Abweichung
/// direkt nach dem Laden auf genau diese Objekte gelegt, sehen alle sie, ohne
/// dass eine einzige Aufrufstelle angefasst wird.</para>
///
/// <para><b>Der Planwert geht nicht verloren.</b> Bevor ein Feld überschrieben
/// wird, merkt sich die Überlagerung den Wert aus der Datei — am Spalten-Objekt
/// selbst (<see cref="ConditionalWeakTable{TKey,TValue}"/>). Lädt der Loader neu,
/// entstehen neue Objekte und der Merker beginnt von vorn. So kann die Seite
/// „Plan: 18 °C" neben „dein Wert: 19 °C" zeigen, und das Zurücksetzen stellt
/// den Dateiwert wieder her, ohne die Datei neu zu lesen.</para>
/// </remarks>
public sealed class WochenwertUeberlagerung
{
    private readonly WochenwertRepository _repo;
    private readonly ILogger<WochenwertUeberlagerung> _logger;
    private readonly object _lock = new();
    private readonly ConditionalWeakTable<FeedChartColumn, Dictionary<string, double?>> _planwerte = new();

    private IReadOnlyList<NutrientProgramDefinition> _programme = Array.Empty<NutrientProgramDefinition>();
    private IReadOnlyList<Wochenwert> _gespeichert = Array.Empty<Wochenwert>();

    public WochenwertUeberlagerung(WochenwertRepository repo, ILogger<WochenwertUeberlagerung> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>Wird vom Loader nach jedem Laden aufgerufen.</summary>
    public void Anwenden(IReadOnlyList<NutrientProgramDefinition> programme)
    {
        lock (_lock)
        {
            _programme = programme;
            AnwendenOhneLock();
        }
    }

    /// <summary>Nach dem Speichern: Datenbank neu lesen und auf die geladenen Programme legen.</summary>
    public void Auffrischen()
    {
        lock (_lock)
        {
            AnwendenOhneLock();
        }
    }

    /// <summary>Der Wert aus der Programmdatei — auch wenn gerade eine Abweichung gilt.</summary>
    public double? Planwert(FeedChartColumn spalte, Wochenwertfelder.Feld feld)
    {
        lock (_lock)
        {
            return _planwerte.TryGetValue(spalte, out var gemerkt) && gemerkt.TryGetValue(feld.Name, out var plan)
                ? plan
                : feld.Lesen(spalte);
        }
    }

    public bool IstGeaendert(string programmId, string spalteId, string feld)
    {
        lock (_lock)
        {
            return _gespeichert.Any(w =>
                string.Equals(w.ProgrammId, programmId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(w.SpalteId, spalteId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(w.Feld, feld, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void AnwendenOhneLock()
    {
        IReadOnlyList<Wochenwert> gespeichert;
        try
        {
            gespeichert = _repo.Alle();
        }
        catch (Exception ex)
        {
            // Eine kaputte Tabelle darf das Wissen nicht mitreißen — dann gilt
            // der Plan aus der Datei, und das steht im Log.
            _logger.LogWarning(ex, "Wochenwerte konnten nicht gelesen werden — es gilt der Plan aus der Datei.");
            gespeichert = Array.Empty<Wochenwert>();
        }
        _gespeichert = gespeichert;

        var angewendet = 0;
        foreach (var programm in _programme)
        {
            if (programm.FeedChart is not { } chart) continue;

            foreach (var spalte in chart.Columns)
            {
                // Erst alles zurück auf den Plan — sonst bliebe eine gelöschte
                // Abweichung bis zum nächsten Neustart stehen.
                if (_planwerte.TryGetValue(spalte, out var gemerkt))
                {
                    foreach (var (name, plan) in gemerkt)
                    {
                        Wochenwertfelder.Finden(name)?.Schreiben(spalte, plan);
                    }
                    gemerkt.Clear();
                }

                foreach (var wert in gespeichert.Where(w =>
                             string.Equals(w.ProgrammId, programm.Id, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(w.SpalteId, spalte.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    if (Wochenwertfelder.Finden(wert.Feld) is not { } feld) continue;

                    var merker = _planwerte.GetOrCreateValue(spalte);
                    if (!merker.ContainsKey(feld.Name)) merker[feld.Name] = feld.Lesen(spalte);
                    feld.Schreiben(spalte, wert.Wert);
                    angewendet++;
                }
            }
        }

        if (gespeichert.Count > 0)
        {
            _logger.LogInformation(
                "Wochenwerte: {Angewendet} von {Gespeichert} eigenen Werten auf den Plan gelegt.",
                angewendet, gespeichert.Count);
        }
    }
}
