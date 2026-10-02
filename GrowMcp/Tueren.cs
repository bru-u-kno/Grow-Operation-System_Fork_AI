using System.Net;

namespace GrowMcp;

/// <summary>Was mit einer Anfrage geschehen soll.</summary>
public enum Zutritt
{
    /// <summary>Durchlassen.</summary>
    Erlaubt,

    /// <summary>Diesen Weg gibt es an dieser Tür nicht — 404.</summary>
    NichtGefunden,

    /// <summary>Richtige Tür, falscher oder fehlender Schlüssel — 401.</summary>
    SchluesselFehlt,

    /// <summary>Richtige Tür, aber nicht vom Ingress-Proxy und nicht vom eigenen Rechner — 403.</summary>
    Verboten,
}

/// <summary>
/// Welcher Port was darf.
/// </summary>
/// <remarks>
/// <para>An einer Stelle, weil die Trennung die eigentliche Absicherung ist: die
/// Seite mit dem Schlüssel hängt am Ingress-Port, die Schnittstelle am Netz-Port.
/// Wer hier etwas ändert, ändert, wer was sehen darf — deshalb ist die
/// Entscheidung eine eigene Funktion mit eigenen Tests und keine Verzweigung
/// mitten in der Middleware.</para>
/// </remarks>
public static class Tueren
{
    /// <summary>Home Assistant reicht die Einrichtungsseite hierüber durch.</summary>
    public const int IngressPort = 5078;

    /// <summary>Der einzige Port, der ins Heimnetz veröffentlicht wird.</summary>
    public const int NetzPort = 5079;

    /// <summary>Wo die MCP-Schnittstelle liegt.</summary>
    public const string McpPfad = "/mcp";

    /// <summary>
    /// Die Adresse des Ingress-Proxys im Supervisor.
    /// </summary>
    /// <remarks>
    /// <para>Quelle: Home-Assistant-Entwicklerdoku „Presenting your app → Ingress":
    /// ein Add-on soll Ingress-Anfragen nur von 172.30.32.2 annehmen. Dieselbe
    /// Adresse prüft Grow OS in <c>AdminAccessPolicy</c>.</para>
    ///
    /// <para><b>Warum (Sicherheitsprüfung 02.10.2026).</b> Kestrel lauscht auf allen
    /// Adressen, auch am Ingress-Port. Nach draussen ist der zwar zu — im internen
    /// Add-on-Netz (172.30.32.0/23) aber nicht. Jedes andere Add-on konnte
    /// <c>http://&lt;grow_mcp&gt;:5078/</c> abrufen und den Schlüssel von der
    /// Einrichtungsseite ablesen. Nur IPv4: eine IPv6 im Add-on-Netz vergibt
    /// Docker frei, sie könnte einem anderen Container gehören.</para>
    /// </remarks>
    public static readonly IPAddress IngressProxy = IPAddress.Parse("172.30.32.2");

    /// <summary>Darf diese Anfrage weiter?</summary>
    /// <param name="port">Der Port, auf dem sie hereinkam.</param>
    /// <param name="pfad">Der angefragte Pfad.</param>
    /// <param name="schluesselStimmt">Hat der Aufrufer den richtigen Schlüssel mitgeschickt?</param>
    /// <param name="absender">Die Adresse, von der die Anfrage kommt.</param>
    public static Zutritt Pruefen(int port, string pfad, bool schluesselStimmt, IPAddress? absender)
    {
        var amNetz = port == NetzPort;
        var zurSchnittstelle = pfad.StartsWith(McpPfad, StringComparison.OrdinalIgnoreCase)
            && (pfad.Length == McpPfad.Length || pfad[McpPfad.Length] == '/');

        // Jede Tür hat genau eine Aufgabe. Am Netz-Port gibt es nur die
        // Schnittstelle — sonst könnte jeder im WLAN die Seite mit dem Schlüssel
        // aufrufen und sich den Schlüssel einfach abholen. Am Ingress-Port gibt es
        // nur die Seite.
        if (amNetz != zurSchnittstelle) return Zutritt.NichtGefunden;

        // Die Schnittstelle schützt der Schlüssel, nicht die Herkunft: Klienten
        // kommen aus dem Heimnetz, von beliebigen Adressen.
        if (zurSchnittstelle) return schluesselStimmt ? Zutritt.Erlaubt : Zutritt.SchluesselFehlt;

        // Die Seite mit dem Schlüssel: nur über Home Assistant (Ingress-Proxy)
        // oder vom eigenen Rechner.
        return VomIngressOderLokal(absender) ? Zutritt.Erlaubt : Zutritt.Verboten;
    }

    /// <summary>Kommt die Anfrage vom Ingress-Proxy oder vom eigenen Rechner?</summary>
    public static bool VomIngressOderLokal(IPAddress? absender)
    {
        if (absender is null) return false;
        if (absender.IsIPv4MappedToIPv6) absender = absender.MapToIPv4();
        return IPAddress.IsLoopback(absender) || absender.Equals(IngressProxy);
    }
}
