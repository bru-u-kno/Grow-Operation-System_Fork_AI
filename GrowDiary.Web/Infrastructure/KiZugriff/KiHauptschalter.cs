using GrowDiary.Web.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-011, 06.10.2026): Der globale Hauptschalter „KI-Funktionen".
/// </summary>
/// <remarks>
/// <para><b>Wozu.</b> Manche Anwender des Fork wollen keine KI. Der Schalter gilt
/// für den ganzen Fork: aus heisst, dass keine KI-Seite, keine KI-Karte und kein
/// Zugriff von aussen per Schlüssel übrig bleibt. Er steht über dem
/// Zugriff für KI-Assistenten (<see cref="KiZugriffDienst.EinstellungAktiv"/>) und
/// über jeder Schlüsselstufe; deren Einstellungen bleiben dabei gespeichert.</para>
///
/// <para><b>Voreinstellung.</b> Ist nichts gespeichert, gilt: aus — ausser der
/// Zugriff für KI-Assistenten ist schon an. Dann war die KI in dieser
/// Installation schon in Benutzung, und nach dem Update soll nichts verschwinden.
/// Die Vorentscheidung wird bewusst <b>nicht</b> gespeichert: wer den Zugriff
/// später einschaltet, bekäme sonst eine eingefrorene Vorentscheidung.
/// „KI an" schaltet den Zugriff von aussen nie von selbst ein.</para>
/// </remarks>
public sealed class KiHauptschalter
{
    /// <summary>Schlüssel in AppSettings.</summary>
    public const string Einstellung = "ki.aktiv";

    private readonly AppSettingsRepository _einstellungen;

    public KiHauptschalter(AppSettingsRepository einstellungen) => _einstellungen = einstellungen;

    public bool Aktiv
    {
        get
        {
            if (_einstellungen.GetValue(Einstellung) is { } gespeichert)
                return string.Equals(gespeichert, "true", StringComparison.OrdinalIgnoreCase);

            return string.Equals(_einstellungen.GetValue(KiZugriffDienst.EinstellungAktiv), "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Setzen(bool aktiv) => _einstellungen.SetValue(Einstellung, aktiv ? "true" : "false");

    /// <summary>Fehlercode, wenn etwas bei ausgeschalteter KI aufgerufen wird.</summary>
    public const string FehlerCode = "ki_aus";

    public const string FehlerText = "Die KI-Funktionen sind ausgeschaltet (Einstellungen → KI-Funktionen).";
}

/// <summary>
/// Fork AI (A-011): Dieser Controller gehört zu den KI-Funktionen — bei „KI aus"
/// antwortet er mit 404 <c>ki_aus</c>.
/// </summary>
/// <remarks>
/// Am Controller, nicht an jeder Aktion: eine neue Aktion darin ist damit schon
/// gesperrt. Gezählt wird das in <c>KiHauptschalterTests</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NurMitKiAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var schalter = context.HttpContext.RequestServices.GetRequiredService<KiHauptschalter>();
        if (schalter.Aktiv) return;

        context.Result = new NotFoundObjectResult(
            ApiErrorFactory.NotFound(KiHauptschalter.FehlerCode, KiHauptschalter.FehlerText, context.HttpContext.TraceIdentifier));
    }
}
