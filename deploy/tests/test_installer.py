"""Installer integration checks without starting containers or changing the host.

Run: python3 -m unittest discover -s deploy/tests -v
Requires Bash, OpenSSL, and the Docker Compose plugin; no Docker daemon needed.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

INSTALLER = Path(__file__).resolve().parents[1] / 'install.sh'
DOCKER = shutil.which('docker')

MOCK_DOCKER = r'''#!/usr/bin/env python3
import json, os, pathlib, shutil, subprocess, sys
args = sys.argv[1:]
with open(os.environ['MOCK_LOG'], 'a') as log:
    log.write(json.dumps(args) + '\n')
if args[:2] == ['buildx', 'version']:
    sys.exit(1 if os.environ.get('MOCK_BUILDX_MISSING') == '1' else 0)
if args[:1] == ['info']: sys.exit(0)
if args[:1] == ['ps']:
    print(os.environ.get('MOCK_CONTAINERS', ''), end='')
    sys.exit(0)
if args[:2] == ['volume', 'ls']:
    print(os.environ.get('MOCK_VOLUMES', ''), end='')
    sys.exit(0)
if args[:1] == ['rm'] or args[:2] == ['volume', 'rm']:
    sys.exit(42 if os.environ.get('MOCK_REMOVE_FAIL') == '1' else 0)
if args[:1] == ['inspect']:
    print(os.environ.get('MOCK_OLD_DIRECTORY', ''))
    sys.exit(0)
if args[:2] == ['network', 'ls']:
    if pathlib.Path(os.environ['MOCK_NETWORK']).exists(): print('mock-network')
    sys.exit(0)
if args[:2] == ['network', 'create']:
    pathlib.Path(os.environ['MOCK_NETWORK']).touch()
    print('mock-network')
elif args[:2] == ['network', 'rm']:
    pathlib.Path(os.environ['MOCK_NETWORK']).unlink(missing_ok=True)
elif args[:2] == ['network', 'inspect']:
    if not pathlib.Path(os.environ['MOCK_NETWORK']).exists(): sys.exit(1)
    print('172.30.0.0/16')
elif args[:1] == ['compose']:
    if args[1:2] == ['version']:
        print('Docker Compose mock')
    elif args[1:3] == ['up', '--help']:
        print('--wait')
    else:
        index = args.index('--file')
        path = args[index + 1]
        command = args[index + 2:]
        if command[:1] == ['config']:
            sys.exit(subprocess.call([os.environ['REAL_DOCKER']] + args))
        if '--migrate' in command and pathlib.Path(os.environ['MOCK_FAIL']).exists():
            sys.exit(42)
        if command[:1] == ['cp']:
            if os.environ.get('MOCK_CP_FAIL') == '1': sys.exit(1)
            shutil.copyfile(os.environ['MOCK_CA'], command[-1])
else:
    raise SystemExit('Unexpected Docker invocation: ' + repr(args))
'''

MOCK_ACCOUNT = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
command = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
with open(os.environ['MOCK_ACCOUNT_LOG'], 'a') as log:
    log.write(json.dumps([command] + args) + '\n')
account = pathlib.Path(os.environ['MOCK_ACCOUNT_FILE'])
if command == 'getent' and args == ['passwd', 'webboard-agent']:
    if not account.exists(): sys.exit(2)
    print(account.read_text(), end='')
elif command == 'useradd' and args[-1:] == ['webboard-agent']:
    if os.environ.get('MOCK_ACCOUNT_CREATE_FAIL') == '1': sys.exit(42)
    if account.exists(): sys.exit(9)
    account.write_text('webboard-agent:x:432:433:webBoard host agent:/nonexistent:/usr/sbin/nologin\n')
else:
    raise SystemExit('Unexpected account command: ' + repr([command] + args))
'''

MOCK_INSTALL = r'''#!/usr/bin/env python3
import json, os, pathlib, subprocess, sys
args = sys.argv[1:]
destination = pathlib.Path(args[-1])
if destination.parent != pathlib.Path(os.environ['MOCK_POLICY_DIR']):
    raise SystemExit('Unexpected privileged install destination: ' + str(destination))
with open(os.environ['MOCK_POLICY_LOG'], 'a') as log:
    log.write(json.dumps(args) + '\n')
if os.environ.get('MOCK_POLICY_INSTALL_FAIL') == '1': sys.exit(42)
# Record root ownership requirements but do not chown anything on the test host.
filtered = []
index = 0
while index < len(args):
    if args[index] in ('-o', '-g'): index += 2
    else:
        filtered.append(args[index])
        index += 1
sys.exit(subprocess.call(['/usr/bin/install'] + filtered))
'''

@unittest.skipUnless(DOCKER and shutil.which('openssl'), 'Docker Compose and OpenSSL required')
class InstallerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cert_temp = tempfile.TemporaryDirectory(prefix='webboard-installer-ca-test-')
        cls.addClassCleanup(cls.cert_temp.cleanup)
        cls.ca = Path(cls.cert_temp.name) / 'root.crt'
        subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes',
                        '-days', '1', '-subj', '/CN=Test Browser CA', '-keyout',
                        str(Path(cls.cert_temp.name) / 'root.key'), '-out', str(cls.ca)],
                       check=True, capture_output=True, timeout=30)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='webboard-installer-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.deploy = self.root / 'deployment with spaces'
        self.policy_dir = self.root / 'polkit-rules'
        self.policy_dir.mkdir()
        self.policy_path = self.policy_dir / '50-webboard-agent-installer.rules'
        # Redirect policy paths in a test copy, including when tests run as root.
        # No test should ever read or write the real host's polkit rules.
        self.installer = self.root / 'install.sh'
        self.installer.write_text(INSTALLER.read_text().replace('/etc/polkit-1/rules.d', str(self.policy_dir)))
        bin_dir = self.root / 'bin'
        bin_dir.mkdir()
        mock = bin_dir / 'docker'
        mock.write_text(MOCK_DOCKER)
        mock.chmod(0o755)
        sleep = bin_dir / 'sleep'
        sleep.write_text('#!/usr/bin/env bash\nexit 0\n')
        sleep.chmod(0o755)
        self.env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ['PATH'],
                        MOCK_LOG=str(self.root / 'log'), MOCK_NETWORK=str(self.root / 'network'),
                        MOCK_FAIL=str(self.root / 'fail'), MOCK_CA=str(self.ca), REAL_DOCKER=DOCKER)
        self.env.update(MOCK_ACCOUNT_FILE=str(self.root / 'account'),
                        MOCK_ACCOUNT_LOG=str(self.root / 'account-log'),
                        MOCK_POLICY_DIR=str(self.policy_dir), MOCK_POLICY_LOG=str(self.root / 'policy-log'))
        for name in ('getent', 'useradd'):
            mock = bin_dir / name
            mock.write_text(MOCK_ACCOUNT)
            mock.chmod(0o755)
        sudo = bin_dir / 'sudo'
        sudo.write_text('#!/usr/bin/env bash\nexec "$@"\n')
        sudo.chmod(0o755)
        install = bin_dir / 'install'
        install.write_text(MOCK_INSTALL)
        install.chmod(0o755)
        pkcheck = bin_dir / 'pkcheck'
        pkcheck.write_text('#!/usr/bin/env bash\nexit 0\n')
        pkcheck.chmod(0o755)
        # Keep inherited Compose settings from influencing validation/deployment.
        for key in ('COMPOSE_FILE', 'COMPOSE_PROJECT_NAME', 'COMPOSE_PROFILES'):
            self.env.pop(key, None)

    def run_installer(self, answers='', *options):
        return subprocess.run(['bash', str(self.installer), '--directory', str(self.deploy),
                               '--port', '8088', *options], input=answers, text=True,
                              capture_output=True, env=self.env, timeout=60)

    def config(self):
        result = subprocess.run([DOCKER, 'compose', '--project-name', 'webboard',
                                 '--file', str(self.deploy / 'compose.yaml'), 'config', '--format', 'json'],
                                text=True, capture_output=True, timeout=15, env=self.env)
        self.assertEqual(result.returncode, 0, result.stderr)
        cfg = json.loads(result.stdout)
        # Compose escapes dollars in rendered output so it can be used as Compose
        # input again. Docker receives the unescaped values (checked with live run).
        for service in cfg['services'].values():
            for key, value in service.get('environment', {}).items():
                if isinstance(value, str): service['environment'][key] = value.replace('$$', '$')
        return cfg

    def calls(self):
        return [json.loads(line) for line in (self.root / 'log').read_text().splitlines()]


    def answers(self, components='db,ui', *, https='existing', bind='127.0.0.1',
                browser='', https_port='443', db_ca='', password='external password',
                remote=None, private='', token='', db_publish=False, agent_publish=False,
                systemd=False, systemd_controls=()):
        chosen = set(components.lower().replace(' ', '').split(','))
        db = 'db' in chosen or 'database' in chosen
        ui, agent = 'ui' in chosen, 'agent' in chosen
        values = [components]
        if db:
            values += ['', '']  # database/owner names
            if ui: values += ['y' if db_publish else 'n']
            if not ui or db_publish: values += ['192.168.1.20', '5433', 'db.internal']
        elif ui:
            values += ['db.internal', '5432', '', '', password, 'verify-full', db_ca]
        if ui:
            values += ['', https]  # JWT generation and HTTPS mode
            if https == 'local': values += [bind, browser, https_port]
            elif https == 'public': values += [browser or 'board.example.org', 'y']
        if agent:
            if Path('/var/run/docker.sock').is_socket(): values += ['y', 'my-server', '']
            if Path('/run/dbus/system_bus_socket').is_socket() and Path('/run/systemd/system').is_dir():
                values += ['y' if systemd else 'n']
                if systemd:
                    values += ['y' if systemd_controls else 'n']
                    if systemd_controls: values += [*systemd_controls, '']
            values += [token]
            if ui: values += ['y' if agent_publish else 'n']
            if not ui or agent_publish: values += ['192.168.1.30', '5081', 'agent.internal']
        if ui:
            if remote:
                host, remote_token, ca, cidr = remote
                values += ['y', host, '5081', remote_token, ca]
                if not all(part.isdigit() for part in host.split('.')): values += [cidr]
            values += ['n', private]
            if private: values += ['']
            if https == 'local': values += ['n']  # optional host trust import
        return '\n'.join(values) + '\n'

    def assert_ok(self, result):
        self.assertEqual(result.returncode, 0, result.stderr[-2000:])

    def test_all_component_combinations(self):
        combinations = ('db', 'ui', 'agent', 'db,ui', 'db,agent', 'ui,agent', 'db,ui,agent')
        for components in combinations:
            with self.subTest(components=components):
                if 'agent' in components and not Path('/var/run/docker.sock').is_socket():
                    continue
                case = type(self)('test_all_component_combinations')
                case.setUp()
                try:
                    result = case.run_installer(case.answers(components))
                    case.assert_ok(result)
                    cfg = case.config()
                    wanted = {'database' if name == 'db' else name for name in components.split(',')}
                    self.assertEqual(set(cfg['services']), wanted)
                    calls = case.calls()
                    migration_calls = [c for c in calls if '--migrate' in c]
                    self.assertEqual(len(migration_calls), int('ui' in wanted))
                    db_start = [c for c in calls if 'up' in c and c[-1] == 'database']
                    self.assertEqual(len(db_start), int('database' in wanted))
                    ui_stop = [c for c in calls if 'stop' in c and c[-1] == 'ui']
                    self.assertEqual(len(ui_stop), int('ui' in wanted))
                    self.assertEqual((case.deploy / 'database-connection.txt').exists(), 'database' in wanted)
                    self.assertEqual((case.deploy / 'agent-connection.txt').exists(), 'agent' in wanted)
                    self.assertEqual((case.deploy / 'trust/Dockerfile').exists(), {'ui', 'agent'} <= wanted)
                    if 'database' in wanted:
                        db = cfg['services']['database']
                        self.assertIn('ssl=on', db['command'])
                        self.assertEqual('ports' in db, 'ui' not in wanted)
                    if 'agent' in wanted:
                        agent = cfg['services']['agent']
                        self.assertEqual('ports' in agent, 'ui' not in wanted)
                        if 'ui' in wanted:
                            self.assertEqual(agent['environment']['Security__Tokens__0'], cfg['services']['ui']['environment']['Agents__0__Token'])
                    if 'ui' in wanted:
                        ui = cfg['services']['ui']
                        self.assertEqual('depends_on' in ui, 'database' in wanted)
                        self.assertIn('Host="database"' if 'database' in wanted else 'Host="db.internal"', ui['environment']['ConnectionStrings__WebboardDatabase'])
                finally:
                    case.doCleanups()

    def test_component_prompt_is_first_and_rejects_invalid_lists(self):
        result = self.run_installer('ui,,agent\nunknown\nDB, UI, db\n' + self.answers().split('\n', 1)[1])
        self.assert_ok(result)
        self.assertTrue(result.stderr.startswith('Components to install'))
        self.assertEqual(set(self.config()['services']), {'database', 'ui'})

    def test_default_component_selection(self):
        if not Path('/var/run/docker.sock').is_socket(): self.skipTest('Host Docker socket needed')
        result = self.run_installer('\n' + self.answers('db,ui,agent').split('\n', 1)[1])
        self.assert_ok(result)
        self.assertEqual(set(self.config()['services']), {'database', 'ui', 'agent'})

    def test_existing_proxy_secrets_and_no_agent(self):
        result = self.run_installer(self.answers(private='10.0.0.0/24'))
        self.assert_ok(result)
        cfg = self.config()
        db = cfg['services']['database']['environment']['POSTGRES_PASSWORD']
        env = cfg['services']['ui']['environment']
        self.assertEqual(len(db), 64)
        self.assertNotEqual(db, env['Authentication__Jwt__SigningKey'])
        self.assertIn('Password="' + db + '"', env['ConnectionStrings__WebboardDatabase'])
        self.assertIn('SSL Mode=VerifyFull', env['ConnectionStrings__WebboardDatabase'])
        self.assertIn('Root Certificate="/tls/database-ca.crt"', env['ConnectionStrings__WebboardDatabase'])
        self.assertEqual(env['Outbound__AllowedPrivateNetworks__0'], '10.0.0.0/24')
        for name in ('compose.yaml', 'database-connection.txt'):
            self.assertEqual((self.deploy / name).stat().st_mode & 0o777, 0o600)
        before = (self.deploy / 'compose.yaml').read_bytes()
        result = self.run_installer('db,ui\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())

    def test_external_database_password_escaping_and_ca_mount(self):
        password = 'p;Host=evil;"quo\'te$word\\end '
        result = self.run_installer(self.answers('ui', password=password, db_ca=str(self.ca)))
        self.assert_ok(result)
        ui = self.config()['services']['ui']
        connection = ui['environment']['ConnectionStrings__WebboardDatabase']
        self.assertIn('Password="p;Host=evil;""quo\'te$word\\end "', connection)
        self.assertIn('Host="db.internal"', connection)
        self.assertIn('Root Certificate="/tls/database-ca.crt"', connection)
        self.assertNotIn('depends_on', ui)
        self.assertTrue((self.deploy / 'tls/database-ca.crt').exists())
        self.assertNotIn(password, result.stdout + result.stderr)

    def test_imported_ca_extracts_only_public_certificate(self):
        bundle = self.root / 'ca-with-private-key.pem'
        bundle.write_bytes(self.ca.read_bytes() + (self.ca.parent / 'root.key').read_bytes())
        self.assert_ok(self.run_installer(self.answers('ui', db_ca=str(bundle), remote=('10.2.3.4', 'a' * 64, str(bundle), ''))))
        for certificate in (self.deploy / 'tls/database-ca.crt', self.deploy / 'trust/remote-agent-ca-0.crt'):
            self.assertIn('BEGIN CERTIFICATE', certificate.read_text())
            self.assertNotIn('PRIVATE KEY', certificate.read_text())

    def test_local_services_can_be_exposed_for_other_ui_hosts(self):
        if not Path('/var/run/docker.sock').is_socket(): self.skipTest('Host Docker socket needed')
        self.assert_ok(self.run_installer(self.answers('db,ui,agent', db_publish=True, agent_publish=True)))
        services = self.config()['services']
        for service, address, port in [('database', '192.168.1.20', 5433), ('agent', '192.168.1.30', 5081)]:
            published = services[service]['ports'][0]
            self.assertEqual(published['host_ip'], address)
            self.assertEqual(int(published['published']), port)
        env = services['ui']['environment']
        self.assertIn('Host="database";Port=5432;', env['ConnectionStrings__WebboardDatabase'])
        self.assertEqual(env['Agents__0__BaseUrl'], 'https://agent:8443')

    def test_database_and_agent_port_collision_stops_before_generation(self):
        if not Path('/var/run/docker.sock').is_socket(): self.skipTest('Host Docker socket needed')
        answers = self.answers('db,agent').replace('192.168.1.30\n5081\n', '192.168.1.20\n5433\n')
        result = self.run_installer(answers)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Agent and database ports overlap', result.stderr)
        self.assertFalse((self.deploy / 'tls').exists())

    def test_external_database_without_private_ca(self):
        result = self.run_installer(self.answers('ui'))
        self.assert_ok(result)
        connection = self.config()['services']['ui']['environment']['ConnectionStrings__WebboardDatabase']
        self.assertNotIn('Root Certificate=', connection)
        self.assertFalse((self.deploy / 'tls').exists())

    def test_external_database_tls_choices(self):
        for choice, expected in [('require', 'Require'), ('disable', 'Disable')]:
            with self.subTest(choice=choice):
                case = type(self)('test_external_database_tls_choices'); case.setUp()
                try:
                    answers = case.answers('ui').replace('verify-full\n\n', choice + '\n', 1)
                    case.assert_ok(case.run_installer(answers))
                    self.assertIn('SSL Mode=' + expected, case.config()['services']['ui']['environment']['ConnectionStrings__WebboardDatabase'])
                finally: case.doCleanups()

    def test_remote_agent_trust_token_and_exact_ip_allowance(self):
        token = 'remote$quo\'te"' + 'a' * 40
        result = self.run_installer(self.answers('ui', remote=('10.2.3.4', token, str(self.ca), '')))
        self.assert_ok(result)
        ui = self.config()['services']['ui']
        self.assertEqual(ui['environment']['Agents__0__BaseUrl'], 'https://10.2.3.4:5081')
        self.assertEqual(ui['environment']['Agents__0__Token'], token)
        self.assertEqual(ui['environment']['Outbound__AllowedPrivateNetworks__0'], '10.2.3.4/32')
        self.assertEqual(ui['pull_policy'], 'never')
        self.assertTrue((self.deploy / 'trust/remote-agent-ca-0.crt').exists())
        self.assertNotIn(token, result.stdout + result.stderr)

    def test_remote_agent_public_ca_does_not_build_private_trust_image(self):
        result = self.run_installer(self.answers('ui', remote=('agent.example.org', 'a' * 64, '', '')))
        self.assert_ok(result)
        self.assertFalse((self.deploy / 'trust').exists())
        self.assertNotIn('Outbound__AllowedPrivateNetworks__0', self.config()['services']['ui']['environment'])

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed')
    def test_local_and_remote_agent_profiles_get_distinct_indices(self):
        result = self.run_installer(self.answers('ui,agent', remote=('agent.other.lan', 'b' * 64, str(self.ca), '10.4.0.0/24')))
        self.assert_ok(result)
        env = self.config()['services']['ui']['environment']
        self.assertEqual(env['Agents__0__BaseUrl'], 'https://agent:8443')
        self.assertEqual(env['Agents__1__BaseUrl'], 'https://agent.other.lan:5081')
        self.assertEqual(env['Outbound__AllowedPrivateNetworks__0'], '172.30.0.0/16')
        self.assertEqual(env['Outbound__AllowedPrivateNetworks__1'], '10.4.0.0/24')

    def test_local_https_input_validation(self):
        answers = self.answers(https='local', private='10.1.0.0/16')
        answers = answers.replace('local\n127.0.0.1\n\n443', 'wrong\nlocal\nbad-ip\n127.0.0.1\nbad/name\nlocalhost\n99999\n8443')
        answers = answers.replace('10.1.0.0/16', 'bad-cidr\n10.1.0.0/16')
        result = self.run_installer(answers)
        self.assert_ok(result)
        self.assertEqual(self.config()['services']['proxy']['ports'][0]['published'], '8443')
        self.assertIn('tls internal', (self.deploy / 'Caddyfile').read_text())
        self.assertEqual(self.config()['services']['ui']['environment']['ReverseProxy__KnownNetworks__0'], '172.30.0.0/16')
        self.assertTrue((self.deploy / 'browser-ca.crt').exists())

    def test_lan_bind_defaults_caddy_site_and_browser_url_to_ip(self):
        result = self.run_installer(self.answers(https='local', bind='192.168.1.42'))
        self.assert_ok(result)
        self.assertEqual(self.config()['services']['proxy']['ports'][0]['host_ip'], '192.168.1.42')
        caddy = (self.deploy / 'Caddyfile').read_text()
        self.assertIn('https://192.168.1.42 {', caddy)
        self.assertIn('default_sni 192.168.1.42', caddy)
        self.assertNotIn('localhost', caddy)
        self.assertIn('Browser address: https://192.168.1.42', result.stdout)

    def test_nondefault_loopback_uses_its_own_browser_ip(self):
        result = self.run_installer(self.answers(https='local', bind='127.0.0.2'))
        self.assert_ok(result)
        self.assertIn('https://127.0.0.2 {', (self.deploy / 'Caddyfile').read_text())

    def test_lan_bind_preserves_explicit_dns_name(self):
        result = self.run_installer(self.answers(https='local', bind='192.168.1.42', browser='board.lan', https_port='8443'))
        self.assert_ok(result)
        self.assertIn('https://board.lan {', (self.deploy / 'Caddyfile').read_text())
        self.assertIn('Browser address: https://board.lan:8443', result.stdout)

    def test_wildcard_bind_requires_concrete_browser_address(self):
        answers = self.answers(https='local', bind='0.0.0.0', browser='192.168.1.42').replace('0.0.0.0\n192.168.1.42', '0.0.0.0\n\n0.0.0.0\n999.1.2.3\n192.168.1.42')
        self.assert_ok(self.run_installer(answers))
        self.assertIn('https://192.168.1.42 {', (self.deploy / 'Caddyfile').read_text())

    def test_public_https(self):
        self.assert_ok(self.run_installer(self.answers(https='public')))
        self.assertEqual({p['published'] for p in self.config()['services']['proxy']['ports']}, {'80', '443'})
        self.assertNotIn('tls internal', (self.deploy / 'Caddyfile').read_text())

    def test_public_requires_readiness_before_generating(self):
        result = self.run_installer(self.answers(https='public').replace('board.example.org\ny', 'board.example.org\nn'))
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.deploy / 'compose.yaml').exists())
        self.assertFalse((self.root / 'network').exists())

    def test_public_https_rejects_conflicting_http_port(self):
        result = self.run_installer(self.answers(https='public'), '--port', '443')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('cannot be 80 or 443', result.stderr)

    def test_public_internal_names_are_rejected(self):
        result = self.run_installer(self.answers(https='public', browser='board.INTERNAL'))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Public HTTPS needs a public DNS name', result.stderr)

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed')
    def test_agent_certificates_migration_failure_and_resume(self):
        (self.root / 'fail').touch()
        result = self.run_installer(self.answers('db,ui,agent'))
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue((self.deploy / '.webboard-ready').exists())
        self.assertFalse(any('up' in call and '--help' not in call and call[-1] != 'database' for call in self.calls()))
        checked = subprocess.run(['openssl', 'verify', '-CAfile', str(self.deploy / 'tls/ca/agent/ca.crt'), '-verify_hostname', 'agent', str(self.deploy / 'tls/agent.crt')], capture_output=True)
        self.assertEqual(checked.returncode, 0, checked.stderr)
        checked = subprocess.run(['openssl', 'pkcs12', '-in', str(self.deploy / 'tls/agent.pfx'), '-passin', 'file:' + str(self.deploy / 'tls/pfx-password'), '-noout'], capture_output=True)
        self.assertEqual(checked.returncode, 0, checked.stderr)
        before = (self.deploy / 'compose.yaml').read_bytes()
        (self.root / 'fail').unlink()
        self.assert_ok(self.run_installer('', '--resume'))
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())
        self.assertFalse(any('pull' in call and 'ui' in call for call in self.calls()))
        result = subprocess.run(['bash', str(self.deploy / 'manage.sh'), 'update'], env=self.env, capture_output=True, text=True, timeout=60)
        self.assert_ok(result)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())

    def test_database_certificate_matches_internal_and_remote_names(self):
        self.assert_ok(self.run_installer(self.answers('db')))
        for name in ('database', 'db.internal'):
            result = subprocess.run(['openssl', 'verify', '-CAfile', str(self.deploy / 'tls/database-ca.crt'), '-verify_hostname', name, str(self.deploy / 'tls/database/server.crt')], capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.deploy / 'tls/database/server.key').stat().st_mode & 0o777, 0o600)
        details = (self.deploy / 'database-connection.txt').read_text()
        self.assertIn('Host: db.internal', details)
        self.assertIn('Port: 5433', details)

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed')
    def test_standalone_agent_certificate_and_export(self):
        self.assert_ok(self.run_installer(self.answers('agent')))
        result = subprocess.run(['openssl', 'verify', '-CAfile', str(self.deploy / 'tls/ca/agent/ca.crt'), '-verify_hostname', 'agent.internal', str(self.deploy / 'tls/agent.crt')], capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('Agent URL: https://agent.internal:5081', (self.deploy / 'agent-connection.txt').read_text())
        self.assertFalse(any('buildx' in call or '--migrate' in call for call in self.calls()))

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed')
    def test_existing_agent_token_is_preserved(self):
        token = 'existing' + 'a' * 64
        self.assert_ok(self.run_installer(self.answers('ui,agent', token=token)))
        cfg = self.config()
        self.assertEqual(cfg['services']['agent']['environment']['Security__Tokens__0'], token)
        self.assertEqual(cfg['services']['ui']['environment']['Agents__0__Token'], token)

    def test_existing_generated_auxiliary_files_are_preserved(self):
        self.deploy.mkdir()
        target = self.root / 'keep-me'; target.write_text('untouched')
        (self.deploy / 'database-connection.txt').symlink_to(target)
        result = self.run_installer('db\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(target.read_text(), 'untouched')

    def test_partial_generation_releases_network_and_cannot_resume(self):
        chmod = self.root / 'bin/chmod'
        chmod.write_text('#!/usr/bin/env bash\nif [[ "$*" == "0700 manage.sh" ]]; then exit 42; fi\nexec /usr/bin/chmod "$@"\n')
        chmod.chmod(0o755)
        result = self.run_installer(self.answers())
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.deploy / '.webboard-ready').exists())
        self.assertFalse((self.root / 'network').exists())
        self.assertNotEqual(self.run_installer('', '--resume').returncode, 0)

    def test_failed_ca_export_does_not_succeed_with_stale_ca(self):
        self.assert_ok(self.run_installer(self.answers(https='local')))
        before = (self.deploy / 'browser-ca.crt').read_bytes()
        self.env['MOCK_CP_FAIL'] = '1'
        result = subprocess.run(['bash', str(self.deploy / 'manage.sh'), 'start'], env=self.env, capture_output=True, text=True, timeout=30)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Could not export browser CA', result.stderr)
        self.assertEqual(before, (self.deploy / 'browser-ca.crt').read_bytes())
        self.assertFalse((self.deploy / '.webboard-operation.lock').exists())
        self.assertEqual(list(self.deploy.glob('.webboard-ca.*')), [])

    def test_lifecycle_lock_prevents_overlapping_stop(self):
        self.assert_ok(self.run_installer(self.answers()))
        (self.deploy / '.webboard-operation.lock').mkdir()
        count = len(self.calls())
        result = subprocess.run(['bash', str(self.deploy / 'manage.sh'), 'stop'], env=self.env, capture_output=True, text=True, timeout=15)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Another lifecycle command', result.stderr)
        self.assertEqual(count, len(self.calls()))
        result = subprocess.run(['bash', str(self.deploy / 'manage.sh'), 'logs'], env=self.env, capture_output=True, text=True, timeout=15)
        self.assert_ok(result)

    def run_manage(self, command, answers=''):
        return subprocess.run(['bash', str(self.deploy / 'manage.sh'), command],
                              input=answers, env=self.env, capture_output=True,
                              text=True, timeout=30)

    def test_remove_defaults_to_abort_and_preserves_installation(self):
        self.assert_ok(self.run_installer(self.answers('ui')))
        before = (self.deploy / 'compose.yaml').read_bytes()
        count = len(self.calls())
        result = self.run_manage('remove', '\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())
        self.assertFalse(any(c[0] == 'rm' or c[:2] == ['volume', 'rm']
                             for c in self.calls()[count:]))
        result = self.run_installer('ui\n\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Installation aborted', result.stderr)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())

    def test_remove_deletes_owned_resources_and_keeps_unrelated_files(self):
        self.assert_ok(self.run_installer(self.answers(https='local')))
        keep = self.deploy / 'keep.txt'; keep.write_text('keep')
        outside = self.root / 'outside'; outside.mkdir()
        (outside / 'keep.txt').write_text('outside')
        (self.deploy / 'tls').rename(self.deploy / 'original-tls')
        (self.deploy / 'tls').symlink_to(outside, target_is_directory=True)
        self.env.update(MOCK_CONTAINERS='container-1\ncontainer-2',
                        MOCK_VOLUMES='database-volume\ncertificate-volume')
        self.assert_ok(self.run_manage('remove', 'y\n'))
        self.assertEqual(keep.read_text(), 'keep')
        self.assertEqual((outside / 'keep.txt').read_text(), 'outside')
        for name in ('compose.yaml', '.webboard-installer', 'manage.sh', 'tls',
                     'browser-ca.crt', '.webboard-operation.lock'):
            self.assertFalse((self.deploy / name).exists(), name)
        self.assertFalse((self.root / 'network').exists())
        calls = self.calls()
        remove = calls.index(['rm', '-f', '--', 'container-2'])
        volume = calls.index(['volume', 'rm', '--', 'database-volume'])
        network = calls.index(['network', 'rm', '--', 'mock-network'])
        self.assertLess(remove, volume)
        self.assertLess(volume, network)

    def test_installer_can_remove_and_replace_existing_install(self):
        self.assert_ok(self.run_installer(self.answers('ui')))
        keep = self.deploy / 'keep.txt'; keep.write_text('keep')
        answers = self.answers('db')
        self.assert_ok(self.run_installer(answers.replace('db\n', 'db\ny\n', 1)))
        self.assertEqual(set(self.config()['services']), {'database'})
        self.assertEqual(keep.read_text(), 'keep')

    def test_installer_finds_install_in_another_directory(self):
        self.assert_ok(self.run_installer(self.answers('ui')))
        old = self.deploy
        self.env.update(MOCK_CONTAINERS='container-1', MOCK_OLD_DIRECTORY=str(old))
        self.deploy = self.root / 'new deployment'
        self.assert_ok(self.run_installer(self.answers('ui').replace('ui\n', 'ui\ny\n\n', 1)))
        self.assertFalse((old / '.webboard-installer').exists())
        self.assertTrue((self.deploy / '.webboard-ready').exists())

    def test_remove_failure_keeps_configuration_and_releases_lock(self):
        self.assert_ok(self.run_installer(self.answers('ui')))
        self.env.update(MOCK_VOLUMES='database-volume', MOCK_REMOVE_FAIL='1')
        result = self.run_manage('remove', 'y\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue((self.deploy / 'compose.yaml').exists())
        self.assertTrue((self.deploy / '.webboard-installer').exists())
        self.assertFalse((self.deploy / '.webboard-operation.lock').exists())

    def test_remove_respects_lifecycle_lock_and_validates_marker(self):
        self.assert_ok(self.run_installer(self.answers('ui')))
        lock = self.deploy / '.webboard-operation.lock'; lock.mkdir()
        count = len(self.calls())
        self.assertNotEqual(self.run_manage('remove', 'y\n').returncode, 0)
        self.assertFalse(any(c[0] == 'rm' for c in self.calls()[count:]))
        lock.rmdir()
        (self.deploy / '.webboard-installer').write_text('unrelated')
        self.assertNotEqual(self.run_manage('remove', 'y\n').returncode, 0)
        self.assertTrue((self.deploy / 'compose.yaml').exists())

    def test_remove_accepts_partial_generation(self):
        self.assert_ok(self.run_installer(self.answers('ui')))
        (self.deploy / '.webboard-ready').unlink()
        self.assert_ok(self.run_manage('remove', 'y\n'))
        self.assertFalse((self.deploy / '.webboard-installer').exists())

    def test_unrecognized_artifacts_cannot_be_removed(self):
        self.deploy.mkdir()
        compose = self.deploy / 'compose.yaml'; compose.write_text('keep')
        result = self.run_installer('ui\ny\n\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(compose.read_text(), 'keep')
        self.assertFalse(any(c[0] == 'rm' for c in self.calls()))

    def run_helpers(self, code):
        helpers = self.root / 'helpers.sh'
        helpers.write_text(self.installer.read_text().split('deployment_dir=')[0])
        return subprocess.run(['bash', '-c', 'source "$1"; ' + code, 'test', str(helpers)], env=self.env, capture_output=True, text=True, timeout=15)

    def account_calls(self):
        path = self.root / 'account-log'
        return [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []

    def test_systemd_account_uses_host_allocation_and_is_reused(self):
        for _ in range(2):
            result = self.run_helpers('prepare_agent_account; printf "%s:%s" "$agent_uid" "$agent_gid"')
            self.assert_ok(result)
            self.assertTrue(result.stdout.endswith('432:433'))
        created = [call for call in self.account_calls() if call[0] == 'useradd']
        self.assertEqual(len(created), 1)
        self.assertIn('--system', created[0])
        self.assertIn('--user-group', created[0])
        self.assertIn('--no-create-home', created[0])
        self.assertNotIn('--uid', created[0])

    def test_systemd_account_rejects_root_or_login_identity(self):
        for uid, gid, shell in [('0', '433', '/usr/sbin/nologin'),
                                ('432', '0', '/usr/sbin/nologin'),
                                ('432', '433', '/bin/bash')]:
            with self.subTest(uid=uid, gid=gid, shell=shell):
                (self.root / 'account').write_text(f'webboard-agent:x:{uid}:{gid}:Agent:/nonexistent:{shell}\n')
                result = self.run_helpers('prepare_agent_account')
                self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(call[0] == 'useradd' for call in self.account_calls()))

    @unittest.skipUnless(Path('/run/dbus/system_bus_socket').is_socket() and
                         Path('/run/systemd/system').is_dir(), 'Host systemd sockets needed')
    def test_systemd_installer_sets_account_and_keeps_docker_group(self):
        self.assert_ok(self.run_installer(self.answers('agent', systemd=True)))
        agent = self.config()['services']['agent']
        self.assertEqual(agent['user'], '432:433')
        targets = {mount['target'] for mount in agent['volumes']}
        self.assertIn('/run/dbus/system_bus_socket', targets)
        self.assertIn('/run/systemd/system', targets)
        if Path('/var/run/docker.sock').is_socket():
            self.assertEqual(agent['group_add'], [str(Path('/var/run/docker.sock').stat().st_gid)])
        self.assertIn('webboard-agent (432:433)', (self.deploy / 'NEXT-STEPS.txt').read_text())
        self.assertNotIn('Controls__Systemd__0', agent['environment'])
        self.assertFalse(self.policy_path.exists())
        before = (self.deploy / 'compose.yaml').read_bytes()
        account_count = len(self.account_calls())
        self.assert_ok(self.run_installer('', '--resume'))
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())
        self.assertEqual(account_count, len(self.account_calls()))

    @unittest.skipUnless(Path('/run/dbus/system_bus_socket').is_socket() and
                         Path('/run/systemd/system').is_dir(), 'Host systemd sockets needed')
    def test_systemd_controls_install_policy_and_matching_agent_allowlist(self):
        self.assert_ok(self.run_installer(self.answers('agent', systemd=True,
                                                      systemd_controls=('frpc.service', 'other.service'))))
        env = self.config()['services']['agent']['environment']
        self.assertEqual(env['Controls__Systemd__0'], 'frpc.service')
        self.assertEqual(env['Controls__Systemd__1'], 'other.service')
        policy = self.policy_path.read_text()
        self.assertIn("var units = ['frpc.service', 'other.service'];", policy)
        self.assertNotIn('manage-unit-files', policy)
        self.assertEqual(self.policy_path.stat().st_mode & 0o777, 0o644)
        self.assertIn('Host start/stop/restart authorization configured',
                      (self.deploy / 'NEXT-STEPS.txt').read_text())
        before = self.policy_path.read_bytes()
        self.assert_ok(self.run_installer('', '--resume'))
        self.assertEqual(before, self.policy_path.read_bytes())
        self.assert_ok(self.run_manage('remove', 'y\n'))
        self.assertEqual(before, self.policy_path.read_bytes())
        self.assertTrue((self.root / 'account').exists())

    def run_authorization(self, *services):
        options = [part for service in services for part in ('--authorize-systemd', service)]
        return subprocess.run(['bash', str(self.installer), *options],
                              env=self.env, capture_output=True, text=True, timeout=15)

    def test_authorization_repairs_policy_without_touching_deployment_or_containers(self):
        self.deploy.mkdir()
        compose = self.deploy / 'compose.yaml'
        compose.write_text('existing secrets and configuration')
        self.assert_ok(self.run_authorization('frpc.service'))
        self.assertIn("var units = ['frpc.service'];", self.policy_path.read_text())
        result = self.run_authorization('other.service')
        self.assert_ok(result)
        self.assertIn('Controls__Systemd__0: other.service', result.stdout)
        self.assertIn('user: "432:433"', result.stdout)
        self.assertNotIn('frpc.service', self.policy_path.read_text())
        self.assertEqual(compose.read_text(), 'existing secrets and configuration')
        self.assertFalse((self.root / 'log').exists())
        calls = [json.loads(line) for line in (self.root / 'policy-log').read_text().splitlines()]
        self.assertIn('-o', calls[0])
        self.assertIn('-g', calls[0])
        self.assertIn('0', calls[0])

    def test_authorization_rejects_invalid_names_before_host_changes(self):
        for name in ('--bad.service', 'bad/name.service', '*.service', "bad'.service",
                     'bad\nname.service', 'a' * 249 + '.service'):
            with self.subTest(name=name):
                result = self.run_authorization(name)
                self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.account_calls(), [])
        self.assertFalse(self.policy_path.exists())

    def test_authorization_preserves_unmanaged_policy_and_symlinks(self):
        self.policy_path.write_text('// Manual host policy\nkeep this')
        result = self.run_authorization('frpc.service')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.policy_path.read_text(), '// Manual host policy\nkeep this')
        self.policy_path.unlink()
        outside = self.root / 'outside-policy'
        outside.write_text('keep outside')
        self.policy_path.symlink_to(outside)
        self.assertNotEqual(self.run_authorization('frpc.service').returncode, 0)
        self.assertEqual(outside.read_text(), 'keep outside')

    def test_policy_install_failure_is_reported(self):
        self.env['MOCK_POLICY_INSTALL_FAIL'] = '1'
        result = self.run_authorization('frpc.service')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Could not install the systemd control policy', result.stderr)
        self.assertFalse(self.policy_path.exists())

    def test_missing_polkit_installs_available_debian_package(self):
        release = self.root / 'os-release'
        release.write_text('ID=ubuntu\n')
        self.installer.write_text(self.installer.read_text().replace('/etc/os-release', str(release)))
        for candidate, expected in [('polkitd', 'polkitd'), ('none', 'policykit-1')]:
            with self.subTest(package=expected):
                code = '''
polkit_ready=false
command() {
    if [[ "$1" == -v && "$2" == pkcheck ]]; then "$polkit_ready"; else builtin command "$@"; fi
}
root_command() {
    printf '%%s\\n' "$*"
    [[ "$1 $2" != 'apt-get install' ]] || polkit_ready=true
}
apt_has_candidate() { [[ "$1" == '%s' ]]; }
ensure_polkit
''' % candidate
                result = self.run_helpers(code)
                self.assert_ok(result)
                self.assertIn('apt-get update', result.stdout)
                self.assertIn('apt-get install -y ' + expected, result.stdout)

    @unittest.skipUnless(shutil.which('node'), 'Node required for generated policy evaluation')
    def test_policy_grants_only_named_runtime_actions_to_agent_user(self):
        escaped = r'example@disk\x2dname.service'
        self.assert_ok(self.run_authorization('frpc.service', escaped))
        code = r'''
const fs = require('fs'), vm = require('vm');
let rule;
vm.runInNewContext(fs.readFileSync(process.argv[1], 'utf8'), {
  polkit: {addRule: callback => rule = callback, Result: {YES: 'yes'}}
});
const cases = JSON.parse(process.argv[2]);
console.log(JSON.stringify(cases.map(([user, unit, verb, id]) =>
  rule({id, lookup: key => ({unit, verb})[key]}, {user}) || null)));
'''
        action = 'org.freedesktop.systemd1.manage-units'
        cases = [['webboard-agent', 'frpc.service', verb, action] for verb in ('start', 'stop', 'restart')]
        cases += [['webboard-agent', escaped, 'restart', action],
                  ['other-user', 'frpc.service', 'restart', action],
                  ['webboard-agent', 'other.service', 'restart', action],
                  ['webboard-agent', 'frpc.service', 'reload', action],
                  ['webboard-agent', 'frpc.service', 'enable', 'org.freedesktop.systemd1.manage-unit-files'],
                  ['webboard-agent', 'frpc.service', 'restart', 'org.freedesktop.systemd1.reload-daemon']]
        result = subprocess.run(['node', '-e', code, str(self.policy_path), json.dumps(cases)],
                                capture_output=True, text=True, timeout=15)
        self.assert_ok(result)
        self.assertEqual(json.loads(result.stdout), ['yes'] * 4 + [None] * 5)

    def test_authorization_cannot_be_combined_with_installation_options(self):
        for options in (['--resume'], ['--components', 'agent'], ['--directory', str(self.deploy)], ['--port', '8080']):
            result = subprocess.run(['bash', str(self.installer), '--authorize-systemd', 'frpc.service', *options],
                                    env=self.env, capture_output=True, text=True, timeout=15)
            self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.account_calls(), [])

    @unittest.skipUnless(Path('/run/dbus/system_bus_socket').is_socket() and
                         Path('/run/systemd/system').is_dir(), 'Host systemd sockets needed')
    def test_account_creation_failure_stops_before_generating_deployment(self):
        self.env['MOCK_ACCOUNT_CREATE_FAIL'] = '1'
        result = self.run_installer(self.answers('agent', systemd=True))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Could not create the webboard-agent host account', result.stderr)
        self.assertFalse((self.deploy / 'compose.yaml').exists())
        self.assertFalse((self.root / 'network').exists())

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed')
    def test_docker_only_agent_keeps_image_user_and_does_not_create_host_account(self):
        self.assert_ok(self.run_installer(self.answers('agent')))
        self.assertNotIn('user', self.config()['services']['agent'])
        self.assertEqual(self.account_calls(), [])

    def test_ambiguous_ipv4_is_rejected(self):
        result = self.run_helpers('if valid_ipv4 010.0.0.1 || valid_cidr 010.0.0.0/8; then exit 1; fi; valid_ipv4 10.0.0.1; valid_cidr 10.0.0.0/8')
        self.assert_ok(result)

    def test_package_detection_requires_installable_candidate(self):
        result = self.run_helpers('apt-cache() { if [[ "$2" == docker-compose-plugin ]]; then printf "  Candidate: (none)\\n"; else printf "  Candidate: 2.0.0\\n"; fi; }; if apt_has_candidate docker-compose-plugin; then exit 1; fi; apt_has_candidate docker-compose-v2')
        self.assert_ok(result)

    def test_component_cli_option_and_resume_constraints(self):
        self.assert_ok(self.run_installer(self.answers().split('\n', 1)[1], '--components', 'db,ui'))
        result = self.run_installer('', '--resume', '--components', 'agent')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('saved component selection', result.stderr)

    def test_default_directory_and_port_prompts(self):
        self.env['HOME'] = str(self.root)
        answers = self.answers().split('\n')
        answers[1:1] = ['', '']
        result = subprocess.run(['bash', str(INSTALLER)], input='\n'.join(answers), text=True, capture_output=True, env=self.env, timeout=60)
        self.assert_ok(result)
        self.assertIn('127.0.0.1:8080:8080', (self.root / 'webboard/compose.yaml').read_text())

    def test_invalid_port_does_not_create_directory(self):
        result = self.run_installer('', '--port', '0')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.deploy.exists())

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed')
    def test_missing_buildx_stops_before_creating_secrets(self):
        self.env['MOCK_BUILDX_MISSING'] = '1'
        result = self.run_installer(self.answers('ui,agent'))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('requires Docker Buildx', result.stderr)
        self.assertFalse((self.deploy / 'tls').exists())
        self.assertFalse((self.root / 'network').exists())

if __name__ == '__main__':
    unittest.main()
