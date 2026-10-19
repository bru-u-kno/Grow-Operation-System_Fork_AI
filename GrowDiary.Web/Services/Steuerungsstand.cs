using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Eine Voraussetzung und ob sie erfüllt ist.</summary>
/// <param name="Titel">Kurz, drei bis fünf Wörter — die Zeile in der Liste.</param>
/// <param name="Erfuellt">Grün oder nicht.</param>
/// <param name="Text">Was es bedeutet; bei „nicht erfüllt" <b>was zu tun ist</b>.</param>
/// <param name="Schluessel">
/// Die stabile Kennung des Glieds (<see cref="KettenSchluessel"/>). Die
/// Oberfläche hängt daran den Knopf, der zur richtigen Stelle führt —
/// „Der Schalter unten steht auf aus" SAGT nur, der Knopf FÜHRT hin. Genau
/// das fehlte dem Nutzer, der die Steuerung nicht angeschaltet bekam.
/// </param>
/// <param name="Fehlt">
/// Wie dieses Glied in einem Satz genannt wird, wenn es fehlt — als Ergänzung
/// zu „Es fehlt: …".
///
/// <para><b>Warum ein eigenes Feld und nicht der Titel.</b> Der Titel ist eine
/// Zeile in einer Liste („Zielgerät zugeordnet"), kein Satzteil. Die Kurzfassung
/// hat ihn bis zum 24.08.2026 mit <c>ToLowerInvariant()</c> in den Satz
/// gezwungen — heraus kam „Nicht aktiv. Es fehlt: zielgerät zugeordnet.", also
/// ein kleingeschriebenes deutsches Hauptwort mitten im Satz. Dieselbe Sorte
/// Fehler wie der englische Dezimalpunkt in beta.50: englische Gewohnheit auf
/// deutschen Text angewandt.</para>
/// </param>
public sealed record Voraussetzung(
    string Titel, bool Erfuellt, string Text, string Fehlt, string Schluessel = "");

/// <summary>
/// Steuert Cultivate Control gerade wirklich — und wenn nicht, woran liegt es?
/// </summary>
/// <param name="RampeSchreibt">Ist alles eingerichtet, damit der Sollwert geschrieben wird?</param>
/// <param name="KuehlerSchaltet">
/// Ist alles eingerichtet, damit der Regler die Steckdose schalten darf?
/// <b>Nicht</b> „schaltet gerade": der Regler prüft jede Minute und entscheidet
/// meistens, nichts zu tun — das ist sein Normalfall und kein Stillstand.
/// </param>
/// <param name="Kurzfassung">Der eine Satz für ganz oben.</param>
public sealed record Steuerungsstand(
    bool RampeSchreibt,
    bool KuehlerSchaltet,
    string Kurzfassung,
    IReadOnlyList<Voraussetzung> Rampe,
    IReadOnlyList<Voraussetzung> Kuehler,
    DateTime? LetzterSollwertUtc,
    DateTime? LetzteSchaltungUtc);

/// <summary>
/// Baut den Steuerungsstand — rein, ohne Datenbank und ohne Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Rückmeldung des Testers zu Crop Steering: „dort steht
/// nicht, wann es aktiv ist." Die Seite zeigte den Plan, die Tabelle und die
/// Einstellungen — aber nirgends, <i>ob</i> gerade etwas passiert. Im Code hängt
/// das an einer ganzen Kette: Schalter an, Zielgerät gesetzt, Home Assistant
/// verbunden, Blütewoche vorhanden. Fehlt ein Glied, passiert nichts, und die
/// Seite sah aus wie vorher.</para>
///
/// <para><b>Warum eine Liste und keine Ampel.</b> Eine rote Ampel sagt „geht
/// nicht" und lässt den Nutzer suchen. Die Kette zeigt, <b>welches</b> Glied
/// fehlt und was zu tun ist. Genau das war die Beschwerde.</para>
///
/// <para><b>Die Gründe werden nicht neu erfunden.</b> Was der Rampe fehlt, sagt
/// <see cref="Absenkplan.Luecke"/> — dieselbe Zeichenkette, die
/// <see cref="NachtabsenkungService"/> beim Rechnen erzeugt. Was den Kühler
/// zurückhält, sagt <see cref="KuehlerUrteil.Grund"/> aus
/// <see cref="KuehlerService.Entscheiden"/>. Eine zweite Fassung dieser Sätze
/// würde von der ersten abdriften.</para>
/// </remarks>
public static class SteuerungsstandBauer
{
    /// <param name="zelt">Das Zelt des Grows; <c>null</c>, wenn keines zugeordnet ist.</param>
    /// <param name="haVerbunden">Ist Home Assistant überhaupt eingerichtet?</param>
    public static Steuerungsstand Bauen(
        GrowRun grow,
        Tent? zelt,
        Absenkplan plan,
        bool haVerbunden,
        bool testbetrieb,
        DateTime? letzterSollwertUtc,
        DateTime? letzteSchaltungUtc)
    {
        var rampe = RampeSchritte(grow, zelt, plan, haVerbunden, testbetrieb);
        var kuehler = KuehlerSchritte(zelt, haVerbunden, plan, testbetrieb);

        var rampeLaeuft = rampe.All(s => s.Erfuellt);
        var kuehlerLaeuft = kuehler.All(s => s.Erfuellt);

        return new Steuerungsstand(
            rampeLaeuft,
            kuehlerLaeuft,
            Kurzfassung(rampeLaeuft, kuehlerLaeuft, rampe, kuehler),
            rampe,
            kuehler,
            letzterSollwertUtc,
            letzteSchaltungUtc);
    }

