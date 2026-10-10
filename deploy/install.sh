#!/usr/bin/env bash
# Guided single-host deployment of published webBoard images.
set -Eeuo pipefail
umask 077

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
usage() {
    cat <<'HELP'
Usage: bash install.sh [--directory PATH] [--port PORT] [--resume]

Interactive single-host installer for UI, PostgreSQL, HTTPS, and an optional agent.
Prompts for deployment settings and generates all passwords, tokens, and TLS keys.
Offers prerequisite installation on fresh Ubuntu/Debian hosts (requires sudo).
Other hosts require Bash, OpenSSL, Docker Engine, and Compose with up --wait.

  --directory PATH  Deployment directory (default: $HOME/webboard)
  --port PORT       Loopback HTTP upstream port (default: 8080)
  --resume          Retry a generated installation without changing its secrets
  -h, --help        Show this help

Public HTTPS requires DNS pointing at this host and reachable ports 80/443.
Local HTTPS requires trusting the generated public CA on each browser device.
Existing deployments are never overwritten. One webboard stack per Docker host.
HELP
}
ask() {
    local prompt_response
    printf '%s [%s]: ' "$2" "$3" >&2
    IFS= read -r prompt_response || fail 'Input ended; run interactively with Bash.'
    printf -v "$1" '%s' "${prompt_response:-$3}"
}
yesno() {
    local answer
    while true; do
        ask answer "$1 (y/n)" "$2"
        case "$answer" in [Yy]|[Yy][Ee][Ss]) return 0;; [Nn]|[Nn][Oo]) return 1;; esac
        printf 'Enter y or n.\n' >&2
    done
}
valid_port() { [[ "$1" =~ ^[0-9]{1,5}$ ]] && ((10#$1 >= 1 && 10#$1 <= 65535)); }
valid_ipv4() {
    local octet; local -a octets
    [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || return 1
    IFS=. read -r -a octets <<< "$1"
    for octet in "${octets[@]}"; do
        [[ ${#octet} -le 3 ]] && ((10#$octet <= 255)) || return 1
    done
}
valid_cidr() {
    [[ "$1" == */* ]] && valid_ipv4 "${1%/*}" &&
        [[ "${1#*/}" =~ ^[0-9]{1,2}$ ]] && ((10#${1#*/} <= 32))
}
valid_hostname() {
    local label; local -a labels
    [[ ${#1} -le 253 && "$1" =~ ^[A-Za-z0-9.-]+$ && "$1" != *..* && "$1" != .* && "$1" != *. ]] || return 1
    IFS=. read -r -a labels <<< "$1"
    for label in "${labels[@]}"; do
        [[ ${#label} -le 63 && "$label" != -* && "$label" != *- ]] || return 1
    done
}
root_command() {
    if ((EUID == 0)); then "$@"; else sudo "$@"; fi
}
install_prerequisites() {
    [[ -r /etc/os-release ]] || fail 'Install Docker and OpenSSL using your OS package manager first.'
    # shellcheck disable=SC1091
    . /etc/os-release
    [[ "$ID" == ubuntu || "$ID" == debian ]] || fail 'Automatic prerequisite installation supports Ubuntu/Debian only.'
    if ((EUID != 0)); then command -v sudo >/dev/null || fail 'sudo is required for package installation.'; fi
    yesno 'Install missing prerequisites with apt (system packages and Docker repository)' y || fail 'Install prerequisites and rerun.'
    root_command apt-get update
    root_command apt-get install -y ca-certificates curl openssl
    if ! command -v docker >/dev/null; then
        local package
        for package in docker.io docker-compose docker-compose-v2 docker-doc podman-docker containerd runc; do
            if dpkg-query -W -f='${Status}' "$package" 2>/dev/null | rg_installed; then
                fail "Conflicting package $package is installed. Follow Docker's conflict-removal guide before retrying."
            fi
        done
        root_command install -m 0755 -d /etc/apt/keyrings
        root_command curl -fsSL "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/docker.asc
        root_command chmod a+r /etc/apt/keyrings/docker.asc
        [[ ! -e /etc/apt/sources.list.d/docker.sources ]] || fail 'Docker repository file already exists; finish Docker installation manually.'
        local repository
        repository=$(mktemp)
        cat > "$repository" <<REPO
Types: deb
URIs: https://download.docker.com/linux/$ID
Suites: ${UBUNTU_CODENAME:-${VERSION_CODENAME:?Missing OS release codename}}
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
REPO
        root_command install -m 0644 "$repository" /etc/apt/sources.list.d/docker.sources
        rm -f -- "$repository"
        root_command apt-get update
        root_command apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
        root_command systemctl enable --now docker
    elif ! docker compose version >/dev/null 2>&1; then
        # Use an available v2 plugin package without replacing an existing engine.
        if apt-cache show docker-compose-plugin >/dev/null 2>&1; then
            root_command apt-get install -y docker-compose-plugin
        elif apt-cache show docker-compose-v2 >/dev/null 2>&1; then
            root_command apt-get install -y docker-compose-v2
        else
            fail 'No Compose v2 package available. Configure the Docker repository using the Docker host guide and retry.'
        fi
    fi
}
# Avoid depending on ripgrep on a fresh host.
rg_installed() { local status; IFS= read -r status || true; [[ "$status" == 'install ok installed' ]]; }

deployment_dir="${HOME:?HOME must be set}/webboard"
ui_port=8080
resume=false
directory_given=false
port_given=false
while (($#)); do
    case "$1" in
        --directory) (($# >= 2)) && [[ -n "$2" ]] || fail '--directory requires a path'; deployment_dir=$2; directory_given=true; shift 2;;
        --port) (($# >= 2)) || fail '--port requires a number'; ui_port=$2; port_given=true; shift 2;;
        --resume) resume=true; shift;;
        -h|--help) usage; exit 0;;
        *) fail "Unknown option: $1 (use --help)";;
    esac
done
valid_port "$ui_port" || fail 'Port must be between 1 and 65535'
if ! $resume; then
    printf '\nwebBoard guided installer — UI, database, HTTPS, and optional host agent\n\n'
    $directory_given || ask deployment_dir 'Deployment directory' "$deployment_dir"
    if ! $port_given; then
        while true; do
            ask ui_port 'Loopback UI HTTP port' "$ui_port"
            valid_port "$ui_port" && break
            printf 'Enter a port from 1 to 65535.\n'
        done
    fi
fi
ui_port=$((10#$ui_port))
mkdir -p -- "$deployment_dir"
cd -- "$deployment_dir"
deployment_dir=$PWD
if $resume; then
    [[ -f .webboard-installer && -f compose.yaml && -f manage.sh ]] || fail 'No installer-generated deployment to resume in this directory.'
else
    for existing in compose.yaml compose.yml docker-compose.yaml docker-compose.yml .webboard-installer tls trust Caddyfile manage.sh; do
        [[ ! -e "$existing" && ! -L "$existing" ]] || fail "Existing deployment artifact: $deployment_dir/$existing. Use --resume for a generated installation."
    done
fi
if ! command -v openssl >/dev/null || ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then install_prerequisites; fi
command -v openssl >/dev/null || fail 'OpenSSL is required.'
command -v docker >/dev/null || fail 'Docker is required.'
docker_cmd=(docker)
if ! docker info >/dev/null 2>&1; then
    printf 'Docker is unavailable to this account.\n'
    yesno 'Use sudo Docker for this installation (does not change group membership)' y || fail 'Start Docker and grant this account access before rerunning.'
    command -v sudo >/dev/null || fail 'sudo is unavailable.'
    docker_cmd=(sudo docker)
    "${docker_cmd[@]}" info >/dev/null || fail 'Docker daemon is unavailable; start it and rerun.'
fi
"${docker_cmd[@]}" compose version >/dev/null || fail 'Install the Docker Compose plugin first.'
compose_help=$("${docker_cmd[@]}" compose up --help)
[[ "$compose_help" == *'--wait'* ]] || fail 'Update Docker Compose to a version supporting up --wait.'
if $resume; then
    exec bash ./manage.sh start
if [[ "$https_mode" == local ]] && command -v update-ca-certificates >/dev/null; then
    if yesno 'Install the local HTTPS CA in this host trust store (requires root/sudo; browsers may need a separate import)' n; then
        trust_destination=/usr/local/share/ca-certificates/webboard-local.crt
        if [[ -e "$trust_destination" ]] && ! cmp -s browser-ca.crt "$trust_destination"; then
            fail "A different CA already exists at $trust_destination. Import browser-ca.crt manually."
        fi
        root_command install -m 0644 browser-ca.crt "$trust_destination"
        root_command update-ca-certificates
    fi
fi
fi
existing_containers=$("${docker_cmd[@]}" ps -aq --filter label=com.docker.compose.project=webboard)
existing_volumes=$("${docker_cmd[@]}" volume ls -q --filter label=com.docker.compose.project=webboard)
[[ -z "$existing_containers" && -z "$existing_volumes" ]] || fail 'A webboard project already exists. Use its original deployment directory.'
if "${docker_cmd[@]}" network inspect webboard-private >/dev/null 2>&1; then
    fail 'Network webboard-private already exists. Resume its original installation.'
fi

printf '\nHTTPS: local = private CA; public = automatic public certificates; existing = your own proxy.\n'
while true; do
    ask https_mode 'HTTPS mode (local/public/existing)' local
    case "$https_mode" in local|public|existing) break;; esac
    printf 'Choose local, public, or existing.\n'
done
ui_host=localhost
https_port=443
proxy_bind=127.0.0.1
if [[ "$https_mode" != existing ]]; then
    while true; do
        ask ui_host 'Browser DNS hostname (no scheme or port)' "$([[ "$https_mode" == local ]] && printf localhost || printf board.example.com)"
        valid_hostname "$ui_host" && break
        printf 'Enter a valid DNS hostname.\n'
    done
    if [[ "$https_mode" == public ]]; then
        [[ "$ui_host" == *.* && "$ui_host" != localhost && "$ui_host" != *.localhost && "$ui_host" != *.local ]] && ! valid_ipv4 "$ui_host" || fail 'Public HTTPS needs a public DNS name.'
        printf 'Point DNS for %s at this host and allow/forward TCP 80 and 443.\n' "$ui_host"
        yesno 'DNS and inbound ports are ready' n || fail 'Prepare DNS/ports, then rerun the installer.'
        proxy_bind=0.0.0.0
    else
        while true; do
            ask proxy_bind 'HTTPS bind IPv4 (use a private/VPN IP for remote browsers)' 127.0.0.1
            valid_ipv4 "$proxy_bind" && break
            printf 'Enter an IPv4 address assigned to this host.\n'
        done
        while true; do
            ask https_port 'HTTPS port' 443
            valid_port "$https_port" && break
        done
        https_port=$((10#$https_port))
        ((https_port != ui_port)) || fail 'HTTPS and HTTP upstream ports must differ.'
    fi
fi
install_agent=false
monitor_docker=false
monitor_systemd=false
docker_targets=()
if yesno 'Install a host monitoring agent on this machine' y; then
    install_agent=true
    [[ "$(uname -s)" == Linux ]] || fail 'Host agents require Linux.'
    if [[ -S /var/run/docker.sock ]] && yesno 'Enable Docker monitoring (mounts the host Docker socket)' y; then
        monitor_docker=true
        docker_gid=$(stat -c '%g' /var/run/docker.sock)
        printf 'Docker controls are optional; only explicitly listed containers can be controlled.\n'
        while true; do
            ask target 'Container name or full ID to allow start/stop/restart (blank finishes)' ''
            [[ -n "$target" ]] || break
            [[ "$target" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || { printf 'Enter a container name or ID.\n'; continue; }
            docker_targets+=("$target")
        done
    fi
    if [[ -S /run/dbus/system_bus_socket && -d /run/systemd/system ]] && yesno 'Enable systemd monitoring (service controls stay disabled)' y; then
        monitor_systemd=true
    fi
    $monitor_docker || $monitor_systemd || fail 'No host monitoring source selected or available.'
fi
private_networks=()
printf '\nThe agent Docker subnet will be allowed automatically.\n'
while true; do
    ask cidr 'Additional private monitoring IPv4 CIDR (blank finishes)' ''
    [[ -n "$cidr" ]] || break
    valid_cidr "$cidr" || { printf 'Enter an IPv4 CIDR, e.g. 10.0.0.0/24.\n'; continue; }
    private_networks+=("$cidr")
done
printf '\nInstalling in %s with HTTPS mode %s; host agent: %s.\n' "$deployment_dir" "$https_mode" "$install_agent"
printf 'Passwords, JWT signing key, agent token, and certificate passwords are generated automatically.\n'

on_error() {
    printf '\nInstallation stopped. Configuration and data are preserved in %s.\n' "$deployment_dir" >&2
    if [[ -f manage.sh ]]; then
        printf 'Inspect logs with: bash %q logs\nRetry with: bash %q start\n' "$deployment_dir/manage.sh" "$deployment_dir/manage.sh" >&2
    else
        printf 'Configuration generation did not finish. Keep this directory for recovery; use a new directory to retry.\n' >&2
    fi
}
trap on_error ERR
network_created=false
cleanup_partial_network() {
    # No services can have started before manage.sh exists. Keep TLS files for
    # recovery, but release the empty network so a fresh-directory retry works.
    if $network_created && [[ ! -f manage.sh ]]; then
        "${docker_cmd[@]}" network rm webboard-private >/dev/null 2>&1 || true
    fi
}
trap cleanup_partial_network EXIT
# Protect against concurrent installers using this directory.
set -o noclobber
printf 'webBoard installer v2\n' > .webboard-installer
set +o noclobber
# Let Docker allocate an unused subnet, then allow that exact subnet in the UI.
"${docker_cmd[@]}" network create --label com.docker.compose.project=webboard webboard-private >/dev/null
network_created=true
agent_subnet=$("${docker_cmd[@]}" network inspect --format '{{(index .IPAM.Config 0).Subnet}}' webboard-private)
valid_cidr "$agent_subnet" || fail 'Docker did not allocate an IPv4 subnet.'
database_password=$(openssl rand -hex 32)
signing_key=$(openssl rand -hex 32)
ui_image=shujidev/webboard:latest
if $install_agent; then
    agent_token=$(openssl rand -hex 32)
    pfx_password=$(openssl rand -hex 32)
    ca_password=$(openssl rand -hex 32)
    mkdir -p tls/ca trust
    printf '%s\n' "$ca_password" > tls/ca/password
    printf '%s\n' "$pfx_password" > tls/pfx-password
    openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 \
        -passout file:tls/ca/password -keyout tls/ca/agent-ca.key -out tls/ca/agent-ca.crt \
        -subj '/CN=webBoard Agent CA' -addext 'basicConstraints=critical,CA:TRUE' \
        -addext 'keyUsage=critical,keyCertSign,cRLSign' -addext 'subjectKeyIdentifier=hash'
    openssl req -new -newkey rsa:3072 -nodes -keyout tls/agent.key -out tls/agent.csr -subj '/CN=agent'
    cat > tls/agent.ext <<'EXT'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:agent
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
EXT
    openssl x509 -req -in tls/agent.csr -CA tls/ca/agent-ca.crt -CAkey tls/ca/agent-ca.key \
        -passin file:tls/ca/password -set_serial "0x$(openssl rand -hex 16)" \
        -days 365 -sha256 -extfile tls/agent.ext -out tls/agent.crt
    openssl verify -CAfile tls/ca/agent-ca.crt -purpose sslserver -verify_hostname agent tls/agent.crt
    openssl pkcs12 -export -out tls/agent.pfx -inkey tls/agent.key -in tls/agent.crt \
        -certfile tls/ca/agent-ca.crt -passout file:tls/pfx-password
    # Encrypted PFX is readable by the non-root container; private parent directory
    # prevents other host accounts from reading it. Unencrypted key stays mode 0600.
    chmod 0644 tls/agent.pfx
    cp tls/ca/agent-ca.crt trust/agent-ca.crt
    chmod 0644 trust/agent-ca.crt
    cat > trust/Dockerfile <<'DOCKERFILE'
FROM shujidev/webboard:latest
USER root
COPY agent-ca.crt /usr/local/share/ca-certificates/agent-ca.crt
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && update-ca-certificates \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
DOCKERFILE
    ui_image=webboard-ui:trusted
fi
cat > compose.yaml <<COMPOSE
# Generated by webBoard. Contains secrets; keep private and preserve for upgrades.
name: webboard
services:
  database:
    image: postgres:18.4-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: dashboard
      POSTGRES_USER: dashboard
      POSTGRES_PASSWORD: "$database_password"
    volumes:
      - dashboard-postgres-data:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dashboard -d dashboard"]
      interval: 5s
      timeout: 5s
      retries: 20
  ui:
    image: $ui_image
COMPOSE
if $install_agent; then
    cat >> compose.yaml <<'COMPOSE'
    pull_policy: never
    build:
      context: ./trust
COMPOSE
fi
cat >> compose.yaml <<COMPOSE
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__WebboardDatabase: "Host=database;Port=5432;Database=dashboard;Username=dashboard;Password=$database_password"
      Authentication__Jwt__SigningKey: "$signing_key"
      Monitoring__IntervalSeconds: 30
      Monitoring__RetentionDays: 30
COMPOSE
network_index=0
if $install_agent; then
    cat >> compose.yaml <<COMPOSE
      Agents__0__BaseUrl: https://agent:8443
      Agents__0__Token: "$agent_token"
      Outbound__AllowedPrivateNetworks__0: "$agent_subnet"
COMPOSE
    network_index=1
fi
for cidr in "${private_networks[@]}"; do
    printf '      Outbound__AllowedPrivateNetworks__%s: "%s"\n' "$network_index" "$cidr" >> compose.yaml
    network_index=$((network_index + 1))
done
cat >> compose.yaml <<COMPOSE
    ports:
      - "127.0.0.1:$ui_port:8080"
    depends_on:
      database:
        condition: service_healthy
COMPOSE
if $install_agent; then
    cat >> compose.yaml <<COMPOSE
  agent:
    image: shujidev/webboard-agent:latest
    restart: unless-stopped
    environment:
      ASPNETCORE_URLS: https://+:8443
      Security__Tokens__0: "$agent_token"
      Kestrel__Certificates__Default__Path: /tls/agent.pfx
      Kestrel__Certificates__Default__Password: "$pfx_password"
COMPOSE
    for ((i=0; i<${#docker_targets[@]}; i++)); do
        printf '      Controls__Docker__%s: "%s"\n' "$i" "${docker_targets[i]}" >> compose.yaml
    done
    if $monitor_docker; then
        printf '    group_add:\n      - "%s"\n' "$docker_gid" >> compose.yaml
    fi
    cat >> compose.yaml <<'COMPOSE'
    # No host port: agent is reachable only by services in the private Docker network.
    volumes:
      - type: bind
        source: ./tls/agent.pfx
        target: /tls/agent.pfx
        read_only: true
        bind:
          create_host_path: false
COMPOSE
    if $monitor_docker; then
        cat >> compose.yaml <<'COMPOSE'
      - type: bind
        source: /var/run/docker.sock
        target: /var/run/docker.sock
        bind:
          create_host_path: false
COMPOSE
    fi
    if $monitor_systemd; then
        cat >> compose.yaml <<'COMPOSE'
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
COMPOSE
    fi
fi
if [[ "$https_mode" != existing ]]; then
    if [[ "$https_mode" == local ]]; then
        cat > Caddyfile <<CONFIG
{
    auto_https disable_redirects
}
https://$ui_host {
    tls internal
    reverse_proxy ui:8080
}
CONFIG
    else
        cat > Caddyfile <<CONFIG
$ui_host {
    reverse_proxy ui:8080
}
CONFIG
    fi
    cat >> compose.yaml <<COMPOSE
  proxy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "$proxy_bind:$https_port:443"
COMPOSE
    [[ "$https_mode" != public ]] || printf '      - "0.0.0.0:80:80"\n' >> compose.yaml
    cat >> compose.yaml <<'COMPOSE'
    volumes:
      - type: bind
        source: ./Caddyfile
        target: /etc/caddy/Caddyfile
        read_only: true
        bind:
          create_host_path: false
      - caddy-data:/data
      - caddy-config:/config
    depends_on:
      - ui
COMPOSE
fi
cat >> compose.yaml <<'COMPOSE'
networks:
  default:
    external: true
    name: webboard-private
volumes:
  dashboard-postgres-data:
COMPOSE
[[ "$https_mode" == existing ]] || printf '  caddy-data:\n  caddy-config:\n' >> compose.yaml

# A portable lifecycle helper: keeps trusted image builds and migrations ordered,
# and retries safely with the same generated credentials after interruptions.
cat > manage.sh <<'MANAGE'
#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
docker_cmd=(docker)
if ! docker info >/dev/null 2>&1; then docker_cmd=(sudo docker); fi
compose=("${docker_cmd[@]}" compose --project-name webboard --project-directory "$PWD" --file "$PWD/compose.yaml")
trap 'printf "Command failed. Configuration/data are preserved; inspect with bash manage.sh logs.\n" >&2' ERR
case "${1:-start}" in
    start|update)
        "${compose[@]}" config --quiet
        services=$("${compose[@]}" config --services)
        published=()
        while IFS= read -r service; do
            [[ "$service" != ui || ! -f trust/Dockerfile ]] || continue
            published+=("$service")
        done <<< "$services"
        "${compose[@]}" pull "${published[@]}"
        if [[ -f trust/Dockerfile ]]; then "${compose[@]}" build --pull ui; fi
        "${compose[@]}" up -d --wait --wait-timeout 120 database
        # Stop the UI before migrations, including when resuming/upgrading.
        "${compose[@]}" stop ui
        "${compose[@]}" run --rm --no-deps ui --migrate
        if [[ "$services" == *proxy* ]]; then
            "${compose[@]}" run --rm --no-deps proxy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
        fi
        "${compose[@]}" up -d --wait --wait-timeout 120
        if [[ -f tls/agent.crt ]]; then
            # Probe from inside the trusted UI image; no insecure TLS bypass.
            "${compose[@]}" exec -T ui bash -c 'for attempt in {1..30}; do
                if curl --fail --silent --show-error --header "Authorization: Bearer $Agents__0__Token" "$Agents__0__BaseUrl/health"; then exit 0; fi
                sleep 2
            done; exit 1'
        fi
        if [[ -f local-https ]]; then
            # Caddy creates its CA asynchronously. Export only the public root.
            for ((attempt=0; attempt<30; attempt++)); do
                if "${compose[@]}" cp proxy:/data/caddy/pki/authorities/local/root.crt ./browser-ca.crt 2>/dev/null; then break; fi
                sleep 2
            done
            [[ -s browser-ca.crt ]] || { printf 'Could not export browser CA; inspect proxy logs.\n' >&2; exit 1; }
            chmod 0644 browser-ca.crt
        fi
        "${compose[@]}" ps
        cat NEXT-STEPS.txt
        ;;
    logs) "${compose[@]}" logs --tail=100;;
    stop) "${compose[@]}" down;;
    *) printf 'Usage: bash manage.sh [start|update|logs|stop]\n' >&2; exit 1;;
esac
MANAGE
chmod 0700 manage.sh
if [[ "$https_mode" == local ]]; then printf 'local HTTPS\n' > local-https; fi
if [[ "$https_mode" == existing ]]; then
    ui_url="your existing HTTPS proxy (upstream http://127.0.0.1:$ui_port)"
else
    ui_url="https://$ui_host"
    ((https_port == 443)) || ui_url="$ui_url:$https_port"
fi
cat > NEXT-STEPS.txt <<STEPS

webBoard services started. Browser address: $ui_url
Deployment directory: $deployment_dir
Register your administrator account first; registration is completed in the browser.
STEPS
if [[ "$https_mode" == local ]]; then
    cat >> NEXT-STEPS.txt <<STEPS
Local HTTPS uses a private CA. Import $deployment_dir/browser-ca.crt
as a trusted certificate authority on each browser device, then restart the browser.
For remote access, make $ui_host resolve to this host's HTTPS bind address.
Only the public browser-ca.crt should be transferred to client devices.
STEPS
elif [[ "$https_mode" == public ]]; then
    printf 'Caddy obtains/renews public certificates; if issuance is pending, inspect proxy logs.\n' >> NEXT-STEPS.txt
else
    printf 'Configure your HTTPS proxy before browser registration/login.\n' >> NEXT-STEPS.txt
fi
if $install_agent; then
    cat >> NEXT-STEPS.txt <<'STEPS'
In Host management, add this machine with agent URL https://agent:8443.
Its matching token, CA trust, and exact Docker subnet are already configured in the UI.
The agent certificate expires in 365 days; renew using the agent TLS guide.
Service controls need the optional host polkit setup in the agent guide.
STEPS
fi
cat >> NEXT-STEPS.txt <<'STEPS'
Keep Compose, TLS keys/passwords, database volume, and Caddy volumes backed up privately.
Back up PostgreSQL before upgrading; run bash manage.sh update to build/pull and migrate.
Retry an interrupted install: bash manage.sh start. Inspect: bash manage.sh logs.
Stop without deleting database/certificate volumes: bash manage.sh stop.
Guides: https://github.com/murczi/webBoard/tree/main/deploy/docs
STEPS
bash ./manage.sh start
if [[ "$https_mode" == local ]] && command -v update-ca-certificates >/dev/null; then
    if yesno 'Install the local HTTPS CA in this host trust store (requires root/sudo; browsers may need a separate import)' n; then
        trust_destination=/usr/local/share/ca-certificates/webboard-local.crt
        if [[ -e "$trust_destination" ]] && ! cmp -s browser-ca.crt "$trust_destination"; then
            fail "A different CA already exists at $trust_destination. Import browser-ca.crt manually."
        fi
        root_command install -m 0644 browser-ca.crt "$trust_destination"
        root_command update-ca-certificates
    fi
fi
