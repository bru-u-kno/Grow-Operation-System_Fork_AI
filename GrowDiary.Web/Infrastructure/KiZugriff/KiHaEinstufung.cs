namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>Was ein Schlüssel für einen Home-Assistant-Dienst braucht.</summary>
public enum KiHaUrteil
{
    /// <summary>Geräte schalten genügt — die Vorgabe für jede Domain, die nicht in der Tabelle steht.</summary>
    GeraeteSchalten,

    /// <summary>Zusätzlich zu Geräte schalten auch Verwaltung.</summary>
    Verwaltung,

    /// <summary>Über einen Schlüssel nie, egal welche Stufen er hat.</summary>
    Nie,
}

/// <summary>Ein Eintrag der Tabelle: Domain (oder Dienstname), Urteil und warum.</summary>
public sealed record KiHaRegel(string Name, KiHaUrteil Urteil, string Grund);

/// <summary>
/// Fork AI (A-003 Etappe B, 03.10.2026): Welche Home-Assistant-Dienste ein
/// KI-Assistent über <c>POST /api/ki-ha/dienst</c> rufen darf.
/// </summary>
/// <remarks>
/// <para><b>Eine Tabelle, nicht drei Listen.</b> Jede Domain steht genau einmal
/// darin, mit Urteil und Grund. Zwei Listen nebeneinander könnten dieselbe Domain
/// zweimal führen — und welche dann gilt, entschiede die Reihenfolge der Abfrage.
/// Steht ein Name doppelt in <see cref="Domains"/>, wirft schon das Laden des
/// Typs; <c>KiHaSchnittstelleTests</c> zählt das zusätzlich nach.</para>
///
/// <para><b>Die Aktion selbst</b> trägt <see cref="KiStufe.GeraeteSchalten"/>:
/// die Sperre prüft das vor dem Controller und zählt jeden Aufruf ins
/// Stundenfenster der Schaltbefehle. Diese Tabelle legt nur <b>zusätzliche</b>
/// Anforderungen darauf. Was hier fehlt, genügt mit Geräte schalten — gedacht für
/// light, switch, fan, climate, humidifier, cover, number, select, button, valve,
/// water_heater, vacuum, media_player und Ähnliches.</para>
///
/// <para><b>Im Zweifel die höhere Stufe</b>, wie im Bauplan
/// (<c>docs/ki-zugriff.md</c>): was die Logik von Home Assistant selbst ändert
/// (Automationen, Skripte, Helfer), braucht Verwaltung; was Home Assistant als
/// Ganzes, seine Daten, Dateien, Türen oder Nachrichten an Menschen betrifft,
/// geht nie.</para>
/// </remarks>
public static class KiHaEinstufung
{
    private static readonly KiHaRegel[] Tabelle =
    [
        // ---------------------------------------- Verwaltung zusätzlich
        // Was die Logik von Home Assistant ändert oder auslöst: nicht ein Gerät,
        // sondern eine Regel, die viele Geräte schalten kann.
        new("automation", KiHaUrteil.Verwaltung,
            "Automationen ein-/ausschalten oder auslösen ändert, was Home Assistant von selbst tut — oft an mehreren Geräten."),
        new("script", KiHaUrteil.Verwaltung,
            "Ein Skript kann beliebige Dienste rufen, also mehr als die Domain verrät."),
        new("scene", KiHaUrteil.Verwaltung,
            "Eine Szene setzt viele Geräte auf einmal und kann neue Szenen anlegen."),
        new("input_boolean", KiHaUrteil.Verwaltung,
            "Helfer sind Schalter der Automationen — wer sie setzt, steuert deren Logik."),
        new("input_number", KiHaUrteil.Verwaltung,
            "Helfer tragen Sollwerte der Automationen (etwa die Fork-Steuerung) — ein Wert hier verschiebt die Regelung."),
        new("input_select", KiHaUrteil.Verwaltung,
            "Helfer wählen Betriebsarten der Automationen."),
        new("input_text", KiHaUrteil.Verwaltung,
            "Helfer tragen Texte, die Automationen auswerten."),
        new("input_datetime", KiHaUrteil.Verwaltung,
            "Helfer tragen Zeitpunkte, zu denen Automationen schalten."),
        new("input_button", KiHaUrteil.Verwaltung,
            "Ein Helfer-Knopf löst Automationen aus, nicht ein Gerät."),
        new("timer", KiHaUrteil.Verwaltung,
            "Timer starten oder beenden Abläufe in Automationen."),
        new("counter", KiHaUrteil.Verwaltung,
            "Zähler sind Zustand der Automationen."),
        new("schedule", KiHaUrteil.Verwaltung,
            "Zeitpläne bestimmen, wann Automationen schalten."),

        // ------------------------------------------------------------ nie
        new("homeassistant", KiHaUrteil.Nie,
            "Neustart, Stopp, Konfiguration neu laden und generisches Schalten jeder Domain — das umginge diese Tabelle."),
        new("hassio", KiHaUrteil.Nie,
            "Supervisor: Add-ons, Host, Neustart, Sicherungen — Eingriffe ins System, nicht in den Grow."),
        new("backup", KiHaUrteil.Nie,
            "Sicherungen anlegen oder zurückspielen; auch in Grow OS nie über einen Schlüssel."),
        new("recorder", KiHaUrteil.Nie,
            "Löscht oder sperrt die aufgezeichnete Geschichte — nicht zurückzudrehen."),
        new("system_log", KiHaUrteil.Nie,
            "Leert oder schreibt das Systemprotokoll — das ist die Spur dessen, was passiert ist."),
        new("logger", KiHaUrteil.Nie,
            "Ändert, was protokolliert wird."),
        new("shell_command", KiHaUrteil.Nie,
            "Führt Befehle auf dem Host aus."),
        new("python_script", KiHaUrteil.Nie,
            "Führt beliebigen Code in Home Assistant aus."),
        new("pyscript", KiHaUrteil.Nie,
            "Führt beliebigen Code in Home Assistant aus."),
        new("rest_command", KiHaUrteil.Nie,
            "Schickt Anfragen an beliebige Adressen im Netz."),
        new("notify", KiHaUrteil.Nie,
            "Nachrichten an Menschen gehen von Grow OS selbst aus, nicht im Namen eines Assistenten."),
        new("persistent_notification", KiHaUrteil.Nie,
            "Meldungen in der Oberfläche von Home Assistant — ein Assistent soll dem Betreiber nichts unterschieben."),
        new("tts", KiHaUrteil.Nie,
            "Sprachausgabe an Lautsprecher im Haus — spricht Menschen an."),
        new("conversation", KiHaUrteil.Nie,
            "Der Sprachassistent führt beliebige Absichten aus — das umginge diese Tabelle."),
        new("lock", KiHaUrteil.Nie,
            "Schlösser: Haus- und Zelttüren öffnen ist kein Grow-Betrieb."),
        new("alarm_control_panel", KiHaUrteil.Nie,
            "Alarmanlage scharf/unscharf schalten."),
        // Fork AI (A-003 Etappe B): über den Auftrag hinaus ergänzt — begründet im Bericht.
        new("update", KiHaUrteil.Nie,
            "Installiert Firmware und Updates — Eingriff ins System wie hassio, nicht zurückzudrehen."),
        new("mqtt", KiHaUrteil.Nie,
            "mqtt.publish schreibt an beliebige Themen und erreicht so jedes Gerät — das umginge diese Tabelle."),
        new("downloader", KiHaUrteil.Nie,
            "Lädt Dateien aus dem Netz auf den Host."),
    ];

