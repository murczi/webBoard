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

Build from the repository root because the Dockerfile uses paths relative to
the repository:

```sh
docker build -f src/WebBoard.Agent/Dockerfile -t webboard-agent .
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
