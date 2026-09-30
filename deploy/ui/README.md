# Webboard UI

Run `shujidev/webboard:latest` on the machine serving the web UI. The database
and agents can run on other machines. You only need Docker with Compose;
no repository clone, .NET SDK, or `.env` is required.

[Deployment overview](../../README.md) · [Database setup](../database/README.md) ·
[Agent setup](../agent/README.md)

## Compose file

Create a directory on the UI machine and save this as `compose.yaml`
(or download the [Compose file](compose.yaml)):

```yaml
services:
  ui:
    image: shujidev/webboard:latest
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      # Replace with the database machine's private DNS name/IP and credentials.
      ConnectionStrings__WebboardDatabase: 'Host=db.internal;Port=5432;Database=dashboard;Username=dashboard;Password=REPLACE_WITH_DATABASE_PASSWORD'
      Authentication__Jwt__SigningKey: "" # Paste output of openssl rand -hex 32
    ports:
      - "127.0.0.1:8080:8080"
```

Replace `db.internal` with the database machine's private DNS name or IP
address, and match its database name, username, and password. The UI container
must be able to reach that address. `localhost` would refer to the UI container,
not the database machine. For a managed PostgreSQL service, use the provider's
connection details and TLS settings (for example, `SSL Mode=VerifyFull` with
its trusted CA configured).

Generate a signing key with `openssl rand -hex 32` and paste it into
`Authentication__Jwt__SigningKey`. Keep the key stable across upgrades and use
the same key on all UI replicas. Keep your configured Compose file private.

## Initialize and start

Start the database first. In the directory containing this Compose file:

```sh
docker compose pull ui
docker compose run --rm --no-deps ui --migrate
docker compose up -d ui
docker compose logs -f ui
```

The migration command uses this image's embedded migrations and the configured
connection string, then exits. It does not need a signing key and does not
start the web server. Run it once per database, before starting the UI. A
failed migration exits unsuccessfully; resolve it before starting the UI.
The database user used for this command must have schema-change permissions.

The UI listens on `127.0.0.1:8080` on this machine. Place an HTTPS reverse proxy
on the same machine in front of it; login cookies are Secure and need HTTPS.
If your proxy runs in a separate container or machine, connect it using a
shared Docker network or bind the UI port to a private interface reachable
by that proxy. Do not use the proxy container's `localhost` to reach the UI.

Register your administrator account first: on an empty database, the first
registration receives all supported CRUD permissions. Later accounts start
without access. Use **Users → Set Permissions** to grant permissions;
`Users:update` allows managing anyone's permissions. Write access automatically
includes read access. Existing accounts retain their stored grants on upgrade;
first-user bootstrap only runs when the `Users` table is empty.

Add each monitored machine in the UI with its reachable agent URL, for example
`https://agent.example.com:5081` with a matching authenticated agent profile. Docker service names such as `agent`
only work on the same Docker network; they do not resolve across machines.

## Upgrade

Back up the database and stop all UI replicas before a schema upgrade. Run
these commands on the UI machine (migrations only once per database):

```sh
docker compose pull ui
docker compose stop ui
docker compose run --rm --no-deps ui --migrate
docker compose up -d ui
```

`latest` follows new published builds; pulling it does not restart containers.
For controlled upgrades, replace `latest` with a numbered tag such as `1.2`
and use the same commands. Database changes may prevent rolling back an older
image without restoring a compatible database backup.

## Background monitoring and history

Monitoring starts with the UI process and runs without dashboard viewers. Configure:

| Environment variable | Default | Meaning |
| --- | --- | --- |
| `Monitoring__Enabled` | `true` | Enables this instance's collector; reads/history still work when false. |
| `Monitoring__IntervalSeconds` | `30` | Minimum interval per enabled module (5–86400 seconds). |
| `Monitoring__TimeoutSeconds` | `5` | Overall check deadline, at most the interval. |
| `Monitoring__StaleSeconds` | `90` | Freshness duration; at least interval plus timeout, at most 7 days. |
| `Monitoring__MaxConcurrency` | `8` | Maximum simultaneous checks per UI instance (1–64). |
| `Monitoring__RetentionDays` | `30` | History retention (1–3650 days). |
| `Outbound__AllowedPrivateNetworks__0` | none | Explicit private/loopback CIDR permitted for monitoring, e.g. `10.0.0.0/24`. Add numbered entries as needed. |
| `Agents__0__BaseUrl` | none | Exact configured agent base URL. |
| `Agents__0__Token` | none | Its matching secret bearer token, at least 32 characters. |
| `Agents__0__AllowHttpOverEncryptedTransport` | `false` | Opt-in HTTP exception only over an encrypted VPN/tunnel. |

Agent-free deployments should remove all `Agents__…` entries. HTTP, Minecraft, and
Steam checks do not require an agent. Private targets still require the outbound
CIDR configuration. Redirects are not followed, metadata/link-local/multicast targets
are rejected, and DNS results are checked at connection time. No credentials are
sent by HTTP module health checks. Management URLs remain ordinary browser links.

For private-CA agent certificates, build a derived image with the CA installed:

```dockerfile
FROM shujidev/webboard:latest
USER root
COPY agent-ca.crt /usr/local/share/ca-certificates/agent-ca.crt
RUN update-ca-certificates
USER $APP_UID
```

Use identical monitoring settings on all UI replicas. PostgreSQL advisory locks
coordinate each module so replicas cannot produce duplicate concurrent results.
Locks span the network check and its persistence transaction; size the PostgreSQL
connection pool for configured concurrency plus interactive requests. Poolers must
support transactions spanning these commands. No Redis, external scheduler, or
SignalR deployment is required. Individual failures are stored without stopping
the collector. On shutdown, checks are canceled without recording false outages.

Results and the latest-result projection are separate from audit events. Cleanup
runs hourly, deletes expired samples in 10,000-row transactions, and retains the
latest projection. Expired latest results are shown as Unknown, including the last
observation timestamp. No-data periods and Not configured states are distinct from
failed checks. Configuration changes invalidate current results immediately.

The dashboard polls every 15 seconds; history polls every 30 seconds. Hidden tabs
pause requests. History has hour/day/week/custom ranges, a shared axis, per-account
local browser visibility, and backward/forward navigation. Canvas rows load when
near the viewport; bounded server queries merge observations before returning
blocks. Hover, click/tap, or use arrow keys on a timeline to inspect a period. All
stored timestamps are UTC; controls and labels use the browser timezone. Custom
ranges are limited to ten years, with periods outside retention shown as Unknown.
History visibility changes do not change monitoring configuration.

Existing module readers receive `MonitoringHistory:read` during migration. Users
must Refresh access or sign in again to load that grant. Stop all UI replicas and
run `--migrate` once before restarting them.
