using System.IO.Compression;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Api.Controllers;

public sealed partial class SystemApiController
{
    /// <summary>
    /// Läuft dieser Server auf erfundenen Werten?
    /// </summary>
    /// <remarks>
    /// Die Oberfläche fragt das beim Start und zeigt einen Streifen, solange es
    /// so ist. Erfundene Messwerte, die man für echte hält, wären nicht bloss
    /// falsch — an ihnen hängen Alarme und die Dosierung.
    /// </remarks>
    [HttpGet("demo-mode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DemoMode() => Ok(new
    {
        enabled = DemoData.IsEnabled,
        hint = DemoData.IsEnabled
            ? "Testdaten: alle Messwerte sind erfunden. Es geht nichts an ein echtes Gerät — "
              + "was hier gestellt wird, merkt sich nur der Testbestand."
            : null,
    });

    [HttpGet("backend-health")]
    [ProducesResponseType(typeof(BackendHealthDto), StatusCodes.Status200OK)]
    public ActionResult<BackendHealthDto> BackendHealth()
    {
        var tents = _repository.GetTents(includeArchived: true);
        var hydroSetups = _repository.GetHydroSetups(includeArchived: true);
        var grows = _repository.GetAllGrows();

        return Ok(new BackendHealthDto(
            AppName: "Grow OS",
            BackendSchema: "backend-core.v0.18-candidate",
            CheckedAtUtc: DateTime.UtcNow,
            TentCount: tents.Count,
            HydroSetupCount: hydroSetups.Count,
            GrowCount: grows.Count,
            ZeroTentStartupSupported: true,
            BauKennung: Bauzeit.Kennung,
            Capabilities: new[]
            {
                "zero-tent-startup",
                "tent-crud",
                "hydro-setup-dwc-rdwc-only",
                "grow-requires-hydro-setup",
                "hardware-hydro-setup-link",
                "addback-hydro-setup-volume",
                "addback-log",
                "changeout-log",
                "grow-export-v1",
                "local-backup-without-secrets",
                "database-status",
                "backup-validation",
                "api-contract-manifest",
                "grow-export-integrity",
                "grow-export-validation",
                "security-status",
                "local-only-admin-default",
                "ingress-authenticated-access",
                "schema-migration-status",
                "upgrade-preflight-backup",
                "backup-restore-plan",
                "backup-restore-execute",
                "grow-import-plan",
                "grow-import-execute",
                "system-audit-events",
                "uniform-api-error-format",
                "legacy-mvc-endpoint-containment",
                "remote-product-api-guard",
                "schema-migration-plan",
                "safe-migration-engine-foundation",
                "grow-run-snapshots"
            }));
    }


    [HttpGet("release-readiness")]
    [ProducesResponseType(typeof(BackendReleaseReadinessDto), StatusCodes.Status200OK)]
    public ActionResult<BackendReleaseReadinessDto> ReleaseReadiness()
    {
        var checks = new List<ReleaseReadinessCheckDto>
        {
            new("zero_tent_startup", "pass", "Frische Datenbanken starten ohne automatisch gesetzte Zelte."),
            new("tent_aggregate", "pass", "Zelte haben CRUD, Status, technische Details und Sensor-Mappings."),
            new("hydro_setup_aggregate", "pass", "HydroSetups sind DWC/RDWC-only, zeltgebunden und archivierungsfähig."),
            new("grow_hydro_setup_link", "pass", "Neue Grows benötigen ein aktives HydroSetup und übernehmen technische Systemdaten daraus."),
            new("hardware_hydro_setup_link", "pass", "HardwareItems können direkt an HydroSetups gebunden werden."),
            new("addback_volume", "pass", "Addback nutzt zuerst das HydroSetup-Gesamtvolumen und fällt nur für Legacy-Grows zurück."),
            new("operation_logs", "pass", "Addback- und Changeout-Protokolle existieren backendseitig."),
            new("grow_export_v1", "pass", "Grow-Export v1 enthält Grow, Messungen, Zelt-/HydroSetup-Snapshot, Logs und optionale Foto-Metadaten."),
            new("local_backup", "pass", "Lokale Backups schließen HA-Config, DataProtectionKeys, Uploads und Logs aus."),
            new("database_status", "pass", "Das Backend stellt einen Datenbank-Status mit Schema-Version und Pflichttabellen-/Spaltencheck bereit."),
            new("backup_validation", "pass", "Backups können vor einem Restore auf Struktur und ausgeschlossene Secrets geprüft werden."),
            new("api_contract_manifest", "pass", "Das Backend liefert ein maschinenlesbares API-Manifest für Kernbereiche, Endpunkte und Produktregeln."),
            new("export_integrity", "pass", "Grow-Exports enthalten ExportId, SectionCounts und IntegrityHash."),
            new("import_validate", "pass", "Grow-Export-Dateien können serverseitig validiert werden, ohne Daten zu importieren."),
            new("security_guardrails", "pass", "Administrative System-, Settings- und Export-Endpunkte sind nur lokal oder ueber den Home-Assistant-Ingress erreichbar; Home Assistant authentifiziert den Zugriff. Der Ingress-Kopf zaehlt nur, wenn die Anfrage von Home Assistant selbst kommt (172.30.32.1/.2)."),
            new("security_status", "pass", "Das Backend stellt einen Security-Status fuer die local-only/Ingress-Guardrails bereit."),
            new("migration_status", "pass", "Das Backend protokolliert angewendete Schema-Migrationen und zeigt offene Migrationen an."),
            new("upgrade_preflight", "pass", "Vor einem Update kann ein Preflight mit Datenbankstatus, Migrationstatus und validiertem Backup ausgeführt werden."),
            new("restore_plan", "pass", "Backups können als Restore-Dry-Run analysiert werden, ohne Dateien zu überschreiben."),
            new("grow_import_plan", "pass", "Grow-Exports können als Import-Dry-Run analysiert werden, ohne Daten zu schreiben."),
            new("system_audit_events", "pass", "Kritische Backend-Operationen werden in einem System-Audit-Log protokolliert."),
            new("api_error_format", "pass", "API-Fehler verwenden ein einheitliches ApiError-Format mit Code, Message, FieldErrors, Status, TraceId und SchemaVersion."),
            new("legacy_mvc_containment", "pass", "Alte MVC-Backup-/Export-/Kamera-/Mutationsrouten umgehen die neuen Backup-, Export- und Security-Regeln nicht mehr."),
            new("remote_product_api_guard", "pass", "Produkt-APIs sind nur lokal oder ueber den Home-Assistant-Ingress erreichbar; externer Zugriff laeuft ueber Home Assistant (App/Web)."),
            new("restore_api", "pass", "Backups koennen nach Preflight, Safety-Backup und Integritaetscheck kontrolliert wiederhergestellt werden."),
            new("migration_engine_foundation", "pass", "Migrationen besitzen einen maschinenlesbaren Plan, Backup-Pflicht und Destructive-Guardrails als Fundament."),
            new("grow_snapshots", "pass", "Neue Grows speichern unveränderliche Zelt- und HydroSetup-Snapshots für stabile Vergleiche und Exporte."),
            new("migration_engine", "partial", "Schema-Migrationen werden protokolliert; destructive Rollbacks und echte Restore-/Rollback-Automation fehlen noch."),
            new("auth_remote", "pass", "Als Home-Assistant-Add-on laeuft Grow OS hinter dem Ingress; Home Assistant uebernimmt Authentifizierung und Remote-Zugriff."),
            new("grow_import_execute", "pass", "Grow-Exports koennen kontrolliert als neue lokale Vergleichs-Grows importiert werden, ohne bestehende Grows, Zelte oder HydroSetups zu ueberschreiben.")
        };

        var dto = new BackendReleaseReadinessDto(
            Status: "backend.v0.20-ready-not-v1.0",
            BackendSchema: "backend-core.v0.18-candidate",
            CheckedAtUtc: DateTime.UtcNow,
            Checks: checks,
            CompletedFoundations: new[]
            {
                "zero-tent-startup",
                "tent-crud",
                "hydro-setup-dwc-rdwc-only",
                "grow-requires-hydro-setup",
                "hardware-hydro-setup-link",
                "addback-hydro-setup-volume",
                "addback-log",
                "changeout-log",
                "grow-export-v1",
                "local-backup-without-secrets",
                "database-status",
                "backup-validation",
                "api-contract-manifest",
                "grow-export-integrity",
                "grow-export-validation",
                "security-status",
                "local-only-admin-default",
                "ingress-authenticated-access",
                "schema-migration-status",
                "upgrade-preflight-backup",
                "backup-restore-plan",
                "backup-restore-execute",
                "grow-import-plan",
                "grow-import-execute",
                "system-audit-events",
                "uniform-api-error-format",
                "legacy-mvc-endpoint-containment",
                "remote-product-api-guard",
                "schema-migration-plan",
                "safe-migration-engine-foundation",
                "grow-run-snapshots"
            },
            RemainingBeforeV1: new[]
            {
                "destructive-migration-rollback",
                "release-upgrade-test-with-existing-app-data"
            });

        LogSystemAudit("system", "release-readiness-read", "Release-Readiness abgefragt.", true);
        return Ok(dto);
    }


    [HttpGet("api-manifest")]
    [ProducesResponseType(typeof(ApiManifestDto), StatusCodes.Status200OK)]
    public ActionResult<ApiManifestDto> ApiManifest()
    {
        var globalRules = new[]
        {
            "Frische Datenbanken starten ohne automatisch gesetzte Zelte.",
            "Neue Grows benötigen ein aktives HydroSetup.",
            "HydroSetups sind im MVP DWC/RDWC-only.",
            "Secrets wie Home-Assistant-Tokens dürfen nicht in API-Responses, Exports oder Backups erscheinen.",
            "Grow-Exports müssen SectionCounts und IntegrityHash tragen, bevor sie importiert werden dürfen.",
            "Import-Planung ist ein Dry-Run und schreibt keine Daten in die Datenbank.",
            "Echter Grow-Import legt neue lokale Vergleichs-Grows an und ueberschreibt keine bestehenden Grows, Zelte oder HydroSetups.",
            "Administrative System-, Settings- und Export-Endpunkte sind nur lokal oder ueber den Home-Assistant-Ingress erreichbar.",
            "Produkt-APIs sind nur lokal oder ueber den Home-Assistant-Ingress erreichbar; externer Zugriff laeuft ueber Home Assistant.",
            "Upgrade-Preflight erstellt vor riskanten Updates ein validierbares Backup.",
            "Restore erfordert einen gueltigen Restore-Plan, Schema-Kompatibilitaet, Safety-Backup und Integritaetscheck.",
            "Schema-Migrationen werden in AppliedSchemaMigrations protokolliert.",
            "Migrationen liefern einen Dry-Run-Plan mit Backup-Pflicht und Destructive-Guardrails, bevor riskante Updates umgesetzt werden.",
            "Kritische Backend-Operationen werden im SystemAuditEvents-Log protokolliert.",
            "API-Fehler folgen dem einheitlichen grow-os.api-error.v1 Format.",
            "Runtime-Daten aus App_Data werden nicht als Source-Artefakte behandelt.",
            "Legacy-MVC-Endpunkte duerfen Backup, Export, Security oder Produktregeln nicht umgehen."
        };

        var areas = new[]
        {
            new ApiAreaDto(
                Key: "tents",
                Title: "Zelte",
                Description: "Physische Räume für Klima, Licht, Sensoren und Systemzuordnung.",
                Endpoints: new[]
                {
                    Endpoint("GET", "/api/settings/tents", "Zelte listen, optional inklusive archivierter Zelte.", true, "includeArchived=true lädt archivierte Zelte mit."),
                    Endpoint("GET", "/api/settings/tents/{id}", "Ein einzelnes Zelt mit technischen Details laden.", true),
                    Endpoint("POST", "/api/settings/tents", "Zelt anlegen.", true, "Name und TentType müssen gültig sein."),
                    Endpoint("PUT", "/api/settings/tents/{id}", "Zelt bearbeiten.", true, "Sensor-Mappings werden validiert."),
                    Endpoint("POST", "/api/settings/tents/{id}/archive", "Zelt archivieren.", true),
                    Endpoint("DELETE", "/api/settings/tents/{id}", "Zelt löschen oder bei Abhängigkeiten archivieren.", true)
                }),
            new ApiAreaDto(
                Key: "hydro-setups",
                Title: "HydroSetups",
                Description: "Technische DWC/RDWC-Systeme mit Volumen, Layout und Technikmerkmalen.",
                Endpoints: new[]
                {
                    Endpoint("GET", "/api/hydro-setups", "HydroSetups listen.", true, "Standardmäßig nur aktive HydroSetups.", "includeArchived=true lädt archivierte HydroSetups mit."),
                    Endpoint("GET", "/api/hydro-setups?tentId={id}", "HydroSetups nach Zelt filtern.", true),
                    Endpoint("GET", "/api/hydro-setups/{id}", "Ein HydroSetup laden.", true),
                    Endpoint("POST", "/api/hydro-setups", "HydroSetup anlegen.", true, "Nur DWC oder RDWC erlaubt.", "TentId muss existieren.", "RDWC benötigt mindestens zwei Sites und eine Tankposition."),
                    Endpoint("PUT", "/api/hydro-setups/{id}", "HydroSetup bearbeiten.", true),
                    Endpoint("POST", "/api/hydro-setups/{id}/archive", "HydroSetup archivieren.", true)
                }),
            new ApiAreaDto(
                Key: "grows",
                Title: "Grows",
                Description: "Konkrete Pflanzenläufe, die an Zelt und HydroSetup hängen.",
                Endpoints: new[]
                {
                    Endpoint("GET", "/api/grows", "Grows listen.", true),
                    Endpoint("GET", "/api/grows/{id}", "Grow-Details laden.", true),
                    Endpoint("POST", "/api/grows", "Neuen Grow erstellen.", true, "SystemId/HydroSetup ist für neue Grows Pflicht.", "HydroSetup muss aktiv sein und zum Zelt passen."),
                    Endpoint("PUT", "/api/grows/{id}", "Grow bearbeiten.", true, "Bestehende Legacy-Grows bleiben updatefähig."),
                    Endpoint("DELETE", "/api/grows/{id}", "Grow archivieren/löschen gemäß Repository-Regel.", true)
                }),
            new ApiAreaDto(
                Key: "operations",
                Title: "Addback, Changeout und Messungen",
                Description: "Betriebsprotokolle und Messdaten für DWC/RDWC-Grows.",
                Endpoints: new[]
                {
                    Endpoint("GET", "/api/grows/{id}/addback", "Addback-Kontext laden.", true, "Volumen wird zuerst aus HydroSetup berechnet."),
                    Endpoint("POST", "/api/grows/{id}/addback/calculate", "Addback berechnen.", true),
                    Endpoint("GET", "/api/grows/{id}/addback/logs", "Addback-Protokoll laden.", true),
                    Endpoint("POST", "/api/grows/{id}/addback/logs", "Addback-Protokolleintrag speichern.", true),
                    Endpoint("GET", "/api/grows/{id}/changeouts", "Changeout-Protokoll laden.", true),
                    Endpoint("POST", "/api/grows/{id}/changeouts", "Changeout-Protokolleintrag speichern.", true),
                    Endpoint("GET", "/api/grows/{growId}/measurements", "Messwerte eines Grows laden.", true),
                    Endpoint("POST", "/api/grows/{growId}/measurements", "Messwert anlegen.", true)
                }),
            new ApiAreaDto(
                Key: "hardware",
                Title: "Hardware",
                Description: "Inventar, Sensoren, Wartung und Kalibrierung.",
                Endpoints: new[]
                {
                    Endpoint("GET", "/api/hardware-items", "Hardware listen und filtern.", true, "Filter nach TentId, GrowId, SetupId oder HydroSetupId möglich."),
                    Endpoint("POST", "/api/hardware-items", "Hardware anlegen.", true, "HydroSetupId muss existieren, wenn gesetzt."),
                    Endpoint("PUT", "/api/hardware-items/{id}", "Hardware bearbeiten.", true),
                    Endpoint("GET", "/api/maintenance-events", "Wartungen listen.", true),
                    Endpoint("GET", "/api/calibration-events", "Kalibrierungen listen.", true),
                    Endpoint("GET", "/api/risk-events", "Risiken listen.", true)
                }),
            new ApiAreaDto(
                Key: "export-backup-system",
                Title: "Export, Backup und System",
                Description: "Produktnahe Systemendpunkte für Export, Backup, Schema und Release-Readiness.",
                Endpoints: new[]
                {
                    Endpoint("GET", "/api/exports/grows/{id}", "Grow exportieren.", true, "anonymize=true entfernt/neutralisiert nutzerbezogene Angaben.", "Export enthält ExportId, SectionCounts und IntegrityHash."),
                    Endpoint("POST", "/api/exports/grows/validate", "Grow-Export validieren, ohne Daten zu importieren.", true, "Prüft SchemaVersion, SectionCounts, IntegrityHash und potenzielle Secrets."),
                    Endpoint("POST", "/api/exports/grows/import-plan", "Grow-Export als Import-Dry-Run analysieren, ohne Daten zu schreiben.", true, "Plant neue lokale IDs, Snapshot-Behandlung und Konflikte."),
                    Endpoint("POST", "/api/exports/grows/import", "Grow-Export kontrolliert als neuen lokalen Vergleichs-Grow importieren.", true, "Erstellt vor dem Import automatisch ein Safety-Backup.", "Importiert als neue lokale Grow-Id und ueberschreibt keine bestehenden Grows."),
                    Endpoint("GET", "/grows/{id}/export", "Legacy-Export-Route; leitet auf /api/exports/grows/{id} um und liefert keine alten Rohdaten mehr.", false),
                    Endpoint("GET", "/settings/backup", "Legacy-Backup-Route; direkter SQLite-Download ist deaktiviert.", true),
                    Endpoint("GET", "/api/system/backend-health", "Backend-Zustand und Capabilities laden.", false),
                    Endpoint("GET", "/api/system/release-readiness", "Release-Readiness prüfen.", true),
                    Endpoint("GET", "/api/system/database-status", "Datenbankstatus und Pflichtschema prüfen.", true),
                    Endpoint("GET", "/api/system/api-manifest", "Maschinenlesbares API-Manifest laden.", true),
                    Endpoint("GET", "/api/system/security-status", "Security-Status und local-only/Ingress-Guardrails laden.", true),
                    Endpoint("GET", "/api/system/audit-events", "System-Audit-Events fuer kritische Backend-Operationen laden.", true),
                    Endpoint("GET", "/api/grows/{growId}/chronik", "Chronik eines Grows: was wann geaendert wurde. Ohne Knopf — man liest sie, wenn etwas passiert ist.", true),
                    Endpoint("GET", "/api/system/error-contract", "Einheitlichen API-Fehlervertrag laden.", true),
                    Endpoint("GET", "/api/system/migration-status", "Schema-Migrationen und offene Migrationen prüfen.", true),
                    Endpoint("GET", "/api/system/migration-plan", "Schema-Migrationsplan als Dry-Run laden.", true, "Zeigt Pending/Applied, Backup-Pflicht und destructive Guardrails."),
                    Endpoint("POST", "/api/system/upgrade-preflight", "Update-Vorprüfung mit Datenbankstatus, Migrationstatus und validiertem Backup ausführen.", true),
                    Endpoint("POST", "/api/system/backup", "Lokales Backup ohne Secrets erstellen.", true, "Behaelt je Art die letzten fuenf Sicherungen; aeltere werden entfernt und im Audit vermerkt."),
                    Endpoint("GET", "/api/system/backup", "Vorhandene Sicherungen auflisten (neueste zuerst).", true),
                    Endpoint("GET", "/api/system/backup/{fileName}", "Backup herunterladen.", true),
                    Endpoint("GET", "/api/system/backup/{fileName}/validate", "Backup vor Restore validieren.", true),
                    Endpoint("POST", "/api/system/backup/{fileName}/restore-plan", "Restore-Dry-Run erzeugen, ohne Dateien zu überschreiben.", true, "Prüft Backupvalidität, Schema-Kompatibilität und betroffene Zielpfade."),
                    Endpoint("POST", "/api/system/backup/{fileName}/restore", "Backup kontrolliert wiederherstellen.", true, "Erstellt vor dem Restore automatisch ein Safety-Backup.", "Restored nur schema-kompatible, validierte Backups.", "Fuehrt SQLite-Integritaetscheck vor dem Swap aus.")
                })
        };

        var dto = new ApiManifestDto(
            SchemaVersion: "grow-os.api-manifest.v1",
            BackendSchema: "backend-core.v0.18-candidate",
            GeneratedAtUtc: DateTime.UtcNow,
            GlobalRules: globalRules,
            Areas: areas);

        LogSystemAudit("system", "api-manifest-read", "API-Manifest abgefragt.", true);
        return Ok(dto);
    }


    [HttpGet("error-contract")]
    [ProducesResponseType(typeof(ApiErrorContractDto), StatusCodes.Status200OK)]
    public ActionResult<ApiErrorContractDto> ErrorContract()
    {
        var dto = new ApiErrorContractDto(
            SchemaVersion: ApiErrorFactory.SchemaVersion,
            Format: "ApiError",
            GeneratedAtUtc: DateTime.UtcNow,
            RequiredFields: new[] { "code", "message", "schemaVersion" },
            OptionalFields: new[] { "fieldErrors", "status", "traceId" },
            StandardCodes: new[]
            {
                "validation_failed",
                "not_found",
                "invalid_export",
                "invalid_backup_file",
                "admin_access_required",
                "active_sop_exists",
                "internal_server_error"
            },
            StandardStatuses: new[] { "400", "403", "404", "409", "500" },
            Rules: new[]
            {
                "Fehlerantworten enthalten immer code, message und schemaVersion.",
                "Validierungsfehler verwenden fieldErrors als Feldname-zu-Fehlerliste Dictionary.",
                "status und traceId werden gesetzt, wenn der Fehler über die Backend-Helper erzeugt wird.",
                "Controller sollen BadRequestError, NotFoundError, ConflictError, ForbiddenError oder ValidationError verwenden.",
                "Blockierte Zugriffe und unerwartete Fehler verwenden dasselbe ApiError-Format."
            });

        LogSystemAudit("system", "error-contract-read", "API-Error-Contract abgefragt.", true);
        return Ok(dto);
    }


    [HttpGet("security-status")]
    [ProducesResponseType(typeof(BackendSecurityStatusDto), StatusCodes.Status200OK)]
    public ActionResult<BackendSecurityStatusDto> SecurityStatus()
    {
        var dto = new BackendSecurityStatusDto(
            SecuritySchema: "grow-os.security.v1",
            CheckedAtUtc: DateTime.UtcNow,
            AdminAccessMode: "local-and-ingress",
            LocalOnlyAdminDefault: true,
            IngressTrusted: true,
            ProtectedRoutePrefixes: AdminAccessPolicy.ProtectedRoutePrefixes,
            Notes: new[]
            {
                "Grow OS laeuft als Home-Assistant-Add-on hinter dem Ingress-Proxy; Home Assistant authentifiziert jeden Zugriff.",
                "Der Add-on-Port ist nicht ins Netzwerk veroeffentlicht (ingress-only) und daher nicht direkt erreichbar.",
                "Administrative System-, Settings- und Export-Endpunkte sind zusaetzlich nur lokal oder ueber den Ingress erreichbar."
            },
            SecretHandling: new[]
            {
                "Home-Assistant-Token werden in API-Responses maskiert.",
                "Backups schliessen ha-config.json, DataProtectionKeys, Uploads und Logs aus.",
                "Grow-Exports pruefen potenzielle Secrets und tragen IntegrityHash."
            });

        LogSystemAudit("security", "security-status-read", "Security-Status abgefragt.", true);
        return Ok(dto);
    }


    [HttpGet("audit-events")]
    [ProducesResponseType(typeof(IReadOnlyList<SystemAuditEventDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SystemAuditEventDto>> AuditEvents([FromQuery] int limit = 100, [FromQuery] string? eventType = null)
    {
        var events = _auditRepository.GetRecent(limit, eventType).Select(entry => entry.ToDto()).ToList();
        LogSystemAudit(
            eventType: "system",
            action: "audit-events-read",
            summary: $"System-Audit-Events abgefragt ({events.Count} Eintrag/Eintraege).",
            success: true);
        return Ok(events);
    }


    [HttpGet("database-status")]
    [ProducesResponseType(typeof(DatabaseStatusDto), StatusCodes.Status200OK)]
    public ActionResult<DatabaseStatusDto> DatabaseStatus()
    {
        var requiredTables = new[]
        {
            "AppSettings", "Tents", "GrowSystems", "Grows", "Measurements", "HardwareItems",
            "AddbackLogs", "ChangeoutEntries", "JournalEntries", "GrowTasks", "HarvestEntries", "AppliedSchemaMigrations", "SystemAuditEvents"
        };
        var requiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tents"] = new[] { "Id", "Name", "TentType", "Status", "UpdatedAtUtc" },
            ["GrowSystems"] = new[] { "Id", "TentId", "HydroStyle", "PotCount", "PotSizeLiters", "ReservoirLiters", "Status", "LayoutType", "ReservoirPosition" },
            ["Grows"] = new[] { "Id", "TentId", "SystemId", "HydroStyle", "ReservoirSize", "ContainerSize", "TentSnapshotJson", "HydroSetupSnapshotJson", "SnapshotsCapturedAtUtc" },
            ["HardwareItems"] = new[] { "Id", "TentId", "HydroSetupId", "Name", "Category", "Status" },
            ["AddbackLogs"] = new[] { "Id", "GrowId", "HydroSetupId", "Kind", "PerformedAtUtc", "ReservoirLiters", "EcBefore", "EcTarget", "LitersAdded", "CreatedAtUtc" },
            ["ChangeoutEntries"] = new[] { "Id", "GrowId", "HydroSetupId", "Kind", "PerformedAtUtc", "VolumeChangedLiters", "Notes", "CreatedAtUtc" },
            ["AppliedSchemaMigrations"] = new[] { "Id", "Name", "RequiredForSchemaVersion", "AppliedAtUtc", "Status", "CompletedAtUtc", "RequiresBackup", "IsDestructive", "EngineVersion" },
            ["SystemAuditEvents"] = new[] { "Id", "EventType", "Action", "Summary", "Severity", "Source", "Success", "CreatedAtUtc" }
        };

        var presentTables = new List<string>();
        var missingTables = new List<string>();
        var presentColumns = new List<string>();
        var missingColumns = new List<string>();
        var warnings = new List<string>();
        string? storedSchemaVersion = null;

        if (!System.IO.File.Exists(_paths.DatabasePath))
        {
            return Ok(new DatabaseStatusDto(
                ExpectedSchemaVersion: DatabaseInitializer.CurrentSchemaVersion,
                StoredSchemaVersion: null,
                CheckedAtUtc: DateTime.UtcNow,
                DatabaseExists: false,
                IsCurrent: false,
                RequiredTablesPresent: Array.Empty<string>(),
                MissingRequiredTables: requiredTables,
                RequiredColumnsPresent: Array.Empty<string>(),
                MissingRequiredColumns: requiredColumns.SelectMany(pair => pair.Value.Select(column => $"{pair.Key}.{column}")).ToArray(),
                Warnings: new[] { "Datenbankdatei existiert noch nicht." }));
        }

        using var connection = OpenReadConnection();
        storedSchemaVersion = ReadAppSetting(connection, DatabaseInitializer.CurrentSchemaAppSettingKey);
        foreach (var table in requiredTables)
        {
            if (TableExists(connection, table))
            {
                presentTables.Add(table);
            }
            else
            {
                missingTables.Add(table);
            }
        }

        foreach (var pair in requiredColumns)
        {
            foreach (var column in pair.Value)
            {
                var qualified = $"{pair.Key}.{column}";
                if (ColumnExists(connection, pair.Key, column))
                {
                    presentColumns.Add(qualified);
                }
                else
                {
                    missingColumns.Add(qualified);
                }
            }
        }

        if (!string.Equals(storedSchemaVersion, DatabaseInitializer.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            warnings.Add("Gespeicherte Schema-Version weicht von der erwarteten Backend-Version ab.");
        }

        return Ok(new DatabaseStatusDto(
            ExpectedSchemaVersion: DatabaseInitializer.CurrentSchemaVersion,
            StoredSchemaVersion: storedSchemaVersion,
            CheckedAtUtc: DateTime.UtcNow,
            DatabaseExists: true,
            IsCurrent: missingTables.Count == 0
                && missingColumns.Count == 0
                && string.Equals(storedSchemaVersion, DatabaseInitializer.CurrentSchemaVersion, StringComparison.Ordinal),
            RequiredTablesPresent: presentTables,
            MissingRequiredTables: missingTables,
            RequiredColumnsPresent: presentColumns,
            MissingRequiredColumns: missingColumns,
            Warnings: warnings));
    }


}
