# WebBoard.Agent

`WebBoard.Agent` is the host-side HTTP API used by `Webboard.Web`. It is intended
to expose narrowly scoped host operations such as querying Docker and systemd
and running an allowlist of predefined commands.

The agent exposes its own health plus read-only Docker and systemd discovery and
status endpoints. Docker endpoints require access to the Docker Unix socket;
systemd endpoints require `systemctl` access to the host's system manager.

## Run locally

The project targets .NET 10. From the repository root:

```sh
dotnet run --project src/WebBoard.Agent --urls http://localhost:5080
```

Verify the service:

```sh
curl http://localhost:5080/health
```

OpenAPI JSON is available at `/openapi/v1.json` when the environment is
`Development`.

## Run in Docker

CI publishes this image to `shujidev/webboard-agent` on every push to `main`,
using the same version as `shujidev/webboard` (starting at `1.0`). See the
[CI/CD setup](../../README.md#docker-images-and-cicd) for credentials and
versioning details. To use a published version with the commands below,
replace `webboard-agent` at the end of the `docker run` command with
`shujidev/webboard-agent:1.0` (or the version you want).

## Docker Compose

For the UI, database, and agent together, follow the
[full-stack setup](../../README.md#docker-compose). From the repository root:

```sh
docker compose --env-file .env -f deploy/docker-compose.yml up -d agent
docker compose --env-file .env -f deploy/docker-compose.yml logs -f agent
curl http://localhost:5080/health
```

The shared file requires all variables from the main setup even when only
starting the agent. For an agent on a separate machine, save this standalone
configuration as `compose.yaml`:

```yaml
services:
  agent:
    image: shujidev/webboard-agent:${WEBBOARD_VERSION:-1.0}
    restart: unless-stopped
    ports:
      - "127.0.0.1:${AGENT_PORT:-5080}:8080"
    group_add:
      - "${DOCKER_GID:?Set the Docker socket group ID}"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - /run/dbus/system_bus_socket:/run/dbus/system_bus_socket:ro
      - /run/systemd/system:/run/systemd/system:ro
```

On a Linux Docker host running systemd:

```sh
export WEBBOARD_VERSION=1.0
export DOCKER_GID="$(stat -c '%g' /var/run/docker.sock)"
docker compose up -d
curl http://localhost:5080/health
```

Set the version to one published by CI. For upgrades, change the version,
then run `docker compose pull` and `docker compose up -d`.
The default binding only permits access from the agent host. To connect a UI
on another machine, use a private authenticated tunnel or proxy and configure
that address in the UI; the agent currently has no built-in authentication.
For the full stack's shared network, use `http://agent:8080` in the UI.

These mounts require a Linux host with Docker and systemd. On hosts without
systemd, remove the two `/run/…` mounts; systemd endpoints will be unavailable.
The Docker socket group ID is specific to each host. See the access notes
below before mounting it.

## Build and run manually

The Dockerfile and its ignore file live in `deploy/agent`. Build from the
repository root because the Dockerfile uses paths relative to the repository:

```sh
docker build -f deploy/agent/Dockerfile -t webboard-agent .
docker run --rm --name webboard-agent \
  --publish 127.0.0.1:5080:8080 \
  --volume /run/dbus/system_bus_socket:/run/dbus/system_bus_socket:ro \
  --volume /run/systemd/system:/run/systemd/system:ro \
  webboard-agent
```

The image includes the `systemctl` client but still runs the agent as the
non-root .NET image user. The read-only system D-Bus socket mount lets that
client query the host service manager; it does not start systemd in the
container.

### Docker socket access

Docker Engine can be reached by bind-mounting its Unix socket. The container
user must also be granted the socket's host group ID:

```sh
docker run --rm --name webboard-agent \
  --publish 127.0.0.1:5080:8080 \
  --volume /var/run/docker.sock:/var/run/docker.sock \
  --volume /run/dbus/system_bus_socket:/run/dbus/system_bus_socket:ro \
  --volume /run/systemd/system:/run/systemd/system:ro \
  --group-add "$(stat -c '%g' /var/run/docker.sock)" \
  webboard-agent
```

Access to `docker.sock` is effectively root-level control of the host. Keep the
agent on a private network, require authentication and authorization before
adding host endpoints, and never forward the API port publicly.

### systemd access

When the agent runs directly on a systemd host, no additional configuration is
normally needed for its read-only `systemctl show` calls. For the provided
container image, mount the host's system D-Bus socket and the directory
`systemctl` uses to detect the running manager:

```sh
--volume /run/dbus/system_bus_socket:/run/dbus/system_bus_socket:ro
--volume /run/systemd/system:/run/systemd/system:ro
```

The host's D-Bus policy still controls what the non-root container user may
query. Do not use `--privileged`; service discovery and status checks only need
read access. On distributions whose runtime paths live elsewhere, adjust the
mount sources accordingly.

### Predefined commands

Commands must be server-side definitions identified by stable IDs. API callers
should never be allowed to provide executable paths, shell fragments, or raw
arguments. Run commands without a shell, validate any supported parameters,
apply timeouts, cap output, and record an audit event for every invocation.

## Current endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Confirms that the agent process is running. |
| `GET` | `/docker/containers` | Lists containers from the mounted Docker socket. |
| `GET` | `/docker/containers/{id}/status` | Returns the current state and health of a container. |
| `GET` | `/systemd/services` | Lists systemd services and their current states. |
| `GET` | `/systemd/services/{name}/status` | Returns the current state of a systemd service. |
