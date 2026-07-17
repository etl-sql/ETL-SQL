# HTTPS and Network Configuration

## 5. HTTPS & Network Configuration

Both the Orchestrator and Portal use Kestrel. The production templates define these defaults:

| Service | HTTP | HTTPS |
| :--- | :--- | :--- |
| Report Portal | `5000` | `5002` |
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

### Same-Host Service Start

On Windows, if the Portal and Orchestrator run on the same host, the portal can start the Orchestrator service through `ServiceController` when it is offline:

```json
"Portal": {
  "Orchestrator": {
    "SameHost": true
  }
}
```

Leave `SameHost = false` for separate-server deployments.

---

