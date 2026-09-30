# Ports and networking

[Prerequisite guides](README.md) · [HTTPS proxy](https-proxy.md)

## Read a port mapping

Compose uses `HOST_IP:HOST_PORT:CONTAINER_PORT`:

| Example | Who connects and where |
| --- | --- |
| `127.0.0.1:8080:8080` | A process on the UI host connects to `http://127.0.0.1:8080`. |
| `10.0.0.30:5081:8443` | A machine that can reach the host's private IP connects to `https://10.0.0.30:5081`; the agent listens internally on 8443. |
| `8080:8080` | Publishes on all host interfaces by default; use an explicit bind address for these recipes. |

Loopback means the local host. Inside a container, `127.0.0.1` means that
container. A host-installed Caddy can reach the UI's loopback-published port;
a Caddy container should reach `ui:8080` on a shared Docker network. Publishing
is unnecessary for connections between services on that network. These behaviors
are documented in [Docker port publishing](https://docs.docker.com/engine/network/port-publishing/)
and [Compose networking](https://docs.docker.com/compose/how-tos/networking/).

Docker versions older than 28 have a documented caveat where loopback-published
ports may be reachable from the same layer-2 network. Use a current Docker Engine.
Docker-published ports can also bypass ordinary UFW rules; use explicit private
bindings and appropriate network access controls rather than relying on UFW alone.
See [Docker's firewall guidance](https://docs.docker.com/engine/install/ubuntu/#firewall-limitations).

## Find host addresses and verify the UI binding

Run on the container host. Choose an IP actually assigned to its private/VPN
interface for database and agent mappings; the example IPs are placeholders.

```bash
ip -brief address
docker compose ps
docker compose port ui 8080
curl --fail --output /dev/null --write-out 'UI HTTP status: %{http_code}\n' http://127.0.0.1:8080/
```

The UI port command should print `127.0.0.1:8080` for the default deployment.
HTTP verifies reachability; use the [HTTPS recipe](https-proxy.md) for browser login.
For a separate-machine agent, its certificate must match the DNS name or IP in
its UI URL, and the UI uses the **host** port `5081`.

## Discover the single-machine Docker subnet

Run from the directory containing the complete stack's `compose.yaml`. This
creates the database container/network without starting it, then inspects the
networks attached to that container. It also works if the database is running:

```bash
bash <<'NETWORK'
set -euo pipefail
docker compose create database
webboard_database_id=$(docker compose ps --all --quiet database)
webboard_networks=$(docker inspect --format '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' "$webboard_database_id")
while IFS= read -r webboard_network; do
  [ -n "$webboard_network" ] || continue
  docker network inspect --format '{{.Name}}: {{range .IPAM.Config}}{{.Subnet}} {{end}}' "$webboard_network"
done <<< "$webboard_networks"
NETWORK
```

Copy the shared UI/agent network's CIDR into
`Outbound__AllowedPrivateNetworks__0` in the UI environment. Add indexed entries
for any other private monitored networks. This setting permits webBoard's
outbound checks; it does not configure a host firewall or create a VPN.
Docker networks and service-name DNS stay on their Docker host; across machines,
configure real private DNS or use an existing VPN and its reachable addresses.
See [Compose network discovery](https://docs.docker.com/compose/how-tos/networking/).
