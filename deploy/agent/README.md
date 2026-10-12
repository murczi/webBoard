# webBoard agent

Deploy one agent per monitored machine, independently of the UI and PostgreSQL.
All endpoints, including `/health`, discovery, and operations,
require `Authorization: Bearer <token>`. Generate a different token for each agent
with `openssl rand -hex 32`. Never place tokens in URLs, module properties, or
browser JavaScript. The agent fails startup without a token of at least 32 bytes.

[Deployment overview](../../README.md) · [UI setup](../ui/README.md)

For host prerequisites, follow [Docker installation](../docs/docker-host.md) and
[ports/private networking](../docs/networking.md).

## Requirements

Use a Linux Docker host with Compose. Docker monitoring requires
`/var/run/docker.sock`; systemd monitoring requires a systemd host with
`/run/dbus/system_bus_socket` and `/run/systemd/system`. Create a deployment
directory and save the Compose file there. No .NET SDK, repository clone, or
`.env` file is needed for published images.

## Host account for systemd monitoring

Systemd discovery and status queries use the host system bus. D-Bus can reject a
container UID that has no entry in the host account database, even when its socket
is correctly mounted. The guided installer creates or reuses a non-login
`webboard-agent` host account and sets the container's `user` to its numeric UID/GID.
The host allocates these numbers; the installer does not reserve the image's UID
1654. An existing account must have a non-root UID/GID and a non-login shell.
Docker-only installations use the image's default user.

For a manual installation or to repair an existing deployment, run on the agent
host:

```bash
if ! getent passwd webboard-agent >/dev/null; then
    sudo useradd --system --user-group --no-create-home \
        --home-dir /nonexistent --shell /usr/sbin/nologin \
        --comment 'webBoard host agent' webboard-agent
fi
getent passwd webboard-agent
printf '    user: "%s:%s"\n' "$(id -u webboard-agent)" "$(id -g webboard-agent)"
```

Verify that the account has a non-root UID/GID and a shell such as
`/usr/sbin/nologin` or `/bin/false`. Add the printed `user` line under the `agent`
service in your deployment's Compose file, replacing any existing `user` line.
Retain `group_add` for the Docker socket and ensure the chosen UID can read the
mounted PFX. Installer-generated PFX files are mode 0644 and remain inside a
private host directory.

From that deployment directory, recreate just the agent and verify discovery:

```bash
docker compose up -d --force-recreate agent
docker compose exec agent systemctl show docker.service --property=ActiveState
```

This preserves the existing certificates, tokens, and data. The host account is
retained across updates and uninstall so its UID can be reused safely; remove it
manually only when no deployment or host policy uses it. This account setup is
needed for monitoring independently of the optional polkit rules for controls.

## Container deployment

Use [compose.yaml](compose.yaml). Supply `Security__Tokens__0`, a certificate and
password, and a private bind address. The example serves HTTPS on container port
8443 and host port 5081. Mount a PFX at `./tls/agent.pfx`; its SAN must match the DNS
name used by the UI. Give the container's non-root UID read access to the certificate
and socket group access using `stat -c '%g' /var/run/docker.sock`. Protect the
Compose file and certificate password. No privileged container is required.

The complete [Compose file](compose.yaml) is:

```yaml
# Installation: prepare TLS and host sockets as described in README.md, then run:
# docker compose pull agent
# docker compose up -d agent
# Configure this file first; the UI must trust the certificate and use the same token.
# Double underscores nest .NET settings; numeric entries are zero-based list indices.
services:
  agent:
    # Use latest or a published tag for a pinned version.
    image: shujidev/webboard-agent:latest
    # Restart after crashes/host reboots unless explicitly stopped.
    restart: unless-stopped
    environment:
      # HTTPS listener inside the container; must match the port mapping below.
      ASPNETCORE_URLS: https://+:8443
      Security__Tokens__0: "" # Required: independent random agent token (openssl rand -hex 32)
      # Container path to the mounted PFX and its export password.
      Kestrel__Certificates__Default__Path: /tls/agent.pfx
      Kestrel__Certificates__Default__Password: "REPLACE_WITH_CERTIFICATE_PASSWORD"
      # Operations default to off. Allow exact targets to enable all supported operations:
      # Controls__Docker__0: my-game-server
      # Controls__Systemd__0: example.service
      # Enable/disable also require the host polkit setup in README.md.
    # For systemd monitoring, set the dedicated host account's UID:GID (README.md).
    # user: "HOST_UID:HOST_GID"
    # HOST_IP:HOST_PORT:CONTAINER_PORT; replace with this host's private/VPN IP.
    ports:
      - "10.0.0.30:5081:8443"
    # Supplemental host Docker socket group lets the non-root process access Docker.
    group_add:
      - "999" # Replace with: stat -c '%g' /var/run/docker.sock
    # Bind existing host files/sockets. Paths below target the monitored host.
    # create_host_path: false fails on missing files instead of creating directories.
    volumes:
      - type: bind
        # Relative to this Compose file; readable by the container UID.
        source: ./tls/agent.pfx
        target: /tls/agent.pfx
        read_only: true
        bind:
          create_host_path: false
      - type: bind
        # Docker discovery/status/control; socket access grants host-level authority.
        source: /var/run/docker.sock
        target: /var/run/docker.sock
        bind:
          create_host_path: false
      - type: bind
        # Host system bus for systemd queries and authorized operations.
        source: /run/dbus/system_bus_socket
        target: /run/dbus/system_bus_socket
        read_only: true
        bind:
          create_host_path: false
      - type: bind
        # Host systemd runtime marker for the container's systemd tools.
        source: /run/systemd/system
        target: /run/systemd/system
        read_only: true
        bind:
          create_host_path: false
```

