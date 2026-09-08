# webBoard

## Deployment guides

- [UI: Docker Compose, configuration, and upgrades](deploy/ui/README.md)
- [Agent: Docker Compose and host socket access](deploy/agent/README.md)
- [Complete Compose file: UI, agent, and PostgreSQL](deploy/docker-compose.yml)

## Docker Compose

Run these commands from the repository root on a Linux Docker host running
systemd. The full stack monitors that host. See the agent guide for hosts
without systemd or agents deployed on other machines.

### Configure

If you do not already have a `.env`, copy `.env.example` to `.env`. Otherwise,
merge the new variables into your existing file without replacing its database
credentials. Set `WEBBOARD_VERSION` to a version published by CI, for example
`1.0`. Set `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD`, then add:

```dotenv
WEBBOARD_VERSION=1.0
WEBBOARD_PORT=8080
AGENT_PORT=5080
Authentication__Jwt__SigningKey=replace-with-output-from-openssl
DOCKER_GID=replace-with-your-docker-socket-group-id
```

Generate the signing key and find the socket group ID:

```sh
openssl rand -hex 32
stat -c '%g' /var/run/docker.sock
```

Paste the outputs into the corresponding `.env` entries. Keep `.env` private;
it is ignored by Git and excluded from the images. Use a database password
such as a randomly generated hex string so it can also be embedded safely in
the PostgreSQL connection string.

### Initialize the database and start

A fresh PostgreSQL container creates the database, but the application's
schema must be applied separately. Install the .NET 10 SDK and EF tool on the
host for this step. Use source matching the selected image version (the
publishing workflow run identifies its commit).

```sh
# Install once; if already installed, use dotnet tool update instead.
dotnet tool install --global dotnet-ef --version 10.0.9

docker compose --env-file .env -f deploy/docker-compose.yml up -d --wait database

dotnet ef database update \
  --project src/Webboard.Infrastructure.Configuration \
  --startup-project src/Webboard.Infrastructure.Configuration

docker compose --env-file .env -f deploy/docker-compose.yml pull ui agent
docker compose --env-file .env -f deploy/docker-compose.yml up -d
docker compose --env-file .env -f deploy/docker-compose.yml ps
```

The migration command reads `ConnectionStrings__WebboardDatabase` from the
root `.env`. Use `Host=localhost` and the published `POSTGRES_PORT` for local
tooling, as shown in `.env.example`. Inside Compose, the UI connects to
`database:5432`. The UI waits for database health before starting, but this
health check does not verify the application schema.

The UI's HTTP endpoint is `http://localhost:8080`; expose it through an HTTPS
reverse proxy for remote use and Secure authentication cookies. Agent health
is available at `http://localhost:5080/health`. Add the agent in the UI using
`http://agent:8080`. Registration creates an account without access grants;
assign permissions using the user-access backend described below.

The database uses the existing `dashboard-postgres-data` volume. Keep the
same Compose project name when upgrading an existing installation to retain
that volume. Existing database credentials must match the initialized volume;
changing `.env` does not reset a PostgreSQL user's password.

### Complete stack

This is the contents of `deploy/docker-compose.yml`:

```yaml
services:
  database:
    image: postgres:18.4-alpine
    container_name: dashboard-postgres
    restart: unless-stopped

    environment:
      POSTGRES_DB: ${POSTGRES_DB:?Set POSTGRES_DB in .env}
      POSTGRES_USER: ${POSTGRES_USER:?Set POSTGRES_USER in .env}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in .env}

    ports:
      - "127.0.0.1:${POSTGRES_PORT:-5432}:5432"

    volumes:
      - dashboard-postgres-data:/var/lib/postgresql

    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U \"$$POSTGRES_USER\" -d \"$$POSTGRES_DB\""]
      interval: 5s
      timeout: 5s
      retries: 10

  ui:
    image: shujidev/webboard:${WEBBOARD_VERSION:-1.0}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__WebboardDatabase: 'Host=database;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}'
      Authentication__Jwt__SigningKey: ${Authentication__Jwt__SigningKey:?Set a random signing key of at least 32 bytes in .env}
    ports:
      - "127.0.0.1:${WEBBOARD_PORT:-8080}:8080"
    depends_on:
      database:
        condition: service_healthy

  agent:
    image: shujidev/webboard-agent:${WEBBOARD_VERSION:-1.0}
    restart: unless-stopped
    ports:
      - "127.0.0.1:${AGENT_PORT:-5080}:8080"
    group_add:
      - "${DOCKER_GID:?Set DOCKER_GID to the host Docker socket group ID}"
    volumes:
      - type: bind
        source: /var/run/docker.sock
        target: /var/run/docker.sock
        bind:
          create_host_path: false
      - type: bind
        source: /run/dbus/system_bus_socket
        target: /run/dbus/system_bus_socket
        read_only: true
        bind:
          create_host_path: false
      - type: bind
        source: /run/systemd/system
        target: /run/systemd/system
        read_only: true
        bind:
          create_host_path: false

volumes:
  dashboard-postgres-data:
```