    private static List<Voraussetzung> RampeSchritte(
        GrowRun grow, Tent? zelt, Absenkplan plan, bool haVerbunden, bool testbetrieb)
    {
        var liste = new List<Voraussetzung>
        {
            new("Absenkung eingeschaltet", grow.NightRampEnabled,
                grow.NightRampEnabled
                    ? "Der Schalter unten steht auf an."
                    : "Der Schalter unten steht auf aus — dann ist die Tabelle nur eine Vorschau.",
                "die eingeschaltete Absenkung",
                KettenSchluessel.Absenkung),

            // Der Plan sagt selbst, was ihm fehlt. Diesen Satz nicht nachbauen.
            new("Ein Plan für heute", plan.Luecke is null && plan.Wochen.Count > 0,
                plan.Luecke ?? (plan.Wochen.Count > 0
                    ? $"Blütewoche {plan.AktuelleWoche}, Nachtwert {Zahl(plan.HeuteNachtC)} °C."
                    : "Es gibt keine Wochen im Plan."),
                "ein Plan für heute",
                plan.LueckeSchluessel ?? KettenSchluessel.PlanSteht),

            new("Zielgerät zugeordnet", !string.IsNullOrWhiteSpace(zelt?.WaterTargetEntityId),
                string.IsNullOrWhiteSpace(zelt?.WaterTargetEntityId)
                    ? "Ohne Zielgerät wird nur geplant. Trag unten ein climate- oder number-Gerät ein."
                    : $"Der Sollwert geht an {zelt!.WaterTargetEntityId}.",
                "ein zugeordnetes Zielgerät",
                KettenSchluessel.Zielgeraet),

            Verbindung(haVerbunden, testbetrieb, "geschrieben"),
        };

        return liste;
    }

    private static List<Voraussetzung> KuehlerSchritte(
        Tent? zelt, bool haVerbunden, Absenkplan plan, bool testbetrieb)
    {
        var an = zelt?.ChillerControlEnabled == true;
        var steckdose = !string.IsNullOrWhiteSpace(zelt?.ChillerSwitchEntityId);

        return new List<Voraussetzung>
        {
            new("Kühler-Steuerung eingeschaltet", an,
                an
                    ? "Cultivate Control darf die Steckdose schalten."
                    : "Aus. Standard — etwas, das einen Kompressor taktet, schaltet sich nicht selbst ein.",
                "die eingeschaltete Kühler-Steuerung",
                KettenSchluessel.KuehlerSteuerung),

            new("Steckdose zugeordnet", steckdose,
                steckdose
                    ? $"Geschaltet wird {zelt!.ChillerSwitchEntityId}."
                    : "Ohne Steckdose gibt es nichts zu schalten.",
                "eine zugeordnete Steckdose",
                KettenSchluessel.Steckdose),

            new("Ein Sollwert für jetzt", plan.HeuteTagC is not null || plan.HeuteNachtC is not null,
                plan.HeuteTagC is not null || plan.HeuteNachtC is not null
                    ? $"Tag {Zahl(plan.HeuteTagC)} °C, Nacht {Zahl(plan.HeuteNachtC)} °C."
                    : "Ohne Sollwert regelt der Kühler nicht — er bleibt, wie er ist.",
                "ein Sollwert für jetzt",
                plan.LueckeSchluessel ?? KettenSchluessel.PlanSteht),

            Verbindung(haVerbunden, testbetrieb, "geschaltet"),
        };
    }

