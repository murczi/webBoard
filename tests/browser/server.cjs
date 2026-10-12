const fs = require('node:fs'), os = require('node:os'), path = require('node:path');
const https = require('node:https'), http = require('node:http');
const { spawn, execFileSync } = require('node:child_process');
const connection = process.env.WEBBOARD_BROWSER_DATABASE;
const app = process.env.WEBBOARD_BROWSER_APP;
if (!connection || !app || !/Database\s*=\s*webboard_browser(?:[a-z0-9_]*)(?:;|$)/i.test(connection)) throw new Error('Set WEBBOARD_BROWSER_DATABASE to a fresh disposable test database and WEBBOARD_BROWSER_APP to the published DLL.');
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'webboard-browser-'));
const upstreamPort = Number(process.env.WEBBOARD_BROWSER_UPSTREAM_PORT || 19080);
const proxyPort = Number(process.env.WEBBOARD_BROWSER_PROXY_PORT || 19443);
const env = { ...process.env, ConnectionStrings__WebboardDatabase: connection,
    Authentication__Jwt__SigningKey: 'browser-tests-only-signing-key-0123456789abcdef',
    Application__PathBase: process.env.WEBBOARD_BROWSER_PATH_BASE || '',
    Monitoring__Enabled: 'false', ASPNETCORE_ENVIRONMENT: 'Production',
    ASPNETCORE_URLS: `http://127.0.0.1:${upstreamPort}`, ReverseProxy__KnownProxies__0: '127.0.0.1' };
execFileSync('dotnet', [app, '--migrate'], { env, cwd: temp, stdio: 'inherit' });
execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
    '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1',
    '-keyout', path.join(temp, 'key.pem'), '-out', path.join(temp, 'cert.pem')], { stdio: 'ignore' });
const child = spawn('dotnet', [app], { env, cwd: path.dirname(app), stdio: 'inherit' });
const proxy = https.createServer({ key: fs.readFileSync(path.join(temp, 'key.pem')), cert: fs.readFileSync(path.join(temp, 'cert.pem')) }, (request, response) => {
    const upstream = http.request({ hostname: '127.0.0.1', port: upstreamPort, path: request.url, method: request.method,
        headers: { ...request.headers, 'x-forwarded-proto': 'https', 'x-forwarded-for': request.socket.remoteAddress } }, result => {
        response.writeHead(result.statusCode, result.headers); result.pipe(response);
    });
    upstream.on('error', () => { if (!response.headersSent) response.writeHead(503); response.end('Upstream starting'); });
    request.pipe(upstream);
});
proxy.listen(proxyPort, '127.0.0.1');
function cleanup() { proxy.close(); child.kill('SIGTERM'); fs.rmSync(temp, { recursive: true, force: true }); }
process.on('SIGTERM', cleanup); process.on('SIGINT', cleanup);
child.on('exit', code => { proxy.close(); fs.rmSync(temp, { recursive: true, force: true }); process.exit(code || 0); });
