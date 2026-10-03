using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using GrowDiary.Web.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>Was die Prüfung eines Schlüssels ergeben hat.</summary>
public enum KiPruefung
{
    Gueltig,
    /// <summary>Unbekannt oder falsch geformt — zählt als Fehlversuch.</summary>
    Ungueltig,
    /// <summary>Bekannt, aber vom Betreiber gesperrt — zählt ebenfalls als Fehlversuch.</summary>
    Gesperrt,
    /// <summary>Der Hauptschalter ist aus. Geprüft wird dann gar nichts.</summary>
    ZugriffAus,
    /// <summary>Diese Adresse hat zu oft falsch geraten und wird ohne Prüfung abgewiesen.</summary>
    ZuVieleVersuche,
}

/// <param name="SperreBegonnen">Dieser Fehlversuch hat die Sperre der Adresse ausgelöst — einmal ins Prüfprotokoll.</param>
/// <param name="GesperrterSchluesselId">
/// Fork AI (A-003, 03.10.2026): Bei <see cref="KiPruefung.Gesperrt"/> der erkannte
/// Schlüssel — damit die Abweisung im Protokoll bei ihm steht. Sonst null.
/// </param>
public sealed record KiPruefErgebnis(KiPruefung Ergebnis, KiZugriffKontext? Kontext = null, bool SperreBegonnen = false, int? GesperrterSchluesselId = null);

/// <summary>Die Einstellungen der Seite „Zugriff für KI-Assistenten".</summary>
/// <param name="RueckfrageAbStufe">Ab welcher Stufe der Assistent nachfragen soll; null = nie.</param>
public sealed record KiZugriffEinstellungen(bool Aktiv, KiStufe? RueckfrageAbStufe, KiHoechstwerteDto Hoechstwerte);

/// <summary>
/// Fork AI (A-003, 03.10.2026): Schlüssel erzeugen, prüfen, Fehlversuche zählen.
/// </summary>
/// <remarks>
/// <para><b>Warum SHA-256 genügt.</b> Ein Schlüssel hat 256 Bit Zufall. Ein
/// langsamer Hash (bcrypt, PBKDF2) schützt schwache Passwörter vor dem
/// Durchprobieren — hier gibt es nichts durchzuprobieren. Verglichen wird mit
/// <see cref="CryptographicOperations.FixedTimeEquals"/>, damit die Antwortzeit
/// nicht verrät, wie viele Zeichen gestimmt haben.</para>
///
/// <para><b>Die Fehlversuch-Sperre liegt im Speicher.</b> Ein Neustart setzt
/// sie zurück; das ist hinnehmbar, denn wer raten will, braucht 2^256 Versuche
/// und nicht zehn. Sie ist eine Bremse gegen ein durchdrehendes Add-on, keine
/// Mauer.</para>
///
/// <para>Singleton: die Zähler müssen über alle Anfragen hinweg gelten.</para>
/// </remarks>
public sealed class KiZugriffDienst
{
    public const string Vorsilbe = "gok_";
    public const int ZufallsBytes = 32;
    /// <summary>32 Bytes in base64url ohne Auffüllung.</summary>
    public const int KlartextLaengeOhneVorsilbe = 43;
    public const int PraefixLaenge = 8;
    public const int NameHoechstLaenge = 60;

    public const int FehlversucheBisSperre = 10;
    public static readonly TimeSpan FehlversuchFenster = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SperrDauer = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SchaltFenster = TimeSpan.FromHours(1);

    // Schlüssel in AppSettings — so im Bauplan (docs/ki-zugriff.md).
    public const string EinstellungAktiv = "ki-zugriff.aktiv";
    public const string EinstellungRueckfrage = "ki-zugriff.rueckfrage-ab-stufe";
    public const string EinstellungMaxDosis = "ki-zugriff.max-dosis-ml";
    public const string EinstellungMaxSchaltbefehle = "ki-zugriff.max-schaltbefehle-je-stunde";

    // Vorbelegung: aus, Rückfrage ab GrowPlanen, 10 ml, 20 Befehle je Stunde.
    public const bool VorgabeAktiv = false;
    public const KiStufe VorgabeRueckfrage = KiStufe.GrowPlanen;
    public const double VorgabeMaxDosisMl = 10;
    public const int VorgabeMaxSchaltbefehle = 20;

    /// <summary>Vorbelegung eines neuen Schlüssels, wenn keine Stufe angegeben ist.</summary>
    public const KiStufe VorgabeStufen = KiStufe.Dokumentieren;

