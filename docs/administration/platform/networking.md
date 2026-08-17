# HTTPS and Network Configuration

HTTPS, reverse proxies, ports, and the network boundaries each service expects.

## HTTPS & Network Configuration

Both the Orchestrator and Portal use Kestrel. The production templates define these defaults:

| Service | HTTP | HTTPS |
| :--- | :--- | :--- |
| Portal | `5000` | `5002` |
| Orchestrator Service | `5001` | `5003` |

Configure certificates directly in Kestrel or terminate TLS at a reverse proxy:

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://*:5002",
      "Certificate": {
        "Path": "C:\\Certs\\etl-sql.pfx",
        "Password": "ENC:ENCRYPTED_PFX_PASSWORD"
      }
    }
  }
}
```

When the Portal and Orchestrator run on different servers, configure the Portal with the Orchestrator's reachable URL:

```json
"Portal": {
  "Orchestrator": {
    "ApiUrl": "https://orchestrator-server:5003",
    "ApiKey": "your-shared-secret"
  }
}
```

The same values can be set through the Portal Admin UI under **Admin -> Settings -> Orchestrator Connection**. UI-saved values are written to a `portal-orchestrator.json` sidecar file next to the portal database and take precedence over startup configuration.

For report execution, the Orchestrator returns the completed report manifest over the authenticated
job-status API and the Portal writes it under `Portal:SnapshotDirectory`. Separate-host deployments do
not require a shared snapshot filesystem, but both services must have the same non-empty API key so
report data is never returned from the backward-compatible unauthenticated job-status surface. After
configuration, execute a small report and confirm its snapshot manifest and CSV export are available.

### Starting a stopped Orchestrator

The Portal can stop an Orchestrator but never start one. A service under an OS supervisor —
a Windows Service or a systemd unit — restarts itself after a stop, which is what makes the
Portal's **Stop** and **Restart** buttons useful. Starting a service that is genuinely down is done
on its own host, and deliberately so: the alternative is the Portal holding service-control rights
on another machine.

---

