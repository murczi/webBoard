# webBoard

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
