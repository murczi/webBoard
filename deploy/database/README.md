# PostgreSQL database

The database can run on its own machine, alongside the UI, or as a managed
PostgreSQL service. Agents never connect to it. Only the UI and its migration
command need database access.

[Deployment overview](../../README.md) · [UI setup](../ui/README.md)

## Compose file

On the database machine, create a directory and save this as `compose.yaml`
(or download the [Compose file](compose.yaml)). No repository clone or `.env`
is needed.

```yaml
services:
  database:
    image: postgres:18.4-alpine
    container_name: dashboard-postgres
    restart: unless-stopped

    environment:
      POSTGRES_DB: dashboard
      POSTGRES_USER: dashboard
      POSTGRES_PASSWORD: "REPLACE_WITH_DATABASE_PASSWORD"

    ports:
      - "10.0.0.20:5432:5432"

    volumes:
      - dashboard-postgres-data:/var/lib/postgresql

    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U \"$$POSTGRES_USER\" -d \"$$POSTGRES_DB\""]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  dashboard-postgres-data:
```

Replace `10.0.0.20` with this machine's private/VPN interface IP and replace
the password with a random value, for example the output of `openssl rand -hex 32`.
Use the same credentials in the UI connection string. Restrict port `5432`
to the UI machine over a trusted private network/VPN. Keep this configured
file private. For database traffic outside a trusted tunnel, configure
PostgreSQL TLS and certificate verification in the UI connection string.

## Start and initialize

In the directory containing the Compose file:

```sh
docker compose up -d --wait database
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
