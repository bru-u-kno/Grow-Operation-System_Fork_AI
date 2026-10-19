using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Was der Wächter über ein Anlagenteil sagt.</summary>
/// <param name="Schluessel">Die Messgröße, aus der das Urteil stammt.</param>
/// <param name="Stufe">ok, warnung oder kritisch.</param>
/// <param name="Herkunft">Woran er es erkannt hat — für die Nachprüfbarkeit.</param>
public sealed record AnlagenBefund(
    string Schluessel,
    string Name,
    string Stufe,
    string Meldung,
    string Herkunft);

/// <summary>
/// Merkt, wenn der Kühler steht oder die USV auf Batterie läuft.
///
/// <para><b>Warum das hier existiert.</b> Beide Größen ließen sich seit jeher an
/// ein Zelt mappen und erschienen als Kachel — und kein einziger Dienst hat sie
/// gelesen. Die Schlüssel <c>chiller</c>, <c>ups-battery</c> und
/// <c>ups-status</c> kamen im ganzen Services-Ordner nur in der Schlüsseltabelle
/// und einer Formatierzeile vor. Dabei führt das Modell eigens
/// <see cref="RiskEventType.ChillerOffline"/> und
/// <see cref="RiskEventType.UpsOnBattery"/>, beide mit null Verwendungen.</para>
///
/// <para><b>Warum es dringend ist.</b> Im RDWC ist genau das die Kette, die eine
/// Ernte kostet: Kühler aus, Wassertemperatur steigt, gelöster Sauerstoff fällt
/// (warmes Wasser hält weniger), Wurzelfäule. Aus einem gesunden Reservoir wird
/// so in etwa zwei Tagen ein Totalausfall. Ein paar Stunden Vorsprung sind hier
/// der Unterschied zwischen Ärgernis und ganzem Lauf.</para>
///
/// <para>Bei der USV kommt hinzu: läuft sie auf Batterie, ist der Strom schon
/// weg. Die verbleibende Laufzeit ist die Zeit, die zum Handeln bleibt.</para>
/// </summary>
public static class AnlagenWatchService
{
    /// <summary>
    /// Ab diesem Ladestand wird es eng.
    /// </summary>
    /// <remarks>
    /// Faustregel: eine typische Kleinst-USV trägt eine RDWC-Anlage bei 40 %
    /// noch etwa zehn bis zwanzig Minuten. Das ist die Spanne, in der man
    /// entweder einen Generator anwirft oder die Pumpen von Hand sichert.
    /// Der Wert ist eine Faustregel, keine Herstellerangabe.
    /// </remarks>
    public const double BatterieKritischProzent = 40;

    /// <summary>Ab hier lohnt schon ein Blick, ohne dass es brennt.</summary>
    public const double BatterieWarnungProzent = 70;

    /// <summary>
    /// Beurteilt Kühler und USV aus den Live-Zuständen eines Zelts.
    /// </summary>
    /// <remarks>
    /// Statisch und ohne Datenbank — wie beim Pumpen-Wächter: an dieser
    /// Entscheidung hängt eine Nachricht, die jemanden nachts weckt. Sie muss
    /// prüfbar sein, ohne dass ein Home Assistant läuft.
    ///
    /// <b>Kein „unbekannt heisst Gefahr".</b> Wer keinen Kühler und keine USV
    /// hat, hört von diesem Wächter nichts. Alles andere wäre eine Dauerwarnung
    /// über etwas, das gar nicht existiert.
    /// </remarks>
    /// <param name="kuehlerAbsichtlichAus">
    /// Hat CULTIVATE CONTROL den Kuehler gerade selbst abgeschaltet, weil das Wasser kalt
    /// genug ist? Dann ist „aus" kein Ausfall, sondern der Regler bei der
    /// Arbeit — und eine Stoerungsmeldung waere die App, die sich selbst
    /// anzeigt. Seit es die Kuehler-Steuerung gibt, muss diese Seite mitgeteilt
    /// werden; ohne sie meldet der Waechter jeden normalen Regeltakt.
    /// </param>
    public static IReadOnlyList<AnlagenBefund> Beurteilen(
        IReadOnlyDictionary<string, HomeAssistantState> zustaende,
        DateTime nowUtc,
        int schonfristMinuten = PumpWatchService.StandardSchonfristMinuten,
        bool kuehlerAbsichtlichAus = false)
    {
        var befunde = new List<AnlagenBefund>();
        KuehlerPruefen(zustaende, nowUtc, schonfristMinuten, kuehlerAbsichtlichAus, befunde);
        UsvPruefen(zustaende, befunde);
        return befunde;
    }

