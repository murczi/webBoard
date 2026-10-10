# Agent TLS certificates

[Prerequisite guides](README.md) · [Agent configuration](../agent/README.md)

This recipe creates a private CA, a server certificate, and the PFX expected by
the agent. Browser-facing UI HTTPS is covered separately in
[HTTPS with Caddy](https-proxy.md). Use OpenSSL 3 and Bash. Run certificate creation
on your CA administration machine, in a private directory outside the repository.
For one machine, this can be the deployment directory. Keep the CA private key
on that administration machine; the agent needs only the PFX and the UI only the
public CA certificate.

## Create the private CA once

The block refuses to replace an existing CA. Keep this CA for renewals and
additional agents. OpenSSL prompts for a password protecting its private key.
Certificate requests and extensions use
[openssl req](https://docs.openssl.org/master/man1/openssl-req/).

```bash
bash <<'CA'
set -euo pipefail
umask 077
mkdir -p tls/ca
if [ -e tls/ca/agent-ca.key ] || [ -e tls/ca/agent-ca.crt ]; then
  printf '%s\n' 'CA already exists; keep it and continue to server issuance.' >&2
  exit 1
fi
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 \
  -keyout tls/ca/agent-ca.key -out tls/ca/agent-ca.crt \
  -subj '/CN=webBoard Agent CA' \
  -addext 'basicConstraints=critical,CA:TRUE' \
  -addext 'keyUsage=critical,keyCertSign,cRLSign' \
  -addext 'subjectKeyIdentifier=hash'
CA
```

## Issue the agent certificate and export PFX

Change `webboard_agent_dns` to the name used in the UI's agent URL. Keep `agent`
for the complete one-machine Compose example (`https://agent:8443`). For separate
machines, use a real resolvable name such as `agent.example.com`. If the URL uses
an IP, replace `DNS:...` in `subjectAltName` with `IP:10.0.0.30`; the CN alone
is insufficient. Each agent should have its own certificate and private key.

Run alongside the existing `tls/ca` directory. The commands prompt for the CA key
password and then a PFX export password. Put the latter in
`Kestrel__Certificates__Default__Password` in Compose. Issuance and PFX export use
[openssl x509](https://docs.openssl.org/master/man1/openssl-x509/) and
[openssl pkcs12](https://docs.openssl.org/master/man1/openssl-pkcs12/).

```bash
bash <<'CERT'
set -euo pipefail
umask 077
webboard_agent_dns=agent
for webboard_file in tls/agent.key tls/agent.csr tls/agent.crt tls/agent.pfx; do
  if [ -e "$webboard_file" ]; then
    printf 'Existing file: %s; use a fresh issuance directory.\n' "$webboard_file" >&2
    exit 1
  fi
done
openssl req -new -newkey rsa:3072 -noenc \
  -keyout tls/agent.key -out tls/agent.csr -subj "/CN=$webboard_agent_dns"
cat > tls/agent.ext <<EXT
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$webboard_agent_dns
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
EXT
openssl x509 -req -in tls/agent.csr \
  -CA tls/ca/agent-ca.crt -CAkey tls/ca/agent-ca.key \
  -set_serial "0x$(openssl rand -hex 16)" -days 365 -sha256 \
  -extfile tls/agent.ext -out tls/agent.crt
openssl verify -CAfile tls/ca/agent-ca.crt -purpose sslserver \
  -verify_hostname "$webboard_agent_dns" tls/agent.crt
openssl pkcs12 -export -out tls/agent.pfx \
  -inkey tls/agent.key -in tls/agent.crt -certfile tls/ca/agent-ca.crt
CERT
```

For IP certificates, change the verification option to `-verify_ip 10.0.0.30`.
Transfer `tls/agent.pfx` to the agent's deployment directory with an authenticated
file-transfer tool such as `scp`; preserve the destination path `./tls/agent.pfx`.
Transfer `tls/ca/agent-ca.crt` to the UI host as `agent-ca.crt`. Do not transfer the
CA private key to either container.

On the agent host, from its Compose directory, grant the configured agent UID
access (1654 is the supplied image's default; change it if using `user`):

```bash
sudo chown 1654:1654 tls/agent.pfx
sudo chmod 0600 tls/agent.pfx
```

## Trust the CA in the UI image

On the UI host, place the public `agent-ca.crt` in the Compose directory. The
following creates a temporary build context containing only that certificate;
it avoids the project's intentional exclusion of `tls/` from Docker builds.
The `.crt` location and trust refresh follow the
[Debian update-ca-certificates manual](https://manpages.debian.org/bookworm/ca-certificates/update-ca-certificates.8.en.html):

```bash
bash <<'TRUST'
set -euo pipefail
webboard_trust_context=$(mktemp -d)
trap 'rm -rf "$webboard_trust_context"' EXIT
install -m 0644 agent-ca.crt "$webboard_trust_context/agent-ca.crt"
docker build --pull -t webboard-ui:trusted -f - "$webboard_trust_context" <<'DOCKERFILE'
FROM shujidev/webboard:latest
USER root
COPY agent-ca.crt /usr/local/share/ca-certificates/agent-ca.crt
RUN update-ca-certificates
USER $APP_UID
DOCKERFILE
TRUST
```

In the UI service of your existing Compose file, change `image` and add
`pull_policy` alongside it (keep the rest of the service configuration):

```yaml
services:
  ui:
    image: webboard-ui:trusted
    pull_policy: never
```

This is a local image, not a Docker Hub repository. Build it on the same Docker
host that runs the UI. `pull_policy: never` uses the local image and fails if it
is missing; see [Compose pull policy](https://docs.docker.com/reference/compose-file/services/#pull_policy).
Keep the agent URL, token, and private-network allowlist configured.

For the complete one-machine stack, prepare the other images with:

```bash
docker image inspect webboard-ui:trusted >/dev/null && docker compose pull database agent
```

For UI-only Compose, skip the pull step entirely. Continue with the root or UI
guide's migration/start commands. Avoid a blanket `docker compose pull` with a
local UI image; select the published services by name, as in
[Compose pull](https://docs.docker.com/reference/cli/docker/compose/pull/).

For upgrades, rerun the trust-image build above to download the updated base
image, then follow the migration/recreation steps. Keep `pull_policy: never` and
skip `docker compose pull ui`. Pin the `FROM` tag if using a pinned release.

If you see `pull access denied for webboard-ui`, use the selective pull command
above. `docker login` will not make this local image available in a registry.
The interrupted database/agent downloads can be retried with that command.

## Verify and renew

From a machine that can reach the agent, with the public CA certificate available,
run in Bash. Change the URL to the real DNS name and published port:

```bash
read -r -s -p 'Agent token: ' WEBBOARD_AGENT_TOKEN
printf '\n'
curl --fail --cacert agent-ca.crt \
  --header "Authorization: Bearer $WEBBOARD_AGENT_TOKEN" \
  https://agent.example.com:5081/health
unset WEBBOARD_AGENT_TOKEN
```

For the one-machine example, run the host probe with
`--resolve agent:5081:127.0.0.1` and URL `https://agent:5081/health`; the UI's
internal URL remains `https://agent:8443`. Do not bypass certificate checks.

Before the 365-day server certificate expires, issue a replacement in a fresh
private directory using the same CA, install the new PFX, and recreate the agent
with `docker compose up -d --force-recreate agent` so it reloads the certificate.
Keep the old PFX for recovery until the authenticated probe succeeds. CA renewal
requires updating the UI trust image as well. Check expiry with:

```bash
openssl x509 -in tls/agent.crt -noout -dates
```

## Certificates created by the guided installer

The guided installer stores each component's CA separately. Agent CA files are
`tls/ca/agent/ca.crt`, `tls/ca/agent/ca.key`, and `tls/ca/agent/password`; the PFX
export password is `tls/pfx-password`. A standalone agent certificate includes
its prompted remote hostname/IP as well as `agent` for local Docker access.
Database CA files are under `tls/ca/database/`; the public database CA is also
exported as `tls/database-ca.crt`, and its server files are `tls/database/server.*`.
Do not run the earlier CA-creation recipe over an installer-generated deployment.

To renew the installer-generated server certificates using their existing keys,
CA, and saved SAN extensions, run this in Bash from its deployment directory.
It handles whichever TLS components were installed and leaves CA trust/tokens
stable. Schedule a maintenance window: the lifecycle helper stops the UI and
recreates selected services. Back up PostgreSQL first if it is installed.

```bash
bash <<'RENEW'
set -euo pipefail
umask 077
webboard_renewal_dir=$(mktemp -d)
trap 'rm -rf "$webboard_renewal_dir"' EXIT
for webboard_component in database agent; do
  if [[ "$webboard_component" == database ]]; then
    webboard_prefix=tls/database/server
  else
    webboard_prefix=tls/agent
  fi
  [[ -f "$webboard_prefix.csr" ]] || continue
  webboard_ca="tls/ca/$webboard_component"
  openssl x509 -req -in "$webboard_prefix.csr" \
    -CA "$webboard_ca/ca.crt" -CAkey "$webboard_ca/ca.key" \
    -passin "file:$webboard_ca/password" \
    -set_serial "0x$(openssl rand -hex 16)" -days 365 -sha256 \
    -extfile "$webboard_prefix.ext" -out "$webboard_renewal_dir/$webboard_component.crt"
  openssl verify -CAfile "$webboard_ca/ca.crt" -purpose sslserver \
    "$webboard_renewal_dir/$webboard_component.crt"
  if [[ "$webboard_component" == agent ]]; then
    openssl pkcs12 -export -out "$webboard_renewal_dir/agent.pfx" \
      -inkey tls/agent.key -in "$webboard_renewal_dir/agent.crt" \
      -certfile "$webboard_ca/ca.crt" -passout file:tls/pfx-password
    install -m 0644 "$webboard_renewal_dir/agent.pfx" tls/agent.pfx
  fi
  install -m 0600 "$webboard_renewal_dir/$webboard_component.crt" "$webboard_prefix.crt"
done
bash ./manage.sh start
RENEW
```

The database helper resolves its PostgreSQL UID inside the image and copies the
renewed server key/certificate into its private named TLS volume before restart.
The agent and proxy are recreated so replaced bind-mounted files are loaded.
If changing an agent/database hostname, update its saved `.ext` SAN list before
issuance and update the corresponding UI connection/profile. CA rotation is a
separate operation requiring new public CA trust on all affected UI hosts.
