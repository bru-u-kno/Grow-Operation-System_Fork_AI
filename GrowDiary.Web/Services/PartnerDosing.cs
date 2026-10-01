using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Zweikomponenten-Dünger: A und B im Verhältnis, aber nie gleichzeitig.
/// </summary>
/// <remarks>
/// <para>Warum getrennt: konzentriert fällt das Calcium aus Komponente A mit den
/// Sulfaten und Phosphaten aus B als Gips aus. Was ausgeflockt ist, kommt bei
/// der Pflanze nie an — im Becken schwimmen weisse Flocken, der EC steigt trotz
/// Dünger kaum, und der naheliegende Schluss („zu wenig gegeben") führt dazu,
/// dass noch mehr nachgekippt wird.</para>
///
/// <para>Deshalb: A läuft, die Trennzeit vergeht, dann B. Verdünnt im ganzen
/// Beckenvolumen ist die Begegnung harmlos — genau darum geht es bei der Zeit
/// dazwischen.</para>
/// </remarks>
public static class PartnerDosing
{
    /// <summary>Kleinste sinnvolle Trennzeit in Minuten.</summary>
    /// <remarks>
    /// Unter einer Minute ist es keine Trennung mehr, sondern zwei Pumpen, die
    /// praktisch zusammen laufen — der Fall, den das hier verhindern soll.
    /// </remarks>
    public const int MinDelayMinutes = 1;

    /// <summary>Was der Partner für diese Menge bekommt.</summary>
    /// <remarks>
    /// Null, wenn kein Partner eingerichtet ist oder das Verhältnis unbrauchbar
    /// ist. Lieber gar keine zweite Dosis als eine geratene: bei A ohne B fehlt
    /// ein Nährstoff, bei falschem Verhältnis stimmt das ganze Profil nicht.
    /// </remarks>
    public static double? PartnerMl(DosingPump pump, double dosedMl)
    {
        if (pump.PartnerPumpId is null or <= 0) return null;
        if (pump.PartnerRatio <= 0) return null;
        if (dosedMl <= 0) return null;

        return Math.Round(dosedMl * pump.PartnerRatio, 2);
    }

    /// <summary>Wann der Partner frühestens laufen darf.</summary>
    public static DateTime PartnerDueAt(DosingPump pump, DateTime dosedAtUtc)
        => dosedAtUtc.AddMinutes(Math.Max(pump.PartnerDelayMinutes, MinDelayMinutes));

    /// <summary>
    /// Darf diese Pumpe jetzt laufen, oder wartet noch ein Partner auf sie?
    /// </summary>
    /// <remarks>
    /// Der Riegel gegen die eigentliche Gefahr: solange für <b>eine der beiden</b>
    /// Pumpen des Paares noch etwas aussteht, darf keine von beiden erneut
    /// starten. Sonst gäbe eine zweite Anforderung A ein zweites Mal, während
    /// das erste B noch wartet — und irgendwann treffen sich zwei frische Dosen
    /// doch.
    /// </remarks>
    public static bool IsBlockedByPending(IReadOnlyList<PendingDose> pendingForPair)
        => pendingForPair.Count > 0;

    /// <summary>
    /// So oft wird eine zweite Hälfte an einem nicht erreichbaren Home
    /// Assistant versucht, bevor sie verworfen wird.
    /// </summary>
    /// <remarks>
    /// Der Dosiertakt läuft jede Minute, also sind das rund eine halbe Stunde —
    /// genug für einen Neustart von Home Assistant samt Host (der Auswurf beim
    /// Start rechnet mit fünf Minuten). Länger nicht: solange B aussteht,
    /// dosiert im ganzen Becken niemand, auch die pH-Pumpe nicht. Gilt nur für
    /// <c>Pumpenlauf.NichtGesendet</c>, wo nachweislich nichts geflossen ist.
    /// </remarks>
    public const int MaxFehlversuche = 30;

    /// <summary>Was eine eingeplante zweite Hälfte ausmacht — für die Meldung an den Nutzer.</summary>
    public sealed record EingeplanteHaelfte(DosingPump Partner, double Ml, int Minuten);

