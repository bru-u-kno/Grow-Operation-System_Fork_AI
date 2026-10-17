using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Der Basispfad, unter dem Home Assistant die App per Ingress ausliefert.
/// </summary>
/// <remarks>
/// <para><b>Warum ein Muster (Sicherheitsprüfung 02.10.2026).</b> Der Wert kommt
/// aus dem Kopf <c>X-Ingress-Path</c>, und den kann jeder setzen, der die App
/// erreicht. Für den Zugriff zählt er seit dem 01.10.2026 nur vom Ingress-Proxy
/// (<see cref="AdminAccessPolicy.IsIngressRequest"/>). PathBase und das
/// <c>&lt;base href&gt;</c> der Startseite nahmen ihn aber ungeprüft — ein Wert
/// mit <c>"&gt;&lt;script&gt;</c> stand dadurch als Skript im Kopf der Seite.</para>
///
/// <para>Home Assistant baut den Pfad immer als
/// <c>/api/hassio_ingress/&lt;token&gt;</c>; das Token ist URL-sicheres Base64.
/// Alles andere wird verworfen, die App läuft dann an der Wurzel.</para>
/// </remarks>
public static partial class IngressPfad
{
    [GeneratedRegex(@"^/api/hassio_ingress/[A-Za-z0-9_-]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex Muster();

    /// <summary>
    /// Den Kopfwert prüfen. Liefert den Basispfad ohne Schrägstrich am Ende oder
    /// <c>null</c>, wenn der Wert nicht wie ein Ingress-Pfad aussieht.
    /// </summary>
    public static string? Pruefen(string? kopfwert)
    {
        if (string.IsNullOrEmpty(kopfwert)) return null;

        var wert = kopfwert.TrimEnd('/');
        return Muster().IsMatch(wert) ? wert : null;
    }

    /// <summary>
    /// Der Pfad einer Weiterleitung INNERHALB der App — mit dem Ingress-Präfix, wenn die Anfrage eines hat.
    /// </summary>
    /// <remarks>
    /// <c>Redirect("/zelte")</c> schreibt den Pfad unverändert in <c>Location</c>. Im Add-on liegt die App aber unter
    /// <c>/api/hassio_ingress/&lt;token&gt;</c>; ein absoluter Pfad ohne dieses Präfix führt dort an der App vorbei
    /// (Neuling-Test A-017, Lücke 7). Das Präfix steht schon im <c>PathBase</c> der Anfrage (Program.cs setzt es nur für
    /// einen Wert, der <see cref="Pruefen"/> besteht) — hier wird es nur vorangestellt, nichts Neues geführt.
    /// Ohne Präfix (Aufruf an der Wurzel, kein Add-on) bleibt der Pfad, wie er ist.
    /// </remarks>
    /// <param name="request">Die laufende Anfrage; <c>null</c> (z. B. in einem Test ohne Kontext) heißt: kein Präfix.</param>
    /// <param name="pfad">Der Pfad ab der Wurzel der App, mit führendem Schrägstrich, ohne Abfrage.</param>
    public static string Weiterleitung(HttpRequest? request, string pfad)
    {
        var basis = request?.PathBase.Value;
        return string.IsNullOrEmpty(basis) ? pfad : basis.TrimEnd('/') + pfad;
    }

    /// <summary>Das <c>&lt;base&gt;</c>-Element für die Startseite — immer HTML-kodiert.</summary>
    /// <remarks>
    /// Doppelt gesichert: der Pfad hat das Muster schon passiert, kodiert wird
    /// trotzdem. Wer das Muster später lockert, öffnet damit kein Loch im HTML.
    /// </remarks>
    public static string BaseTag(PathString pathBase)
    {
        var href = pathBase.HasValue ? pathBase.Value + "/" : "/";
        return $"<base href=\"{WebUtility.HtmlEncode(href)}\" />";
    }
}
