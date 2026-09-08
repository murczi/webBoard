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
without access. Use **Users → Set CRUD access** to grant permissions;
`Users:update` allows managing anyone's permissions. Write access automatically
includes read access. Existing accounts retain their stored grants on upgrade;
first-user bootstrap only runs when the `Users` table is empty.

Add each monitored machine in the UI with its reachable agent URL, for example
`http://10.0.0.30:5080` over a private VPN. Docker service names such as `agent`
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
