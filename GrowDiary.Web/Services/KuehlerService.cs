using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Was der Regler mit dem Kühler vorhat.</summary>
public enum KuehlerSchaltung
{
    /// <summary>Nichts tun — der Zustand passt oder etwas spricht dagegen.</summary>
    Nichts,

    /// <summary>Einschalten: das Wasser ist zu warm.</summary>
    Ein,

    /// <summary>Ausschalten: das Wasser ist kalt genug.</summary>
    Aus,
}

/// <summary>
/// Alles, was der Regler über die Lage wissen muss.
/// </summary>
/// <param name="SollC">
/// Die Zieltemperatur, die JETZT gilt — Tag- oder Nachtwert aus dem
/// Sollwert-Profil, bei Nacht mit der Absenkrampe. <c>null</c> heißt: es gibt
/// keinen Sollwert (kein Flip, kein Profil), und dann wird <b>nicht
/// geschaltet</b> — auch nicht ausgeschaltet.
/// </param>
/// <param name="IstC">Die gemessene Wassertemperatur.</param>
/// <param name="MesswertAlter">
/// Wie alt der Istwert ist. Auf einen halbstündigen Wert zu regeln ist etwas
/// anderes, als ihn anzuzeigen.
/// </param>
/// <param name="KuehlerLaeuftGerade">
/// Der aktuelle Zustand der Steckdose. <c>null</c> = unbekannt; dann wird nicht
/// geschaltet, weil weder Mindestlauf noch Mindestpause zu beurteilen sind.
/// </param>
/// <param name="LetzteSchaltung">Wann zuletzt geschaltet wurde — aus der Datenbank, nicht aus dem Speicher.</param>
/// <param name="Tagbetrieb">Brennt das Licht? Nur für den Klartext im Grund.</param>
public sealed record KuehlerLage(
    double? SollC,
    double? IstC,
    TimeSpan? MesswertAlter,
    bool? KuehlerLaeuftGerade,
    DateTime? LetzteSchaltung,
    bool Tagbetrieb);

/// <summary>Das Urteil des Reglers.</summary>
public sealed record KuehlerUrteil(KuehlerSchaltung Schaltung, string Grund)
{
    public static KuehlerUrteil Nichts(string grund) => new(KuehlerSchaltung.Nichts, grund);
}

/// <summary>
/// Entscheidet, ob der Wasserkühler laufen soll — rein, ohne Home Assistant
/// und ohne Datenbank.
///
/// <para><b>Woher die Idee kommt.</b> Ein Hailea-Kühler nimmt keinen Sollwert
/// von außen; er hat seinen eigenen Thermostat und kein Bussystem. Damit war
/// die Nachtabsenkung, die Cultivate Control seit beta.32 <i>plant</i>, nicht
/// umzusetzen. Der Tester hat es umgedreht: den Kühler auf eine tiefe
/// Untergrenze stellen — er regelt dann nicht mehr, sondern schaltet nur noch
/// im Notfall ab — und ihn über eine smarte Steckdose ein- und ausschalten.
/// Die Temperatur kommt von einem eigenen Messgerät. Die Steckdose wird der
/// Thermostat, und der Sollwert kommt aus dem Profil.</para>
///
/// <para><b>Warum die tiefe Untergrenze der kluge Teil ist.</b> Sie legt die
/// Richtung des Fehlerfalls fest. Bleibt die Steckdose hängen und der Kühler
/// läuft durch, stoppt sein eigener Thermostat — kalt, aber nicht tödlich.
/// Wäre er auf 5 °C gestellt, wäre derselbe Fehler ein Wurzelschaden. Deshalb
/// steht die Untergrenze in der Oberfläche als <b>Bedingung</b>, nicht als
/// Empfehlung.</para>
///
/// <para><b>Warum das rein ist.</b> Am Ende dieser Rechnung schaltet ein
/// Kompressor. Zu häufiges Takten zerstört ihn — das ist kein „falscher Wert
/// auf einer Kachel", sondern ein Gerät weniger. Dieselbe Begründung wie bei
/// <see cref="DosingCalculator"/>.</para>
/// </summary>
public static class KuehlerService
{
    /// <summary>Standard-Totband um den Sollwert, in Grad.</summary>
    /// <remarks>
    /// 0,4 nach oben und unten, also ein Fenster von 0,8 °C. Enger klappert,
    /// weiter lässt die Temperatur zu weit wandern. Am Zelt einstellbar, weil
    /// es an der Trägheit des Beckens hängt: 100 Liter schwingen anders als 30.
    /// </remarks>
    public const double StandardHystereseC = 0.4;

    /// <summary>Wie lange der Kompressor mindestens läuft, bevor er wieder darf.</summary>
    public const int StandardMindestlaufMinuten = 5;

