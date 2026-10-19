using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.69): Legt die Rechenwerte einer Steuerung an — die
/// Template-Helfer, die Ziel, Bedarf, Klima-Freigabe und Impulslänge rechnen.
/// </summary>
/// <remarks>
/// <para><b>Warum das nicht wie die anderen Helfer geht.</b> Zahlen und Schalter
/// entstehen über einen einzigen Befehl am WebSocket. Template-Helfer nicht: sie
/// entstehen über denselben Einrichtungsdialog, den ein Mensch im Browser
/// durchklickt, und den gibt es nur als REST. Drei Aufrufe — Dialog öffnen, Art
/// wählen, Felder abschicken.</para>
/// <para><b>Was dabei schiefgehen kann.</b> Der Dialog lebt zwischen den
/// Aufrufen im Speicher von Home Assistant. Bricht einer ab, bleibt ein halb
/// offener Dialog zurück — er verfällt nicht von selbst, sondern bleibt bis zum
/// nächsten Neustart von Home Assistant. Deshalb wird er nach einem Fehlschlag
/// ausdrücklich geschlossen, nicht weitergemacht, und gemeldet.</para>
/// <para><b>Die Reihenfolge ist nicht beliebig.</b> Die Impulslänge liest das
/// Ziel, der Bedarf liest ebenfalls das Ziel. Entsteht das Ziel zuletzt, stehen
/// die anderen kurz auf „nicht verfügbar" — harmlos, aber verwirrend. Der
/// Katalog nennt sie in der richtigen Folge, und diese Folge bleibt erhalten.</para>
/// </remarks>
public sealed class SteuerungRechenwertService
{
    private const string DialogPfad = "api/config/config_entries/flow";

    /// <summary>Fenster eines neu angelegten Mittelwerts; einstellbar auf der CO₂-Seite.</summary>
    public const int StandardMittelMinuten = 5;

    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungRechenwertService> _log;

    public SteuerungRechenwertService(HomeAssistantService ha, ILogger<SteuerungRechenwertService> log)
    {
        _ha = ha;
        _log = log;
    }

    public sealed record Ergebnis(string EntityId, string Name, bool Angelegt, string? Fehler);

    /// <summary>
    /// Fork AI (forkai.225): Eine feste Änderung an einem Rechenwert, die auch bei einer
    /// vorhandenen Anlage ankommen soll — als Austausch genau eines Textstücks.
    /// </summary>
    /// <param name="EntityId">Der Rechenwert, der das Textstück enthält.</param>
    /// <param name="Name">Sein Titel in Home Assistant (so heißt der Eintrag bei Neuanlage durch den Fork).</param>
    /// <param name="Alt">Das Textstück, wie es in der älteren Vorlage steht.</param>
    /// <param name="Neu">Das Textstück, das an seine Stelle tritt.</param>
    /// <param name="Merker">
    /// Fork AI (forkai.234): Ein Helfer, den das neue Textstück liest und der vorher stimmen muss.
    /// Vor dem Tausch wird geprüft, dass er da ist, und er wird auf den jetzigen Zustand des
    /// Rechenwerts gesetzt — sonst rechnete der Rechenwert gleich nach dem Tausch mit einem leeren
    /// Merker und schaltete im Haltebereich um.
    /// </param>
    /// <param name="Abgleich">
    /// Die Automation, die den Merker führt. Läuft sie nicht, friert der Merker ein und hielte den
    /// Rechenwert im Haltebereich falsch — dann wird nicht getauscht.
    /// </param>
    public sealed record Nachzug(string EntityId, string Name, string Alt, string Neu, string? Merker = null, string? Abgleich = null)
    {
        /// <summary>Die Entity-ID in diesem Namensraum (im Bestand die alte Kennung; sonst unverändert).</summary>
        public string Kennung => Kennungen.Id(EntityId);
    }

