namespace GrowDiary.Web.Api.Contracts;

/// <summary>Eine Seite des Grow-Tagebuchs: einige Tage, neueste zuerst.</summary>
/// <param name="GrowId">Der Grow.</param>
/// <param name="ZeltId">Das Zelt, aus dem Sensorkurven und Sprünge kommen — null ohne Zelt.</param>
/// <param name="Tage">Die Tage dieser Seite, nur solche mit Einträgen.</param>
/// <param name="AeltereAb">Ab diesem Tag (yyyy-MM-dd) gibt es ältere Einträge — für „Ältere Tage laden"; null = keine.</param>
/// <param name="RohdatenAb">Der älteste Tag mit Rohwerten; davor gibt es nur Tageswerte.</param>
public sealed record TagebuchSeiteDto(
    int GrowId,
    int? ZeltId,
    IReadOnlyList<TagebuchTagDto> Tage,
    string? AeltereAb,
    string? RohdatenAb);

/// <param name="Datum">Der Ortstag, yyyy-MM-dd.</param>
/// <param name="Wochentag">„Sonntag".</param>
/// <param name="Phase">„Blüte · Woche 7 · Tag 47" — null vor dem Start.</param>
/// <param name="Ereignisse">Neueste zuerst.</param>
/// <param name="Auffaellig">Wie viele „Auffällig"-Zeilen der Tag trägt.</param>
public sealed record TagebuchTagDto(
    string Datum,
    string Wochentag,
    string? Phase,
    IReadOnlyList<TagebuchEreignisDto> Ereignisse,
    int Auffaellig);

/// <summary>Ein Ereignis eines Tages. Genau eines der Detailfelder passt zur <c>Art</c>.</summary>
/// <param name="Schluessel">Eindeutig auf der Seite, z. B. „messung-115".</param>
/// <param name="Art">messung · wechsel · addback · dosierung · notiz · meilenstein · foto · verbrauch · auffaellig.</param>
/// <param name="ZeitpunktUtc">Wann.</param>
/// <param name="Uhrzeit">„16:10" in Ortszeit der Anlage — dieselbe Uhr wie die Tagesgrenze.</param>
/// <param name="Minute">Minute des Ortstags (0–1439) — für die Striche in den Kurven.</param>
/// <param name="Titel">Die Überschrift der Zeile.</param>
/// <param name="Wasser">Gehört in den Filter „Wasser" (Wechsel, Nachfüllen, Dosis, Lösungswechsel …).</param>
public sealed record TagebuchEreignisDto(
    string Schluessel,
    string Art,
    DateTime ZeitpunktUtc,
    string Uhrzeit,
    int Minute,
    string Titel,
    bool Wasser,
    TagebuchMessungDto? Messung,
    TagebuchWechselDto? Wechsel,
    TagebuchAddbackDto? Addback,
    TagebuchDosisDto? Dosis,
    TagebuchNotizDto? Notiz,
    TagebuchAuffaelligDto? Auffaellig,
    IReadOnlyList<TagebuchPostenDto> Posten,
    IReadOnlyList<PhotoAssetDto> Fotos);

/// <summary>Die Werte einer Messung oder eines Vorher/Nachher.</summary>
public sealed record TagebuchWerteDto(
    double? Ph,
    double? Ec,
    double? WasserC,
    double? LuftC,
    double? FeuchteProzent,
    double? Co2Ppm,
    double? OrpMv,
    double? SauerstoffMgL,
    double? FuellstandL,
    double? Ppfd);

/// <param name="Id">Die Messung — für „Bearbeiten".</param>
/// <param name="Herkunft">sensor · hand · import.</param>
/// <param name="Loesungswechsel">Die Messung trägt den Haken „Lösungswechsel".</param>
/// <param name="Abgleich">Nur bei Handmessungen: der Sensor zur selben Zeit.</param>
/// <param name="Notiz">Was der Bediener dazugeschrieben hat (Automatik-Vermerke nicht).</param>
public sealed record TagebuchMessungDto(
    int Id,
    string Herkunft,
    bool Loesungswechsel,
    TagebuchWerteDto Werte,
    IReadOnlyList<TagebuchAbgleichDto> Abgleich,
    string? Notiz);

/// <summary>Handwert gegen Sensorwert zur selben Zeit.</summary>
/// <param name="Name">„pH", „EC", „Wasser".</param>
/// <param name="Toleranz">Bis hierher gilt es als „passt".</param>
/// <param name="Passt">|Hand − Sensor| ≤ Toleranz.</param>
public sealed record TagebuchAbgleichDto(
    string Name,
    double Hand,
    double Sensor,
    double Toleranz,
    int Nachkomma,
    bool Passt);

