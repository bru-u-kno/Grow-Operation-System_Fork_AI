using System.Text;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

/// <summary>
/// Das Wissen von Cultivate Control als lesbarer Text.
/// </summary>
/// <remarks>
/// <para>Die Wissensdateien sind für die App geschrieben, nicht für einen
/// Leser: verschachteltes JSON mit Kürzeln, Bedingungen und Verweisen. Ein
/// Assistent kann das zwar entziffern, verschwendet aber die halbe
/// Aufmerksamkeit darauf — und Kürzel wie <c>sop-s1</c> gibt er dann als
/// Antwort zurück.</para>
///
/// <para>Deshalb hier dieselben Inhalte als Fließtext mit Überschriften. Was
/// den Agenten zum Fachmann macht, ist genau dieser Bestand: die Abläufe, die
/// Behandlungen samt Dosierung, die Symptome mit ihren Ursachen und die Regeln
/// aus dem Quellmaterial. Ohne ihn ist er ein Modell mit Forenwissen.</para>
///
/// <para>Reine Textumwandlung, kein Zustand: was hineingegeben wird, bestimmt
/// das Ergebnis. Dadurch prüfbar, ohne Datenbank und ohne Dateisystem.</para>
/// </remarks>
public static class AgentKnowledgeRenderer
{
    /// <summary>Die Abläufe — das Rückgrat jeder Empfehlung.</summary>
    public static string Sops(IReadOnlyList<SopDefinition> sops)
    {
        var text = new StringBuilder();
        Kopf(text, "Abläufe (SOPs)",
            "Jeder Ablauf hat ein Kürzel. Nenne es, wenn du dich darauf beziehst — "
            + "der Betreiber kann ihn dann in Cultivate Control öffnen und Schritt für Schritt abhaken.");

        foreach (var sop in sops.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {sop.Id} · {sop.Name}");
            text.AppendLine();

            var kopfzeile = new List<string> { $"Art: {sop.Type}" };
            if (sop.IntervalDays is { } intervall) kopfzeile.Add($"alle {intervall} Tage");
            if (sop.DurationDays is { } dauer) kopfzeile.Add($"läuft über {dauer} Tage");
            if (sop.EstimatedDurationMinutes is { } minuten) kopfzeile.Add($"etwa {minuten} Minuten Arbeit");
            if (sop.ApplicableSetups.Count > 0) kopfzeile.Add($"für {string.Join(", ", sop.ApplicableSetups)}");
            text.AppendLine(string.Join(" · ", kopfzeile));
            text.AppendLine();

            foreach (var ausloeser in sop.Triggers)
            {
                var teile = new List<string> { $"Auslöser: {ausloeser.Type}" };
                if (ausloeser.IntervalDays is { } i) teile.Add($"alle {i} Tage");
                if (ausloeser.WarningAfterDays is { } w) teile.Add($"Warnung nach {w} Tagen");
                if (ausloeser.CriticalAfterDays is { } k) teile.Add($"kritisch nach {k} Tagen");
                if (ausloeser.SymptomTags is { Count: > 0 } tags) teile.Add($"bei {string.Join(", ", tags)}");
                text.AppendLine($"- {string.Join(" · ", teile)}");
            }

            if (sop.RequiredMaterials.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"Material: {string.Join(", ", sop.RequiredMaterials)}");
            }

            text.AppendLine();
            text.AppendLine("Schritte:");
            foreach (var schritt in sop.Steps.OrderBy(s => s.Order))
            {
                var zusatz = new List<string>();
                if (schritt.WaitMinutes is { } warten) zusatz.Add($"{warten} min warten");
                if (!string.IsNullOrWhiteSpace(schritt.SubSopId)) zusatz.Add($"führt in {schritt.SubSopId}");
                if (!string.IsNullOrWhiteSpace(schritt.RepeatFor)) zusatz.Add($"je {schritt.RepeatFor} wiederholen");
                if (schritt.PhotoRequired) zusatz.Add("Foto nötig");

                // Verzweigungen ausschreiben: „nur wenn stark befallen" ist der
                // Unterschied zwischen einem Ablauf und einem Merkzettel.
                foreach (var bedingung in schritt.AllConditions())
                {
                    zusatz.Add($"nur wenn {bedingung.Key} = {string.Join(" oder ", bedingung.EqualsAny)}");
                }

                var klammer = zusatz.Count > 0 ? $" ({string.Join("; ", zusatz)})" : string.Empty;
                text.AppendLine($"{schritt.Order}. **{schritt.Title}**{klammer}");
                if (!string.IsNullOrWhiteSpace(schritt.Description))
                {
                    text.AppendLine($"   {Einzeilig(schritt.Description)}");
                }
            }

            Quellen(text, sop.Sources);
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Die Behandlungen — mit Dosierung, Anwendung und Konflikten.</summary>
    public static string Treatments(IReadOnlyList<TreatmentDefinition> treatments)
    {
        var text = new StringBuilder();
        Kopf(text, "Behandlungen",
            "Dosierungen stehen so da, wie sie in der Quelle stehen. Rechne sie nicht um "
            + "und runde sie nicht — wenn eine Menge vom Beckenvolumen abhängt, sag das und "
            + "nenne das Volumen aus dem Lagebericht.");

        foreach (var mittel in treatments.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {mittel.Id} · {mittel.Name}");
            text.AppendLine();
            text.AppendLine($"Art: {mittel.Type} · Schwierigkeit: {mittel.Difficulty}");

            if (mittel.TargetSymptoms.Count > 0)
            {
                text.AppendLine($"Gegen: {string.Join(", ", mittel.TargetSymptoms)}");
            }

            text.AppendLine($"Dosierung: {mittel.Dosage.Standard}");
            if (!string.IsNullOrWhiteSpace(mittel.Dosage.Severe)) text.AppendLine($"Bei starkem Befall: {mittel.Dosage.Severe}");
            if (!string.IsNullOrWhiteSpace(mittel.Dosage.Context)) text.AppendLine($"Bezug: {mittel.Dosage.Context}");

            text.AppendLine($"Anwendung: {mittel.Application.Method}");
            if (!string.IsNullOrWhiteSpace(mittel.Application.Timing)) text.AppendLine($"Zeitpunkt: {mittel.Application.Timing}");
            if (!string.IsNullOrWhiteSpace(mittel.Application.Frequency)) text.AppendLine($"Häufigkeit: {mittel.Application.Frequency}");
            if (!string.IsNullOrWhiteSpace(mittel.Application.DurationStandard)) text.AppendLine($"Dauer: {mittel.Application.DurationStandard}");
            if (!string.IsNullOrWhiteSpace(mittel.ExpectedTimeToEffect)) text.AppendLine($"Wirkt nach: {mittel.ExpectedTimeToEffect}");

            // Das Wichtigste zuletzt und deutlich: was NICHT geht.
            if (mittel.PhaseFilter is { } phasen)
            {
                if (phasen.Blocked.Count > 0) text.AppendLine($"**Nicht in Phase:** {string.Join(", ", phasen.Blocked)}");
                if (phasen.Allowed.Count > 0) text.AppendLine($"Nur in Phase: {string.Join(", ", phasen.Allowed)}");
                if (phasen.BlockAfterFlowerWeek is { } woche) text.AppendLine($"**Nicht ab Blütewoche {woche}.**");
            }

            foreach (var einschraenkung in mittel.Restrictions)
            {
                text.AppendLine($"**Einschränkung:** {Einzeilig(einschraenkung)}");
            }

            foreach (var konflikt in mittel.Conflicts)
            {
                text.AppendLine($"**Konflikt:** {Einzeilig(Konflikt(konflikt))}");
            }

            if (mittel.HardwareRequirements.Count > 0)
            {
                text.AppendLine($"Braucht: {string.Join(", ", mittel.HardwareRequirements)}");
            }

            Quellen(text, mittel.Sources);
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Symptome und Erreger — der Weg von der Beobachtung zur Ursache.</summary>
    public static string SymptomsAndPathogens(
        IReadOnlyList<SymptomDefinition> symptoms,
        IReadOnlyList<PathogenDefinition> pathogens)
    {
        var text = new StringBuilder();
        Kopf(text, "Symptome und Erreger",
            "Ein Symptom hat fast immer mehrere mögliche Ursachen. Nenne sie, und nenne die "
            + "Prüfung, die sie auseinanderhält — rate nicht auf die häufigste.");

        text.AppendLine("# Symptome");
        text.AppendLine();
        foreach (var symptom in symptoms.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {symptom.Id} · {symptom.Name}");
            text.AppendLine($"Bereich: {symptom.Category}");
            if (symptom.PossibleCauses.Count > 0) text.AppendLine($"Mögliche Ursachen: {string.Join(", ", symptom.PossibleCauses)}");
            if (symptom.DiagnosticChecks.Count > 0)
            {
                text.AppendLine("Prüfen:");
                foreach (var pruefung in symptom.DiagnosticChecks) text.AppendLine($"- {Einzeilig(pruefung)}");
            }
            if (symptom.SuggestedSopIds.Count > 0) text.AppendLine($"Passende Abläufe: {string.Join(", ", symptom.SuggestedSopIds)}");
            if (symptom.SuggestedTreatmentIds.Count > 0) text.AppendLine($"Passende Behandlungen: {string.Join(", ", symptom.SuggestedTreatmentIds)}");
            text.AppendLine();
        }

        text.AppendLine("# Erreger");
        text.AppendLine();
        foreach (var erreger in pathogens.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {erreger.Id} · {erreger.Name}");
            if (!string.IsNullOrWhiteSpace(erreger.ScientificName)) text.AppendLine($"Wissenschaftlich: {erreger.ScientificName}");
            text.AppendLine($"Gruppe: {erreger.Category} · Risiko: {erreger.RiskLevel} · behandelbar: {(erreger.Treatable ? "ja" : "nein")}");
            if (erreger.Symptoms.Count > 0) text.AppendLine($"Zeigt sich als: {string.Join(", ", erreger.Symptoms)}");
            if (!string.IsNullOrWhiteSpace(erreger.TreatmentSopId)) text.AppendLine($"Behandlung: {erreger.TreatmentSopId}");
            if (!string.IsNullOrWhiteSpace(erreger.PreventiveSopId)) text.AppendLine($"Vorbeugung: {erreger.PreventiveSopId}");
            if (!string.IsNullOrWhiteSpace(erreger.Notes)) text.AppendLine(Einzeilig(erreger.Notes));
            Quellen(text, erreger.Sources);
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Die Regeln — kurze Sätze, die im Zweifel den Ausschlag geben.</summary>
    public static string Guidance(IReadOnlyList<GuidanceDefinition> guidance)
    {
        var text = new StringBuilder();
        Kopf(text, "Regeln",
            "Kurze Sätze aus dem Quellmaterial. Der häufige Fehler steht dabei — "
            + "das ist oft der wertvollere Teil.");

        foreach (var regel in guidance.OrderBy(g => g.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {regel.Id} · {regel.Title}");
            text.AppendLine(Einzeilig(regel.Rule));
            if (!string.IsNullOrWhiteSpace(regel.Rationale)) text.AppendLine($"Warum: {Einzeilig(regel.Rationale)}");
            if (!string.IsNullOrWhiteSpace(regel.CommonMistake)) text.AppendLine($"Häufiger Fehler: {Einzeilig(regel.CommonMistake)}");

            var geltung = new List<string>();
            if (regel.Metrics.Count > 0) geltung.Add($"Messgrößen: {string.Join(", ", regel.Metrics)}");
            if (regel.Stages.Count > 0) geltung.Add($"Phasen: {string.Join(", ", regel.Stages)}");
            if (regel.ApplicableSetups.Count > 0) geltung.Add($"Systeme: {string.Join(", ", regel.ApplicableSetups)}");
            if (geltung.Count > 0) text.AppendLine($"Gilt für — {string.Join(" · ", geltung)}");

            Quellen(text, regel.Sources);
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Sollwerte je Phase und die Nährstoffprogramme.</summary>
    public static string Setpoints(
        IReadOnlyList<SetpointDefinition> setpoints,
        IReadOnlyList<NutrientProgramDefinition> programs)
    {
        var text = new StringBuilder();
        Kopf(text, "Sollwerte und Nährstoffprogramme",
            "Diese Werte sind die mitgelieferte Grundlage. Hat der Betreiber eigene Grenzwerte "
            + "eingetragen, gelten SEINE — der Lagebericht sagt in der Spalte „Ziel kommt von“, "
            + "welche Herkunft der jeweilige Wert hat.");

        foreach (var profil in setpoints.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {profil.Id} · {profil.Name}");
            text.AppendLine($"System: {profil.SystemType}");
            text.AppendLine();
            text.AppendLine("| Phase | pH | EC | ORP | Wasser Tag/Nacht | VPD | PPFD |");
            text.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var (phase, werte) in profil.Stages)
            {
                text.AppendLine(
                    $"| {phase} | {Spanne(werte.PhMin, werte.PhMax)} | {Spanne(werte.EcMin, werte.EcMax)} mS/cm "
                    + $"| {Spanne(werte.OrpMin, werte.OrpMax)} mV | {Zahl(werte.WaterTempDayC)}/{Zahl(werte.WaterTempNightC)} °C "
                    + $"| {Spanne(werte.VpdMin, werte.VpdMax)} kPa | {Spanne(werte.PpfdMin, werte.PpfdMax)} |");
            }
            text.AppendLine();
        }

        foreach (var programm in programs.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            text.AppendLine($"## {programm.Id} · {programm.Name}");
            text.AppendLine($"Hersteller: {programm.Manufacturer} · {programm.Category}");
            if (!string.IsNullOrWhiteSpace(programm.Summary)) text.AppendLine(Einzeilig(programm.Summary));
            if (!string.IsNullOrWhiteSpace(programm.BestFor)) text.AppendLine($"Passt zu: {Einzeilig(programm.BestFor)}");
            if (!string.IsNullOrWhiteSpace(programm.PhGuidance)) text.AppendLine($"pH: {Einzeilig(programm.PhGuidance)}");
            if (!string.IsNullOrWhiteSpace(programm.EcGuidance)) text.AppendLine($"EC: {Einzeilig(programm.EcGuidance)}");
            if (!string.IsNullOrWhiteSpace(programm.WaterGuidance)) text.AppendLine($"Wasser: {Einzeilig(programm.WaterGuidance)}");

            if (programm.Stages.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("| Phase | Dosis | Ziel | Hinweis |");
                text.AppendLine("|---|---|---|---|");
                foreach (var phase in programm.Stages)
                {
                    text.AppendLine($"| {phase.Stage} | {phase.Dose} | {phase.Target} | {Einzeilig(phase.Notes)} |");
                }
            }

            foreach (var tipp in programm.Tips) text.AppendLine($"- {Einzeilig(tipp)}");
            text.AppendLine();
        }

        return text.ToString();
    }

    private static void Kopf(StringBuilder text, string titel, string hinweis)
    {
        text.AppendLine($"# {titel}");
        text.AppendLine();
        text.AppendLine(hinweis);
        text.AppendLine();
    }

    private static void Quellen(StringBuilder text, IReadOnlyList<KnowledgeSource> sources)
    {
        if (sources.Count == 0) return;

        var namen = sources.Select(quelle =>
            string.IsNullOrWhiteSpace(quelle.Reference) ? quelle.Title : $"{quelle.Title} ({quelle.Reference})");
        text.AppendLine($"Quelle: {string.Join("; ", namen)}");
    }

    /// <summary>Ein Konflikt so, wie ihn ein Mensch lesen würde.</summary>
    private static string Konflikt(TreatmentConflict konflikt)
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(konflikt.With)) teile.Add($"nicht zusammen mit {konflikt.With}");
        if (konflikt.MinimumGapHours > 0) teile.Add($"mindestens {konflikt.MinimumGapHours} h Abstand");
        if (!string.IsNullOrWhiteSpace(konflikt.Reason)) teile.Add(konflikt.Reason);
        return teile.Count > 0 ? string.Join(" — ", teile) : "siehe Quelle";
    }

    /// <summary>Zeilenumbrüche zerlegen eine Tabellenzelle — also raus damit.</summary>
    private static string Einzeilig(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace('\r', ' ').Replace('\n', ' ').Replace("  ", " ").Trim();

    private static string Zahl(double wert) => wert.ToString("0.##", AppCulture.German);

    private static string Spanne(double min, double max)
        => Math.Abs(min - max) < 0.0001 ? Zahl(min) : $"{Zahl(min)}–{Zahl(max)}";
}