| Field | Purpose and what to change |
| --- | --- |
| `image` / `restart` | Agent image version and `unless-stopped` restart policy. Use a published tag to pin an image. |
| `ASPNETCORE_URLS` | Internal listener, `https://+:8443`; match the container port in `ports`. |
| `Security__Tokens__0` | Random API bearer token of at least 32 bytes. Use the identical value in this agent's UI profile. Additional indexed tokens support rotation. |
| `Kestrel__Certificates__Default__Path` | PFX path inside the container; match the TLS mount target. |
| `Kestrel__Certificates__Default__Password` | Password used when exporting the PFX. |
| `ports` | `HOST_IP:HOST_PORT:CONTAINER_PORT`; replace `10.0.0.30` with this host's private/VPN interface IP. The UI uses host port `5081`. |
| `user` | For systemd monitoring, use the dedicated host account's numeric UID/GID. Docker-only monitoring can use the image default. |
| `group_add` | Numeric group owning the host Docker socket, from `stat -c '%g' /var/run/docker.sock`. |
| TLS bind mount | Existing certificate at `./tls/agent.pfx`, relative to Compose; mounted read-only at `/tls/agent.pfx`. |
| Docker socket bind mount | Access to host Docker discovery, status, and allowed operations. Socket access grants effective host-root authority to the agent process. |
| D-Bus/systemd bind mounts | Host system bus and runtime marker used for service queries/controls; host policy still applies. |
| `read_only` / `bind.create_host_path` | Read-only mounts prevent file writes; `create_host_path: false` rejects missing sources instead of creating directories. A read-only socket mount still allows API calls. |
| `Controls__Docker__…` / `Controls__Systemd__…` | Optional lists of exact targets; each target allows all supported operations; controls stay off until configured. See below. |

.NET settings use double underscores between sections and zero-based list
indices. Add a second target as `Controls__Docker__1`, and so on.

In JSON, use target string arrays, for example
`"Controls": { "Docker": ["my-game-server"], "Systemd": ["example.service"] }`.
When upgrading from per-operation rules, replace `Controls__…__0__Target` with
`Controls__…__0` and remove the `Operations` entries. Listed targets now allow
every supported operation.

## Prepare TLS and start

Follow the [agent certificate recipe](../docs/agent-tls.md) to create a private CA,
issue a certificate for the exact agent URL, export `tls/agent.pfx`, grant the
container UID read access, and build the UI trust image. It also covers using a
certificate IP SAN, authenticated health checks, and renewal. If you already have
CA-issued PEM files, export them with:

```sh
mkdir -p tls
openssl pkcs12 -export -out tls/agent.pfx -inkey agent.key -in agent.crt -certfile ca-chain.crt
openssl rand -hex 32
stat -c '%g' /var/run/docker.sock
```

Set the export password, generated token, socket group, and host private bind IP
in Compose. Omit `-certfile` if the issuer supplies no separate chain file.

```sh
docker compose config --quiet
docker compose pull agent
docker compose up -d agent
docker compose ps
docker compose logs --tail=100 agent
```

Configure the UI with the same URL and token:

```yaml
Agents__0__BaseUrl: https://agent.example.com:5081
Agents__0__Token: "REPLACE_WITH_RANDOM_AGENT_TOKEN"
Outbound__AllowedPrivateNetworks__0: 10.0.0.0/24
```

