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

Change the UI Compose `image` to `webboard-ui:trusted`. Keep its agent URL, token,
and private-network allowlist configured as in the agent guide. Run migrations
and start/recreate the UI using that guide. For upgrades, rebuild this derived
image first, then migrate and recreate the UI; skip `docker compose pull ui` for
this local image. Pin the `FROM` tag if your deployment uses a pinned release.

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
