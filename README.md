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

Download and run the [guided installer](deploy/install.sh) in an interactive Bash
terminal. No repository clone or .NET SDK is needed:

```bash
curl -fsSL https://raw.githubusercontent.com/murczi/webBoard/main/deploy/install.sh -o webboard-install.sh
bash webboard-install.sh
```

The **first prompt** asks which components to install: enter `db`, `ui`, `agent`,
or any comma-separated combination, for example `db,ui` or `ui,agent`.
`database` is also accepted; spaces, capitalization, and repeated names are handled.
The default is `db,ui,agent`. Only selected components are written to Compose and
started. Run the installer separately on each host for a distributed deployment.

| Selection | Remaining connection setup |
| --- | --- |
| `db` | Generates PostgreSQL credentials/TLS, asks for bind address, port, and certificate hostname, and saves connection details for remote UIs. |
| `ui` | Asks for existing database host/port/name/user/password and TLS/CA settings; configures browser HTTPS and optional remote agents. |
| `agent` | Generates token/TLS, asks for private bind address, port, and certificate hostname, and saves connection details for remote UIs. |
| `db,ui` | Connects UI to the local database automatically with verified TLS. |
| `db,agent` | Exposes each component using its chosen bind address and exports separate connection details; no UI migrations run. |
| `ui,agent` | Asks for external database details and configures the local agent token, CA trust, and exact Docker subnet automatically. |
| `db,ui,agent` | Wires all three together automatically; database and agent have no host ports unless you opt into remote access. |

For a newly installed database, choose its name/owner and the installer generates
an independent password. It creates a server certificate for `database` and the
selected remote hostname/IP, initializes certificate permissions inside the
PostgreSQL image, and configures the UI with `SSL Mode=VerifyFull` and the public
CA mounted at `/tls/database-ca.crt`. `database-connection.txt` contains the
credentials and connection string for later UI installs. Transfer only the
public `tls/database-ca.crt` to those UI hosts and provide its path when prompted.
A database-only install does not run webBoard migrations: the UI installer does
that when connecting to it. Restrict exposed database ports to authorized UI hosts.

For an existing external database, the password prompt is hidden. The installer
builds the connection string from the individual fields, escaping passwords and
names correctly. Choose `verify-full` (default), `require`, or `disable` according
to that server's TLS configuration; `verify-full` optionally imports a public CA
PEM file, otherwise it uses system certificate authorities. Use a host reachable
from the UI container; `localhost` refers to that container.

Agent installs prompt for Docker/systemd monitoring, optional Docker control
targets, and an existing token or automatic token generation. An agent sharing
this deployment with a UI uses `https://agent:8443`; its profile and trust are
configured automatically. An agent without a UI asks for the reachable hostname
or IPv4 used by remote UIs and publishes HTTPS on the selected private address.
`agent-connection.txt` contains its URL/token and public CA location. Give remote
UI installers that hostname/port, token, and public CA PEM file. Numeric remote
agent IPs receive an exact `/32` outbound allowance; DNS-based private agents
prompt for their network CIDR. UI installs can configure multiple remote agents
and additional private monitoring networks. Tokens and keys stay on the server.
Agent certificates expire after 365 days; follow the
[renewal guide](deploy/docs/agent-tls.md#certificates-created-by-the-guided-installer).

Systemd monitoring mounts the host D-Bus socket; service controls remain disabled
until you follow the [host policy guide](deploy/agent/README.md#one-time-host-authorization).
Docker monitoring mounts the host Docker socket and only explicitly listed
containers receive control access.

UI installs offer local HTTPS, public HTTPS, or an existing proxy. Local mode asks
for the bind IP first and defaults the browser/Caddy certificate address to it;
you may choose a DNS hostname instead. Wildcard binds require a concrete browser
address. Caddy's `default_sni` handles IP-based TLS through Docker port forwarding.
Import the exported `browser-ca.crt` into each browser device's trusted
certificate authorities. Public mode requires your DNS and incoming TCP 80/443
to reach this host; Caddy obtains/renews certificates. With an existing proxy,
point its HTTPS route at the chosen loopback HTTP port before registration/login.
DNS/router changes and browser trust happen on the relevant provider/device.

The installer offers Docker/OpenSSL prerequisite installation on fresh
Ubuntu/Debian hosts with sudo. Other hosts need Bash, OpenSSL, Docker, and Compose
with `up --wait`. Agents need Linux host sockets. A UI using private agent CAs
also needs Docker Buildx for its trust image; the fresh-host package setup includes
it. It builds that image locally, selectively pulls published images, stops the
UI before migrations, and preserves credentials on retries/upgrades.

To preselect components, directory, and UI HTTP port:

```bash
bash webboard-install.sh --components db,ui --directory "$HOME/webboard" --port 8088
```

The fixed project name `webboard` permits one deployment per Docker host. Existing
installs prompt to remove the old installation or abort (the default). Removal
permanently deletes its database and certificate volumes, secrets, and generated
files; unrelated files in the deployment directory are kept. Incomplete
configuration is not started and its unused Docker network is released. Once configuration is complete, retry using
the saved component selection and credentials:

```bash
bash webboard-install.sh --directory "$HOME/webboard" --resume
```

The generated `manage.sh` operates only on this deployment's selected services:

```bash
bash "$HOME/webboard/manage.sh" logs
bash "$HOME/webboard/manage.sh" start   # retry/start
# Back up PostgreSQL before upgrading:
bash "$HOME/webboard/manage.sh" update  # pull/build, migrate only if UI selected
bash "$HOME/webboard/manage.sh" stop    # preserves data/certificate volumes
bash "$HOME/webboard/manage.sh" remove  # confirms uninstall, permanently deletes data
```

Read `NEXT-STEPS.txt` for the browser URL and connection-detail file locations.
Register your administrator first. Keep Compose, private TLS material,
connection-detail files, and volumes backed up privately. Lifecycle mutations
are serialized; if a forcibly killed command leaves `.webboard-operation.lock`,
confirm no command is running before removing that empty directory. Updates
recreate file-mounted services and validate CA exports before replacing them.
Removal keeps shared Docker images and any CA you imported into host/browser
trust stores; remove those trust entries separately if no longer needed.

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
