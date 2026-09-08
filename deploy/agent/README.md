# WebBoard agent

Run `shujidev/webboard-agent:latest` on each machine you want to monitor. It
reports that machine's Docker containers and systemd services. It does not
need PostgreSQL, a UI signing key, a repository clone, or `.env`.

[Deployment overview](../../README.md) · [UI setup](../ui/README.md)

## Compose file

On each Linux Docker host running systemd, create a directory and save this
as `compose.yaml` (or download the [Compose file](compose.yaml)):

```yaml
services:
  agent:
    image: shujidev/webboard-agent:latest
    restart: unless-stopped
    ports:
      - "10.0.0.30:5080:8080"
    group_add:
      - "999" # Replace with: stat -c '%g' /var/run/docker.sock
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
```

Replace the example `10.0.0.30` with this machine's private/VPN interface IP.
Replace `999` in `group_add` with the output of:

```sh
stat -c '%g' /var/run/docker.sock
```

The mounts access this machine's Docker and systemd, regardless of where the
UI runs. On a host without systemd, remove the two `/run/…` mounts; systemd
endpoints will then be unavailable. The image runs as a non-root user; host
D-Bus policy determines which systemd queries it can perform.

## Start and connect

In the directory containing the Compose file:

```sh
docker compose pull agent
docker compose up -d agent
docker compose logs -f agent
curl http://10.0.0.30:5080/health
```

Use your actual agent address in the health check. Add the same base URL
(`http://10.0.0.30:5080` in this example) when configuring a host in the UI.
The address must be reachable from the UI container. Each agent has its own
machine address; `http://agent:8080` only works for the single-machine stack's
shared Docker network.

The agent currently has no built-in authentication. Keep this endpoint on a
trusted private VPN/network restricted to the UI machine; never publish it
to the internet. Docker socket access gives effective root-level host control.
Use loopback binding (`127.0.0.1:5080:8080`) if accessing it through a tunnel
on the agent machine instead. These read-only discovery operations do not
require a privileged container.

## Upgrade

On each agent machine:

```sh
docker compose pull agent
docker compose up -d agent
```

Use `latest` for the current published image, or replace it with a numbered
tag such as `1.2` to choose a specific build. Agent updates do not run database
migrations or restart the UI.

## Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Agent health. |
| `GET` | `/docker/containers` | Docker container discovery. |
| `GET` | `/docker/containers/{id}/status` | Container state and health. |
| `GET` | `/systemd/services` | Systemd service discovery. |
| `GET` | `/systemd/services/{name}/status` | Systemd service state. |