    /// <summary>
    /// Die Änderungen, die vorhandene Rechenwerte nachgezogen bekommen.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum das keine Ausnahme von „Vorhandenes wird nie überschrieben" ist.</b>
    /// Getauscht wird ein einzelnes Textstück, nicht die Vorlage: alles andere im
    /// Rechenwert — auch Abweichungen des Nutzers wie die geglättete Differenz — bleibt
    /// stehen. Ist das alte Stück nicht genau so da, passiert nichts. Und das neue Stück
    /// rechnet mit der Vorgabe 0,5 weiter, wenn der Helfer fehlt, verändert das Verhalten
    /// also nicht.</para>
    /// </remarks>
    public static readonly IReadOnlyList<Nachzug> Nachzuege = new[]
    {
        // Fork AI (forkai.234, A-024): Der Haltebereich rechnet mit dem Merker statt mit this.state.
        // ERST dieser Nachzug, dann der Abschalt-Abstand: Jeder Tausch lädt den Rechenwert neu, und ein
        // Rechenwert ohne Merker fiele dabei im Haltebereich auf "aus" (this.state ist nach dem Neuladen
        // "unavailable"). Mit dem Merker zuerst trägt schon der zweite Tausch den Zustand über das Neuladen.
        new Nachzug(
            "binary_sensor.zuluft_bedarf",
            "Zuluft Bedarf",
            "{% set prev = (this.state == 'on') if this is defined else false %}",
            "{% set m = states('input_boolean.zuluft_bedarf_merker') %}{% set prev = (m == 'on') if m in ['on','off'] else ((this.state == 'on') if this is defined else false) %}",
            Merker: ZuluftSteuerungService.Entitaeten.BedarfMerker,
            Abgleich: ZuluftSteuerungService.Entitaeten.MerkerAbgleich),
        // Fork AI (A-025): Derselbe Rückfall im Kühlbedarf des Wasserkühlers. Der Titel ist der, unter dem der Fork den
        // Eintrag anlegt; eine handgebaute Fassung mit anderem Titel oder anderem Wortlaut bleibt unberührt.
        new Nachzug(
            "binary_sensor.chiller_kuhlbedarf",
            "Chiller Kuhlbedarf",
            "{% set prev = (this.state == 'on') if this is defined else false %}",
            "{% set m = states('input_boolean.chiller_kuhlbedarf_merker') %}{% set prev = (m == 'on') if m in ['on','off'] else ((this.state == 'on') if this is defined else false) %}",
            Merker: ChillerSteuerungService.Entitaeten.KuehlbedarfMerker,
            Abgleich: ChillerSteuerungService.Entitaeten.MerkerAbgleich),
        new Nachzug(
            "binary_sensor.zuluft_bedarf",
            "Zuluft Bedarf",
            "d < s - 0.5 or",
            "d < s - (states('input_number.zuluft_abschalt_abstand') | float(0.5)) or"),
        // Cultivate Control (2.0.0-cc.255): Die Höchsttemperatur gleitet nach Licht-aus vom Tag- auf den Nachtwert. Die Entity-ID
        // und der Titel stehen im neuen Namensraum da; im Bestand (Kennungen.Id/Name) werden sie zu den alten. Fehlen die Helfer
        // (Dauer, Zeitstempel, Merker-Automation), rechnet das neue Stück mit seinen Vorgaben weiter und verhält sich wie das alte.
        new Nachzug(
            "sensor.fork_entfeuchter_temp_max_aktiv",
            "Fork Entfeuchter Temp Max aktiv",
            NachtUebergang.Alt,
            NachtUebergang.Neu),
    };

    /// <summary>Wie es einem Nachzug ergangen ist.</summary>
    /// <param name="Stand">
    /// <c>angepasst</c>, <c>aktuell</c> (schon neu), <c>nicht_gefunden</c> (Eintrag fehlt oder anders benannt),
    /// <c>abweichend</c> (altes Textstück steht nicht darin), <c>wartet</c> (Merker fehlt oder Zustand unklar — später noch einmal) oder <c>fehler</c>.
    /// </param>
    public sealed record NachzugErgebnis(string EntityId, string Stand, string? Hinweis);

    public sealed record Bilanz(
        bool Erreichbar,
        int Angelegt,
        int Uebersprungen,
        int Fehlgeschlagen,
        IReadOnlyList<string> OhneGeraet,
        IReadOnlyList<Ergebnis> Einzeln,
        IReadOnlyList<NachzugErgebnis>? Nachgezogen = null);

