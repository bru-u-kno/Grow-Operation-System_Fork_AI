namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>Was ein Schlüssel für einen Home-Assistant-Dienst braucht.</summary>
public enum KiHaUrteil
{
    /// <summary>Geräte schalten genügt — nur für Domains der Positivliste und dort nur deren Dienste.</summary>
    GeraeteSchalten,

    /// <summary>Zusätzlich zu Geräte schalten auch Verwaltung.</summary>
    Verwaltung,

    /// <summary>Über einen Schlüssel nie, egal welche Stufen er hat — auch die Vorgabe für alles Unbekannte.</summary>
    Nie,
}

/// <summary>Ein Eintrag der Tabelle: Domain (oder Dienstname), Urteil und warum.</summary>
/// <param name="Dienste">
/// Nur bei <see cref="KiHaUrteil.GeraeteSchalten"/>: die Dienste dieser Domain, die
/// erlaubt sind. Jeder andere Dienst der Domain geht nie — sonst käme über eine
/// erlaubte Domain ein schädlicher Dienst durch (<c>media_player.play_media</c>
/// mit beliebiger Adresse, <c>vacuum.send_command</c>).
/// </param>
public sealed record KiHaRegel(string Name, KiHaUrteil Urteil, string Grund, IReadOnlyList<string>? Dienste = null);

/// <summary>
/// Fork AI (A-003 Etappe B, 03.10.2026; Positivliste seit dem Prüferbefund vom
/// 04.10.2026): Welche Home-Assistant-Dienste ein KI-Assistent über
/// <c>POST /api/ki-ha/dienst</c> rufen darf.
/// </summary>
/// <remarks>
/// <para><b>Positivliste, nicht Verbotsliste.</b> Bis zum 04.10.2026 galt für
/// jede Domain, die nicht in der Tabelle stand, „Geräte schalten". Der Prüfer kam
/// damit bis Home Assistant: Türschloss über <c>zha.issue_zigbee_cluster_command</c>,
/// Nachrichten über <c>telegram_bot</c>, Fernzugang über <c>cloud.remote_connect</c>,
/// Dateien über <c>camera.snapshot</c> … Eine Verbotsliste scheitert an jeder
/// Integration, die jemand nachinstalliert. Jetzt gilt: was nicht ausdrücklich
/// erlaubt ist, geht nie (<see cref="Unbekannt"/>).</para>
///
/// <para><b>Eine Tabelle, nicht drei Listen.</b> Jede Domain steht genau einmal
/// darin, mit Urteil und Grund — Positivliste, Verwaltung und die Nie-Liste
/// können sich so nicht überschneiden. Steht ein Name doppelt in
/// <see cref="Domains"/>, wirft schon das Laden des Typs;
/// <c>KiHaEinstufungTests</c> zählt das zusätzlich nach. Die Nie-Einträge
/// ändern am Ergebnis nichts mehr (unbekannt ist ohnehin nie) — sie bleiben als
/// dokumentierte Begründung und damit niemand sie später „ergänzt".</para>
///
/// <para><b>Die Aktion selbst</b> trägt <see cref="KiStufe.GeraeteSchalten"/>:
/// die Sperre prüft das vor dem Controller und zählt jeden Aufruf ins
/// Stundenfenster der Schaltbefehle. Diese Tabelle legt nur <b>zusätzliche</b>
/// Anforderungen darauf.</para>
///
/// <para><b>Im Zweifel weglassen.</b> Die Dienste je Gerätedomain sind die, die
/// Home Assistant für die Domain selbst anbietet (abgefragt am 04.10.2026 an der
/// Anlage), ohne die, die beliebige Befehle, Adressen oder Dateien annehmen.</para>
/// </remarks>
public static class KiHaEinstufung
{
    /// <summary>Was für eine Domain gilt, die nirgends in der Tabelle steht.</summary>
    public const string UnbekanntGrund =
        "Über einen KI-Assistenten sind nur bekannte Gerätebereiche erlaubt "
        + "(light, switch, fan, climate, humidifier, cover, valve, number, select, button, water_heater, vacuum, media_player) "
        + "und mit Verwaltung die Logik von Home Assistant (Automationen, Skripte, Szenen, Helfer).";

