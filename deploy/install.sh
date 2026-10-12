#!/usr/bin/env bash
# Guided deployment of selected webBoard components on this Docker host.
# shellcheck disable=SC2129
set -Eeuo pipefail
umask 077

fail() { printf 'Error: %s\n' "$*" >&2; exit 1; }
usage() {
    cat <<'HELP'
Usage: bash install.sh [--components db,ui,agent] [--directory PATH] [--port PORT] [--resume]
       bash install.sh --authorize-systemd SERVICE [--authorize-systemd SERVICE ...]

Choose db, ui, agent, or any comma-separated combination to install on this host.
Prompts for existing services when dependent components are installed elsewhere.
Prompts for deployment settings and generates all passwords, tokens, and TLS keys.
Offers prerequisite installation on fresh Ubuntu/Debian hosts (requires sudo).
Other hosts require Bash, OpenSSL, Docker Engine, and Compose with up --wait.

  --components LIST Components to install (otherwise prompted first)
  --directory PATH  Deployment directory (default: $HOME/webboard)
  --port PORT       Loopback HTTP upstream port (default: 8080)
  --resume          Retry a generated installation without changing its secrets
  --authorize-systemd SERVICE
                    Install host start/stop/restart policy for an existing agent
                    Repeat for each service; replaces the installer-managed list
  -h, --help        Show this help

Public HTTPS requires DNS pointing at this host and reachable ports 80/443.
Local HTTPS requires trusting the generated public CA on each browser device.
Existing installs prompt to remove (including data) or abort. One stack per host.
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
        # Leading zeroes can be interpreted as octal by .NET or rejected by Docker.
        [[ "$octet" == 0 || "$octet" != 0* ]] || return 1
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
apt_has_candidate() {
    local field value _remainder
    while read -r field value _remainder; do
        if [[ "$field" == Candidate: ]]; then [[ -n "$value" && "$value" != '(none)' ]]; return; fi
    done < <(apt-cache policy "$1")
    return 1
}
root_command() {
    if ((EUID == 0)); then "$@"; else sudo "$@"; fi
}
prepare_agent_account() {
    # D-Bus resolves the peer's numeric UID using the host account database.
    # Let the host allocate an identity instead of assuming the image's UID is free.
    local account_entry account_name account_password account_gecos account_home account_shell
    command -v getent >/dev/null || fail 'Systemd monitoring requires getent for host account lookup.'
    if ! account_entry=$(getent passwd webboard-agent); then
        command -v useradd >/dev/null || fail 'Systemd monitoring requires useradd to create the webboard-agent host account.'
        [[ -x /usr/sbin/nologin ]] || fail 'Systemd monitoring requires /usr/sbin/nologin for the host account.'
        if ((EUID != 0)); then command -v sudo >/dev/null || fail 'sudo is required to create the webboard-agent host account.'; fi
        printf 'Creating the non-login webboard-agent host account for systemd monitoring.\n'
        root_command useradd --system --user-group --no-create-home \
            --home-dir /nonexistent --shell /usr/sbin/nologin \
            --comment 'webBoard host agent' webboard-agent || fail 'Could not create the webboard-agent host account.'
        account_entry=$(getent passwd webboard-agent) || fail 'Could not look up the new webboard-agent host account.'
    fi
    IFS=: read -r account_name account_password agent_uid agent_gid account_gecos account_home account_shell <<< "$account_entry"
    [[ "$account_name" == webboard-agent && "$agent_uid" =~ ^[0-9]+$ && "$agent_gid" =~ ^[0-9]+$ ]] &&
        ((10#$agent_uid > 0 && 10#$agent_gid > 0)) || fail 'The webboard-agent host account must have a non-root UID and GID.'
    case "$account_shell" in
        */nologin|*/false) ;;
        *) fail 'The existing webboard-agent host account must have a non-login shell; refusing to use a login account.';;
    esac
}
valid_systemd_service() {
    local pattern='^[A-Za-z0-9_][A-Za-z0-9_.@:\\-]{0,249}\.service$'
    [[ ${#1} -le 256 && "$1" =~ $pattern ]]
}
ensure_polkit() {
    command -v pkcheck >/dev/null && return
    [[ -r /etc/os-release ]] || fail 'Install the host polkit package before enabling systemd controls.'
    # shellcheck disable=SC1091
    local ID
    . /etc/os-release
    [[ "$ID" == ubuntu || "$ID" == debian ]] || fail 'Install the host polkit package before enabling systemd controls.'
    printf 'Installing polkit for systemd controls.\n'
    root_command apt-get update
    local package=polkitd
    apt_has_candidate "$package" || package=policykit-1
    root_command apt-get install -y "$package"
    command -v pkcheck >/dev/null || fail 'polkit installation did not provide pkcheck.'
}
install_systemd_policy() (
    # Only the named services' runtime verbs are granted. Unit-file changes and
    # daemon reload require the separate, broader manual authorization recipe.
    local service policy_temp policy_path=/etc/polkit-1/rules.d/50-webboard-agent-installer.rules
    local marker='// Managed by webBoard installer: systemd runtime controls.'
    (($# > 0)) || fail 'Specify at least one systemd service to authorize.'
    for service in "$@"; do valid_systemd_service "$service" || fail "Invalid systemd service: $service"; done
    if ((EUID != 0)); then command -v sudo >/dev/null || fail 'sudo is required to install systemd control policy.'; fi
    ensure_polkit
    root_command test -d /etc/polkit-1/rules.d || fail 'The host polkit rules directory is missing; install polkit with JavaScript rule support.'
    if root_command test -L "$policy_path"; then fail 'Refusing to replace a symlinked systemd control policy.'; fi
    if root_command test -e "$policy_path"; then
        root_command test -f "$policy_path" &&
            [[ $(root_command head -n 1 "$policy_path") == "$marker" ]] || fail 'A different systemd control policy exists at the installer path; preserve it and configure permissions manually.'
    fi
    policy_temp=$(mktemp)
    trap 'rm -f -- "$policy_temp"' EXIT
    {
        printf '%s\n' "$marker"
        cat <<'RULE'
polkit.addRule(function(action, subject) {
    if (subject.user !== 'webboard-agent') return;
RULE
        printf '    var units = ['
        local separator=''
        for service in "$@"; do
            # Unit names may contain literal backslashes (systemd escaped names).
            printf "%s'%s'" "$separator" "${service//\\/\\\\}"
            separator=', '
        done
        cat <<'RULE'
];
    var verbs = ['start', 'stop', 'restart'];
    if (action.id === 'org.freedesktop.systemd1.manage-units' &&
        units.indexOf(action.lookup('unit')) >= 0 &&
        verbs.indexOf(action.lookup('verb')) >= 0) {
        return polkit.Result.YES;
    }
});
RULE
    } > "$policy_temp"
    root_command install -o 0 -g 0 -m 0644 "$policy_temp" "$policy_path" || fail 'Could not install the systemd control policy.'
    printf 'Installed host start/stop/restart policy for: %s\n' "$*"
)
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
        if [[ -e /etc/apt/sources.list.d/docker.sources ]]; then
            if ! cmp -s "$repository" /etc/apt/sources.list.d/docker.sources; then
                rm -f -- "$repository"
                fail 'A different Docker repository configuration exists; finish prerequisite installation manually.'
            fi
        fi
        root_command install -m 0755 -d /etc/apt/keyrings
        root_command curl -fsSL "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/docker.asc
        root_command chmod a+r /etc/apt/keyrings/docker.asc
        if [[ ! -e /etc/apt/sources.list.d/docker.sources ]]; then
            root_command install -m 0644 "$repository" /etc/apt/sources.list.d/docker.sources
        fi
        rm -f -- "$repository"
        root_command apt-get update
        root_command apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
        root_command systemctl enable --now docker
    elif ! docker compose version >/dev/null 2>&1; then
        # Use an available v2 plugin package without replacing an existing engine.
        if apt_has_candidate docker-compose-plugin; then
            root_command apt-get install -y docker-compose-plugin
        elif apt_has_candidate docker-compose-v2; then
            root_command apt-get install -y docker-compose-v2
        else
            fail 'No Compose v2 package available. Configure the Docker repository using the Docker host guide and retry.'
        fi
    fi
}
# Avoid depending on ripgrep on a fresh host.
rg_installed() { local status; IFS= read -r status || true; [[ "$status" == 'install ok installed' ]]; }


ask_secret() {
    local secret_response
    printf '%s: ' "$2" >&2
    IFS= read -r -s secret_response || fail 'Input ended while reading a secret.'
    printf '\n' >&2
    printf -v "$1" '%s' "$secret_response"
}
parse_components() {
    local entry normalized
    local -a entries
    install_database=false; install_ui=false; install_agent=false
    normalized=$(printf '%s' "$1" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')
    [[ -n "$normalized" && "$normalized" != ,* && "$normalized" != *, && "$normalized" != *,,* ]] || return 1
    IFS=, read -r -a entries <<< "$normalized"
    for entry in "${entries[@]}"; do
        case "$entry" in db|database) install_database=true;; ui) install_ui=true;; agent) install_agent=true;; *) return 1;; esac
    done
}
ask_port() {
    local port_response
    while true; do
        ask port_response "$2" "$3"
        if valid_port "$port_response"; then printf -v "$1" '%s' "$((10#$port_response))"; return; fi
        printf 'Enter a port from 1 to 65535.\n'
    done
}
ask_bind() {
    local bind_response
    while true; do
        ask bind_response "$2" "$3"
        if valid_ipv4 "$bind_response"; then printf -v "$1" '%s' "$bind_response"; return; fi
        printf 'Enter an IPv4 address assigned to this host (0.0.0.0 binds all IPv4 interfaces).\n'
    done
}
ask_host() {
    local host_response
    while true; do
        ask host_response "$2" "$3"
        if valid_ipv4 "$host_response" && [[ "$host_response" != 0.0.0.0 ]]; then printf -v "$1" '%s' "$host_response"; return; fi
        if valid_hostname "$host_response" && [[ ! "$host_response" =~ ^[0-9.]+$ ]]; then
            host_response=$(printf '%s' "$host_response" | tr '[:upper:]' '[:lower:]')
            printf -v "$1" '%s' "$host_response"; return
        fi
        printf 'Enter a DNS hostname or IPv4 without a scheme/port; 0.0.0.0 is only a bind address.\n'
    done
}
suggest_bind() {
    local address; local -a addresses
    if command -v hostname >/dev/null 2>&1; then
        read -r -a addresses <<< "$(hostname -I 2>/dev/null || true)"
        for address in "${addresses[@]}"; do
            if valid_ipv4 "$address" && [[ "$address" != 127.* ]]; then printf '%s' "$address"; return; fi
        done
    fi
    printf '127.0.0.1'
}
valid_token() { [[ ${#1} -ge 32 && ! "$1" =~ [[:space:]] ]]; }
check_ca() {
    [[ -f "$1" ]] || fail "CA file not found: $1"
    openssl x509 -in "$1" -noout >/dev/null 2>&1 || fail "Not a PEM certificate: $1"
}
copy_ca() {
    # Extract only the certificate even if the supplied PEM also contains a key.
    openssl x509 -in "$1" -out "$2"
    chmod 0644 "$2"
}
ports_conflict() { [[ "$2" == "$4" && ( "$1" == "$3" || "$1" == 0.0.0.0 || "$3" == 0.0.0.0 ) ]]; }
yaml_quote() {
    local encoded=$1
    encoded=${encoded//\$/\$\$}
    encoded=${encoded//\'/\'\'}
    printf "'%s'" "$encoded"
}
env_field() { printf '      %s: ' "$1"; yaml_quote "$2"; printf '\n'; }
connection_value() { local encoded=$1; encoded=${encoded//\"/\"\"}; printf '"%s"' "$encoded"; }
database_connection() {
    printf 'Host=%s;Port=%s;Database=%s;Username=%s;Password=%s;SSL Mode=%s' \
        "$(connection_value "$1")" "$2" "$(connection_value "$3")" "$(connection_value "$4")" "$(connection_value "$5")" "$6"
    [[ -z "$7" ]] || printf ';Root Certificate=%s' "$(connection_value "$7")"
}
certificate_san() { if valid_ipv4 "$1"; then printf 'IP:%s' "$1"; else printf 'DNS:%s' "$1"; fi; }
make_ca() {
    mkdir -p -- "$1"
    openssl rand -hex 32 > "$1/password"
    openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 \
        -passout "file:$1/password" -keyout "$1/ca.key" -out "$1/ca.crt" \
        -subj "/CN=$2" -addext 'basicConstraints=critical,CA:TRUE' \
        -addext 'keyUsage=critical,keyCertSign,cRLSign' -addext 'subjectKeyIdentifier=hash'
}
make_server() {
    mkdir -p -- "$(dirname -- "$1")"
    openssl req -new -newkey rsa:3072 -nodes -keyout "$1.key" -out "$1.csr" -subj "/CN=$3"
    cat > "$1.ext" <<EXT
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=$4
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
EXT
    openssl x509 -req -in "$1.csr" -CA "$2/ca.crt" -CAkey "$2/ca.key" \
        -passin "file:$2/password" -set_serial "0x$(openssl rand -hex 16)" \
        -days 365 -sha256 -extfile "$1.ext" -out "$1.crt"
    openssl verify -CAfile "$2/ca.crt" -purpose sslserver "$1.crt"
}

# Shared with generated manage.sh; only installer-owned files and Docker resources
# carrying the webboard project label are removed. Keep the directory itself.
remove_deployment() (
    local target=$1 marker containers volumes networks resource
    cd -- "$target" || fail "Cannot access deployment directory: $target"
    [[ -f .webboard-installer && ! -L .webboard-installer ]] || fail 'Removal requires the original installer-generated deployment directory.'
    marker=$(< .webboard-installer)
    [[ "$marker" =~ ^webBoard\ installer\ v[0-9]+$ ]] || fail 'Unrecognized installer marker; refusing to remove files.'
    mkdir .webboard-operation.lock 2>/dev/null || fail 'Another lifecycle command holds .webboard-operation.lock. Confirm no command is running before removing a stale lock.'
    trap 'rmdir .webboard-operation.lock' EXIT
    containers=$("${docker_cmd[@]}" ps -aq --filter label=com.docker.compose.project=webboard)
    volumes=$("${docker_cmd[@]}" volume ls -q --filter label=com.docker.compose.project=webboard)
    networks=$("${docker_cmd[@]}" network ls -q --filter label=com.docker.compose.project=webboard)
    # Remove containers before volumes/networks; stop on errors and keep files
    # so a failed removal can be retried from the original directory.
    while IFS= read -r resource; do
        [[ -z "$resource" ]] || "${docker_cmd[@]}" rm -f -- "$resource" >/dev/null || exit 1
    done <<< "$containers"
    while IFS= read -r resource; do
        [[ -z "$resource" ]] || "${docker_cmd[@]}" volume rm -- "$resource" >/dev/null || exit 1
    done <<< "$volumes"
    while IFS= read -r resource; do
        [[ -z "$resource" ]] || "${docker_cmd[@]}" network rm -- "$resource" >/dev/null || exit 1
    done <<< "$networks"
    rm -rf -- compose.yaml compose.yml docker-compose.yaml docker-compose.yml \
        .webboard-ready tls trust Caddyfile manage.sh NEXT-STEPS.txt local-https \
        browser-ca.crt database-connection.txt agent-connection.txt
    rm -f -- .webboard-installer
    printf 'Removed webBoard installation from %s.\n' "$PWD"
)

deployment_dir="${HOME:?HOME must be set}/webboard"
ui_port=8080
resume=false
directory_given=false
port_given=false
components_given=false
components=''
authorized_systemd_targets=()
while (($#)); do
    case "$1" in
        --directory) (($# >= 2)) && [[ -n "$2" ]] || fail '--directory requires a path'; deployment_dir=$2; directory_given=true; shift 2;;
        --port) (($# >= 2)) || fail '--port requires a number'; ui_port=$2; port_given=true; shift 2;;
        --components) (($# >= 2)) && [[ -n "$2" ]] || fail '--components requires a comma-separated list'; components=$2; components_given=true; shift 2;;
        --resume) resume=true; shift;;
        --authorize-systemd) (($# >= 2)) && valid_systemd_service "$2" || fail '--authorize-systemd requires an exact .service name'; authorized_systemd_targets+=("$2"); shift 2;;
        -h|--help) usage; exit 0;;
        *) fail "Unknown option: $1 (use --help)";;
    esac
done
if ((${#authorized_systemd_targets[@]})); then
    ! $resume && ! $components_given && ! $directory_given && ! $port_given || fail '--authorize-systemd only configures host policy; do not combine it with installation options.'
    prepare_agent_account
    install_systemd_policy "${authorized_systemd_targets[@]}"
    printf 'Ensure the existing agent uses user: "%s:%s" and these environment entries:\n' "$agent_uid" "$agent_gid"
    for ((i=0; i<${#authorized_systemd_targets[@]}; i++)); do
        printf '  Controls__Systemd__%s: %s\n' "$i" "${authorized_systemd_targets[i]}"
    done
    printf 'Apply any Compose changes with docker compose up -d agent. Existing configuration, secrets, and containers were preserved.\n'
    exit 0
fi
valid_port "$ui_port" || fail 'Port must be between 1 and 65535'
install_database=false
install_ui=false
install_agent=false
if ! $resume; then
    printf '\nwebBoard guided installer\nComponents: db (PostgreSQL), ui (web interface), agent (host monitoring).\n'
    if $components_given; then
        parse_components "$components" || fail 'Choose a nonempty comma-separated list of db, ui, agent.'
    else
        while true; do
            ask components 'Components to install (comma-separated)' 'db,ui,agent'
            parse_components "$components" && break
            printf 'Use db, ui, agent, or a combination such as ui,agent; no empty entries.\n'
        done
    fi
    $directory_given || ask deployment_dir 'Deployment directory' "$deployment_dir"
    if $install_ui && ! $port_given; then ask_port ui_port 'Loopback UI HTTP port' 8080; fi
elif $components_given; then
    fail '--resume uses the saved component selection; do not pass --components.'
fi
ui_port=$((10#$ui_port))
mkdir -p -- "$deployment_dir"
cd -- "$deployment_dir"
deployment_dir=$PWD
if $resume; then
    [[ -f .webboard-installer && -f compose.yaml && -x manage.sh && -f NEXT-STEPS.txt ]] || fail 'No complete installer-generated configuration to resume in this directory.'
    if [[ $(< .webboard-installer) != 'webBoard installer v2' ]]; then
        [[ -f .webboard-ready ]] || fail 'Configuration generation did not finish; preserve this directory and retry in a fresh directory.'
    fi
else
    existing_artifacts=false
    for existing in compose.yaml compose.yml docker-compose.yaml docker-compose.yml .webboard-installer .webboard-ready tls trust Caddyfile manage.sh NEXT-STEPS.txt local-https browser-ca.crt database-connection.txt agent-connection.txt .webboard-operation.lock; do
        [[ ! -e "$existing" && ! -L "$existing" ]] || existing_artifacts=true
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
if $resume; then exec bash ./manage.sh start; fi
existing_containers=$("${docker_cmd[@]}" ps -aq --filter label=com.docker.compose.project=webboard)
existing_volumes=$("${docker_cmd[@]}" volume ls -q --filter label=com.docker.compose.project=webboard)
existing_networks=$("${docker_cmd[@]}" network ls -q --filter label=com.docker.compose.project=webboard)
network_exists=false
if "${docker_cmd[@]}" network inspect webboard-private >/dev/null 2>&1; then network_exists=true; fi
if $existing_artifacts || [[ -n "$existing_containers$existing_volumes$existing_networks" ]] || $network_exists; then
    printf 'An existing webBoard installation was found. Removal permanently deletes its database, certificates, secrets, and generated files.\n'
    yesno 'Remove the old installation and continue (answer n to abort; --resume preserves it)' n || fail 'Installation aborted; existing installation preserved.'
    old_directory=$deployment_dir
    if [[ ! -f .webboard-installer ]]; then
        old_directory=''
        if [[ -n "$existing_containers" ]]; then
            first_container=${existing_containers%%$'\n'*}
            old_directory=$("${docker_cmd[@]}" inspect --format '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$first_container")
            [[ "$old_directory" != '<no value>' ]] || old_directory=''
        fi
        ask old_directory 'Original deployment directory to remove' "$old_directory"
        [[ -n "$old_directory" ]] || fail 'The original deployment directory is required for removal.'
    fi
    remove_deployment "$old_directory"
    # An unrelated network with this name must never be deleted.
    if "${docker_cmd[@]}" network inspect webboard-private >/dev/null 2>&1; then
        fail 'Network webboard-private is not owned by this installation; resolve the conflict before retrying.'
    fi
    for existing in compose.yaml compose.yml docker-compose.yaml docker-compose.yml .webboard-installer .webboard-ready tls trust Caddyfile manage.sh NEXT-STEPS.txt local-https browser-ca.crt database-connection.txt agent-connection.txt; do
        [[ ! -e "$existing" && ! -L "$existing" ]] || fail "Existing deployment artifact remains: $deployment_dir/$existing"
    done
fi

# Database settings. Local PostgreSQL always has TLS, including its internal name.
db_name=dashboard
db_user=dashboard
db_password=''
db_host=database
db_port=5432
db_ssl=VerifyFull
db_ca_source=''
db_publish=false
db_bind=127.0.0.1
db_public_port=5432
db_certificate_host=database
if $install_database; then
    ask db_name 'New PostgreSQL database name' dashboard
    ask db_user 'New PostgreSQL database owner username' dashboard
    if ! $install_ui || yesno 'Expose PostgreSQL for additional UI hosts' n; then
        db_publish=true
        ask_bind db_bind 'PostgreSQL LAN/VPN bind IPv4 (loopback only works from this host)' "$(suggest_bind)"
        ask_port db_public_port 'PostgreSQL host port' 5432
        ask_host db_certificate_host 'Database DNS name or IPv4 used by remote UIs (certificate identity)' "$db_bind"
    fi
elif $install_ui; then
    printf '\nConnect the UI to an existing PostgreSQL server. The UI must be able to reach it from Docker.\n'
    ask_host db_host 'PostgreSQL hostname or IPv4 (not this container localhost)' db.internal
    [[ "$db_host" != localhost && "$db_host" != 127.* ]] || fail 'Use a database host reachable from the UI container, not localhost/loopback.'
    ask_port db_port 'PostgreSQL port' 5432
    ask db_name 'Existing PostgreSQL database name' dashboard
    ask db_user 'Existing PostgreSQL username' dashboard
    while true; do
        ask_secret db_password 'Existing PostgreSQL password'
        [[ -n "$db_password" ]] && break
        printf 'A database password is required.\n'
    done
    while true; do
        ssl_choice=verify-full
        ask ssl_choice 'Database TLS mode (verify-full/require/disable)' verify-full
        case "$ssl_choice" in verify-full) db_ssl=VerifyFull; break;; require) db_ssl=Require; break;; disable) db_ssl=Disable; break;; esac
    done
    if [[ "$db_ssl" == VerifyFull ]]; then
        ask db_ca_source 'Database public CA PEM path (blank uses system CAs)' ''
        [[ -z "$db_ca_source" ]] || check_ca "$db_ca_source"
    fi
fi

# Browser HTTPS is relevant only when the UI is installed here.
https_mode=existing
ui_host=localhost
https_port=443
proxy_bind=127.0.0.1
signing_key=''
if $install_ui; then
    ask_secret signing_key 'Existing JWT signing key (blank generates a new key; share it only for UI replicas)'
    jwt_key_bytes=$(printf '%s' "$signing_key" | wc -c)
    [[ -z "$signing_key" ]] || ((jwt_key_bytes >= 32)) || fail 'JWT signing keys must contain at least 32 UTF-8 bytes.'
    printf '\nHTTPS: local = private CA; public = automatic public certificates; existing = your own proxy.\n'
    while true; do
        ask https_mode 'HTTPS mode (local/public/existing)' local
        case "$https_mode" in local|public|existing) break;; esac
        printf 'Choose local, public, or existing.\n'
    done
    if [[ "$https_mode" == public ]]; then
        ask_host ui_host 'Public browser DNS hostname (no scheme or port)' board.example.com
        public_host_lower=$(printf '%s' "$ui_host" | tr '[:upper:]' '[:lower:]')
        [[ "$ui_host" == *.* && "$public_host_lower" != *.localhost && "$public_host_lower" != *.local && "$public_host_lower" != *.internal && "$public_host_lower" != *.home.arpa && ! "$ui_host" =~ ^[0-9.]+$ ]] || fail 'Public HTTPS needs a public DNS name.'
        ((ui_port != 80 && ui_port != 443)) || fail 'The loopback UI port cannot be 80 or 443 when using public HTTPS.'
        printf 'Point DNS for %s at this host and allow/forward TCP 80 and 443.\n' "$ui_host"
        yesno 'DNS and inbound ports are ready' n || fail 'Prepare DNS/ports, then rerun the installer.'
        proxy_bind=0.0.0.0
    elif [[ "$https_mode" == local ]]; then
        ask_bind proxy_bind 'HTTPS bind IPv4 (use this server LAN/VPN IP for remote browsers)' 127.0.0.1
        browser_default=$proxy_bind
        [[ "$proxy_bind" != 127.0.0.1 ]] || browser_default=localhost
        [[ "$proxy_bind" != 0.0.0.0 ]] || browser_default=''
        ask_host ui_host 'Browser hostname or IPv4 (Caddy certificate/site address; no scheme or port)' "$browser_default"
        ask_port https_port 'HTTPS port' 443
        ((https_port != ui_port)) || fail 'HTTPS and HTTP upstream ports must differ.'
    fi
fi

# Agent settings: share the generated local profile, or export a remote profile.
monitor_docker=false
monitor_systemd=false
docker_targets=()
systemd_targets=()
agent_token=''
agent_publish=false
agent_bind=127.0.0.1
agent_port=5081
agent_host=agent
if $install_agent; then
    [[ "$(uname -s)" == Linux ]] || fail 'Host agents require Linux.'
    if [[ -S /var/run/docker.sock ]] && yesno 'Enable Docker monitoring (mounts the host Docker socket)' y; then
        monitor_docker=true
        docker_gid=$(stat -c '%g' /var/run/docker.sock)
        while true; do
            ask target 'Container name or full ID to allow start/stop/restart (blank finishes)' ''
            [[ -n "$target" ]] || break
            [[ "$target" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || { printf 'Enter a container name or ID.\n'; continue; }
            docker_targets+=("$target")
        done
    fi
    if [[ -S /run/dbus/system_bus_socket && -d /run/systemd/system ]] && yesno 'Enable systemd monitoring (creates/reuses a non-login host account)' y; then
        monitor_systemd=true
        if yesno 'Enable systemd start/stop/restart controls (installs host polkit policy)' n; then
            while true; do
                ask target 'Exact systemd service to allow start/stop/restart (blank finishes)' ''
                [[ -n "$target" ]] || break
                valid_systemd_service "$target" || { printf 'Enter an exact service name such as frpc.service.\n'; continue; }
                systemd_targets+=("$target")
            done
        fi
    fi
    $monitor_docker || $monitor_systemd || fail 'No host monitoring source selected or available.'
    ask_secret agent_token 'Existing agent token (blank generates a new token)'
    [[ -z "$agent_token" ]] || valid_token "$agent_token" || fail 'Agent tokens require at least 32 characters and no whitespace.'
    if ! $install_ui || yesno 'Expose this agent for additional UI hosts' n; then
        agent_publish=true
        ask_bind agent_bind 'Agent LAN/VPN bind IPv4' "$(suggest_bind)"
        ask_port agent_port 'Agent host HTTPS port' 5081
        ask_host agent_host 'Agent DNS name or IPv4 used by remote UIs (certificate identity)' "$agent_bind"
    fi
fi

# UI profiles for agents installed elsewhere; credentials never go to the browser.
remote_urls=()
remote_tokens=()
remote_ca_sources=()
remote_host=''
remote_port=5081
remote_token=''
private_networks=()
if $install_ui; then
    while yesno 'Configure a remote agent in this UI' n; do
        ask_host remote_host 'Remote agent DNS hostname or IPv4' agent.internal
        [[ "$remote_host" != localhost && "$remote_host" != 127.* && "$remote_host" != agent ]] || fail 'Use a remote agent address reachable from the UI container.'
        ask_port remote_port 'Remote agent HTTPS port' 5081
        remote_url="https://$remote_host:$remote_port"
        [[ "$remote_url" != 'https://agent:8443' ]] || fail 'That URL is reserved for the local agent.'
        for url in "${remote_urls[@]}"; do [[ "$url" != "$remote_url" ]] || fail 'Remote agent profiles must not overlap.'; done
        while true; do
            ask_secret remote_token 'Remote agent token (must match its configured token)'
            valid_token "$remote_token" && break
            printf 'Enter at least 32 characters with no whitespace.\n'
        done
        ask remote_ca_source 'Remote agent public CA PEM path (blank uses system CAs)' ''
        [[ -z "$remote_ca_source" ]] || check_ca "$remote_ca_source"
        remote_urls+=("$remote_url")
        remote_tokens+=("$remote_token")
        remote_ca_sources+=("$remote_ca_source")
        if valid_ipv4 "$remote_host"; then
            # An exact host allowance also works for VPN/private IPs.
            private_networks+=("$remote_host/32")
        else
            while true; do
                ask cidr 'Remote agent private IPv4 CIDR (blank if public)' ''
                [[ -z "$cidr" ]] && break
                valid_cidr "$cidr" && { private_networks+=("$cidr"); break; }
                printf 'Enter an IPv4 CIDR, e.g. 10.0.0.0/24.\n'
            done
        fi
    done
    while true; do
        ask cidr 'Additional private monitoring IPv4 CIDR (blank finishes)' ''
        [[ -n "$cidr" ]] || break
        valid_cidr "$cidr" || { printf 'Enter an IPv4 CIDR, e.g. 10.0.0.0/24.\n'; continue; }
        private_networks+=("$cidr")
    done
fi
need_trust=false
$install_ui && $install_agent && need_trust=true
for source in "${remote_ca_sources[@]}"; do [[ -z "$source" ]] || need_trust=true; done
if $need_trust; then
    "${docker_cmd[@]}" buildx version >/dev/null 2>&1 || fail 'The UI trust image requires Docker Buildx. Install the Docker Buildx plugin and retry.'
fi
# Reject known collisions before generating credentials or creating containers.
if $db_publish && $install_ui; then
    if ports_conflict "$db_bind" "$db_public_port" 127.0.0.1 "$ui_port"; then fail 'Database and UI HTTP ports overlap.'; fi
    if [[ "$https_mode" != existing ]] && ports_conflict "$db_bind" "$db_public_port" "$proxy_bind" "$https_port"; then fail 'Database and HTTPS ports overlap.'; fi
    if [[ "$https_mode" == public ]] && ports_conflict "$db_bind" "$db_public_port" 0.0.0.0 80; then fail 'Database and proxy HTTP ports overlap.'; fi
fi
if $agent_publish; then
    if $db_publish && ports_conflict "$agent_bind" "$agent_port" "$db_bind" "$db_public_port"; then fail 'Agent and database ports overlap.'; fi
    if $install_ui; then
        if ports_conflict "$agent_bind" "$agent_port" 127.0.0.1 "$ui_port"; then fail 'Agent and UI HTTP ports overlap.'; fi
        if [[ "$https_mode" != existing ]] && ports_conflict "$agent_bind" "$agent_port" "$proxy_bind" "$https_port"; then fail 'Agent and browser HTTPS ports overlap.'; fi
        if [[ "$https_mode" == public ]] && ports_conflict "$agent_bind" "$agent_port" 0.0.0.0 80; then fail 'Agent and proxy HTTP ports overlap.'; fi
    fi
fi
if $monitor_systemd; then prepare_agent_account; fi
if ((${#systemd_targets[@]})); then install_systemd_policy "${systemd_targets[@]}"; fi
printf '\nInstalling components: %s in %s\n' "$components" "$deployment_dir"

on_error() {
    printf '\nInstallation stopped. Configuration and data are preserved in %s.\n' "$deployment_dir" >&2
    if [[ -f .webboard-ready ]]; then
        printf 'Inspect logs with: bash %q logs\nRetry with: bash %q start\n' "$deployment_dir/manage.sh" "$deployment_dir/manage.sh" >&2
    else
        printf 'Configuration generation did not finish. Keep this directory for recovery; use a new directory to retry.\n' >&2
    fi
}
trap on_error ERR
network_created=false
cleanup_partial_network() {
    if $network_created && [[ ! -f .webboard-ready ]]; then "${docker_cmd[@]}" network rm webboard-private >/dev/null 2>&1 || true; fi
}
trap cleanup_partial_network EXIT
set -o noclobber
printf 'webBoard installer v4\n' > .webboard-installer
set +o noclobber
"${docker_cmd[@]}" network create --label com.docker.compose.project=webboard webboard-private >/dev/null
network_created=true
agent_subnet=$("${docker_cmd[@]}" network inspect --format '{{(index .IPAM.Config 0).Subnet}}' webboard-private)
valid_cidr "$agent_subnet" || fail 'Docker did not allocate an IPv4 subnet.'
if $install_database; then
    db_password=$(openssl rand -hex 32)
    make_ca tls/ca/database 'webBoard Database CA'
    make_server tls/database/server tls/ca/database "$db_certificate_host" "DNS:database,$(certificate_san "$db_certificate_host")"
    cp tls/ca/database/ca.crt tls/database-ca.crt
    chmod 0644 tls/database-ca.crt
elif [[ -n "$db_ca_source" ]]; then
    mkdir -p tls
    copy_ca "$db_ca_source" tls/database-ca.crt
fi
ui_image=shujidev/webboard:latest
if $install_agent; then
    [[ -n "$agent_token" ]] || agent_token=$(openssl rand -hex 32)
    pfx_password=$(openssl rand -hex 32)
    make_ca tls/ca/agent 'webBoard Agent CA'
    make_server tls/agent tls/ca/agent "$agent_host" "DNS:agent,$(certificate_san "$agent_host")"
    printf '%s\n' "$pfx_password" > tls/pfx-password
    openssl pkcs12 -export -out tls/agent.pfx -inkey tls/agent.key -in tls/agent.crt \
        -certfile tls/ca/agent/ca.crt -passout file:tls/pfx-password
    # Encrypted PFX is container-readable; its parent and unencrypted key are private.
    chmod 0644 tls/agent.pfx
fi
if $need_trust; then
    mkdir -p trust
    if $install_agent; then copy_ca tls/ca/agent/ca.crt trust/local-agent-ca.crt; fi
    for ((i=0; i<${#remote_ca_sources[@]}; i++)); do
        [[ -z "${remote_ca_sources[i]}" ]] || copy_ca "${remote_ca_sources[i]}" "trust/remote-agent-ca-$i.crt"
    done
    chmod 0644 trust/*.crt
    cat > trust/Dockerfile <<'DOCKERFILE'
FROM shujidev/webboard:latest
USER root
COPY *.crt /usr/local/share/ca-certificates/
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && update-ca-certificates \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
DOCKERFILE
    ui_image=webboard-ui:trusted
fi
if $install_ui; then
    [[ -n "$signing_key" ]] || signing_key=$(openssl rand -hex 32)
    db_ca_container=''
    [[ ! -f tls/database-ca.crt ]] || db_ca_container=/tls/database-ca.crt
    connection_string=$(database_connection "$db_host" "$db_port" "$db_name" "$db_user" "$db_password" "$db_ssl" "$db_ca_container")
fi

# Write only the selected services. All arbitrary credentials are escaped both
# for their connection-string syntax and for Compose/YAML interpolation.
cat > compose.yaml <<'COMPOSE'
# Generated by webBoard. Contains secrets; keep private and preserve for upgrades.
name: webboard
services:
COMPOSE
if $install_database; then
    cat >> compose.yaml <<'COMPOSE'
  database:
    image: postgres:18.4-alpine
    restart: unless-stopped
    command: ["postgres", "-c", "ssl=on", "-c", "ssl_cert_file=/var/lib/postgresql/tls/server.crt", "-c", "ssl_key_file=/var/lib/postgresql/tls/server.key"]
    environment:
COMPOSE
    env_field POSTGRES_DB "$db_name" >> compose.yaml
    env_field POSTGRES_USER "$db_user" >> compose.yaml
    env_field POSTGRES_PASSWORD "$db_password" >> compose.yaml
    if $db_publish; then printf '    ports:\n      - "%s:%s:5432"\n' "$db_bind" "$db_public_port" >> compose.yaml; fi
    cat >> compose.yaml <<'COMPOSE'
    volumes:
      - dashboard-postgres-data:/var/lib/postgresql
      - database-tls:/var/lib/postgresql/tls
      - type: bind
        source: ./tls/database
        target: /tls-source
        read_only: true
        bind:
          create_host_path: false
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -h 127.0.0.1 -p 5432 -U \"$$POSTGRES_USER\" -d \"$$POSTGRES_DB\""]
      interval: 5s
      timeout: 5s
      retries: 20
COMPOSE
fi
if $install_ui; then
    printf '  ui:\n    image: %s\n' "$ui_image" >> compose.yaml
    if $need_trust; then
        cat >> compose.yaml <<'COMPOSE'
    pull_policy: never
    build:
      context: ./trust
COMPOSE
    fi
    printf '    restart: unless-stopped\n    environment:\n' >> compose.yaml
    env_field ASPNETCORE_ENVIRONMENT Production >> compose.yaml
    env_field ReverseProxy__KnownNetworks__0 "$agent_subnet" >> compose.yaml
    env_field ConnectionStrings__WebboardDatabase "$connection_string" >> compose.yaml
    env_field Authentication__Jwt__SigningKey "$signing_key" >> compose.yaml
    env_field Monitoring__IntervalSeconds 30 >> compose.yaml
    env_field Monitoring__RetentionDays 30 >> compose.yaml
    profile_index=0
    if $install_agent; then
        env_field Agents__0__BaseUrl https://agent:8443 >> compose.yaml
        env_field Agents__0__Token "$agent_token" >> compose.yaml
        profile_index=1
        private_networks=("$agent_subnet" "${private_networks[@]}")
    fi
    for ((i=0; i<${#remote_urls[@]}; i++)); do
        env_field "Agents__${profile_index}__BaseUrl" "${remote_urls[i]}" >> compose.yaml
        env_field "Agents__${profile_index}__Token" "${remote_tokens[i]}" >> compose.yaml
        profile_index=$((profile_index + 1))
    done
    for ((i=0; i<${#private_networks[@]}; i++)); do env_field "Outbound__AllowedPrivateNetworks__${i}" "${private_networks[i]}" >> compose.yaml; done
    printf '    ports:\n      - "127.0.0.1:%s:8080"\n' "$ui_port" >> compose.yaml
    if [[ -n "$db_ca_container" ]]; then
        cat >> compose.yaml <<'COMPOSE'
    volumes:
      - type: bind
        source: ./tls/database-ca.crt
        target: /tls/database-ca.crt
        read_only: true
        bind:
          create_host_path: false
COMPOSE
    fi
    if $install_database; then
        cat >> compose.yaml <<'COMPOSE'
    depends_on:
      database:
        condition: service_healthy
COMPOSE
    fi
fi
if $install_agent; then
    cat >> compose.yaml <<'COMPOSE'
  agent:
    image: shujidev/webboard-agent:latest
    restart: unless-stopped
    environment:
COMPOSE
    env_field ASPNETCORE_URLS https://+:8443 >> compose.yaml
    env_field Security__Tokens__0 "$agent_token" >> compose.yaml
    env_field Kestrel__Certificates__Default__Path /tls/agent.pfx >> compose.yaml
    env_field Kestrel__Certificates__Default__Password "$pfx_password" >> compose.yaml
    for ((i=0; i<${#docker_targets[@]}; i++)); do env_field "Controls__Docker__${i}" "${docker_targets[i]}" >> compose.yaml; done
    for ((i=0; i<${#systemd_targets[@]}; i++)); do env_field "Controls__Systemd__${i}" "${systemd_targets[i]}" >> compose.yaml; done
    if $monitor_systemd; then printf '    user: "%s:%s"\n' "$agent_uid" "$agent_gid" >> compose.yaml; fi
    if $agent_publish; then printf '    ports:\n      - "%s:%s:8443"\n' "$agent_bind" "$agent_port" >> compose.yaml; fi
    if $monitor_docker; then printf '    group_add:\n      - "%s"\n' "$docker_gid" >> compose.yaml; fi
    cat >> compose.yaml <<'COMPOSE'
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
if $install_ui && [[ "$https_mode" != existing ]]; then
    if [[ "$https_mode" == local ]]; then
        cat > Caddyfile <<CONFIG
{
    auto_https disable_redirects
    default_sni $ui_host
}
https://$ui_host {
    tls internal
    reverse_proxy ui:8080
}
CONFIG
    else
        printf '%s {\n    reverse_proxy ui:8080\n}\n' "$ui_host" > Caddyfile
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
COMPOSE
if $install_database || [[ "$https_mode" != existing ]]; then
    printf 'volumes:\n' >> compose.yaml
    if $install_database; then printf '  dashboard-postgres-data:\n  database-tls:\n' >> compose.yaml; fi
    [[ "$https_mode" == existing ]] || printf '  caddy-data:\n  caddy-config:\n' >> compose.yaml
fi

# Lifecycle commands operate only on services selected for this deployment.
{
    printf '#!/usr/bin/env bash\n'
    declare -f fail ask yesno remove_deployment
    cat <<'MANAGE'
set -Eeuo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ "${1:-start}" == remove || -f .webboard-ready ]] || { printf 'Configuration generation did not finish; do not start this deployment.\n' >&2; exit 1; }
lock_held=false
export_dir=''
cleanup() {
    [[ -z "$export_dir" ]] || rm -rf -- "$export_dir"
    if $lock_held; then rmdir .webboard-operation.lock; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
case "${1:-start}" in
    start|update|stop)
        if ! mkdir .webboard-operation.lock 2>/dev/null; then
            printf 'Another lifecycle command holds .webboard-operation.lock. If it was killed, remove that empty directory only after confirming no command is running.\n' >&2
            exit 1
        fi
        lock_held=true
        ;;
esac
docker_cmd=(docker)
if ! docker info >/dev/null 2>&1; then docker_cmd=(sudo docker); fi
compose=("${docker_cmd[@]}" compose --project-name webboard --project-directory "$PWD" --file "$PWD/compose.yaml")
trap 'printf "Command failed. Configuration/data are preserved; inspect with bash manage.sh logs.\n" >&2' ERR
has_service() {
    local candidate
    while IFS= read -r candidate; do [[ "$candidate" != "$1" ]] || return 0; done <<< "$services"
    return 1
}
case "${1:-start}" in
    start|update)
        "${compose[@]}" config --quiet
        services=$("${compose[@]}" config --services)
        published=()
        while IFS= read -r service; do
            [[ "$service" != ui || ! -f trust/Dockerfile ]] || continue
            published+=("$service")
        done <<< "$services"
        if ((${#published[@]})); then "${compose[@]}" pull "${published[@]}"; fi
        if has_service ui && [[ -f trust/Dockerfile ]]; then "${compose[@]}" build --pull ui; fi
        # Do not touch a database/agent service that isn't part of this deployment.
        if has_service ui; then "${compose[@]}" stop ui; fi
        if has_service database; then
            # Official PostgreSQL variants have different numeric UIDs. Resolve the
            # postgres account inside its image instead of changing host ownership.
            "${compose[@]}" run --rm -T --interactive=false --no-deps --user 0 --entrypoint sh database -c \
                'set -eu
                 cp /tls-source/server.crt /var/lib/postgresql/tls/server.crt
                 cp /tls-source/server.key /var/lib/postgresql/tls/server.key
                 chown postgres:postgres /var/lib/postgresql/tls/server.crt /var/lib/postgresql/tls/server.key
                 chmod 0600 /var/lib/postgresql/tls/server.key
                 chmod 0644 /var/lib/postgresql/tls/server.crt'
            "${compose[@]}" up -d --wait --wait-timeout 120 --force-recreate database
        fi
        if has_service ui; then "${compose[@]}" run --rm -T --interactive=false --no-deps ui --migrate; fi
        if has_service proxy; then
            "${compose[@]}" run --rm -T --interactive=false --no-deps proxy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
        fi
        # Recreate file mounts so atomic certificate/config replacements load.
        file_services=()
        while IFS= read -r service; do case "$service" in agent|proxy) file_services+=("$service");; esac; done <<< "$services"
        if ((${#file_services[@]})); then
            "${compose[@]}" up -d --wait --wait-timeout 120 --force-recreate "${file_services[@]}"
        fi
        "${compose[@]}" up -d --wait --wait-timeout 120
        if has_service ui && has_service agent; then
            # Variables expand inside the trusted UI container.
            # shellcheck disable=SC2016
            "${compose[@]}" exec -T --interactive=false ui bash -c 'for attempt in {1..30}; do
                if curl --fail --silent --show-error --max-time 5 --header "Authorization: Bearer $Agents__0__Token" "$Agents__0__BaseUrl/health"; then exit 0; fi
                sleep 2
            done; exit 1'
        fi
        if has_service proxy && [[ -f local-https ]]; then
            export_dir=$(mktemp -d "$PWD/.webboard-ca.XXXXXX")
            exported=false
            for ((attempt=0; attempt<30; attempt++)); do
                if "${compose[@]}" cp proxy:/data/caddy/pki/authorities/local/root.crt "$export_dir/browser-ca.crt" 2>/dev/null &&
                    openssl x509 -in "$export_dir/browser-ca.crt" -noout >/dev/null 2>&1; then
                    exported=true; break
                fi
                sleep 2
            done
            $exported || { printf 'Could not export browser CA; inspect proxy logs.\n' >&2; exit 1; }
            chmod 0644 "$export_dir/browser-ca.crt"
            mv -f -- "$export_dir/browser-ca.crt" browser-ca.crt
            rmdir -- "$export_dir"
            export_dir=''
        fi
        "${compose[@]}" ps
        cat NEXT-STEPS.txt
        ;;
    remove)
        printf 'Removal permanently deletes this webBoard installation, including database and certificate volumes, secrets, and generated files.\n'
        yesno 'Remove this installation (answer n to abort)' n || fail 'Removal aborted; existing installation preserved.'
        remove_deployment "$PWD"
        ;;
    logs) "${compose[@]}" logs --tail=100;;
    stop) "${compose[@]}" down;;
    *) printf 'Usage: bash manage.sh [start|update|logs|stop|remove]\n' >&2; exit 1;;
esac
MANAGE
} > manage.sh
chmod 0700 manage.sh
printf '\nwebBoard components started: %s\nDeployment directory: %s\n' "$components" "$deployment_dir" > NEXT-STEPS.txt
if $install_database; then
    export_db_host=database
    export_db_port=5432
    if $db_publish; then export_db_host=$db_certificate_host; export_db_port=$db_public_port; fi
    exported_connection=$(database_connection "$export_db_host" "$export_db_port" "$db_name" "$db_user" "$db_password" VerifyFull /tls/database-ca.crt)
    cat > database-connection.txt <<DETAILS
Private PostgreSQL connection details. Do not publish this file.
Host: $export_db_host
Port: $export_db_port
Database: $db_name
Username: $db_user
Password: $db_password
SSL mode: VerifyFull
Public CA certificate: $deployment_dir/tls/database-ca.crt
UI connection string (CA must be mounted at /tls/database-ca.crt):
$exported_connection
Transfer only the public CA to remote UI hosts, then supply this information
in their UI-only installer prompts. Never transfer the CA/server private keys.
DETAILS
    printf 'Database connection details: %s/database-connection.txt (contains a password; keep private).\n' "$deployment_dir" >> NEXT-STEPS.txt
    if ! $install_ui; then printf 'Database schema migrations run when you install/start a UI connected to this database.\n' >> NEXT-STEPS.txt; fi
    if $db_publish; then printf 'Allow database port %s only from authorized UI hosts.\n' "$db_public_port" >> NEXT-STEPS.txt; fi
fi
if $install_ui; then
    if [[ "$https_mode" == existing ]]; then
        ui_url="your HTTPS proxy (upstream http://127.0.0.1:$ui_port)"
    else
        ui_url="https://$ui_host"
        ((https_port == 443)) || ui_url="$ui_url:$https_port"
    fi
    printf 'Browser address: %s\nRegister the administrator account first.\n' "$ui_url" >> NEXT-STEPS.txt
    if [[ "$https_mode" == local ]]; then
        printf 'local HTTPS\n' > local-https
        cat >> NEXT-STEPS.txt <<STEPS
Import $deployment_dir/browser-ca.crt as a trusted certificate authority on each
browser device. HTTPS listens on $proxy_bind:$https_port; if using a hostname,
make it resolve to this server. Transfer only the public CA to client devices.
STEPS
    elif [[ "$https_mode" == public ]]; then
        printf 'Caddy obtains/renews public certificates; inspect proxy logs if issuance is pending.\n' >> NEXT-STEPS.txt
    else
        printf 'Configure your HTTPS proxy before browser registration/login.\n' >> NEXT-STEPS.txt
    fi
    if $install_agent; then printf 'Add this host in Host management with https://agent:8443; its local profile is already configured.\n' >> NEXT-STEPS.txt; fi
    for url in "${remote_urls[@]}"; do printf 'Remote agent profile configured: %s (use this exact URL in Host management).\n' "$url" >> NEXT-STEPS.txt; done
fi
if $install_agent; then
    if $monitor_systemd; then
        printf 'Systemd monitoring uses host account webboard-agent (%s:%s). This account and installed host policies are retained on uninstall.\n' "$agent_uid" "$agent_gid" >> NEXT-STEPS.txt
        if ((${#systemd_targets[@]})); then
            printf 'Host start/stop/restart authorization configured for: %s. Enable/disable require the manual unit-file policy.\n' "${systemd_targets[*]}" >> NEXT-STEPS.txt
        else
            printf 'Systemd controls were not configured. Use install.sh --authorize-systemd SERVICE and add the matching agent allowlist entry.\n' >> NEXT-STEPS.txt
        fi
    fi
    exported_agent_url=https://agent:8443
    if $agent_publish; then exported_agent_url="https://$agent_host:$agent_port"; fi
    cat > agent-connection.txt <<DETAILS
Private agent connection details. Do not publish this file.
Agent URL: $exported_agent_url
Token: $agent_token
Public CA certificate: $deployment_dir/tls/ca/agent/ca.crt
Supply this URL's hostname/port, token, and public CA in remote UI installer prompts.
For private DNS names, also provide the agent network's CIDR. Numeric agent IPs
receive an exact /32 allowance automatically. Never transfer CA/server private keys.
DETAILS
    printf 'Agent connection details: %s/agent-connection.txt (contains a token; keep private).\n' "$deployment_dir" >> NEXT-STEPS.txt
    printf 'Agent certificates expire in 365 days; renew using the TLS guide. Service controls require optional host polkit policy.\n' >> NEXT-STEPS.txt
fi
cat >> NEXT-STEPS.txt <<'STEPS'
Keep Compose, private TLS material/passwords, connection details, and volumes backed up privately.
Back up PostgreSQL before upgrades. Update: bash manage.sh update.
Retry: bash manage.sh start. Logs: bash manage.sh logs. Stop: bash manage.sh stop.
Uninstall (permanently deletes data): bash manage.sh remove.
Guides: https://github.com/murczi/webBoard/tree/main/deploy/docs
STEPS
printf 'configuration complete\n' > .webboard-ready
bash ./manage.sh start < /dev/null
if $install_ui && [[ "$https_mode" == local ]] && command -v update-ca-certificates >/dev/null; then
    if yesno 'Install the local HTTPS CA in this host trust store (root/sudo; browsers may need a separate import)' n; then
        trust_destination=/usr/local/share/ca-certificates/webboard-local.crt
        if [[ -e "$trust_destination" ]] && ! cmp -s browser-ca.crt "$trust_destination"; then fail "A different CA already exists at $trust_destination. Import browser-ca.crt manually."; fi
        root_command install -m 0644 browser-ca.crt "$trust_destination"
        root_command update-ca-certificates
    fi
fi
