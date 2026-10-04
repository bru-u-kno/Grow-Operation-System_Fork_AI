import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { V1Badge, V1Card, V1Section } from '../../components/v1'
import { altesKopieren } from '../ki-zugriff/kopieren'
import './mappe.css'
import './ki-assistent.css'

/**
 * Reiter „Verbinden" der Seite KI-Assistent (Fork AI, 04.10.2026).
 *
 * Bru: „Die meisten Endanwender haben keinen API-Schlüssel, sondern den
 * kostenlosen Plan oder ein Abo." Deshalb führt jeder Weg hier über das
 * EIGENE Konto des Bedieners bei Claude oder ChatGPT — Grow OS bekommt keine
 * Zugangsdaten und ruft selbst keine KI auf. Umgekehrt bekommt der Assistent
 * einen Schlüssel von hier (Reiter „Zugriff & Schlüssel").
 *
 * Beschrieben ist nur, was heute geht. Kein Weg wird versprochen, den es noch
 * nicht gibt — eine Anleitung ins Leere ist schlimmer als keine.
 */

type MobileAccess = { slug: string | null }

export function KiVerbinden() {
  const [slug, setSlug] = useState<string | null>(null)

  useEffect(() => {
    const abbruch = new AbortController()
    // Derselbe Weg wie die Handy-Seite: der Supervisor kennt den Namen des
    // Add-ons, raten geht nicht (local_… oder <repo-hash>_…).
    apiFetch<MobileAccess>('/api/system/mobile-access', { signal: abbruch.signal })
      .then((antwort) => { if (!abbruch.signal.aborted) setSlug(antwort.slug) })
      .catch(() => { /* ohne Slug steht im Text ein Platzhalter */ })
    return () => abbruch.abort()
  }, [])

  const addon = slug ?? '<Name des Add-ons aus Einstellungen → Add-ons>'
  const projektText = [
    `Grow OS Fork AI läuft bei mir als Home-Assistant-Add-on „${addon}“.`,
    'Lies und schreibe es über das Werkzeug ha_manage_app im Proxy-Modus auf dieses Add-on.',
    'Gib bei jeder Anfrage die Kopfzeile „Authorization: Bearer gok_…“ mit (mein Schlüssel).',
    'Ruf zuerst GET /api/ki-zugriff/ich auf: „stufen“ ist, was du darfst; bei „rueckfrageBei“ fragst du mich vorher.',
    'Trag ein, was ich dir diktiere, und sag mir danach kurz, was du eingetragen hast.',
  ].join('\n')

  return (
    <div className="kv" data-audit="ki-verbinden">
      <V1Section title="Dein Assistent, dein Konto">
        <V1Card>
          <p className="ab-text">
            Willst du mit einem Assistenten über deine Anlage sprechen oder ihm Messwerte diktieren, nutzt
            er <b>dein eigenes Konto</b> bei Claude oder ChatGPT.
            Grow OS bekommt dafür keine Zugangsdaten und ruft selbst keine KI auf. Es kostet also nichts
            zusätzlich, und einen API-Schlüssel brauchst du nicht.
          </p>
          <p className="ab-text">
            Umgekehrt bekommt dein Assistent einen <b>Schlüssel von hier</b>. Was er damit <b>in Grow OS</b> darf
            — eintragen, planen, schalten — legst du unter <Link to="/ki?tab=zugriff">Zugriff &amp; Schlüssel</Link>{' '}
            fest. Ab Werk ist alles aus, jede Änderung über den Schlüssel steht im Protokoll, und Sperren wirkt
            sofort.
          </p>
        </V1Card>
      </V1Section>

      <V1Section title="Wähle deinen Weg">
        <div className="kv-wege">
          <Weg
            audit="ki-weg-claude-app"
            titel="Claude-App"
            wo="Windows, Mac, Android, iPhone, Browser"
            plan="kostenlos oder Abo"
          >
            <p className="ab-text">
              Die Claude-App spricht über einen <b>Connector</b> mit Home Assistant und darüber mit Grow OS.
              Connectors laufen über die Server von Anthropic — dein Home Assistant muss dafür von außen
              erreichbar sein, etwa über Home Assistant Cloud. Im kostenlosen Plan ist ein eigener Connector
              erlaubt; einer genügt.
            </p>
            <ol className="ab-schritte">
              <li>Unter <Link to="/ki?tab=zugriff">Zugriff &amp; Schlüssel</Link> den Zugriff erlauben und einen
                Schlüssel anlegen, etwa „Claude am Telefon“. <b>Gleich kopieren</b> — er wird nur einmal
                gezeigt und ist weg, sobald du den Reiter wechselst.</li>
              <li>In Home Assistant das Add-on <b>Home Assistant MCP Server</b> installieren, dazu für den Weg
                von außen <b>Webhook Proxy for HA MCP</b> — beide aus dem Repository{' '}
                <code>github.com/homeassistant-ai/ha-mcp</code>.</li>
              <li>In Claude unter <b>Einstellungen → Connectors → Eigenen Connector hinzufügen</b> die Adresse
                eintragen, die der Webhook Proxy in seinem Protokoll nennt.</li>
              <li>In Claude ein Projekt anlegen (etwa „Grow“) und den Text unten in seine Anweisungen kopieren —
                statt <code>gok_…</code> deinen Schlüssel. Ohne Projekt: den Text an den Anfang des Chats.</li>
            </ol>
            <Kopierfeld text={projektText} knopf="Anweisung kopieren" audit="ki-weg-claude-app-text" />
            <p className="ab-text ab-hinweis">
              Der Schlüssel liegt dann in deinem Claude-Konto. Geht etwas schief, sperrst oder löschst du ihn
              hier — das wirkt bei der nächsten Anfrage.
            </p>
            <div className="kv-warnung" role="note" data-audit="ki-weg-claude-app-warnung">
              <strong>Wichtig: Dieser Connector geht an Grow OS vorbei.</strong>
              <p>
                Der Home Assistant MCP Server hat selbst <b>vollen Zugriff auf Home Assistant</b> — Geräte,
                Automationen, Add-ons, Sicherungen, Neustart. Stufen, Höchstwerte und Protokoll von hier gelten
                nur für das, was der Assistent <b>über Grow OS</b> tut, nicht für das, was er über diesen
                Connector direkt in Home Assistant tut.
              </p>
              <p>
                Die Adresse des Webhook Proxy ist geheim wie ein Passwort. Schalte im Add-on <b>Enable OAuth</b>{' '}
                ein: Dann verlangt der Connector eine Anmeldung mit deinem Home-Assistant-Konto, und die Adresse
                allein genügt nicht mehr.
              </p>
            </div>
          </Weg>

          <Weg
            audit="ki-weg-claude-code"
            titel="Claude Code im Heimnetz"
            wo="Rechner im selben Netz wie Home Assistant"
            plan="Abo"
          >
            <p className="ab-text">
              Claude Code verbindet sich direkt mit dem Add-on <b>Grow MCP Fork AI</b> — ohne Umweg über das
              Internet. Der Grow MCP bringt fertige Werkzeuge mit: Lagebericht, Verläufe, Messung eintragen,
              Journal, Dosieren, Licht.
            </p>
            <ol className="ab-schritte">
              <li>Das Add-on <b>Grow MCP Fork AI</b> installieren, starten und „Im Seitenleisten-Menü
                anzeigen“ einschalten.</li>
              <li>Seine Seite öffnen: Dort steht der fertige Befehl mit der richtigen Adresse.</li>
              <li>Im Befehl den Schlüssel hinter <code>Bearer</code> durch deinen <code>gok_…</code>-Schlüssel
                von hier ersetzen. Mit dem Schlüssel des Add-ons liest Claude nur; mit deinem darf es, was du
                freigegeben hast.</li>
            </ol>
            <Kopierfeld
              text={'claude mcp add --transport http grow-os-fork-ai http://<adresse>:5080/mcp --header "Authorization: Bearer gok_…"'}
              knopf="Befehl kopieren"
              audit="ki-weg-claude-code-text"
            />
          </Weg>

          <Weg
            audit="ki-weg-chatgpt"
            titel="ChatGPT"
            wo="Browser"
            plan="Plus oder Pro"
          >
            <p className="ab-text">
              ChatGPT bindet eigene Connectors nur im <b>Entwicklermodus</b> ein (Einstellungen → Apps →
              Erweitert). Der Weg ist derselbe wie bei der Claude-App: über den Home Assistant MCP Server mit
              dem Webhook Proxy, und dieselbe Anweisung. Erprobt ist er mit ChatGPT noch nicht. Schreibende
              Aktionen lässt ChatGPT dich jedes Mal einzeln bestätigen.
            </p>
            <p className="ab-text">
              Ohne Entwicklermodus geht mit ChatGPT die <Link to="/ki?tab=mappe">Mappe</Link>.
            </p>
          </Weg>

          <Weg
            audit="ki-weg-mappe"
            titel="Ohne Verbindung: die Mappe"
            wo="jeder Chat"
            plan="jeder Plan"
          >
            <p className="ab-text">
              Die Mappe packt den Stand deines Grows und das Fachwissen von Grow OS in eine Datei, die du in
              jeden Chat hochlädst — auch im kostenlosen Plan. Sie hält den Stand von jetzt fest; eintragen oder
              schalten kann der Assistent damit nicht.
            </p>
            <div className="v1-action-row">
              <Link className="v1-button" to="/ki?tab=mappe">Zur Mappe</Link>
            </div>
          </Weg>
        </div>
      </V1Section>

      <V1Section title="Was ein Grow-OS-Schlüssel nie kann">
        <V1Card>
          <p className="ab-text">
            Das gilt für alles, was über Grow OS läuft — auch über den Grow MCP Fork AI. Ein Connector, der
            selbst an Home Assistant geht (wie der Home Assistant MCP Server), hat eigene Rechte.
          </p>
          <ul className="ab-schritte">
            <li>Schlüssel anlegen, ändern oder löschen — das geht nur hier in der Oberfläche.</li>
            <li>Eine Sicherung zurückspielen oder herunterladen.</li>
            <li>In Home Assistant: Neustart, Add-ons, Sicherungen, Schlösser, Alarmanlage, Benachrichtigungen
              und Updates. Was nicht ausdrücklich als Gerät erlaubt ist, geht nie. Ausnahme mit der Stufe
              Verwaltung: Er kann Automationen, Skripte und Szenen auslösen — und die tun, was sie in Home
              Assistant dürfen.</li>
            <li>Mehr dosieren oder öfter schalten als die Höchstwerte unter <Link to="/ki?tab=zugriff">Zugriff
              &amp; Schlüssel</Link> erlauben.</li>
          </ul>
        </V1Card>
      </V1Section>
    </div>
  )
}

