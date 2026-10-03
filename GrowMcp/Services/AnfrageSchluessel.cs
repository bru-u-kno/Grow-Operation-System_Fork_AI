using GrowOsAccess;

namespace GrowMcp.Services;

/// <summary>
/// Der Fork-Schlüssel aus der laufenden MCP-Anfrage.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Der Connector schickt bei jeder Anfrage
/// <c>Authorization: Bearer …</c>. Steht dort ein Schlüssel aus Grow OS
/// (<c>gok_…</c>), reicht ihn <see cref="GrowOsReader"/> an Grow OS weiter.
/// Steht dort der MCP-Schlüssel dieses Add-ons, kommt hier <c>null</c> heraus:
/// der MCP-Schlüssel öffnet nur die Tür dieses Add-ons und verlässt es nie.</para>
///
/// <para><b>Warum das je Anfrage stimmt.</b> Der Server läuft zustandslos
/// (<c>Stateless = true</c> in <see cref="Aufbau"/>): jeder HTTP-Aufruf bekommt
/// einen frischen MCP-Server, und die Werkzeuge laufen im
/// <c>ExecutionContext</c> genau dieser HTTP-Anfrage
/// (<c>PerSessionExecutionContext</c> bleibt auf seiner Vorgabe <c>false</c>).
/// Damit liefert <see cref="IHttpContextAccessor"/> im Werkzeug die Anfrage,
/// die das Werkzeug ausgelöst hat — nicht eine frühere eines anderen
/// Klienten. Belegt im Durchlauf durch den echten MCP-Weg
/// (<c>DurchreichenTests</c>).</para>
///
/// <para>Nichts wird festgehalten: kein Feld, kein Protokoll. Gelesen wird bei
/// jedem Zugriff aus dem Kopf der Anfrage.</para>
/// </remarks>
public sealed class AnfrageSchluessel(IHttpContextAccessor zugriff) : IForkSchluesselQuelle
{
    public string? Schluessel
    {
        get
        {
            var kopf = zugriff.HttpContext?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(kopf) || !kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

            var wert = kopf["Bearer ".Length..].Trim();
            return ForkSchluessel.HatForm(wert) ? wert : null;
        }
    }
}