    /// <summary>Die Verbindung — mit dem Testbetrieb als eigenem Fall.</summary>
    /// <remarks>
    /// <b>Ein gruener Haken im Testbetrieb waere eine Luege.</b> Bei
    /// <c>GROW_OS_DEMO=1</c> liefert Home Assistant erfundene Werte, und
    /// <see cref="HomeAssistantService.CallEntityServiceAsync"/> kehrt zurueck,
    /// ohne etwas zu senden. Die Einstellungen koennen dabei durchaus gesetzt
    /// sein — „verbunden" waere formal richtig und trotzdem irrefuehrend, weil
    /// nichts ankommt. Genau diese Sorte Halbwahrheit soll die Kette abschaffen.
    /// </remarks>
    private static Voraussetzung Verbindung(bool haVerbunden, bool testbetrieb, string tuwort)
    {
        if (testbetrieb)
        {
            return new("Home Assistant verbunden", false,
                $"Testbetrieb: die Messwerte sind erfunden und es geht nichts an ein echtes Gerät. "
                + "Ohne GROW_OS_DEMO=1 gilt die echte Verbindung.",
                "eine echte Verbindung zu Home Assistant (Testbetrieb)",
                KettenSchluessel.Verbindung);
        }

        return new("Home Assistant verbunden", haVerbunden,
            haVerbunden
                ? "Die Verbindung steht."
                : $"Ohne Verbindung wird nichts {tuwort} — siehe Einrichtung → Home Assistant.",
            "die Verbindung zu Home Assistant",
            KettenSchluessel.Verbindung);
    }

    /// <summary>Der eine Satz für ganz oben — er nennt das ERSTE fehlende Glied.</summary>
    /// <remarks>
    /// Nicht „mehrere Voraussetzungen fehlen": wer eine Kette repariert, fängt
    /// vorne an. Alle fehlenden Glieder stehen ohnehin in der Liste darunter.
    /// </remarks>
    private static string Kurzfassung(
        bool rampeLaeuft, bool kuehlerLaeuft,
        IReadOnlyList<Voraussetzung> rampe, IReadOnlyList<Voraussetzung> kuehler)
    {
        if (rampeLaeuft && kuehlerLaeuft)
        {
            return "Aktiv. Der Sollwert wird geschrieben und der Kühler geregelt.";
        }

        if (rampeLaeuft)
        {
            var fehlt = kuehler.First(s => !s.Erfuellt);
            return $"Der Sollwert wird geschrieben. Der Kühler wird nicht geregelt — es fehlt {fehlt.Fehlt}.";
        }

        if (kuehlerLaeuft)
        {
            var fehlt = rampe.First(s => !s.Erfuellt);
            return $"Der Kühler wird geregelt. Der Sollwert wird nicht geschrieben — es fehlt {fehlt.Fehlt}.";
        }

        var erstes = rampe.First(s => !s.Erfuellt);
        return $"Nicht aktiv. Es fehlt {erstes.Fehlt}.";
    }

    private static string Zahl(double? wert)
        => wert is { } v ? v.ToString("0.#", AppCulture.German) : "–";
}

/// <summary>
/// Woher „zuletzt geschrieben" kommt.
/// </summary>
/// <remarks>
/// <para>Bis zum 02.09.2026 stand im Endpunkt <c>GetRecent(1, "night-ramp")
/// .FirstOrDefault(e =&gt; e.Success)</c> — also: hole <b>einen</b> Eintrag und
/// schau danach, ob er geklappt hat. War der letzte Versuch fehlgeschlagen, kam
/// nichts zurück, und die Seite verschwieg, dass die Rampe seit Wochen
/// zuverlässig schreibt.</para>
///
/// <para>Weil derselbe Griff an zwei Stellen gebraucht wird — Endpunkt und
/// Prüfung —, steht er hier einmal.</para>
/// </remarks>
public static class NightRampAuskunft
{
    /// <summary>
    /// So weit wird zurückgesehen. Zwei Tage Ausfall im Zehn-Minuten-Takt sind
    /// rund 290 Einträge; 500 ist die Obergrenze, die die Ablage zulässt.
    /// </summary>
    public const int Tiefe = 500;

    /// <summary>Der Zeitpunkt des letzten <b>erfolgreichen</b> Eintrags.</summary>
    public static DateTime? LetzterErfolgUtc(SystemAuditRepository protokoll, string art)
        => protokoll.GetRecent(Tiefe, art).FirstOrDefault(e => e.Success)?.CreatedAtUtc;
}