/// <summary>Ein Wasserwechsel mit allem, was dazu eingetragen ist.</summary>
/// <param name="ChangeoutId">Der Satz aus „Wasserwechsel".</param>
/// <param name="Komplett">Komplett- oder Teilwechsel.</param>
/// <param name="Wasser">„Leitungswasser", „Osmosewasser", „Mischung" — null = nicht festgehalten.</param>
/// <param name="MessungId">Die Messung mit dem Lösungswechsel-Haken, die dazugehört.</param>
/// <param name="Notiz">Notiz am Wechsel.</param>
/// <param name="Journal">Der Journaleintrag „Wasserwechsel", der dazugehört.</param>
public sealed record TagebuchWechselDto(
    int ChangeoutId,
    bool Komplett,
    double? Liter,
    double? Prozent,
    string? Wasser,
    double? WasserEc,
    TagebuchWerteDto Vorher,
    TagebuchWerteDto Nachher,
    int? MessungId,
    string? Notiz,
    TagebuchNotizDto? Journal);

public sealed record TagebuchAddbackDto(
    int Id,
    string Art,
    double? LiterDazu,
    double? EcVorher,
    double? EcNachher,
    double? PhVorher,
    double? PhNachher,
    string? Wasser,
    string? Notiz);

/// <param name="Messgroesse">Woran die Pumpe arbeitet: „pH", „EC" — null bei eigenem Mittel.</param>
public sealed record TagebuchDosisDto(
    int Id,
    string Pumpe,
    double Ml,
    string? Messgroesse,
    double? Vorher,
    double? Nachher,
    bool Automatisch);

/// <summary>Ein Journaleintrag — mit allem, was „Bearbeiten" braucht.</summary>
public sealed record TagebuchNotizDto(
    int Id,
    string EntryType,
    string? Titel,
    string? Text,
    DateTime OccurredAtUtc,
    bool Automatisch);

/// <summary>Ein gebuchter Verbrauch: „Aqua Flores A 180 ml".</summary>
public sealed record TagebuchPostenDto(string Name, double Menge, string Einheit);

/// <summary>Ein Sprung ohne passenden Eintrag — „Nachgefüllt?".</summary>
/// <param name="Befunde">Die Sprünge dieser Zeile; mehrere, wenn EC und pH zugleich sprangen. Der erste ist der wichtigste.</param>
public sealed record TagebuchAuffaelligDto(IReadOnlyList<TagebuchSprungDto> Befunde);

/// <param name="Id">Die gemerkte Auffälligkeit — für „War nichts".</param>
/// <param name="Messgroesse">metric key, z. B. reservoir-ec.</param>
/// <param name="Name">„EC".</param>
/// <param name="BeginnUtc">Letzter ruhiger Wert davor.</param>
/// <param name="EndeUtc">Erster ruhiger Wert danach.</param>
/// <param name="BeginnOrtszeit">yyyy-MM-ddTHH:mm in Ortszeit der Anlage — für Formularfelder, die der Server als Ortszeit liest.</param>
/// <param name="Regel">Ab wann gemeldet wird — mit Etikett.</param>
public sealed record TagebuchSprungDto(
    int Id,
    string Messgroesse,
    string Name,
    string? Einheit,
    int Nachkomma,
    double Vorher,
    double Nachher,
    DateTime BeginnUtc,
    DateTime EndeUtc,
    string BeginnUhrzeit,
    string EndeUhrzeit,
    string BeginnOrtszeit,
    string EndeOrtszeit,
    int DauerMinuten,
    string Regel);

/// <summary>Die Sensorkurven eines Tages.</summary>
/// <param name="Datum">yyyy-MM-dd.</param>
/// <param name="Aufloesung">roh · tag · keine — „tag" heißt: nur Min/Median/Max, die Rohwerte sind schon zusammengefasst.</param>
/// <param name="Licht">Wann Licht war, in Minuten des Tages.</param>
/// <param name="LichtQuelle">sensor (geschaltet laut Lichtsensor) · plan (Lichtplan) · keine.</param>
public sealed record TagebuchKurvenDto(
    string Datum,
    string Aufloesung,
    IReadOnlyList<TagebuchKurveDto> Kurven,
    IReadOnlyList<TagebuchSpanneDto> Licht,
    string LichtQuelle);

public sealed record TagebuchKurveDto(
    string Schluessel,
    string Name,
    string? Einheit,
    int Nachkomma,
    double? Min,
    double? Median,
    double? Max,
    IReadOnlyList<TagebuchPunktDto> Punkte);

/// <param name="Minute">Minute des Ortstags.</param>
public sealed record TagebuchPunktDto(int Minute, double Wert);

public sealed record TagebuchSpanneDto(int Von, int Bis);
