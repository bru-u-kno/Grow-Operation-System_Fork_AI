using System.Net;
using System.Net.Sockets;

namespace GrowMcp.Services;

/// <summary>
/// Taugt dieser Hostname als Adresse für die MCP-Tür im Heimnetz?
/// </summary>
/// <remarks>
/// <para><b>Der Fehler, den das verhindert.</b> Die Einrichtungsseite nahm den
/// Namen aus der laufenden Anfrage — die Annahme: „unter dem Namen, unter dem
/// der Betreiber Home Assistant gerade offen hat, erreicht er es auch vom
/// selben Rechner". Für die Seite selbst stimmt das, denn die läuft über
/// Ingress. Für die MCP-Tür stimmt es nicht: die hängt an Tueren.NetzPort (5080), und der
/// ist absichtlich <i>nur</i> im Heimnetz offen.</para>
///
/// <para>Wer Home Assistant über eine eigene Domain aufruft
/// (<c>smarthome.example.org</c>, Nabu Casa, ein Reverse Proxy), bekam deshalb
/// einen Befehl, der nicht funktionieren konnte — und auf derselben Seite stand
/// darunter „Erreichbar ist das nur in deinem eigenen Netz". Zwei Sätze, die
/// sich widersprechen, und der Betreiber sucht den Fehler bei sich.</para>
///
/// <para>Geprüft wird deshalb, ob der Name überhaupt ins Heimnetz zeigt. Alles
/// andere ist keine brauchbare Adresse für diese Tür.</para>
/// </remarks>
public static class Heimnetzadresse
{
    /// <summary>
    /// Zeigt dieser Name ins eigene Netz?
    /// </summary>
    /// <remarks>
    /// Erlaubt sind private IPv4-Bereiche (10.x, 172.16–31.x, 192.168.x), das
    /// Link-Local-Netz, IPv6-Adressen aus dem eigenen Netz, <c>localhost</c>,
    /// sowie Namen, die auf <c>.local</c>, <c>.lan</c>, <c>.home</c>,
    /// <c>.internal</c> oder <c>.fritz.box</c> enden — die üblichen Namen, die
    /// ein Heimrouter vergibt.
    /// </remarks>
    public static bool IstLokal(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var name = host.Trim().Trim('[', ']');
        if (string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

        if (IPAddress.TryParse(name, out var adresse))
        {
            if (IPAddress.IsLoopback(adresse)) return true;

            if (adresse.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = adresse.GetAddressBytes();
                return b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254);
            }

            if (adresse.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // fe80:: (Link-Local) und fc00::/7 (Unique Local) sind das
                // Gegenstueck zu 192.168.x im IPv6-Netz.
                return adresse.IsIPv6LinkLocal
                    || adresse.IsIPv6SiteLocal
                    || (adresse.GetAddressBytes()[0] & 0xFE) == 0xFC;
            }

            return false;
        }

        // Ein Name ohne Punkt ist ein reiner Rechnername im eigenen Netz.
        if (!name.Contains('.')) return true;

        string[] heimEndungen = [".local", ".lan", ".home", ".internal", ".home.arpa", ".fritz.box", ".speedport.ip"];
        return heimEndungen.Any(endung => name.EndsWith(endung, StringComparison.OrdinalIgnoreCase));
    }
}
