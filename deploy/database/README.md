# PostgreSQL database

The database can run on its own machine, alongside the UI, or as a managed
PostgreSQL service. Agents never connect to it. Only the UI and its migration
command need database access.

[Deployment overview](../../README.md) · [UI setup](../ui/README.md)

For host prerequisites, follow [Docker installation](../docs/docker-host.md) and
[ports/private networking](../docs/networking.md).

## Compose file

On the database machine, create a directory and save this as `compose.yaml`
(or download the [Compose file](compose.yaml)). No repository clone or `.env`
is needed.

```yaml
# Installation: replace the password and private bind IP, then run:
# docker compose up -d --wait database
# Run the UI image with --migrate afterward to create application tables.
services:
  database:
    # Pin PostgreSQL; changing major versions requires a data upgrade.
    image: postgres:18.4-alpine
    # Fixed Docker name; remove/change if running multiple stacks on this host.
    container_name: dashboard-postgres
    # Restart after crashes/host reboots unless explicitly stopped.
    restart: unless-stopped

    # Initial database, owner, and password; used only for an empty data volume.
    environment:
      POSTGRES_DB: dashboard
      POSTGRES_USER: dashboard
      POSTGRES_PASSWORD: "REPLACE_WITH_DATABASE_PASSWORD"

    # HOST_IP:HOST_PORT:CONTAINER_PORT; allow access from the UI machine only.
    ports:
      - "10.0.0.20:5432:5432"

    # Named volume persists data at the PostgreSQL 18 image's data mount path.
    volumes:
      - dashboard-postgres-data:/var/lib/postgresql

    # Wait for PostgreSQL to accept connections; this does not create app tables.
    # $$ passes $ to the container shell instead of Compose variable substitution.
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U \"$$POSTGRES_USER\" -d \"$$POSTGRES_DB\""]
      interval: 5s
      timeout: 5s
      retries: 10

# Compose creates this volume; keep the project name stable to reuse it.
volumes:
  dashboard-postgres-data:
```

Replace `10.0.0.20` with this machine's private/VPN interface IP and replace
the password with a random value, for example the output of `openssl rand -hex 32`.
Use the same credentials in the UI connection string. Restrict port `5432`
to the UI machine over a trusted private network/VPN. Keep this configured
file private. For database traffic outside a trusted tunnel, configure
PostgreSQL TLS and certificate verification in the UI connection string.

## Configuration fields

| Field | Purpose and what to change |
| --- | --- |
| `image` | PostgreSQL image version; retain the major version when reusing existing data. |
| `container_name` | Fixed Docker name. Remove or change it if another stack already uses it. |
| `restart` | Restarts after crashes or Docker restarts unless explicitly stopped. |
| `POSTGRES_DB` | Database created on first initialization; match the UI's `Database`. |
| `POSTGRES_USER` | Initial database owner; match the UI's `Username`. |
| `POSTGRES_PASSWORD` | Initial owner password; match the UI's `Password`. Changing this field does not update an existing user. |
| `ports` | `HOST_IP:HOST_PORT:CONTAINER_PORT`; replace `10.0.0.20` with a local private/VPN IP. On a shared Docker network the UI uses `database:5432` and no host publication is needed. |
| `volumes` | Keeps data in a named volume mounted at `/var/lib/postgresql` for PostgreSQL 18. |
| `healthcheck.test` | `pg_isready` checks whether PostgreSQL accepts connections; `$$` leaves variable expansion to the container shell. |
| `healthcheck.interval` / `timeout` / `retries` | Probe every 5 seconds, allow 5 seconds per probe, mark unhealthy after 10 failed probes. |
| Top-level `volumes` | Declares the persistent Compose volume; its actual name includes the project name. |

## Start and initialize

In the directory containing the Compose file:

```sh
docker compose config --quiet
docker compose pull database
docker compose up -d --wait database
docker compose ps
docker compose logs --tail=100 database
```

This initializes PostgreSQL and the named database. Follow the UI guide to
run `docker compose run --rm --no-deps ui --migrate` on the UI machine, then
start the UI. That command creates or updates the application tables using
the published UI image; no source code or .NET tooling is needed here.

If using an existing or managed database, skip this Compose file and supply
its connection details in the UI guide instead.

## Data and upgrades

Data persists in `dashboard-postgres-data`. Keep the same Compose project
name and volume when restarting or moving your Compose file. Changing the
password in Compose does not change an existing PostgreSQL user's password.
When moving to another machine, back up and restore the database; copying
only the Compose file does not transfer its data.

```sh
# Save a logical backup on the database machine:
docker compose exec -T database sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > webboard.dump
# Stop containers while preserving data:
docker compose down
```

Do not add `--volumes` unless you intend to delete the database volume.
PostgreSQL is pinned to `18.4-alpine`; unlike the UI and agent, it does not use
`latest`, because changing database major versions requires a planned data
upgrade. Back up first and follow PostgreSQL's upgrade procedure before
changing the major version.

## Restore a backup

Stop every UI replica before restoring. Restore into an empty database prepared
with the same owner (the commands below use the configured database):

```sh
docker compose exec -T database sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --exit-on-error' < webboard.dump
```

Confirm the restore succeeds, then follow the UI migration/start procedure. The
restore does not erase an existing schema; prepare an empty destination rather
than restoring over live application tables.

## Troubleshooting

- **Port bind fails:** the configured IP must exist on this host and the port must be unused.
- **UI cannot connect:** check the private address, firewall, database health, and matching credentials. `localhost` inside the UI container cannot reach this host.
- **Password changes have no effect:** the initialization variables apply only to an empty volume; change an existing role's password in PostgreSQL and update the UI connection string together.
- **Data appears missing after moving the file:** check the Compose project name and existing volumes with `docker volume ls` before creating a fresh database.