Add another numbered profile for each agent. Profiles may not overlap. Enter that
exact base URL in Host management; the UI performs an authenticated health probe.
A profile is mandatory even for the Test agent action. The browser never receives
its token. Requests cannot follow redirects to another server.

Use certificates issued by a CA trusted by the UI. For a private CA, extend the UI
image with the CA certificate and `update-ca-certificates` as root, then return to
`USER $APP_UID`. Never disable certificate validation. Containers on a shared
network still require matching certificate names and an allowed private subnet.
Docker service names do not resolve across independent machines; use DNS or VPN IPs.

From an authorized machine, verify the configured token and TLS with this Bash
health probe. For a private CA, add `--cacert /path/to/ca.crt`:

```sh
read -r -s -p "Agent token: " WEBBOARD_AGENT_TOKEN
printf '\n'
curl --fail --header "Authorization: Bearer $WEBBOARD_AGENT_TOKEN" https://agent.example.com:5081/health
unset WEBBOARD_AGENT_TOKEN
```

For an existing **encrypted VPN/tunnel**, HTTP is an explicit exception on both
ends: set `Agents__0__AllowHttpOverEncryptedTransport=true` in the UI and
`Security__AllowHttpOverEncryptedTransport=true` in the agent, change its
`ASPNETCORE_URLS`, and bind only the tunnel/private interface. A private address or
ordinary Docker network alone is not encrypted transport. A TLS proxy on the same
host may terminate TLS and forward over loopback using this explicit exception;
never expose the backend listener. HTTPS remains the default.

To rotate keys, add `Security__Tokens__1`, restart the agent, change its UI token,
restart the UI, then remove the old token and restart the agent.

## Docker controls

Discovery and monitoring remain available to authenticated callers. Control is
disabled until an exact container name/full ID is allowed:

```yaml
Controls__Docker__0: my-game-server
```

Each allowed container permits start, stop, and restart.

The agent resolves the container, verifies the allowlist, and sends the Engine API
operation using its full ID. It never starts a shell for Docker controls. The Docker
socket itself grants effective host-root authority to the process; application
allowlists restrict remote API callers, not a compromised process with socket access.
Mount it only on dedicated trusted agents. Do not expose Docker's TCP API.

## systemd controls from Docker

The agent runs only in Docker. No native agent service, host .NET runtime, sudo
execution mode, or arbitrary command configuration is supported. It uses the
mounted host D-Bus socket for monitoring and service controls. Retain the read-only
D-Bus/systemd mounts in Compose; host policy decides which queries are permitted.

Service controls are disabled until the exact service is allowed:

```yaml
Controls__Systemd__0: example.service
```

Each allowed service permits start, stop, restart, enable, and disable, subject to
host permissions. Discovery does not opt services into operations. The target
allowlist is checked again on every operation, even if the caller bypasses the UI.

### One-time host authorization

The guided installer offers optional start/stop/restart controls for exact service
names. If selected, it writes matching agent allowlist entries and installs
`/etc/polkit-1/rules.d/50-webboard-agent-installer.rules` as root (mode 0644).
Polkit is installed automatically if missing on Ubuntu/Debian; on other hosts,
install your distribution's polkit package first. The rule grants only the listed
runtime actions to the dedicated `webboard-agent` account. It does not grant
unit-file changes or daemon reload.

For an existing deployment with missing host permissions, download the current
installer and run on the **agent host**:

```bash
curl -fsSL https://raw.githubusercontent.com/murczi/webBoard/main/deploy/install.sh -o webboard-install.sh
bash webboard-install.sh --authorize-systemd frpc.service
```

Repeat `--authorize-systemd` in the same command for each additional service.
Each invocation replaces the installer-managed runtime service list, so include
every service you want to keep authorized. The command preserves existing Compose
files, secrets, and running containers. It prints the host account's UID/GID and
the `Controls__Systemd__…` entries; ensure your existing agent uses those values,
then run `docker compose up -d agent` from its deployment directory if you changed
Compose. Host polkit normally reloads rules automatically. No service is started,
stopped, or restarted by the authorization command.

The installer refuses to overwrite an unmanaged file or symlink at its policy
path and preserves separately installed manual rules. Host accounts and polkit
rules are retained on uninstall; remove them manually only when no deployment
needs them. For a tunnel service such as `frpc.service`, stop/restart can interrupt
your connection; verify authorization with a disposable service first.

For manual installation or enable/disable support:

