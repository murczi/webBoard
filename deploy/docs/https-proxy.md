# UI HTTPS with Caddy

[Prerequisite guides](README.md) · [UI installation](../ui/README.md)

Run Caddy as a host service on the same Ubuntu/Debian machine as the UI. This
matches the project's `127.0.0.1:8080:8080` mapping: the public proxy serves HTTPS
and forwards to the loopback HTTP endpoint. Start/migrate the UI first. Ports 80
and 443 must be free; this recipe installs a new proxy configuration. If you
already have a proxy, add an equivalent route to its existing configuration.

## Install the host service

Run in Bash with sudo access. These commands use the repository from
[Caddy's official installation instructions](https://caddyserver.com/docs/install#debian-ubuntu-raspbian).

```bash
bash <<'INSTALL'
set -euo pipefail
sudo apt-get update
sudo apt-get install -y debian-keyring debian-archive-keyring apt-transport-https curl gnupg
curl -fsSL https://dl.cloudsmith.io/public/caddy/stable/gpg.key \
  | sudo gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -fsSL https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt \
  | sudo tee /etc/apt/sources.list.d/caddy-stable.list >/dev/null
sudo chmod a+r /usr/share/keyrings/caddy-stable-archive-keyring.gpg /etc/apt/sources.list.d/caddy-stable.list
sudo apt-get update
sudo apt-get install -y caddy
INSTALL
```

Choose **one** configuration below. Both blocks back up the existing Caddyfile
before replacing it, validate the new file, and reload the service. The upstream
address is the UI port on this host, as described in
[Caddy's reverse proxy reference](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).

## Option A: public DNS name

Set a DNS A record for your domain to the UI host's reachable public IPv4 address.
If you add an AAAA record, IPv6 must also reach that host. Allow/forward incoming
TCP ports 80 and 443 at your router, cloud firewall, and host firewall. DNS and
router changes happen in your provider's interface; the commands cannot create
those records for you. Caddy obtains and renews certificates automatically when
the domain and ports are reachable, as explained in
[Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https).

Change the domain in the first assignment:

```bash
bash <<'PUBLIC'
set -euo pipefail
webboard_ui_domain=board.example.com
sudo cp -a /etc/caddy/Caddyfile "/etc/caddy/Caddyfile.backup.$(date +%s)"
sudo tee /etc/caddy/Caddyfile >/dev/null <<CONFIG
$webboard_ui_domain {
    reverse_proxy 127.0.0.1:8080
}
CONFIG
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo systemctl enable --now caddy
sudo systemctl reload caddy
curl --fail --output /dev/null --write-out 'UI HTTPS status: %{http_code}\n' "https://$webboard_ui_domain/"
PUBLIC
```

If the first request races certificate issuance, inspect the logs below and retry
once issuance succeeds. Open `https://board.example.com` using your chosen domain.

## Option B: local HTTPS without a public domain

For use from a browser on the UI host, Caddy can issue a local certificate. This
configuration binds HTTPS to loopback and disables the unnecessary HTTP redirect
listener. Caddy's private CA must be trusted by the browser device; local
certificates are not publicly trusted. See
[Caddy local HTTPS](https://caddyserver.com/docs/automatic-https#local-https).

```bash
bash <<'LOCAL'
set -euo pipefail
sudo cp -a /etc/caddy/Caddyfile "/etc/caddy/Caddyfile.backup.$(date +%s)"
sudo tee /etc/caddy/Caddyfile >/dev/null <<'CONFIG'
{
    auto_https disable_redirects
}
https://localhost {
    bind 127.0.0.1
    tls internal
    reverse_proxy 127.0.0.1:8080
}
CONFIG
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo systemctl enable --now caddy
sudo systemctl reload caddy
LOCAL
```

Once Caddy has generated its CA, trust its public root in the Ubuntu/Debian host
store and verify the endpoint, using
[update-ca-certificates](https://manpages.debian.org/bookworm/ca-certificates/update-ca-certificates.8.en.html):

```bash
sudo install -m 0644 /var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt \
  /usr/local/share/ca-certificates/webboard-caddy.crt
sudo update-ca-certificates
curl --fail --output /dev/null --write-out 'UI HTTPS status: %{http_code}\n' https://localhost/
```

The CA path uses the packaged service's storage directory, documented in
[Caddy service usage](https://caddyserver.com/docs/running#using-the-service).
If the file is not yet present, check the logs and retry after startup completes.
Restart the browser. If it uses its own certificate store, import that public
root as a trusted certificate authority through its certificate settings. A
successful curl check does not establish browser trust. Never copy Caddy's CA
private key to browser devices.

For access from other machines, replace `localhost` with a private DNS name in
the Caddyfile, change `bind` to the host's private/VPN IP, and configure that name
to resolve to the IP on each client. Import the same public root on each browser
device. Keep `tls internal`; public DNS validation is not used for this option.

## Check and maintain

```bash
sudo systemctl status caddy --no-pager
sudo journalctl -u caddy --no-pager -n 100
docker compose port ui 8080
curl --fail --output /dev/null http://127.0.0.1:8080/
```

Run the Docker command from the UI Compose directory. A 502 response usually means
Caddy cannot reach the UI: confirm the container is running and the upstream port
matches. Public certificate failures usually need a DNS/port reachability check.
Browser trust failures on local HTTPS require importing the local public CA.

After editing the Caddyfile, validate and reload it:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo systemctl reload caddy
```

Keep Caddy's persistent service storage for certificate renewal and local CA
continuity. Restoring a backup Caddyfile also requires validation and reload.
Use HTTPS for registration/login; the UI's authentication cookies are Secure.
