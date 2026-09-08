# Webboard UI

The UI image is `shujidev/webboard:<version>`. Its Dockerfile and ignore file
live here. See the [main README](../../README.md) for the full stack and CI/CD,
and the [agent README](../agent/README.md) for host monitoring.

## Docker Compose

From the repository root, configure `.env` using the
[full-stack setup](../../README.md#docker-compose) first, including database
migrations. Then start just the UI and its PostgreSQL dependency:

```sh
docker compose --env-file .env -f deploy/docker-compose.yml up -d database ui
docker compose --env-file .env -f deploy/docker-compose.yml logs -f ui
```

The shared Compose file also defines the agent, so its `DOCKER_GID` variable
must be set even when starting only the UI. No agent sockets are mounted
unless the agent service is started.

The UI listens at `http://localhost:8080` by default. Set `WEBBOARD_PORT` to
change the host port. For remote access, put an HTTPS reverse proxy on the
same host in front of this loopback endpoint; authentication uses Secure
cookies and requires HTTPS. Configure TLS termination in your deployment.

Compose supplies `ConnectionStrings__WebboardDatabase` using the `database`
service name and port `5432`. The connection string in the root `.env` is for
local .NET tooling and uses the published database port instead.
`Authentication__Jwt__SigningKey` must contain a random key of at least 32
bytes. Database schema updates are a separate migration step, described in
the main README; the UI does not apply them at startup.

After starting the agent, add a host in the UI with agent URL
`http://agent:8080` when using the full stack. `localhost` inside the UI
container refers to the UI container itself.

## Upgrade

Back up PostgreSQL, set `WEBBOARD_VERSION` in `.env` to the desired published
version, and check out the matching application source before applying any
new migrations using the main README's migration command. Then:

```sh
docker compose --env-file .env -f deploy/docker-compose.yml pull ui
docker compose --env-file .env -f deploy/docker-compose.yml up -d ui
```

## Build locally

```sh
docker build -f deploy/ui/Dockerfile -t shujidev/webboard:local .
WEBBOARD_VERSION=local docker compose --env-file .env -f deploy/docker-compose.yml up -d database ui
```

Local `.env` files and development settings are excluded from the image.