1. Install the host distribution's polkit package if it is not already installed.
2. Use the dedicated `webboard-agent` host account from the
   [monitoring setup](#host-account-for-systemd-monitoring). The guided installer
   already creates this account and sets the container's numeric UID/GID.
3. Install [50-webboard-agent.rules](host/50-webboard-agent.rules) as root at
   `/etc/polkit-1/rules.d/50-webboard-agent.rules` (mode 0644). Replace the example
   units/verbs with your services. This grants only those runtime controls.
4. To support **Enable/Disable**, set `allowUnitFileChanges = true` in that host
   rule and include the service in the agent allowlist. This also authorizes
   manager reload, required after unit-file changes. The installer-managed runtime
   rule intentionally does not grant these broader permissions.
5. Restart the Docker agent after changing its allowlist. The host polkit service
   normally reloads rule files automatically. Verify using a disposable service.

This is a host configuration step, not a web UI authorization prompt. The agent
never requests interactive authorization. Host refusal is returned as an operation
failure; check the host policy and matching UID.

systemd's unit-file authorization does not expose the target unit to polkit.
Opting into unit-file changes therefore gives the agent identity broader host
unit-file access (distribution policy may also imply runtime control and reload); the agent's `Controls:Systemd` list enforces the individual
service restrictions for API callers. Keep this identity dedicated to the
agent. No privileged container or writable host root mount is required.

Enable/Disable call the host systemd manager through D-Bus and reload it afterward.
Changes persist across reboot, do not force replacement of conflicting links, and
do not start/stop the service. If reload fails after a change, the result explicitly
reports that partial completion. Review service installation metadata: enabling a
unit may also enable associated units. Unit files and executables must remain
administrator-owned.

## UI access and buttons

User management exposes one extra permission: **Modules → Operations**. Granting
it also grants Read. It permits only actions allowed by the agent; module Update
access does not grant control access. All users, including the initial administrator,
start with Operations off. Administrators must grant it explicitly.

The Operations page offers **Up / Down / Restart** for Docker (start/stop/restart
the existing container) and **Start / Stop / Restart / Enable / Disable** for
systemd. Buttons appear only for opted-in actions. There are no Compose stack
creation/removal actions, command text boxes, command associations, or custom
executables. Confirmations identify the module and host before submission.

Operations serialize per agent; conflicts return 409. The UI persists an audit
attempt and unique request ID before dispatch, and does not automatically retry.
A lost connection, agent/UI crash, or timeout can leave an unknown/pending result:
verify the target manually before issuing a new operation. Docker/systemd jobs can
continue after the client times out. Duplicate IDs are never redispatched by the
UI; the agent also caches recent completed IDs in memory, which resets on restart.

## Update and stop

From the agent Compose directory:

```sh
docker compose pull agent
docker compose up -d agent
docker compose logs --tail=100 agent
```

Verify the authenticated health probe after updating. Agent upgrades do not run
database migrations. For a UI/agent upgrade, stop UI replicas and follow the
[UI upgrade procedure](../ui/README.md#upgrade). Preserve tokens, certificates,
and allowlists. Use `docker compose stop agent` to stop monitoring on this host.

## Troubleshooting

Docker modules selected in the editor are saved by container name, so monitoring
and controls reconnect after a container is recreated with the same name on the
same host. Existing modules saved by ID are converted when you open their editor
and save while the original container is still discoverable. If it has already
been removed, reselect the replacement container and save once. Use container
names in the Docker controls allowlist to keep controls available after updates.

- **Startup fails:** fill the token and certificate password, check that the PFX exists and is readable by the container UID, and review agent logs.
- **Port bind fails:** use an IP assigned to this host and an unused host port.
- **Health probe returns 401:** use a configured bearer token; `/health` also requires authentication.
- **TLS verification fails:** match the certificate SAN to the UI URL and install the issuing CA in the UI trust store.
- **Docker discovery fails:** check the socket exists and `group_add` matches its numeric host group.
- **systemd discovery reports `Transport endpoint is not connected`:** if `docker compose exec --user 0 agent busctl --system list` succeeds while the default-user query fails, check the host account and container UID using the [monitoring setup](#host-account-for-systemd-monitoring). A UID without a host account can be rejected by D-Bus.
- **systemd queries or controls fail:** check the mounted host sockets and matching host UID. Controls additionally require the agent allowlist and polkit policy; enable/disable needs the unit-file authorization described above.
- **Buttons are unavailable:** grant **Modules → Operations**, refresh access, and allow the target on the agent.
- **Stop/restart fail after adding a systemd allowlist entry:** the allowlist does not grant host permissions. Use `--authorize-systemd` on the agent host or install the manual polkit rule described above. Confirm that the container UID matches the host `webboard-agent` account.
