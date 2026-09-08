# webBoard

## Docker images and CI/CD

Pull requests to `main` run the .NET build and tests and build both Docker
images. Every push to `main` runs the same .NET checks, builds both images,
and publishes them to Docker Hub:

- `shujidev/webboard:1.0` — the web UI, listening on container port `8080`.
- `shujidev/webboard-agent:1.0` — the host agent, listening on container port `8080`.

Both images share an automatically generated version: `1.0`, `1.1`, `1.2`,
and so on (after `1.9` comes `1.10`). The minor number is the publishing
workflow's run number minus one. Pull requests do not consume versions.
Failed or cancelled publishing runs can leave gaps; re-running a failed run
reuses its version. Keep the publishing workflow's identity to preserve its
counter. Images are published for `linux/amd64` using explicit version tags.

### One-time setup

Create a Docker Hub access token for `shujidev` with **Read & Write** access
to both image repositories. In this GitHub repository, open **Settings →
Secrets and variables → Actions → New repository secret**, and save it as
`DOCKERHUB_TOKEN`. Commit and push the workflow files to `main` to start the
first build. The GitHub Actions run summary lists the published image tags.

Both images must build before publishing starts. Docker Hub pushes are
separate operations: if one fails, re-run the failed publishing job to finish
the pair with the same version. Publishing images does not restart deployed
containers; update your deployment to the desired image version.

### Local builds and configuration

Run from the repository root:

```sh
docker build -f deploy/ui/Dockerfile -t shujidev/webboard:local .
docker build -f deploy/agent/Dockerfile -t shujidev/webboard-agent:local .
```

Configure the UI container using `ConnectionStrings__WebboardDatabase` and
`Authentication__Jwt__SigningKey` environment variables. Serve the UI over
HTTPS for its secure authentication cookies. Local `.env` files and
development settings are excluded from both Docker build contexts.
See the [agent README](src/WebBoard.Agent/README.md) for host socket mounts
and agent configuration.

The workflows follow the [Docker GitHub Actions documentation](https://docs.docker.com/build/ci/github-actions/push-multi-registries/)
and use [GitHub's workflow run counter](https://docs.github.com/en/actions/reference/workflows-and-actions/variables)
for versioning.

## Authentication

The web app signs users in from the existing `Users` table and loads their rows
from `UserCrudAccess`. Successful logins receive an eight-hour JWT in an
HttpOnly, Secure, SameSite=Strict browser-session cookie. Selecting “Remember
me” makes both the JWT and cookie persist for 30 days. Granted operations are
represented by repeatable `access` claims such as `Hosts:read` and
`Modules:update`.

`Users.PasswordHash` supports the user backend's
`PBKDF2-SHA256$<iterations>$<salt>$<hash>` format as well as ASP.NET Core
Identity V3 hashes. For non-development deployments, set a random signing key
of at least 32 bytes before starting the app:

```sh
export Authentication__Jwt__SigningKey='replace-with-a-long-random-production-key'
```

Registration checks usernames case-insensitively and creates new accounts with
no CRUD access grants. Access can then be assigned through the user-access
backend.

The checked-in development configuration contains a local-only signing key and
must not be used in production.
