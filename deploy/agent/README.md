# webBoard agent security and deployment

Deploy one agent per monitored machine, independently of the UI and PostgreSQL.
All endpoints, including `/health`, discovery, and operations,
require `Authorization: Bearer <token>`. Generate a different token for each agent
with `openssl rand -hex 32`. Never place tokens in URLs, module properties, or
browser JavaScript. The agent fails startup without a token of at least 32 bytes.

## Container deployment

Use [compose.yaml](compose.yaml). Supply `Security__Tokens__0`, a certificate and
password, and a private bind address. The example serves HTTPS on container port
8443 and host port 5081. Mount a PFX at `./tls/agent.pfx`; its SAN must match the DNS
name used by the UI. Give the container's non-root UID read access to the certificate
and socket group access using `stat -c '%g' /var/run/docker.sock`. Protect the
Compose file and certificate password. No privileged container is required.

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

A health probe from an authorized machine is:

```sh
curl --fail --header "Authorization: Bearer $WEBBOARD_AGENT_TOKEN" https://agent.example.com:5081/health
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
disabled until configured, separately per operation and exact container name/full ID:

```yaml
Controls__Docker__0__Target: my-game-server
Controls__Docker__0__Operations__0: start
Controls__Docker__0__Operations__1: stop
Controls__Docker__0__Operations__2: restart
```

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

Service controls are disabled until the exact service and each action are opted in:

```yaml
Controls__Systemd__0__Target: example.service
Controls__Systemd__0__Operations__0: start
Controls__Systemd__0__Operations__1: stop
Controls__Systemd__0__Operations__2: restart
Controls__Systemd__0__Operations__3: enable
Controls__Systemd__0__Operations__4: disable
```

Omit unwanted actions. Discovery does not opt services into operations. These rules
are checked again on every operation, even if the caller bypasses the UI.

### One-time host authorization

1. Install the host distribution's polkit package if it is not already installed.
2. Create a dedicated non-login host account named `webboard-agent` whose numeric
   UID matches the container user. The supplied .NET image defaults to UID 1654.
   If that UID is already used, choose an unused UID and set `user: "UID:GID"` in
   Compose to match the dedicated account. Retain Docker socket group access and
   grant that UID read access to the TLS certificate.
3. Install [50-webboard-agent.rules](host/50-webboard-agent.rules) as root at
   `/etc/polkit-1/rules.d/50-webboard-agent.rules` (mode 0644). Replace the example
   units/verbs with your services. This grants only those runtime controls.
4. To support **Enable/Disable**, set `allowUnitFileChanges = true` in that host
   rule and include the actions in the agent allowlist. This also authorizes
   manager reload, required after unit-file changes.
5. Restart the Docker agent after changing its allowlist. The host polkit service
   normally reloads rule files automatically. Verify using a disposable service.

This is a host configuration step, not a web UI authorization prompt. The agent
never requests interactive authorization. Host refusal is returned as an operation
failure; check the host policy and matching UID.

systemd's unit-file authorization does not expose the target unit to polkit.
Opting into unit-file changes therefore gives the agent identity broader host
unit-file access (distribution policy may also imply runtime control and reload); the agent's `Controls:Systemd` list enforces the individual
service/action restrictions for API callers. Keep this identity dedicated to the
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

## API and upgrades

- `GET /health`, `/docker/containers`, `/docker/containers/{id}/status`
- `GET /systemd/services`, `/systemd/services/{name}/status`
- `GET /capabilities?kind=Systemd&target=example.service` returns allowlisted verbs.
- `POST /operations` accepts requestId, moduleId, kind (Docker/Systemd),
  target, and operation. Unknown JSON properties are rejected.

Update containers using `docker compose pull agent` and `docker compose up -d agent`.
Agent upgrades never run UI database migrations. On migration from the old unauthenticated agent, configure
security on both ends together; there is no anonymous compatibility mode.

Upgrade the UI and agent together with UI replicas stopped. Apply the
`RestrictModuleOperations` migration before starting the new UI; it deletes obsolete
command associations and individual action grants and sets Operations to off for
all existing accounts. Historical audit records are retained. Reassign Operations
in User management and refresh the affected sessions. Remove old `Commands` and
`Systemd:UseSudo` configuration. Rolling back the schema cannot recover deleted
command associations or previous permission grants.