    private readonly KiSchluesselRepository _schluessel;
    private readonly AppSettingsRepository _einstellungen;
    private readonly TimeProvider _zeit;

    private readonly object _fehlversucheSperre = new();
    private readonly Dictionary<string, Fehlversuche> _fehlversuche = new(StringComparer.Ordinal);

    private readonly object _schaltSperre = new();
    private readonly Queue<DateTimeOffset> _schaltbefehle = new();

    public KiZugriffDienst(KiSchluesselRepository schluessel, AppSettingsRepository einstellungen, TimeProvider? zeit = null)
    {
        _schluessel = schluessel;
        _einstellungen = einstellungen;
        _zeit = zeit ?? TimeProvider.System;
    }

    // ------------------------------------------------------------- Stufen

    /// <summary>Die Stufen, wie sie über die Leitung gehen — ohne „Keine".</summary>
    public static IReadOnlyList<KiStufe> AlleStufen { get; } =
        Enum.GetValues<KiStufe>().Where(s => s != KiStufe.Keine).ToArray();

    /// <summary>So steht die Stufe in einer Meldung an den Menschen.</summary>
    public static string Anzeigename(KiStufe stufe) => stufe switch
    {
        KiStufe.Dokumentieren => "Dokumentieren",
        KiStufe.GrowPlanen => "Grow planen",
        KiStufe.GeraeteSchalten => "Geräte schalten",
        KiStufe.Verwaltung => "Verwaltung",
        _ => string.Join(", ", AlleStufen.Where(s => (stufe & s) == s).Select(Anzeigename)),
    };

    public static IReadOnlyList<string> StufenNamen(KiStufe stufen)
        => AlleStufen.Where(s => (stufen & s) == s).Select(s => s.ToString()).ToArray();

    /// <summary>Namen in Stufen übersetzen. Unbekannte Namen landen in <paramref name="unbekannt"/>.</summary>
    /// <remarks>Bewusst kein <c>Enum.TryParse</c>: das nähme auch „15" oder „Keine" an.</remarks>
    public static KiStufe StufenLesen(IEnumerable<string?>? namen, out IReadOnlyList<string> unbekannt)
    {
        var stufen = KiStufe.Keine;
        var fehler = new List<string>();
        foreach (var name in namen ?? [])
        {
            if (EinzelneStufe(name) is { } stufe) stufen |= stufe;
            else fehler.Add(name ?? "(leer)");
        }
        unbekannt = fehler;
        return stufen;
    }

    public static KiStufe? EinzelneStufe(string? name)
        => AlleStufen.Cast<KiStufe?>().FirstOrDefault(s => string.Equals(s.ToString(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------- Schlüssel

    /// <summary>Ein neuer Klartext: <c>gok_</c> + 32 Zufallsbytes in base64url.</summary>
    public static string NeuerKlartext()
    {
        var zufall = RandomNumberGenerator.GetBytes(ZufallsBytes);
        var base64 = Convert.ToBase64String(zufall).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Vorsilbe + base64;
    }

    /// <summary>SHA-256 des ganzen Klartexts, hexadezimal klein.</summary>
    public static string Hash(string klartext)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(klartext))).ToLowerInvariant();

    /// <summary>Hat der Text die Form eines Schlüssels? Sonst wird gar nicht erst nachgeschlagen.</summary>
    public static bool HatSchluesselForm(string? klartext)
        => klartext is { Length: 4 + KlartextLaengeOhneVorsilbe }
           && klartext.StartsWith(Vorsilbe, StringComparison.Ordinal)
           && klartext.AsSpan(Vorsilbe.Length).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;

    public static string Praefix(string klartext)
        => klartext.Substring(Vorsilbe.Length, Math.Min(PraefixLaenge, Math.Max(0, klartext.Length - Vorsilbe.Length)));

    /// <summary>
    /// Der Schlüssel aus <c>Authorization: Bearer gok_…</c> — oder null.
    /// </summary>
    /// <remarks>
    /// Nur ein Bearer mit <c>gok_</c> zählt als Schlüssel. Ein anderes Token
    /// (etwa eines, das Home Assistant setzt) geht den Weg von heute.
    /// </remarks>
    public static string? SchluesselAusKopf(HttpRequest request)
    {
        var kopf = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!kopf.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) return null;
        var wert = kopf[bearer.Length..].Trim();
        return wert.StartsWith(Vorsilbe, StringComparison.Ordinal) ? wert : null;
    }

