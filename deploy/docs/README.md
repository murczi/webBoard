# Deployment prerequisites

These recipes cover the host tools and networking around webBoard. Start here
when an installation guide asks for Docker, certificates, or an HTTPS proxy.

| Task | Guide | Run on |
| --- | --- | --- |
| Install Docker, Compose, and command-line tools | [Docker host setup](docker-host.md) | Each fresh Ubuntu container host |
| Choose bind addresses and find the Docker subnet | [Ports and networking](networking.md) | The relevant container host |
| Create an agent certificate and trust it in the UI | [Agent TLS](agent-tls.md) | CA administration machine, agent host, then UI host |
| Serve the UI over HTTPS | [HTTPS with Caddy](https-proxy.md) | UI host; certificate trust also on browser devices |

Run the command blocks in order in **Bash**. Each guide identifies its working
directory and the values you must change. Recipes assume a fresh host or fresh
output directory; they do not replace existing certificates or merge existing
proxy configurations. Root operations use `sudo`. DNS records, router forwarding,
and browser certificate import depend on your infrastructure and are called out
where needed. Official sources are linked beside the relevant steps.

For one machine: install Docker, configure the stack, create agent TLS if using
an agent, migrate/start the UI, then add the HTTPS proxy. For separate machines,
use private/VPN addresses for PostgreSQL and agents. Return to the
[deployment overview](../../README.md) for application configuration.
