# webBoard

webBoard monitors services through a web UI, PostgreSQL, and optional host agents.
Deploy them on one machine or on separate machines connected by private DNS or a VPN.

| Component | Image | Installation guide |
| --- | --- | --- |
| UI | `shujidev/webboard:latest` | [UI configuration, login, and monitoring](deploy/ui/README.md) |
| Database | `postgres:18.4-alpine` or an existing PostgreSQL service | [Database configuration and backups](deploy/database/README.md) |
| Agent (optional) | `shujidev/webboard-agent:latest` | [Host monitoring, TLS, and service controls](deploy/agent/README.md) |

## Requirements

Install Docker Engine and the Compose plugin on each container host. Verify them
with `docker version` and `docker compose version`. Agent hosts need Linux;
systemd monitoring also needs the host systemd/D-Bus sockets. Have `openssl`
available to generate credentials, and an HTTPS reverse proxy for browser access.
Published images do not require a repository clone, .NET SDK, or `.env` file.

Configure values directly in the Compose files. Comments explain each field;
the component guides cover prerequisites and troubleshooting. Replace all blank
secrets and `REPLACE_WITH_…` placeholders before starting. Keep configured files
and TLS private keys private. Generate a separate database password, UI signing
key, and token for each agent with `openssl rand -hex 32`.

## Host setup recipes

[Deployment prerequisite guides](deploy/docs/README.md) provide terminal commands
and official sources for the steps outside webBoard itself:

- [Install Docker and Compose](deploy/docs/docker-host.md) on a fresh Ubuntu host.
- [Understand loopback ports and find the Docker subnet](deploy/docs/networking.md).
- [Create agent certificates and install CA trust in the UI](deploy/docs/agent-tls.md).
- [Set up UI HTTPS with Caddy](deploy/docs/https-proxy.md), using a public domain or a local CA.

Each recipe states where to run its commands and which names/IPs to change.

## Install with a Bash script

For a complete installation on one Docker host, download and run the standalone
[guided installer](deploy/install.sh). No repository clone or .NET SDK is needed.
Run it with Bash in an interactive terminal:

```bash
curl -fsSL https://raw.githubusercontent.com/murczi/webBoard/main/deploy/install.sh -o webboard-install.sh
bash webboard-install.sh
```

The installer prompts for the deployment directory and HTTP port, HTTPS mode
(local private CA, public domain, or an existing proxy), optional host agent,
Docker/systemd monitoring, optional Docker control targets, and additional
private monitoring networks. It offers to install missing Docker/OpenSSL
prerequisites on fresh Ubuntu/Debian hosts with sudo. Other hosts need Bash,
OpenSSL, Docker Engine, and a Compose plugin supporting `up --wait` already
installed. Existing/conflicting Docker installations are preserved; use the
[Docker host guide](deploy/docs/docker-host.md) to resolve prerequisites if needed.