    /// <summary>
    /// Die zweite Hälfte nach einer gelaufenen Dosis einplanen.
    /// </summary>
    /// <remarks>
    /// <para><b>Eine Stelle für beide Wege (01.10.2026).</b> Das stand nur im
    /// Hand-Dosieren des Controllers. Die Automatik gab A und plante B nie ein —
    /// eine Pumpe mit Partner darf aber zugleich Automatik haben, und dann stand
    /// im Becken A ohne B, Takt für Takt.</para>
    ///
    /// <para>Nicht von der Ausführung der zweiten Hälfte aufrufen: B ist die
    /// Vollendung von A, keine neue Dosis — sonst gäbe ein beidseitig
    /// eingerichtetes Paar A, B, A, B … ohne Ende.</para>
    /// </remarks>
    /// <returns>Die eingeplante Hälfte, oder null wenn es keinen brauchbaren Partner gibt.</returns>
    public static EingeplanteHaelfte? Einplanen(
        DosingRepository dosing, DosingPump pump, double gegebenMl, DateTime nowUtc, int? quellEreignisId = null)
    {
        if (PartnerMl(pump, gegebenMl) is not { } partnerMl) return null;

        var partner = dosing.GetPump(pump.PartnerPumpId!.Value);
        if (partner is null) return null;

        dosing.InsertPending(new PendingDose
        {
            PumpId = partner.Id,
            Ml = partnerMl,
            DueAtUtc = PartnerDueAt(pump, nowUtc),
            SourceDoseEventId = quellEreignisId,
            Reason = $"Zweite Hälfte zu {gegebenMl:0.##} ml aus {pump.Name}.",
            CreatedAtUtc = nowUtc,
        });

        return new EingeplanteHaelfte(partner, partnerMl, Math.Max(pump.PartnerDelayMinutes, MinDelayMinutes));
    }

    /// <summary>
    /// Kann diese Pumpe, so wie sie eingerichtet ist, überhaupt je laufen?
    /// </summary>
    /// <returns>Der Grund, warum nicht — oder null.</returns>
    /// <remarks>
    /// Für die zweite Hälfte heisst ein Treffer: sie wird auch im nächsten Takt
    /// nicht laufen. Warten hilft nicht, sie wird verworfen und protokolliert.
    /// Alles, was sich von selbst löst (Tagesgrenze, stehende Umwälzung), steht
    /// bewusst NICHT hier.
    /// </remarks>
    public static string? Unbrauchbar(DosingPump pumpe)
    {
        if (pumpe.MlPerMinute is not > 0)
        {
            return $"{pumpe.Name} ist nicht kalibriert — ohne Fördermenge sind Milliliter keine Laufzeit.";
        }

        if (!pumpe.SimulationMode && string.IsNullOrWhiteSpace(pumpe.HaEntityId))
        {
            return $"{pumpe.Name} hat keine Home-Assistant-Entität.";
        }

        return null;
    }

    /// <summary>
    /// Darf die Automatik A geben — gibt es also ein B, das danach auch läuft?
    /// </summary>
    /// <returns>Der Grund, warum nicht — oder null.</returns>
    /// <remarks>
    /// <para>Unbeaufsichtigt ist A ohne B schlimmer als gar nichts: das
    /// Verhältnis kippt bei jedem Takt ein Stück weiter, und niemand steht
    /// daneben. Ist B nicht lauffähig, gibt die Automatik deshalb auch A nicht.
    /// Von Hand bleibt es erlaubt — wer selbst drückt, sieht die Meldung.</para>
    ///
    /// <para><b>Gleicher Betrieb.</b> A echt und B im Testbetrieb hiesse: B wird
    /// „gegeben", es fliesst aber nichts — still dasselbe wie A ohne B. Umgekehrt
    /// flösse B ohne A.</para>
    /// </remarks>
    public static string? AutomatikSperre(DosingPump pump, DosingPump? partner)
    {
        if (pump.PartnerPumpId is null) return null;

        if (Validate(pump, partner) is { } fehler) return fehler;
        if (Unbrauchbar(partner!) is { } grund) return "Partnerpumpe: " + grund;

        if (pump.SimulationMode != partner!.SimulationMode)
        {
            return "A und B laufen nicht im selben Betrieb (Testbetrieb / echt) — eine Hälfte flösse, die andere nicht.";
        }

        return null;
    }

    /// <summary>
    /// Prüft die Einrichtung eines Paares.
    /// </summary>
    /// <returns>Ein Klartext-Fehler, oder null wenn es passt.</returns>
    public static string? Validate(DosingPump pump, DosingPump? partner)
    {
        if (pump.PartnerPumpId is null) return null;

        if (partner is null)
        {
            return "Die Partnerpumpe existiert nicht.";
        }

        if (partner.Id == pump.Id)
        {
            return "Eine Pumpe kann nicht ihr eigener Partner sein.";
        }

        if (partner.TentId != pump.TentId)
        {
            // Zwei Becken, ein Paar — dann liefe B in ein anderes Reservoir als A.
            return "Partnerpumpen müssen im selben Zelt sein.";
        }

        if (pump.PartnerRatio <= 0)
        {
            return "Das Verhältnis muss über null liegen.";
        }

        if (pump.PartnerDelayMinutes < MinDelayMinutes)
        {
            return $"Die Trennzeit muss mindestens {MinDelayMinutes} Minute betragen — sonst treffen sich A und B konzentriert.";
        }

        return null;
    }
}