    /// <summary>Dienstnamen, die in JEDER Domain nie gehen.</summary>
    private static readonly KiHaRegel[] DienstTabelle =
    [
        new("reload", KiHaUrteil.Nie,
            "Konfiguration neu laden: wirft laufende Zustände weg und kann eine kaputte Konfiguration scharf schalten."),
    ];

    /// <summary>Alle Domains mit ihrem Urteil. Wirft beim Laden, wenn ein Name doppelt steht.</summary>
    public static IReadOnlyDictionary<string, KiHaRegel> Domains { get; } =
        Tabelle.ToDictionary(r => r.Name, StringComparer.Ordinal);

    /// <summary>Dienstnamen, die in jeder Domain nie gehen.</summary>
    public static IReadOnlyDictionary<string, KiHaRegel> Dienste { get; } =
        DienstTabelle.ToDictionary(r => r.Name, StringComparer.Ordinal);

    /// <summary>Die rohen Einträge in Schreibreihenfolge — für die Zählung auf Doppelte.</summary>
    public static IReadOnlyList<KiHaRegel> Eintraege => Tabelle;

    /// <summary>Was ein Schlüssel für <paramref name="domain"/>.<paramref name="dienst"/> braucht.</summary>
    /// <remarks>Der Dienstname wiegt zuerst: <c>automation.reload</c> ist nie, nicht Verwaltung.</remarks>
    public static KiHaRegel Einstufen(string domain, string dienst)
    {
        if (Dienste.TryGetValue(dienst, out var dienstRegel)) return dienstRegel;
        if (Domains.TryGetValue(domain, out var regel)) return regel;
        return new KiHaRegel(domain, KiHaUrteil.GeraeteSchalten, "Gerät schalten — keine zusätzliche Anforderung.");
    }

    /// <summary>Namen, die in den Einträgen mehr als einmal stehen.</summary>
    public static IReadOnlyList<string> Doppelte(IEnumerable<KiHaRegel> eintraege)
        => eintraege.GroupBy(r => r.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
}