The agent has access to the host Docker socket, which grants effective
root-level host control. Its published port is bound to loopback; see the
agent guide for private remote access and mount requirements.

### Logs, updates, and shutdown

```sh
docker compose --env-file .env -f deploy/docker-compose.yml logs -f ui agent
# After choosing a new WEBBOARD_VERSION in .env and applying its migrations:
docker compose --env-file .env -f deploy/docker-compose.yml pull ui agent
docker compose --env-file .env -f deploy/docker-compose.yml up -d
# Stop containers while preserving database data:
docker compose --env-file .env -f deploy/docker-compose.yml down
```

Back up the database before upgrades. `down` preserves its named volume;
adding `--volumes` deletes it.

Compose uses [environment-file interpolation](https://docs.docker.com/compose/how-tos/environment-variables/variable-interpolation/)
and [database health dependencies](https://docs.docker.com/compose/how-tos/startup-order/).
Schema initialization uses the [EF Core CLI](https://learn.microsoft.com/en-us/ef/core/cli/dotnet).

## Docker images and CI/CD

Pull requests to `main` run the .NET build and tests and build both Docker
images. Every push to `main` runs the same .NET checks, builds both images,
and publishes them to Docker Hub:

- `shujidev/webboard:1.0` — the web UI, listening on container port `8080`.
- `shujidev/webboard-agent:1.0` — the host agent, listening on container port `8080`.

Both images share an automatically generated version: `1.0`, `1.1`, `1.2`,
and so on (after `1.9` comes `1.10`). The minor number is the publishing
workflow's run number minus one. Pull requests do not consume versions.
Failed or cancelled publishing runs can leave gaps; re-running a failed run
reuses its version. Keep the publishing workflow's identity to preserve its
counter. Images are published for `linux/amd64` using explicit version tags.

### One-time setup

Create a Docker Hub access token for `shujidev` with **Read & Write** access
to both image repositories. In this GitHub repository, open **Settings →
Secrets and variables → Actions → New repository secret**, and save it as
`DOCKERHUB_TOKEN`. Commit and push the workflow files to `main` to start the
first build. The GitHub Actions run summary lists the published image tags.

Both images must build before publishing starts. Docker Hub pushes are
separate operations: if one fails, re-run the failed publishing job to finish
the pair with the same version. Publishing images does not restart deployed
containers; update your deployment to the desired image version.

### Local builds and configuration

Run from the repository root:

```sh
docker build -f deploy/ui/Dockerfile -t shujidev/webboard:local .
docker build -f deploy/agent/Dockerfile -t shujidev/webboard-agent:local .
```

Configure the UI container using `ConnectionStrings__WebboardDatabase` and
`Authentication__Jwt__SigningKey` environment variables. Serve the UI over
HTTPS for its secure authentication cookies. Local `.env` files and
development settings are excluded from both Docker build contexts.
See the [agent README](deploy/agent/README.md) for host socket mounts
and agent configuration.

The workflows follow the [Docker GitHub Actions documentation](https://docs.docker.com/build/ci/github-actions/push-multi-registries/)
and use [GitHub's workflow run counter](https://docs.github.com/en/actions/reference/workflows-and-actions/variables)
for versioning.

## Authentication

The web app signs users in from the existing `Users` table and loads their rows
from `UserCrudAccess`. Successful logins receive an eight-hour JWT in an
HttpOnly, Secure, SameSite=Strict browser-session cookie. Selecting “Remember
me” makes both the JWT and cookie persist for 30 days. Granted operations are
represented by repeatable `access` claims such as `Hosts:read` and
`Modules:update`.

`Users.PasswordHash` supports the user backend's
`PBKDF2-SHA256$<iterations>$<salt>$<hash>` format as well as ASP.NET Core
Identity V3 hashes. For non-development deployments, set a random signing key
of at least 32 bytes before starting the app:

```sh
export Authentication__Jwt__SigningKey='replace-with-a-long-random-production-key'
```

Registration checks usernames case-insensitively and creates new accounts with
no CRUD access grants. Access can then be assigned through the user-access
backend.

The checked-in development configuration contains a local-only signing key and
must not be used in production.