    private static void KuehlerPruefen(
        IReadOnlyDictionary<string, HomeAssistantState> zustaende,
        DateTime nowUtc,
        int schonfristMinuten,
        bool absichtlichAus,
        List<AnlagenBefund> befunde)
    {
        if (!zustaende.TryGetValue("chiller", out var chiller) || chiller is null) return;

        if (!PumpWatchService.IstAus(chiller.State))
        {
            befunde.Add(new AnlagenBefund("chiller", "Kühler", "ok", "Der Kühler läuft.",
                "Zustand aus Home Assistant."));
            return;
        }

        // Dieselbe Schonfrist wie bei den Pumpen: ein Kühler taktet, und jedes
        // Abschalten sofort zu melden wäre Rauschen. Er hat auch dieselbe
        // Ursache — beide hängen am Strom.
        var seit = chiller.LastChanged is { } geaendert
            ? (int)Math.Max(0, (nowUtc - geaendert.ToUniversalTime()).TotalMinutes)
            : schonfristMinuten;

        if (seit < schonfristMinuten) return;

        // Der Regler hat ihn selbst ausgeschaltet, weil das Wasser kalt genug
        // ist. Das als Stoerung zu melden hiesse, die App zeigt ihre eigene
        // Arbeit als Ausfall an — und wer das ein paarmal gesehen hat, glaubt
        // der Meldung beim echten Ausfall nicht mehr.
        if (absichtlichAus)
        {
            befunde.Add(new AnlagenBefund("chiller", "Kühler", "ok",
                "Der Kühler ist aus — die Steuerung hat ihn abgeschaltet, das Wasser ist kalt genug.",
                "Kühler-Steuerung von Cultivate Control."));
            return;
        }

        befunde.Add(new AnlagenBefund(
            "chiller", "Kühler", "kritisch",
            $"Der Kühler ist seit {seit} Minuten aus. Die Wassertemperatur steigt, "
                + "und warmes Wasser hält weniger Sauerstoff — das ist der Weg in die Wurzelfäule.",
            $"Zustand aus Home Assistant; Schonfrist {schonfristMinuten} Minuten (Faustregel, in den Einstellungen änderbar)."));
    }

    private static void UsvPruefen(
        IReadOnlyDictionary<string, HomeAssistantState> zustaende,
        List<AnlagenBefund> befunde)
    {
        zustaende.TryGetValue("ups-status", out var status);
        zustaende.TryGetValue("ups-battery", out var batterie);
        if (status is null && batterie?.NumericValue is null) return;

        var aufBatterie = status is not null && AufBatterie(status.State);
        var ladung = batterie?.NumericValue;
        var ladungText = ladung is { } l ? $" Ladestand {l.ToString("0", AppCulture.German)} %." : string.Empty;

        if (aufBatterie)
        {
            // Auf Batterie heisst: der Strom ist schon weg. Wie dringend es ist,
            // sagt der Ladestand — ohne ihn bleibt es bei „dringend".
            var kritisch = ladung is null || ladung <= BatterieKritischProzent;
            befunde.Add(new AnlagenBefund(
                "ups-status", "USV", kritisch ? "kritisch" : "warnung",
                $"Die USV läuft auf Batterie — der Netzstrom ist weg.{ladungText}",
                ladung is null
                    ? "Zustand aus Home Assistant; kein Ladestand gemappt, deshalb als dringend eingestuft."
                    : $"Zustand und Ladestand aus Home Assistant; unter {BatterieKritischProzent.ToString("0", AppCulture.German)} % gilt als kritisch (Faustregel)."));
            return;
        }

        if (ladung is { } wert && wert < BatterieWarnungProzent)
        {
            befunde.Add(new AnlagenBefund(
                "ups-battery", "USV", "warnung",
                $"Die USV lädt noch: Ladestand {wert.ToString("0", AppCulture.German)} %. "
                    + "Bei einem Stromausfall jetzt hält sie kürzer durch als sonst.",
                $"Ladestand aus Home Assistant; unter {BatterieWarnungProzent.ToString("0", AppCulture.German)} % ein Hinweis (Faustregel)."));
            return;
        }

        befunde.Add(new AnlagenBefund("ups-status", "USV", "ok", $"Die USV ist am Netz.{ladungText}",
            "Zustand aus Home Assistant."));
    }

    /// <summary>Meldet dieser Zustand „läuft auf Batterie"?</summary>
    /// <remarks>
    /// Die Schreibweisen unterscheiden sich je nach Integration; NUT meldet
    /// „On Battery", andere „onbatt" oder schlicht „discharging".
    /// </remarks>
    private static bool AufBatterie(string? zustand)
    {
        if (string.IsNullOrWhiteSpace(zustand)) return false;
        var wert = zustand.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return wert is "onbattery" or "onbatt" or "ob" or "discharging" or "battery";
    }
}
