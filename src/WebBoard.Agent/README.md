# WebBoard.Agent

`WebBoard.Agent` is the host-side HTTP API used by `Webboard.Web`. It is intended
to expose narrowly scoped host operations such as querying Docker and systemd,
and running an allowlist of predefined commands.

The project currently contains only the API foundation and a health endpoint.
Host integrations and command endpoints will be added separately so their
authorization and privilege boundaries can be designed deliberately.

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
  webboard-agent
```

The image runs as the non-root .NET image user. Do not add host access until an
endpoint actually needs it.

### Docker socket access

Docker Engine can be reached by bind-mounting its Unix socket. The container
user must also be granted the socket's host group ID:

```sh
docker run --rm --name webboard-agent \
  --publish 127.0.0.1:5080:8080 \
  --volume /var/run/docker.sock:/var/run/docker.sock \
  --group-add "$(stat -c '%g' /var/run/docker.sock)" \
  webboard-agent
```

Access to `docker.sock` is effectively root-level control of the host. Keep the
agent on a private network, require authentication and authorization before
adding host endpoints, and never forward the API port publicly.

### systemd access

Talking to the host's systemd instance from a container requires access to a
host IPC socket, normally the system D-Bus socket, plus a deliberate policy for
which units and operations are permitted. The exact mount and policy are
distribution-dependent and are intentionally not enabled in the base image.
Prefer a restricted D-Bus/systemd policy over a privileged container.

### Predefined commands

Commands must be server-side definitions identified by stable IDs. API callers
should never be allowed to provide executable paths, shell fragments, or raw
arguments. Run commands without a shell, validate any supported parameters,
apply timeouts, cap output, and record an audit event for every invocation.

## Current endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Confirms that the agent process is running. |