    public (KiSchluessel Schluessel, string Klartext) Anlegen(string name, KiStufe stufen)
    {
        var klartext = NeuerKlartext();
        var id = _schluessel.Anlegen(name.Trim(), Praefix(klartext), Hash(klartext), stufen, Jetzt.UtcDateTime);
        return (_schluessel.Hole(id)!, klartext);
    }

    public IReadOnlyList<KiSchluessel> Alle() => _schluessel.Alle();
    public KiSchluessel? Hole(int id) => _schluessel.Hole(id);
    public bool Aendern(int id, string name, KiStufe stufen) => _schluessel.Aendern(id, name.Trim(), stufen);
    public bool Sperren(int id) => _schluessel.Sperren(id, Jetzt.UtcDateTime);
    public bool Loeschen(int id) => _schluessel.Loeschen(id);
    public void ZuletztGenutzt(int id) => _schluessel.ZuletztGenutzt(id, Jetzt.UtcDateTime);

    public static KiSchluesselDto ZuDto(KiSchluessel s)
        => new(s.Id, s.Name, s.Praefix, StufenNamen(s.Stufen), s.ErstelltAmUtc, s.ZuletztGenutztAmUtc, s.GesperrtAmUtc);

    // -------------------------------------------------------------- Prüfen

    /// <summary>
    /// Prüft einen Schlüssel aus einer Anfrage von <paramref name="ip"/>.
    /// </summary>
    /// <remarks>
    /// Reihenfolge: gesperrte Adresse (ohne Prüfung), Hauptschalter (ohne
    /// Prüfung — sonst wäre der ausgeschaltete Zugang ein Rate-Orakel), dann
    /// Form, Präfix, Hash, Sperre des Schlüssels.
    /// </remarks>
    public KiPruefErgebnis Pruefen(string? klartext, IPAddress? ip)
    {
        var adresse = AdressSchluessel(ip);
        if (IstAdresseGesperrt(adresse)) return new(KiPruefung.ZuVieleVersuche);

        var einstellungen = Einstellungen();
        if (!einstellungen.Aktiv) return new(KiPruefung.ZugriffAus);

        if (!HatSchluesselForm(klartext))
        {
            return new(KiPruefung.Ungueltig, SperreBegonnen: FehlversuchZaehlen(adresse));
        }

        var berechnet = SHA256.HashData(Encoding.UTF8.GetBytes(klartext!));
        KiSchluessel? treffer = null;
        foreach (var kandidat in _schluessel.MitPraefix(Praefix(klartext!)))
        {
            if (HashGleich(kandidat.Hash, berechnet)) treffer = kandidat;
        }

        if (treffer is null)
        {
            return new(KiPruefung.Ungueltig, SperreBegonnen: FehlversuchZaehlen(adresse));
        }

        if (treffer.Gesperrt)
        {
            return new(KiPruefung.Gesperrt, SperreBegonnen: FehlversuchZaehlen(adresse), GesperrterSchluesselId: treffer.Id);
        }

        return new(KiPruefung.Gueltig, new KiZugriffKontext(treffer.Id, treffer.Name, treffer.Stufen, einstellungen.Hoechstwerte));
    }

