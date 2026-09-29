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

## Updates

After configuring matching authentication and transport on the UI and agent:

```sh
docker compose pull agent
docker compose up -d agent
docker compose logs --tail=100 agent
```

The agent does not need database migrations.