function Weg({ titel, wo, plan, audit, children }: { titel: string; wo: string; plan: string; audit: string; children: ReactNode }) {
  return (
    <V1Card className="kv-weg">
      <div className="kv-weg-kopf" data-audit={audit}>
        <h3>{titel}</h3>
        <V1Badge>{plan}</V1Badge>
      </div>
      <p className="kv-wo">{wo}</p>
      {children}
    </V1Card>
  )
}

/** Ein Text zum Abschreiben — mit Knopf, und lesbar auch dann, wenn die Zwischenablage gesperrt ist. */
function Kopierfeld({ text, knopf, audit }: { text: string; knopf: string; audit: string }) {
  const [kopiert, setKopiert] = useState<'ja' | 'nein' | null>(null)

  async function kopieren() {
    try {
      if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(text)
      else altesKopieren(text)
      setKopiert('ja')
    } catch {
      // Im Home-Assistant-Rahmen ohne HTTPS fehlt die Zwischenablage oft.
      try { altesKopieren(text); setKopiert('ja') } catch { setKopiert('nein') }
    }
  }

  return (
    <div className="kv-kopierfeld">
      <pre className="ab-anweisung" data-audit={audit}>{text}</pre>
      <div className="v1-action-row">
        <button type="button" className="v1-button" onClick={() => void kopieren()}>{kopiert === 'ja' ? 'Kopiert' : knopf}</button>
        {kopiert === 'nein' && <span className="ki-fehler">Kopieren hat nicht geklappt — bitte markieren und von Hand kopieren.</span>}
      </div>
    </div>
  )
}
