# Secrets and Keys

ETL-SQL supports encrypted values for secrets such as passwords, JWT secrets, certificate passwords, and connection strings. Encrypted values use the `ENC:` prefix.

### 4.1 Encrypting Secrets

Encrypt a value with an explicit master password:

```bash
ETL-SQL encrypt "my-secret-password" --pass "YourMasterKey"
```

The CLI also supports machine-bound encryption when no password is supplied. That is convenient for local services, but the encrypted value will not be portable if the machine key changes or the configuration is moved to another host.

### 4.2 Portal JWT Secret

The Portal requires a strong JWT secret. Generate one during deployment:

```bash
ETL-SQL config setup-jwt --update
```

> [!CAUTION]
> Record the plaintext secret in a password manager or deployment vault. If it is stored only as an encrypted value and the machine key is lost, the plaintext cannot be recovered.

For a non-disruptive rotation, place the replacement in `Portal__Jwt__Secret` and retain the old
value temporarily as `Portal__Jwt__PreviousSecrets__0`. The portal signs only with the current secret
and validates against both. Remove the previous value after the maximum access-token lifetime has
elapsed. Removing it sooner intentionally invalidates access tokens signed by that key.

### 4.3 Orchestrator API Key

A shared API key protects every Orchestrator route that submits, cancels, inspects, schedules, or manages jobs — including the ad-hoc execution routes `POST /jobs`, `DELETE /jobs/{id}`, and `GET /jobs/{id}`. Only the unauthenticated probes `GET /health`, `GET /metrics`, and `GET /metrics/prometheus` are exempt. The portal sends the key in the `X-Orchestrator-Key` request header.

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "ScriptRoot": "C:\\ETL-SQL\\scripts"
  }
}
```

The installers (MSI custom action and Linux `postinst`) generate a random `Orchestrator:ApiKey` on first install and mirror it to `Portal:Orchestrator:ApiKey` so the two halves match out of the box.

Rotate without downtime by first adding the new key to `Orchestrator__PreviousApiKeys__0`, restarting
the Orchestrator, switching `Portal__Orchestrator__ApiKey` to the new key, then making the new key
current on the Orchestrator while retaining the old key temporarily in `PreviousApiKeys`. Remove the
old key after every caller has moved. The service compares fixed-length key digests in constant time.

> [!IMPORTANT]
> **The Orchestrator refuses to start unauthenticated on a network-reachable address.** If `Orchestrator:ApiKey` is empty *and* the service binds to a non-loopback address (for example `http://*:5001` or `http://0.0.0.0:5001`), startup fails fast with an actionable error. Configure a key, or bind the service to loopback only (`http://127.0.0.1:5001`). An empty key is permitted **only** for loopback-only bindings, which is development/isolated-host behavior.

