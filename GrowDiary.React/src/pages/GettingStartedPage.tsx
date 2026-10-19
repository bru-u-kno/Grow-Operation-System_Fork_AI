import { V1Card, V1LinkButton, V1Page, V1Section } from '../components/v1'

function GettingStartedPage() {
  return (
    <V1Page
      eyebrow="Willkommen"
      title="Erste Schritte"
      subtitle="In drei Schritten startklar — und ein Überblick, wo die mächtigen Funktionen stecken."
    >
      <V1Section title="1. Einrichten">
        <div className="v1-card-grid">
          <V1Card>
            <span className="v1-card-kicker">Schritt 1</span>
            <h2>Sensoren verbinden</h2>
            <p>Cultivate Control ist als Add-on schon mit Home Assistant verbunden. Ordne beim Zelt deine HA-Sensoren aus dem Dropdown zu — pH, EC, Klima, Kamera und mehr. Kein Token, kein Abtippen.</p>
            <V1LinkButton to="/home-assistant" variant="primary">Home Assistant öffnen</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Schritt 2</span>
            <h2>Zelt anlegen</h2>
            <p>Lege dein Grow-Zelt an (Maße, Licht, Abluft). Es ist der Container für Sensoren, Kamera und deine Grows.</p>
            <V1LinkButton to="/zelte" variant="primary">Zelte öffnen</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Schritt 3</span>
            <h2>Grow starten</h2>
            <p>Starte einen Grow (Sorte, Hydro-System, Nährstoff-Programm). Danach laufen Live-Dashboard, Addback und Diagnose automatisch los.</p>
            <V1LinkButton to="/grows/new" variant="primary">Grow starten</V1LinkButton>
          </V1Card>
        </div>
      </V1Section>

      <V1Section title="Funktionen entdecken">
        <div className="v1-card-grid">
          <V1Card>
            <span className="v1-card-kicker">Im Grow · Tab „Automatisierung"</span>
            <h2>Auto-Messung & Snapshots</h2>
            <p>Öffne einen Grow und wechsle zum Tab <strong>Automatisierung</strong>: Messwerte automatisch erfassen — z. B. 30 Min nach Licht-AN — und optional einen Kamera-Snapshot ins Fototagebuch speichern.</p>
            <V1LinkButton to="/grows">Zu den Grows</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Im Grow · Tab „Diagnose"</span>
            <h2>Diagnose & SOP-Vorschläge</h2>
            <p>Cultivate Control erkennt Abweichungen, ordnet sie Symptomen zu und schlägt passende Behandlungen und SOPs vor — direkt im Grow unter <strong>Diagnose</strong>.</p>
            <V1LinkButton to="/grows">Zu den Grows</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Addback</span>
            <h2>Addback-Assistent</h2>
            <p>Nährlösung logisch ergänzen: messen → Ziel → dosieren → nachmessen. Kein blindes Nachkippen.</p>
            <V1LinkButton to="/addback">Addback öffnen</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Wissen</span>
            <h2>Wissensbasis</h2>
            <p>Durchsuchbare SOPs, Behandlungen, Symptome, Pathogene, Zielwerte und Nährstoff-Programme — mit Querverweisen.</p>
            <V1LinkButton to="/wissen">Wissen öffnen</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Sensoren</span>
            <h2>Kalibrier-Erinnerung</h2>
            <p>Pro Sonde ein Kalibrier-Intervall setzen — Cultivate Control erinnert dich rechtzeitig ans Nachkalibrieren.</p>
            <V1LinkButton to="/hardware">Sensoren öffnen</V1LinkButton>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Zelt-Kamera</span>
            <h2>Live-Kamera</h2>
            <p>Jede Home-Assistant-Kamera-Entity als Live-Bild im Dashboard (z. B. USB-Webcam via go2rtc). Beim Zelt-Mapping auswählen und „Kamera testen".</p>
            <V1LinkButton to="/home-assistant">Kamera einrichten</V1LinkButton>
          </V1Card>
        </div>
      </V1Section>
    </V1Page>
  )
}

export default GettingStartedPage
