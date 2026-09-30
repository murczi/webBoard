# Docker host setup

[Prerequisite guides](README.md) · [Application installation](../../README.md)

## Install on a fresh Ubuntu host

Run in Bash on a supported Ubuntu release with `sudo` access. If Docker already
works, skip installation. Hosts with existing distribution Docker/containerd
packages should follow Docker's conflict-removal instructions first rather than
removing a running installation blindly. The repository and packages below follow
[Docker's official Ubuntu installation guide](https://docs.docker.com/engine/install/ubuntu/).

```bash
bash <<'SETUP'
set -euo pipefail
sudo apt-get update
sudo apt-get install -y ca-certificates curl openssl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
. /etc/os-release
sudo tee /etc/apt/sources.list.d/docker.sources >/dev/null <<REPO
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: ${UBUNTU_CODENAME:-$VERSION_CODENAME}
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
REPO
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl enable --now docker
sudo docker version
sudo docker compose version
SETUP
```

For Debian, Fedora, or another OS, choose its recipe from
[Docker Engine installation](https://docs.docker.com/engine/install/).

## Allow your account to run Docker

The project guides use `docker` without `sudo`. Add your administrative account
to the Docker group, then log out and back in before continuing:

```bash
sudo usermod -aG docker "$(id -un)"
```

After logging back in:

```bash
docker version
docker compose version
```

Docker group membership grants root-level control over the host; alternatively,
keep using `sudo docker` in each guide. See
[Docker's Linux post-installation instructions](https://docs.docker.com/engine/install/linux-postinstall/).

## Prepare a deployment directory

Use one stable directory per stack. Run subsequent application commands there:

```bash
mkdir -p "$HOME/webboard"
cd "$HOME/webboard"
umask 077
```

Save the selected Compose example as `compose.yaml`. `umask 077` keeps newly
created credential files private in this shell. Then follow the application
installation guide and [networking recipe](networking.md).