    /// <summary>Wie lange er mindestens steht, bevor er wieder anlaufen darf.</summary>
    /// <remarks>
    /// <b>Das ist die wichtigere der beiden.</b> Ein Kältekompressor braucht
    /// die Druckangleichung zwischen Hoch- und Niederdruckseite; läuft er zu
    /// früh wieder an, arbeitet er gegen den Restdruck. Fünf Minuten sind der
    /// verbreitete Richtwert der Hersteller.
    /// </remarks>
    public const int StandardMindestpauseMinuten = 5;

    /// <summary>Wie alt ein Messwert höchstens sein darf, um darauf zu schalten.</summary>
    public const int StandardHoechstalterMinuten = 10;

    /// <summary>
    /// Soll der Kühler laufen?
    /// </summary>
    /// <param name="lage">Die Lage — Soll, Ist, Zustand, letzte Schaltung.</param>
    /// <param name="zelt">Die Grenzen dieses Kühlers.</param>
    /// <param name="jetztUtc">Jetzt, in UTC — wie <see cref="DateTime.UtcNow"/>.</param>
    /// <remarks>
    /// Die Reihenfolge der Prüfungen ist Absicht: erst die Gründe, aus denen
    /// gar nicht geschaltet werden darf, dann die Regelung. Wer sie umdreht,
    /// schaltet auf einem veralteten Messwert und begründet es hinterher.
    /// </remarks>
    public static KuehlerUrteil Entscheiden(KuehlerLage lage, Tent zelt, DateTime jetztUtc)
    {
        if (!zelt.ChillerControlEnabled)
        {
            return KuehlerUrteil.Nichts("Die Kühler-Steuerung ist für dieses Zelt aus.");
        }

        if (string.IsNullOrWhiteSpace(zelt.ChillerSwitchEntityId))
        {
            return KuehlerUrteil.Nichts("Keine Steckdose hinterlegt — es gibt nichts zu schalten.");
        }

        // Kein Sollwert heisst NICHT „dann eben aus". Ein Autoflower hat nie
        // einen Flip und damit nie eine Bluetewoche; die Nachtabsenkung
        // schreibt aus demselben Grund bewusst nichts. Den Kuehler daraufhin
        // abzuschalten waere schlimmer als ihn zu lassen, wie er ist.
        if (lage.SollC is not { } soll)
        {
            return KuehlerUrteil.Nichts(
                "Kein Sollwert für jetzt — ohne Blütewoche gibt es keine Absenkung. "
                + "Der Kühler bleibt, wie er ist.");
        }

        if (soll < NachtabsenkungService.AbsoluteUntergrenzeC)
        {
            return KuehlerUrteil.Nichts(
                $"Der Sollwert {soll:0.0} °C liegt unter der harten Untergrenze von "
                + $"{NachtabsenkungService.AbsoluteUntergrenzeC:0.0} °C. Es wird nicht gekühlt.");
        }

        if (lage.IstC is not { } ist)
        {
            return KuehlerUrteil.Nichts("Keine Wassertemperatur gemessen — es gibt nichts zu regeln.");
        }

        var hoechstalter = TimeSpan.FromMinutes(Math.Max(1, zelt.ChillerMaxReadingAgeMinutes));
        if (lage.MesswertAlter is not { } alter)
        {
            return KuehlerUrteil.Nichts("Das Alter des Messwerts ist unbekannt — darauf wird nicht geschaltet.");
        }

        if (alter > hoechstalter)
        {
            return KuehlerUrteil.Nichts(
                $"Der Messwert ist {alter.TotalMinutes:0} Minuten alt (erlaubt sind "
                + $"{hoechstalter.TotalMinutes:0}). Auf einen alten Wert zu regeln, wäre geraten.");
        }

        if (lage.KuehlerLaeuftGerade is not { } laeuft)
        {
            return KuehlerUrteil.Nichts(
                "Der Zustand der Steckdose ist unbekannt. Ohne ihn lassen sich weder Mindestlauf "
                + "noch Mindestpause beurteilen.");
        }

        var hysterese = zelt.ChillerHysteresisC > 0 ? zelt.ChillerHysteresisC : StandardHystereseC;
        var einAb = soll + hysterese;

        // Die Ausschaltschwelle NIE unter die harte Untergrenze. Sonst sagt
        // dieselbe Klasse zwanzig Zeilen weiter oben „unter 12 °C wird nicht
        // gekuehlt" und laesst den Kuehler bei Soll 12 und Totband 3,0 bis
        // 9 °C weiterlaufen. Der eigene Thermostat des Geraets faengt das
        // zwar ab — aber eine Sperre, die nicht haelt, was ihr Text sagt,
        // ist keine Sperre.
        var ausAb = Math.Max(soll - hysterese, NachtabsenkungService.AbsoluteUntergrenzeC);
        var zeitwort = lage.Tagbetrieb ? "Tagwert" : "Nachtwert";

        // Innerhalb des Totbands wird nichts angefasst — das ist der Sinn des
        // Totbands. Ohne es klappert der Kompressor um den Sollwert herum,
        // auch mit Pufferzeit.
        if (laeuft && ist > ausAb)
        {
            return KuehlerUrteil.Nichts(
                $"{ist:0.0} °C, der Kühler läuft und schaltet bei {ausAb:0.0} °C ab "
                + $"({zeitwort} {soll:0.0} °C).");
        }

        if (!laeuft && ist < einAb)
        {
            return KuehlerUrteil.Nichts(
                $"{ist:0.0} °C, der Kühler steht und liefe erst ab {einAb:0.0} °C an "
                + $"({zeitwort} {soll:0.0} °C).");
        }

        // Jetzt WÜRDE geschaltet — bleibt die Frage, ob der Kompressor darf.
        var gewuenscht = laeuft ? KuehlerSchaltung.Aus : KuehlerSchaltung.Ein;
        var sperre = gewuenscht == KuehlerSchaltung.Ein
            ? Math.Max(1, zelt.ChillerMinPauseMinutes)
            : Math.Max(1, zelt.ChillerMinRunMinutes);

        if (lage.LetzteSchaltung is { } zuletzt)
        {
            var seither = jetztUtc - zuletzt;
            if (seither < TimeSpan.FromMinutes(sperre))
            {
                var rest = TimeSpan.FromMinutes(sperre) - seither;
                var was = gewuenscht == KuehlerSchaltung.Ein ? "Mindestpause" : "Mindestlaufzeit";
                return KuehlerUrteil.Nichts(
                    $"{was} läuft noch {rest.TotalMinutes:0.#} Minuten — der Kompressor wird geschont. "
                    + $"({ist:0.0} °C, {zeitwort} {soll:0.0} °C)");
            }
        }

        return gewuenscht == KuehlerSchaltung.Ein
            ? new KuehlerUrteil(KuehlerSchaltung.Ein,
                $"{ist:0.0} °C über {einAb:0.0} °C — Kühler an ({zeitwort} {soll:0.0} °C).")
            : new KuehlerUrteil(KuehlerSchaltung.Aus,
                $"{ist:0.0} °C unter {ausAb:0.0} °C — Kühler aus ({zeitwort} {soll:0.0} °C).");
    }