    /// <summary>Die fehlenden Rechenwerte anlegen.</summary>
    /// <param name="zuordnung">Rollen-Schlüssel auf Entitäts-Id.</param>
    public async Task<Bilanz> AnlegenAsync(
        string modul,
        IReadOnlyDictionary<string, string> zuordnung,
        HomeAssistantSettings settings,
        CancellationToken ct = default)
    {
        var belegt = zuordnung
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var vorhanden = (await _ha.GetEntitiesAsync(settings, ct))
            .Select(e => e.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (vorhanden.Count == 0)
        {
            return new Bilanz(false, 0, 0, 0, Array.Empty<string>(), Array.Empty<Ergebnis>());
        }

        var rechenwerte = SteuerungBauteile.Anwendbar(modul, belegt)
            .Where(b => b.Art is BauteilArt.RechenSensor or BauteilArt.RechenSchalter or BauteilArt.Mittelwert)
            .ToList();

        var uebersprungen = rechenwerte.Count(b => vorhanden.Contains(b.EntityId));
        var offen = rechenwerte.Where(b => !vorhanden.Contains(b.EntityId)).ToList();

        var ohneGeraet = new List<string>();
        var einzeln = new List<Ergebnis>();
        // Legt Helfer über den Einrichtungsdialog an — ein Schreiben: die lange Frist.
        using var client = _ha.CreateClient(settings, _ha.Dienstfrist);

        foreach (var b in offen)
        {
            var vorschrift = b.Vorlage is null ? null : SteuerungBauteile.VorlageFuellen(b.Vorlage, zuordnung);
            if (vorschrift is null)
            {
                // Ein Rechenwert mit stehendem Platzhalter wird nicht ungueltig -
                // er rechnet stumm mit einem Ausweichwert weiter. Lieber nicht.
                ohneGeraet.Add(b.Name);
                continue;
            }

            // Dieselbe Ersetzung wie oben: steht in der Verfügbarkeit ein
            // Platzhalter ohne Gerät, wird der Rechenwert ohne sie angelegt —
            // sonst scheitert der Dialog an einer Vorschrift mit [[…]] darin.
            var verfuegbarkeit = b.Verfuegbarkeit is null
                ? null
                : SteuerungBauteile.VorlageFuellen(b.Verfuegbarkeit, zuordnung);

            // Fork AI (forkai.150): Der Mittelwert ist ein Filter-Helfer mit eigenem
            // Dialog. Seine „Vorschrift" ist der Fühler, über den gemittelt wird.
            var (erfolg, fehler) = b.Art == BauteilArt.Mittelwert
                ? await SteuerungMittelwertService.AnlegenAsync(client, b.Name, vorschrift, StandardMittelMinuten, ct)
                : await AnlegenAsync(client, b, vorschrift, verfuegbarkeit, ct);
            einzeln.Add(new Ergebnis(b.EntityId, b.Name, erfolg, fehler));

            if (erfolg)
            {
                _log.LogInformation("Rechenwert angelegt: {EntityId}", b.EntityId);
            }
            else
            {
                _log.LogWarning("Rechenwert {EntityId} nicht angelegt: {Fehler}", b.EntityId, fehler);
                break; // Ein halb offener Dialog bleibt sonst stehen.
            }
        }

        return new Bilanz(
            true,
            einzeln.Count(e => e.Angelegt),
            uebersprungen,
            einzeln.Count(e => !e.Angelegt),
            ohneGeraet,
            einzeln);
    }

    /// <summary>
    /// Fork AI (forkai.225): Zieht vorhandene Rechenwerte nach, siehe <see cref="Nachzuege"/>.
    /// </summary>
    /// <remarks>
    /// Ändert den Eintrag über denselben Optionsdialog wie „Konfigurieren" in der
    /// Oberfläche. Zuerst wird der Dialog geöffnet und aus ihm gelesen, was gerade
    /// eingestellt ist; zurückgeschickt wird alles davon, nur das Textstück ist ersetzt.
    /// So gehen Geräteklasse, Verfügbarkeit und Verzögerungen nicht verloren.
    /// </remarks>
    public async Task<IReadOnlyList<NachzugErgebnis>> NachziehenAsync(
        string modul, HomeAssistantSettings settings, CancellationToken ct = default)
    {
        var ergebnisse = new List<NachzugErgebnis>();
        var gehoert = SteuerungBauteile.FuerModul(modul).Select(b => b.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Die Nachzüge nennen Kennung und Titel im neuen Namensraum; im Bestand heißen sie anders (Kennungen).
        var offen = Nachzuege.Where(n => gehoert.Contains(Kennungen.Id(n.EntityId))).ToList();
        if (offen.Count == 0) return ergebnisse;

        using var client = _ha.CreateClient(settings, _ha.Dienstfrist);
        JsonElement eintraege;
        try
        {
            using var antwort = await client.GetAsync("api/config/config_entries/entry?domain=template", ct);
            if (!antwort.IsSuccessStatusCode)
            {
                return offen.Select(n => new NachzugErgebnis(n.Kennung, "fehler", $"Eintragsliste nicht lesbar ({(int)antwort.StatusCode}).")).ToList();
            }

            eintraege = await antwort.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return offen.Select(n => new NachzugErgebnis(n.Kennung, "fehler", ex.Message)).ToList();
        }

        foreach (var n in offen)
        {
            var eintrag = eintraege.ValueKind == JsonValueKind.Array
                ? eintraege.EnumerateArray().FirstOrDefault(e => Text(e, "title") == Kennungen.Name(n.EntityId, n.Name, Kennungen.Raum))
                : default;
            var kennung = eintrag.ValueKind == JsonValueKind.Object ? Text(eintrag, "entry_id") : null;
            if (kennung is null)
            {
                ergebnisse.Add(new NachzugErgebnis(Kennungen.Id(n.EntityId), "nicht_gefunden", $"Kein Eintrag mit dem Titel '{Kennungen.Name(n.EntityId, n.Name, Kennungen.Raum)}'."));
                continue;
            }

            ergebnisse.Add(await NachziehenAsync(client, n, kennung, settings, ct));
        }

        return ergebnisse;
    }

    private async Task<NachzugErgebnis> NachziehenAsync(
        HttpClient client, Nachzug n, string eintrag, HomeAssistantSettings settings, CancellationToken ct)
    {
        string? dialog = null;
        try
        {
            var start = await client.PostAsJsonAsync("api/config/config_entries/options/flow", new { handler = eintrag }, ct);
            if (!start.IsSuccessStatusCode)
            {
                return new NachzugErgebnis(n.Kennung, "fehler", $"Optionsdialog liess sich nicht oeffnen ({(int)start.StatusCode}).");
            }

            var formular = await start.Content.ReadFromJsonAsync<JsonElement>(ct);
            dialog = Text(formular, "flow_id");
            var felder = NachzugFelder(formular, n, out var stand);
            if (felder is null)
            {
                await SchliessenAsync(client, "api/config/config_entries/options/flow", dialog, ct);
                return new NachzugErgebnis(n.Kennung, stand, stand == "aktuell" ? null : "Das alte Textstück steht nicht in der Vorlage.");
            }

            // Fork AI (forkai.234): Liest das neue Textstück einen Merker, muss er da sein und stimmen,
            // BEVOR der Rechenwert neu geladen wird. Der Dialog wird dafür wieder geschlossen und
            // beim nächsten Lauf neu geöffnet.
            if (n.Merker is not null)
            {
                var (bereit, hinweis) = await MerkerBereitMachenAsync(settings, n, ct);
                if (!bereit)
                {
                    await SchliessenAsync(client, "api/config/config_entries/options/flow", dialog, ct);
                    _log.LogWarning("Rechenwert {EntityId} nicht nachgezogen, weil er wartet: {Hinweis}", n.Kennung, hinweis);
                    return new NachzugErgebnis(n.Kennung, "wartet", hinweis);
                }
            }

            var abgeschickt = await client.PostAsJsonAsync($"api/config/config_entries/options/flow/{dialog}", felder, ct);
            var antwort = await abgeschickt.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!abgeschickt.IsSuccessStatusCode || Text(antwort, "type") != "create_entry")
            {
                await SchliessenAsync(client, "api/config/config_entries/options/flow", dialog, ct);
                var grund = antwort.ValueKind == JsonValueKind.Object && antwort.TryGetProperty("errors", out var f) ? f.ToString() : Text(antwort, "type");
                return new NachzugErgebnis(n.Kennung, "fehler", $"Abgelehnt: {grund}");
            }

            _log.LogInformation("Rechenwert nachgezogen: {EntityId}", n.Kennung);
            return new NachzugErgebnis(n.Kennung, "angepasst", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (dialog is not null) await SchliessenAsync(client, "api/config/config_entries/options/flow", dialog, ct);
            return new NachzugErgebnis(n.Kennung, "fehler", ex.Message);
        }
    }

    /// <summary>Was vor einem Nachzug mit Merker zu tun ist.</summary>
    public enum MerkerSchritt
    {
        /// <summary>Merker fehlt oder ist nicht lesbar — erst die Helfer anlegen.</summary>
        MerkerFehlt,

        /// <summary>Der Rechenwert ist gerade nicht an oder aus — es gibt keinen Zustand zu übernehmen.</summary>
        ZustandUnklar,

        /// <summary>Merker und Rechenwert sagen schon dasselbe.</summary>
        Passt,

        SetzeAn,
        SetzeAus,
    }

    /// <summary>
    /// Wie der Merker vor dem Tausch zum Rechenwert gebracht wird. Rein, damit es ohne Home Assistant
    /// prüfbar ist.
    /// </summary>
    /// <param name="rechenwert">Zustand des Rechenwerts (<c>on</c>, <c>off</c>, <c>unavailable</c> …) oder null.</param>
    /// <param name="merker">Zustand des Merkers oder null, wenn es ihn nicht gibt.</param>
    public static MerkerSchritt MerkerEntscheiden(string? rechenwert, string? merker)
    {
        var m = merker?.ToLowerInvariant();
        if (m is not ("on" or "off")) return MerkerSchritt.MerkerFehlt;
        var r = rechenwert?.ToLowerInvariant();
        if (r is not ("on" or "off")) return MerkerSchritt.ZustandUnklar;
        if (r == m) return MerkerSchritt.Passt;
        return r == "on" ? MerkerSchritt.SetzeAn : MerkerSchritt.SetzeAus;
    }

    /// <summary>
    /// Fork AI (forkai.234): Setzt den Merker auf den jetzigen Zustand des Rechenwerts und liest nach,
    /// dass er ihn angenommen hat. Erst dann darf das Textstück getauscht werden.
    /// </summary>
    private async Task<(bool Bereit, string? Hinweis)> MerkerBereitMachenAsync(
        HomeAssistantSettings settings, Nachzug n, CancellationToken ct)
    {
        var rechenwert = (await _ha.GetEntityStateAsync(settings, n.Kennung, ct))?.State;
        var merker = (await _ha.GetEntityStateAsync(settings, n.Merker!, ct))?.State;
        var schritt = MerkerEntscheiden(rechenwert, merker);
        if (schritt != MerkerSchritt.MerkerFehlt && n.Abgleich is not null)
        {
            var abgleich = (await _ha.GetEntityStateAsync(settings, n.Abgleich, ct))?.State;
            if (!string.Equals(abgleich, "on", StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Die Automation {n.Abgleich} führt den Merker, läuft aber nicht ({abgleich ?? "fehlt"}) — zuerst die Automationen anlegen und einschalten.");
            }
        }
        switch (schritt)
        {
            case MerkerSchritt.MerkerFehlt:
                return (false, $"Der Merker {n.Merker} fehlt oder ist nicht lesbar (Home Assistant antwortet nicht?) — zuerst die Helfer anlegen.");
            case MerkerSchritt.ZustandUnklar:
                return (false, $"{n.Kennung} ist gerade weder an noch aus ({rechenwert ?? "unbekannt"}) — es gibt keinen Zustand für den Merker. Später noch einmal.");
            case MerkerSchritt.Passt:
                return (true, null);
        }

        var dienst = schritt == MerkerSchritt.SetzeAn ? "turn_on" : "turn_off";
        var domaene = n.Merker!.Split('.', 2)[0];
        if (!await _ha.CallEntityServiceAsync(settings, domaene, dienst, n.Merker!, ct))
        {
            return (false, $"Home Assistant hat {domaene}.{dienst} für {n.Merker} nicht angenommen.");
        }

        var danach = (await _ha.GetEntityStateAsync(settings, n.Merker!, ct))?.State;
        var soll = schritt == MerkerSchritt.SetzeAn ? "on" : "off";
        return string.Equals(danach, soll, StringComparison.OrdinalIgnoreCase)
            ? (true, null)
            : (false, $"Der Merker {n.Merker} steht nach dem Setzen auf {danach ?? "unbekannt"}, nicht auf {soll}.");
    }

    /// <summary>
    /// Die Felder, die an den Optionsdialog zurückgehen: alles, was im Formular steht,
    /// mit dem getauschten Textstück in <c>state</c>. null heißt: nichts zu tun
    /// (<paramref name="stand"/> sagt warum).
    /// </summary>
    public static Dictionary<string, object?>? NachzugFelder(JsonElement formular, Nachzug n, out string stand)
    {
        stand = "abweichend";
        if (formular.ValueKind != JsonValueKind.Object
            || !formular.TryGetProperty("data_schema", out var schema)
            || schema.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var felder = new Dictionary<string, object?>(StringComparer.Ordinal);
        var zusatz = new Dictionary<string, object?>(StringComparer.Ordinal);
        string? state = null;

        void Lesen(JsonElement feld, Dictionary<string, object?> ziel)
        {
            var name = Text(feld, "name");
            if (name is null || !feld.TryGetProperty("description", out var d)
                || !d.TryGetProperty("suggested_value", out var wert)) return;
            ziel[name] = wert.Clone();
        }

        foreach (var feld in schema.EnumerateArray())
        {
            if (Text(feld, "type") == "expandable" && feld.TryGetProperty("schema", out var inner) && inner.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in inner.EnumerateArray()) Lesen(f, zusatz);
                continue;
            }

            Lesen(feld, felder);
        }

        if (felder.TryGetValue("state", out var s) && s is JsonElement { ValueKind: JsonValueKind.String } je) state = je.GetString();
        if (state is null) return null;

        // Die Textstücke nennen Entity-IDs im neuen Namensraum; im Bestand stehen dort die alten.
        var alt = Kennungen.Uebersetzen(n.Alt, Kennungen.Raum);
        var neu = Kennungen.Uebersetzen(n.Neu, Kennungen.Raum);

        if (state.Contains(neu, StringComparison.Ordinal))
        {
            stand = "aktuell";
            return null;
        }

        if (!state.Contains(alt, StringComparison.Ordinal)) return null;

        felder["state"] = state.Replace(alt, neu, StringComparison.Ordinal);
        if (zusatz.Count > 0) felder["additional_options"] = zusatz;
        stand = "angepasst";
        return felder;
    }

    /// <summary>Den dreistufigen Dialog für ein Bauteil durchspielen.</summary>
    private async Task<(bool Erfolg, string? Fehler)> AnlegenAsync(
        HttpClient client, Bauteil b, string vorschrift, string? verfuegbarkeit, CancellationToken ct)
    {
        try
        {
            // 1. Dialog oeffnen. Home Assistant antwortet mit einem Menue der Arten.
            var start = await client.PostAsJsonAsync(
                DialogPfad, new { handler = "template", show_advanced_options = false }, ct);
            if (!start.IsSuccessStatusCode)
            {
                return (false, $"Dialog liess sich nicht oeffnen ({(int)start.StatusCode}).");
            }

            var eroeffnet = await start.Content.ReadFromJsonAsync<JsonElement>(ct);
            var dialog = Text(eroeffnet, "flow_id");
            if (string.IsNullOrWhiteSpace(dialog))
            {
                return (false, "Home Assistant hat keine Dialog-Kennung geliefert.");
            }

            // 2. Art waehlen.
            var schritt = b.Art == BauteilArt.RechenSchalter ? "binary_sensor" : "sensor";
            var gewaehlt = await client.PostAsJsonAsync(
                $"{DialogPfad}/{dialog}", new { next_step_id = schritt }, ct);
            if (!gewaehlt.IsSuccessStatusCode)
            {
                await SchliessenAsync(client, dialog, ct);
                return (false, $"Art '{schritt}' wurde nicht angenommen ({(int)gewaehlt.StatusCode}).");
            }

            // 3. Felder abschicken.
            var abgeschickt = await client.PostAsJsonAsync($"{DialogPfad}/{dialog}", Felder(b, vorschrift, verfuegbarkeit), ct);
            var antwort = await abgeschickt.Content.ReadFromJsonAsync<JsonElement>(ct);

            if (!abgeschickt.IsSuccessStatusCode)
            {
                // Schemafehler kommen als {"errors": {"base": [...]}}, und der
                // Dialog bleibt dabei offen.
                await SchliessenAsync(client, dialog, ct);
                var grund = antwort.ValueKind == JsonValueKind.Object
                    && antwort.TryGetProperty("errors", out var fehlerListe) ? fehlerListe.ToString() : Text(antwort, "message");
                return (false, $"Abgelehnt ({(int)abgeschickt.StatusCode}){(grund is null ? "." : $": {grund}")}");
            }

            // Bei fehlerhaften Feldern antwortet HA mit 200 und einem erneuten
            // Formular statt mit einem Eintrag - das ist kein Erfolg.
            var typ = Text(antwort, "type");
            if (typ != "create_entry")
            {
                var fehler = antwort.TryGetProperty("errors", out var e) ? e.ToString() : typ;
                // Ein erneutes Formular heißt: der Dialog ist noch offen.
                await SchliessenAsync(client, dialog, ct);
                return (false, $"Der Dialog hat nichts angelegt: {fehler}");
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Einen offenen Dialog abbrechen — ein halber bliebe bis zum Neustart stehen.</summary>
    private static Task SchliessenAsync(HttpClient client, string dialog, CancellationToken ct)
        => SchliessenAsync(client, DialogPfad, dialog, ct);

    private static async Task SchliessenAsync(HttpClient client, string pfad, string? dialog, CancellationToken ct)
    {
        if (dialog is null) return;
        try
        {
            using var _ = await client.DeleteAsync($"{pfad}/{dialog}", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Mehr lässt sich nicht tun; gemeldet wird der eigentliche Fehler.
        }
    }

    /// <summary>Die Felder des letzten Dialogschritts.</summary>
    public static IReadOnlyDictionary<string, object?> Felder(Bauteil b, string vorschrift, string? verfuegbarkeit = null)
    {
        var felder = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = b.Name,
            ["state"] = vorschrift,
        };

        if (b.Art == BauteilArt.RechenSensor)
        {
            if (!string.IsNullOrWhiteSpace(b.Einheit)) felder["unit_of_measurement"] = b.Einheit;
            if (!string.IsNullOrWhiteSpace(b.Zustandsklasse)) felder["state_class"] = b.Zustandsklasse;
        }

        // Ohne Verfügbarkeit rechnet ein Rechenwert bei fehlendem Fühler mit dem
        // Vorgabewert weiter, statt sich abzumelden. Bei der Zuluft entstünde so
        // eine Differenz aus 0 °C und 0 %, nach der der Lüfter dann schaltet.
        // Fork AI (forkai.156): Sie steht im Abschnitt „additional_options“
        // (template/config_flow.py, Home Assistant 2026.9). Auf oberster Ebene
        // lehnt der Dialog sie als fremdes Feld ab — und mit ihm den ganzen
        // Rechenwert und alle folgenden.
        if (!string.IsNullOrWhiteSpace(verfuegbarkeit))
        {
            felder["additional_options"] = new Dictionary<string, object?> { ["availability"] = verfuegbarkeit };
        }

        return felder;
    }

    /// <summary>Der Dialogschritt zur Art.</summary>
    public static string Schritt(BauteilArt art) => art switch
    {
        BauteilArt.RechenSchalter => "binary_sensor",
        BauteilArt.RechenSensor => "sensor",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, "Das ist kein Rechenwert."),
    };

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var wert)
           && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;
}