It generates independent database/JWT secrets and, when installing an agent,
its token, encrypted CA key, certificate, and password-protected PFX. It places
TLS material under the deployment directory, builds the UI image with CA trust,
detects the actual Docker subnet, and writes the complete private `compose.yaml`.
The database and agent have no published host ports; HTTP binds to loopback.
Docker monitoring uses the host socket; systemd monitoring uses the host D-Bus
socket. Service controls remain disabled; optional systemd controls require the
[agent guide's host policy setup](deploy/agent/README.md#one-time-host-authorization).

For local or public HTTPS, the installer adds a Caddy container and persistent
certificate storage. Public HTTPS requires a domain resolving to the host and
reachable TCP ports 80/443; the installer prompts you to prepare these. Local
HTTPS exports `browser-ca.crt`: import that public CA into each browser device's
trusted certificate authorities. The installer also offers to install that CA in
the host system trust store when `update-ca-certificates` is available. For remote
local-HTTPS access, choose a private
bind IP and make the selected hostname resolve to it. With an existing proxy,
point its HTTPS route at the chosen loopback HTTP port. DNS/router changes and
browser certificate trust must be completed on the relevant provider/device.

The installer pulls/builds images, waits for PostgreSQL, applies migrations,
starts the services, and probes the configured agent using verified TLS and its
token. It writes `NEXT-STEPS.txt` with the browser URL and remaining client steps.
Register your administrator account first. If an agent was installed, add this
machine in **Host management** with `https://agent:8443`; its UI profile is already
configured. Agent server certificates last 365 days; follow the
[renewal guide](deploy/docs/agent-tls.md#verify-and-renew) before expiry.

To choose a deployment directory or a different HTTP port in advance:

```bash
bash webboard-install.sh --directory "$HOME/webboard" --port 8088
```

The fixed Compose project name `webboard` supports one installation per Docker
host. Existing deployment files are never overwritten. On failure, configuration,
credentials, and database data are preserved. Once configuration generation has
completed, retry with:

```bash
bash webboard-install.sh --directory "$HOME/webboard" --resume
```

The generated `manage.sh` also handles lifecycle commands from any directory:

```bash
bash "$HOME/webboard/manage.sh" logs
bash "$HOME/webboard/manage.sh" start   # retry/start using existing credentials
# Back up PostgreSQL before upgrading:
bash "$HOME/webboard/manage.sh" update  # pull/build, stop UI, migrate, restart
bash "$HOME/webboard/manage.sh" stop    # preserves database and Caddy volumes
```

Keep the deployment directory and volumes backed up privately. The generated
helper rebuilds the trusted UI image when needed and selectively pulls published
images; it does not try to pull the local `webboard-ui:trusted` image.

## Install on separate machines

1. Start PostgreSQL using the [database guide](deploy/database/README.md), or
   obtain the connection details for your existing database.
2. Configure the [UI Compose file](deploy/ui/compose.yaml) with those connection
   details and a stable JWT signing key. Run the UI guide's `--migrate` command
   before starting the web server, then configure HTTPS and register the first account.
3. For Docker/systemd monitoring, follow the [agent guide](deploy/agent/README.md)
   on each monitored host. Configure its certificate and token, add matching UI
   agent profiles, and enter each agent URL in **Host management**.

Only the UI needs database credentials. HTTP, Minecraft, and Steam checks do not
need agents. Private monitoring targets must be included in the UI's
`Outbound__AllowedPrivateNetworks` list.

Docker service names resolve only within a shared Docker network. Across machines,
use reachable private DNS names or VPN IPs. `localhost` inside a container refers
to that container. Restrict database and agent ports to machines that need them.

## Install on one machine

Create a deployment directory and save [deploy/docker-compose.yml](deploy/docker-compose.yml)
as `compose.yaml`. Run commands below from that directory. This example uses
Docker service names for internal connections and loopback host port bindings.

Before starting:

1. Replace the database password in both `POSTGRES_PASSWORD` and the UI connection
   string. Generate and fill the UI JWT signing key.
2. For the agent, generate a separate token and set it in both `Security__Tokens__0`
   and `Agents__0__Token`. Place its certificate at `./tls/agent.pfx`, set its
   password, and grant the container UID read access. The certificate must include
   `agent` in its subject alternative names and be trusted by the UI; see the
   [certificate creation and UI trust recipe](deploy/docs/agent-tls.md).
3. Replace `group_add: "999"` with the group ID from
   `stat -c '%g' /var/run/docker.sock`. Ensure the host sockets exist. Set
   `Outbound__AllowedPrivateNetworks__0` to the Docker network's actual subnet
   using the [network inspection commands](deploy/docs/networking.md#discover-the-single-machine-docker-subnet).
4. For a deployment without agents, remove the `agent` service and UI `Agents__…`
   fields. Keep the private-network allowlist if you monitor other private targets.

Pull the published images first. If the UI still uses `shujidev/webboard:latest`:

```sh
docker compose pull database ui agent
```

If you followed the private-CA recipe and changed the UI to `webboard-ui:trusted`,
that image exists only on the host where you built it. Use this instead:

```sh
docker image inspect webboard-ui:trusted >/dev/null && docker compose pull database agent
```

If inspection fails, complete the [UI trust image build](deploy/docs/agent-tls.md#trust-the-ca-in-the-ui-image)
on this Docker host before continuing. Do not pull `webboard-ui:trusted` from a
registry. Omit `agent` from either pull command when it is not deployed.

Then initialize and start:

```sh
docker compose config --quiet
docker compose up -d --wait database
docker compose run --rm --no-deps ui --migrate
docker compose up -d
docker compose ps
docker compose logs --tail=100 ui
```

Proceed to start the UI only if migrations succeed. For UI/database only, use
`docker compose up -d database ui` as the final start command. The UI HTTP endpoint
is `http://localhost:8080`; put an HTTPS reverse proxy in front of it for browser
login because authentication cookies require HTTPS. Follow the
[Caddy HTTPS recipe](deploy/docs/https-proxy.md) for a host proxy. A containerized proxy should
connect to `ui:8080` over a shared network rather than its own `localhost`.

Register your administrator account first. The first registration in an empty
`Users` table receives CRUD permissions; later accounts start without grants.
Use **Users → Set Permissions** to assign access. **Modules → Operations** must
be granted separately, including for the first account, and requires agent target
allowlists. Each allowed target permits all supported operations, subject to host
permissions.

## Update and stop

Back up PostgreSQL before upgrades. Keep the database credentials, JWT signing
key, TLS files, tokens, and Compose project name stable. For the one-machine stack,
prepare updated images first. For the published UI:

```sh
docker compose pull ui agent
```

For `webboard-ui:trusted`, rerun the [UI trust image build](deploy/docs/agent-tls.md#trust-the-ca-in-the-ui-image)
to include the updated base image, then pull only the published agent:

```sh
docker image inspect webboard-ui:trusted >/dev/null && docker compose pull agent
```

Then apply the upgrade:

```sh
docker compose stop ui
docker compose run --rm --no-deps ui --migrate
docker compose up -d
docker compose ps
```

Omit `agent` from the pull command if it is not deployed. Stop every UI replica
before migrating and run migrations once per database. Resolve failures before
restarting the UI. Separate-machine update commands are in the component guides.

`latest` downloads the current image when pulled; deployed containers update when
recreated with `up -d`. Set a published numbered tag for a pinned version. PostgreSQL
major-version changes require a planned data upgrade. An older UI image may also
need a compatible database backup to roll back.

`docker compose down` stops the stack while preserving the named database volume.
Adding `--volumes` deletes that data. Moving Compose to a different directory can
change its project name and select a different volume; retain the original project
name with `docker compose -p <original-name> …`.

## Build images locally

Clone this repository and run from its root:

```sh
docker build -f deploy/ui/Dockerfile -t shujidev/webboard:local .
docker build -f deploy/agent/Dockerfile -t shujidev/webboard-agent:local .
```

The [UI Dockerfile](deploy/ui/Dockerfile) and [agent Dockerfile](deploy/agent/Dockerfile)
explain the build/runtime stages, dependency caching, ports, and non-root user.
Set the corresponding Compose `image` to the `:local` tag and follow the same
installation procedure, skipping image pulls for locally built images. Development
settings, `.env` files, and private TLS material are excluded from build contexts.