    private static readonly KiHaRegel[] Tabelle =
    [
        // ------------------------------------------- Geräte schalten (Positivliste)
        // Je Domain die Dienste, die Home Assistant für sie selbst anbietet — ohne
        // die, die beliebige Befehle, Adressen oder Dateien annehmen.
        new("light", KiHaUrteil.GeraeteSchalten,
            "Grow-Licht schalten und dimmen.",
            ["turn_on", "turn_off", "toggle"]),
        new("switch", KiHaUrteil.GeraeteSchalten,
            "Steckdosen von Pumpen, Lüftern, Kühler und Licht.",
            ["turn_on", "turn_off", "toggle"]),
        new("fan", KiHaUrteil.GeraeteSchalten,
            "Abluft und Umluft im Zelt.",
            ["turn_on", "turn_off", "toggle", "set_percentage", "increase_speed", "decrease_speed", "oscillate", "set_direction", "set_preset_mode"]),
        new("climate", KiHaUrteil.GeraeteSchalten,
            "Klimageräte und Heizungen am Zelt (Sollwert, Betriebsart).",
            ["turn_on", "turn_off", "toggle", "set_temperature", "set_humidity", "set_hvac_mode", "set_preset_mode", "set_fan_mode", "set_swing_mode", "set_swing_horizontal_mode"]),
        new("humidifier", KiHaUrteil.GeraeteSchalten,
            "Be- und Entfeuchter.",
            ["turn_on", "turn_off", "toggle", "set_humidity", "set_mode"]),
        new("cover", KiHaUrteil.GeraeteSchalten,
            "Klappen und Rollos (Zuluft, Abdunklung).",
            ["open_cover", "close_cover", "stop_cover", "toggle", "set_cover_position",
             "open_cover_tilt", "close_cover_tilt", "stop_cover_tilt", "set_cover_tilt_position", "toggle_cover_tilt"]),
        new("valve", KiHaUrteil.GeraeteSchalten,
            "Ventile der Bewässerung und des Nachfüllens.",
            ["open_valve", "close_valve", "stop_valve", "toggle", "set_valve_position"]),
        new("number", KiHaUrteil.GeraeteSchalten,
            "Stellwerte von Geräten (Leistung in Prozent, Pumpenlaufzeit).",
            ["set_value"]),
        new("select", KiHaUrteil.GeraeteSchalten,
            "Betriebsarten von Geräten (etwa der Modus des AC-Lüfters).",
            ["select_option", "select_first", "select_last", "select_next", "select_previous"]),
        new("button", KiHaUrteil.GeraeteSchalten,
            "Tasten von Geräten (Probelauf, Neustart eines Controllers).",
            ["press"]),
        new("water_heater", KiHaUrteil.GeraeteSchalten,
            "Wassererwärmer am Reservoir.",
            ["turn_on", "turn_off", "set_temperature", "set_operation_mode", "set_away_mode"]),
        // send_command fehlt: es schickt einen beliebigen Rohbefehl an das Gerät.
        new("vacuum", KiHaUrteil.GeraeteSchalten,
            "Saugroboter — etwa für die Zeit, in der das Zelt offen ist.",
            ["start", "pause", "stop", "return_to_base", "clean_spot", "clean_area", "locate", "set_fan_speed"]),
        // play_media, select_source, join, browse_media, search_media fehlen: play_media
        // spielt eine beliebige Adresse ab, die übrigen führen über das Gerät hinaus.
        new("media_player", KiHaUrteil.GeraeteSchalten,
            "Musik im Grow-Raum ein-/ausschalten und leiser stellen.",
            ["turn_on", "turn_off", "toggle", "volume_set", "volume_mute", "media_pause", "media_play", "media_stop"]),

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
        // Seit der Positivliste (04.10.2026) ändern diese Einträge am Ergebnis nichts
        // — unbekannt ist ohnehin nie. Sie bleiben als Begründung, warum diese Domains
        // nie auf die Positivliste gehören, und mit einem genaueren Grund in der Meldung.
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
    /// <remarks>
    /// <para>Der Dienstname wiegt zuerst: <c>automation.reload</c> ist nie, nicht Verwaltung.</para>
    /// <para>Eine Gerätedomain erlaubt nur ihre Dienste; jeder andere Dienst der
    /// Domain ist nie. Eine Domain, die nirgends steht, ist nie
    /// (<see cref="UnbekanntGrund"/>) — es gibt keine Vorgabe „Geräte schalten".</para>
    /// </remarks>
    public static KiHaRegel Einstufen(string domain, string dienst)
    {
        if (Dienste.TryGetValue(dienst, out var dienstRegel)) return dienstRegel;
        if (!Domains.TryGetValue(domain, out var regel)) return Unbekannt(domain);
        if (regel.Urteil == KiHaUrteil.GeraeteSchalten && !(regel.Dienste ?? []).Contains(dienst, StringComparer.Ordinal))
        {
            return new KiHaRegel($"{domain}.{dienst}", KiHaUrteil.Nie,
                $"In „{domain}“ sind nur diese Dienste erlaubt: {string.Join(", ", regel.Dienste ?? [])}.");
        }
        return regel;
    }

    /// <summary>Das Urteil für eine Domain, die nirgends in der Tabelle steht: nie.</summary>
    public static KiHaRegel Unbekannt(string domain) => new(domain, KiHaUrteil.Nie, UnbekanntGrund);

    /// <summary>Namen, die in den Einträgen mehr als einmal stehen.</summary>
    public static IReadOnlyList<string> Doppelte(IEnumerable<KiHaRegel> eintraege)
        => eintraege.GroupBy(r => r.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
}