    private static bool HashGleich(string gespeichertHex, byte[] berechnet)
    {
        byte[] gespeichert;
        try
        {
            gespeichert = Convert.FromHexString(gespeichertHex);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(gespeichert, berechnet);
    }

    private static string AdressSchluessel(IPAddress? ip)
    {
        if (ip is null) return "unbekannt";
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    private DateTimeOffset Jetzt => _zeit.GetUtcNow();

    // ------------------------------------------------------ Fehlversuche

    private sealed class Fehlversuche
    {
        public Queue<DateTimeOffset> Zeitpunkte { get; } = new();
        public DateTimeOffset? GesperrtBis { get; set; }
    }

    /// <summary>Ist diese Adresse gerade gesperrt?</summary>
    public bool IstAdresseGesperrt(IPAddress? ip) => IstAdresseGesperrt(AdressSchluessel(ip));

    private bool IstAdresseGesperrt(string adresse)
    {
        lock (_fehlversucheSperre)
        {
            if (!_fehlversuche.TryGetValue(adresse, out var eintrag) || eintrag.GesperrtBis is not { } bis) return false;
            if (Jetzt < bis) return true;

            // Abgelaufen: frisch anfangen, sonst sperrte der nächste Fehlversuch sofort wieder.
            _fehlversuche.Remove(adresse);
            return false;
        }
    }

    /// <summary>Einen Fehlversuch zählen. True, wenn genau dieser die Sperre auslöst.</summary>
    private bool FehlversuchZaehlen(string adresse)
    {
        lock (_fehlversucheSperre)
        {
            var jetzt = Jetzt;
            AufraeumenWennNoetig(jetzt);

            if (!_fehlversuche.TryGetValue(adresse, out var eintrag))
            {
                eintrag = new Fehlversuche();
                _fehlversuche[adresse] = eintrag;
            }

            while (eintrag.Zeitpunkte.Count > 0 && jetzt - eintrag.Zeitpunkte.Peek() > FehlversuchFenster)
            {
                eintrag.Zeitpunkte.Dequeue();
            }
            eintrag.Zeitpunkte.Enqueue(jetzt);

            if (eintrag.GesperrtBis is null && eintrag.Zeitpunkte.Count >= FehlversucheBisSperre)
            {
                eintrag.GesperrtBis = jetzt + SperrDauer;
                return true;
            }
            return false;
        }
    }

    /// <summary>Nicht unbegrenzt wachsen: alte Einträge fallen weg, wenn viele Adressen auftauchen.</summary>
    private void AufraeumenWennNoetig(DateTimeOffset jetzt)
    {
        if (_fehlversuche.Count < 1000) return;
        foreach (var (adresse, eintrag) in _fehlversuche.ToArray())
        {
            var gesperrt = eintrag.GesperrtBis is { } bis && jetzt < bis;
            var juengster = eintrag.Zeitpunkte.LastOrDefault();
            if (!gesperrt && jetzt - juengster > FehlversuchFenster) _fehlversuche.Remove(adresse);
        }
    }

    // ---------------------------------------------------- Schaltbefehle

    /// <summary>
    /// Einen Schalt- oder Dosierbefehl zulassen und zählen — oder ablehnen, wenn
    /// in der letzten Stunde schon <paramref name="hoechstens"/> liefen.
    /// </summary>
    /// <remarks>Ein gleitendes Fenster über alle Schlüssel zusammen (Bauplan, „Höchstwerte").</remarks>
    public bool SchaltbefehlZulassen(int hoechstens)
    {
        lock (_schaltSperre)
        {
            var jetzt = Jetzt;
            while (_schaltbefehle.Count > 0 && jetzt - _schaltbefehle.Peek() >= SchaltFenster)
            {
                _schaltbefehle.Dequeue();
            }

            if (_schaltbefehle.Count >= hoechstens) return false;
            _schaltbefehle.Enqueue(jetzt);
            return true;
        }
    }

    // ------------------------------------------------------ Einstellungen

    public KiZugriffEinstellungen Einstellungen()
    {
        var aktiv = _einstellungen.GetValue(EinstellungAktiv) is { } a
            ? string.Equals(a, "true", StringComparison.OrdinalIgnoreCase)
            : VorgabeAktiv;

        // Fehlt der Eintrag, gilt die Vorbelegung; ein leerer Eintrag heisst „nie".
        var rueckfrageRoh = _einstellungen.GetValue(EinstellungRueckfrage);
        KiStufe? rueckfrage = rueckfrageRoh is null
            ? VorgabeRueckfrage
            : string.IsNullOrWhiteSpace(rueckfrageRoh) ? null : EinzelneStufe(rueckfrageRoh) ?? VorgabeRueckfrage;

        // Von der App selbst geschrieben, also eine Maschinenzahl (Zahlenlesen, nie selbst umwandeln).
        var maxDosis = GrowDiary.Web.Services.Zahlenlesen.Maschine(_einstellungen.GetValue(EinstellungMaxDosis)) ?? VorgabeMaxDosisMl;
        var maxSchalt = int.TryParse(_einstellungen.GetValue(EinstellungMaxSchaltbefehle), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? s
            : VorgabeMaxSchaltbefehle;

        return new KiZugriffEinstellungen(aktiv, rueckfrage, new KiHoechstwerteDto(maxDosis, maxSchalt));
    }

    public void EinstellungenSpeichern(KiZugriffEinstellungen werte)
    {
        _einstellungen.SetValue(EinstellungAktiv, werte.Aktiv ? "true" : "false");
        _einstellungen.SetValue(EinstellungRueckfrage, werte.RueckfrageAbStufe?.ToString() ?? string.Empty);
        _einstellungen.SetValue(EinstellungMaxDosis, werte.Hoechstwerte.MaxDosisMlJeBefehl.ToString("R", CultureInfo.InvariantCulture));
        _einstellungen.SetValue(EinstellungMaxSchaltbefehle, werte.Hoechstwerte.MaxSchaltbefehleJeStunde.ToString(CultureInfo.InvariantCulture));
    }
}
