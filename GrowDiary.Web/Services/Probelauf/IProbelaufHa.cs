using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Alles, was der Probelauf von Home Assistant braucht — an einer Naht,
/// damit der Eingriff ohne Anlage prüfbar ist.
/// </summary>
/// <remarks>
/// Entity-IDs kommen immer über die zentrale Zuordnung (Geräte &amp; Entitäten) bzw. über die Bauteile,
/// nie aus dem Probelauf-Code selbst.
/// </remarks>
public interface IProbelaufHa
{
    /// <summary>
    /// Die in Home Assistant vorhandenen Automationen dieses Moduls mit der gegebenen Probelauf-Rolle
    /// (handgebaute und vom Fork angelegte).
    /// </summary>
    Task<IReadOnlyList<string>> AutomationenAsync(string modul, ProbelaufRolle rolle, CancellationToken ct);

    /// <summary>Die Entität, die gerade eine Rolle erfüllt (zentral gepflegt); <c>null</c>, wenn nicht zugeordnet.</summary>
    string? Entity(string modul, string rolle);

    /// <summary>Der aktuelle Zustand einer Entität; <c>null</c>, wenn Home Assistant ihn nicht liefert.</summary>
    Task<string?> ZustandAsync(string entityId, CancellationToken ct);

    /// <summary>Eine Automation ein- oder ausschalten. <c>true</c> nur nach Nachkontrolle.</summary>
    Task<bool> AutomationSetzenAsync(string entityId, bool an, CancellationToken ct);

    /// <summary>Ein Gerät ausschalten, mit Nachkontrolle und Wiederholung. <c>true</c> nur, wenn es danach „aus" meldet.</summary>
    Task<bool> AusschaltenAsync(string entityId, CancellationToken ct);

    /// <summary>
    /// Ein Gerät in einen früheren Zustand bringen (<c>on</c>, <c>off</c> oder die Option eines <c>select</c>),
    /// mit Nachkontrolle. <c>true</c> nur, wenn es danach diesen Zustand meldet.
    /// </summary>
    Task<bool> ZustandHerstellenAsync(string entityId, string zustand, CancellationToken ct);
}
