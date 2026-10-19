using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>Eine Datei in der Berater-Mappe.</summary>
public sealed record AgentPackageFile(string Name, string Markdown);

/// <summary>Die fertige Mappe samt Grow-Name für den Dateinamen.</summary>
public sealed record AgentPackage(string GrowName, IReadOnlyList<AgentPackageFile> Files);

/// <summary>
/// Die Berater-Mappe: Anweisung, Lage, Wissen und Selbsttest in einem Paket.
/// </summary>
/// <remarks>
/// <para>Die Idee dahinter: In Cultivate Control steckt keine KI und soll auch keine
/// stecken. Aber das Fachwissen liegt hier — Abläufe, Behandlungen, Symptome,
/// Regeln, Sollwerte. Wer einen Assistenten fragen will, soll ihm nicht nur
/// seine Messwerte vorlegen, sondern auch das Material, an dem sie zu messen
/// sind. Ein Modell mit Forenwissen wird dadurch zu einem Berater, der die
/// Quelle nennen kann.</para>
///
/// <para>Die Nummern im Dateinamen sind kein Schmuck: die meisten Werkzeuge
/// hängen Dateien in Namensreihenfolge an, und die Anweisung soll vor dem
/// Wissen kommen.</para>
/// </remarks>
public sealed class AgentPackageBuilder
{
    private readonly AgentContextBuilder _context;
    private readonly KnowledgeBaseLoader _knowledge;

    public AgentPackageBuilder(AgentContextBuilder context, KnowledgeBaseLoader knowledge)
    {
        _context = context;
        _knowledge = knowledge;
    }

    /// <summary>
    /// Die ganze Mappe für einen Grow, oder null, wenn es den Grow nicht gibt.
    /// </summary>
    public AgentPackage? Build(int growId, DateTime nowUtc)
    {
        if (_context.Build(growId, nowUtc) is not { } lage) return null;

        return new AgentPackage(lage.GrowName,
        [
            new("LIESMICH.md", AgentPromptTexts.Liesmich),
            new("00-anweisung.md", AgentPromptTexts.Systemanweisung),
            new("10-lagebericht.md", AgentContextBuilder.ToMarkdown(lage)),
            new("20-wissen-ablaeufe.md", AgentKnowledgeRenderer.Sops(_knowledge.Sops)),
            new("21-wissen-behandlungen.md", AgentKnowledgeRenderer.Treatments(_knowledge.Treatments)),
            new("22-wissen-symptome.md", AgentKnowledgeRenderer.SymptomsAndPathogens(_knowledge.Symptoms, _knowledge.Pathogens)),
            new("23-wissen-regeln.md", AgentKnowledgeRenderer.Guidance(_knowledge.Guidance)),
            new("24-wissen-sollwerte.md", AgentKnowledgeRenderer.Setpoints(_knowledge.Setpoints, _knowledge.NutrientPrograms)),
            new("90-pruefragen.md", AgentPromptTexts.Pruefragen),
        ]);
    }
}