    /// <summary>
    /// Ist ein stehender Kühler <b>absichtlich</b> aus — oder ausgefallen?
    /// </summary>
    /// <param name="letzterBefehl">
    /// Was dieser Regler zuletzt gesendet hat; <c>null</c>, wenn er für dieses
    /// Zelt noch nie geschaltet hat.
    /// </param>
    /// <remarks>
    /// <b>Die Frage muss über den BEFEHL gehen, nicht über die Uhr.</b> Eine
    /// erste Fassung gab dem Anlagen-Wächter dafür ein Zeitfenster von
    /// zwanzig Minuten nach der letzten Schaltung — ab Minute 21 kam die
    /// kritische Meldung samt Push doch, und eine kühle Nacht ist genau
    /// dieser Fall. Ein Regler, der ein Gerät besitzt, darf es beliebig lange
    /// ausgeschaltet lassen.
    ///
    /// Der echte Ausfall bleibt sichtbar: hat der Regler <b>ein</b> befohlen
    /// und die Steckdose meldet trotzdem aus, ist das keine Regelpause.
    /// </remarks>
    public static bool IstAbsichtlichAus(Tent zelt, bool? letzterBefehl)
        => zelt.ChillerControlEnabled
           && !string.IsNullOrWhiteSpace(zelt.ChillerSwitchEntityId)
           && letzterBefehl == false;

    /// <summary>
    /// Welcher Sollwert gilt jetzt — der Tag- oder der Nachtwert?
    /// </summary>
    /// <remarks>
    /// <b>Es sind zwei Werte, nicht einer.</b> Das Sollwert-Profil führt je
    /// Phase <c>waterTempDayC</c> und <c>waterTempNightC</c>; die Absenkrampe
    /// wirkt nur auf den Nachtwert. Ein Regler, der nur den Nachtwert kennt,
    /// kühlt tagsüber falsch — und zwar den ganzen Tag.
    /// </remarks>
    public static double? SollJetzt(Absenkplan plan, bool lichtAn)
        => lichtAn ? plan.HeuteTagC : plan.HeuteNachtC;
}
