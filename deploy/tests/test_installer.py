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
import json, os, pathlib, subprocess, sys
args = sys.argv[1:]
with open(os.environ['MOCK_LOG'], 'a') as log:
    log.write(json.dumps(args) + '\n')
if args[:1] in [['info'], ['ps'], ['volume']]:
    sys.exit(0)
if args[:2] == ['network', 'create']:
    pathlib.Path(os.environ['MOCK_NETWORK']).touch()
    print('mock-network')
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
            pathlib.Path(command[-1]).write_text('public browser CA mock\n')
else:
    raise SystemExit('Unexpected Docker invocation: ' + repr(args))
'''

@unittest.skipUnless(DOCKER and shutil.which('openssl'), 'Docker Compose and OpenSSL required')
class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='webboard-installer-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.deploy = self.root / 'deployment with spaces'
        bin_dir = self.root / 'bin'
        bin_dir.mkdir()
        mock = bin_dir / 'docker'
        mock.write_text(MOCK_DOCKER)
        mock.chmod(0o755)
        self.env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ['PATH'],
                        MOCK_LOG=str(self.root / 'log'), MOCK_NETWORK=str(self.root / 'network'),
                        MOCK_FAIL=str(self.root / 'fail'), REAL_DOCKER=DOCKER)
        # Keep inherited Compose settings from influencing validation/deployment.
        for key in ('COMPOSE_FILE', 'COMPOSE_PROJECT_NAME', 'COMPOSE_PROFILES'):
            self.env.pop(key, None)

    def run_installer(self, answers='', *options):
        return subprocess.run(['bash', str(INSTALLER), '--directory', str(self.deploy),
                               '--port', '8088', *options], input=answers, text=True,
                              capture_output=True, env=self.env, timeout=60)

    def config(self):
        result = subprocess.run([DOCKER, 'compose', '--project-name', 'webboard',
                                 '--file', str(self.deploy / 'compose.yaml'), 'config', '--format', 'json'],
                                text=True, capture_output=True, timeout=15, env=self.env)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def calls(self):
        return [json.loads(line) for line in (self.root / 'log').read_text().splitlines()]

    def test_existing_proxy_secrets_and_no_agent(self):
        result = self.run_installer('existing\nn\n10.0.0.0/24\n\n')
        self.assertEqual(result.returncode, 0, result.stderr)
        cfg = self.config()
        self.assertEqual(set(cfg['services']), {'database', 'ui'})
        db = cfg['services']['database']['environment']['POSTGRES_PASSWORD']
        env = cfg['services']['ui']['environment']
        self.assertEqual(len(db), 64)
        self.assertNotEqual(db, env['Authentication__Jwt__SigningKey'])
        self.assertIn('Password=' + db, env['ConnectionStrings__WebboardDatabase'])
        self.assertEqual(env['Outbound__AllowedPrivateNetworks__0'], '10.0.0.0/24')
        self.assertEqual((self.deploy / 'compose.yaml').stat().st_mode & 0o777, 0o600)
        self.assertFalse((self.deploy / 'tls').exists())
        before = (self.deploy / 'compose.yaml').read_bytes()
        result = self.run_installer('')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())

    def test_local_https_input_validation(self):
        result = self.run_installer('wrong\nlocal\nbad/name\nlocalhost\n127.0.0.1\n99999\n8443\nn\nbad-cidr\n10.1.0.0/16\n\nn\n')
        self.assertEqual(result.returncode, 0, result.stderr)
        cfg = self.config()
        self.assertEqual(cfg['services']['proxy']['ports'][0]['published'], '8443')
        self.assertEqual(cfg['services']['proxy']['ports'][0]['host_ip'], '127.0.0.1')
        self.assertIn('tls internal', (self.deploy / 'Caddyfile').read_text())
        self.assertTrue((self.deploy / 'browser-ca.crt').exists())
        self.assertIn('trusted certificate authority', result.stdout)

    def test_public_https(self):
        result = self.run_installer('public\nboard.example.org\ny\nn\n\n')
        self.assertEqual(result.returncode, 0, result.stderr)
        cfg = self.config()
        self.assertEqual({p['published'] for p in cfg['services']['proxy']['ports']}, {'80', '443'})
        self.assertNotIn('tls internal', (self.deploy / 'Caddyfile').read_text())
        self.assertFalse((self.deploy / 'browser-ca.crt').exists())

    def test_public_requires_readiness_before_generating(self):
        result = self.run_installer('public\nboard.example.org\nn\n')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.deploy / 'compose.yaml').exists())
        self.assertFalse((self.root / 'network').exists())

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket(), 'Host Docker socket needed for agent selection')
    def test_agent_certificates_migration_failure_and_resume(self):
        # Select Docker monitoring and one control target; decline systemd if present.
        systemd_answer = 'n\n' if Path('/run/dbus/system_bus_socket').is_socket() and Path('/run/systemd/system').is_dir() else ''
        (self.root / 'fail').touch()
        result = self.run_installer('existing\ny\ny\nmy-server\n\n' + systemd_answer + '\n')
        self.assertNotEqual(result.returncode, 0)
        cfg = self.config()
        agent = cfg['services']['agent']
        ui = cfg['services']['ui']
        self.assertNotIn('ports', agent)
        self.assertEqual(agent['environment']['Security__Tokens__0'], ui['environment']['Agents__0__Token'])
        self.assertEqual(agent['environment']['Controls__Docker__0'], 'my-server')
        self.assertEqual(ui['environment']['Outbound__AllowedPrivateNetworks__0'], '172.30.0.0/16')
        self.assertEqual(ui['pull_policy'], 'never')
        self.assertIn('ca-certificates curl', (self.deploy / 'trust/Dockerfile').read_text())
        checked = subprocess.run(['openssl', 'verify', '-CAfile', str(self.deploy / 'tls/ca/agent-ca.crt'),
                                  '-verify_hostname', 'agent', str(self.deploy / 'tls/agent.crt')], capture_output=True)
        self.assertEqual(checked.returncode, 0, checked.stderr)
        checked = subprocess.run(['openssl', 'pkcs12', '-in', str(self.deploy / 'tls/agent.pfx'),
                                  '-passin', 'file:' + str(self.deploy / 'tls/pfx-password'), '-noout'], capture_output=True)
        self.assertEqual(checked.returncode, 0, checked.stderr)
        self.assertEqual((self.deploy / 'tls').stat().st_mode & 0o777, 0o700)
        self.assertEqual((self.deploy / 'tls/agent.key').stat().st_mode & 0o777, 0o600)
        before = (self.deploy / 'compose.yaml').read_bytes()
        # Migration failure must prevent UI/agent startup.
        self.assertFalse(any('--wait' in call and call[-1] != 'database' and '--help' not in call for call in self.calls()))
        (self.root / 'fail').unlink()
        result = self.run_installer('', '--resume')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())
        calls = self.calls()
        self.assertTrue(any('exec' in call and 'ui' in call for call in calls))
        self.assertFalse(any('pull' in call and 'ui' in call for call in calls))
        self.assertTrue(any('build' in call and '--pull' in call for call in calls))
        # A second start/upgrade keeps credentials and builds the trusted image.
        result = subprocess.run(['bash', str(self.deploy / 'manage.sh'), 'update'], env=self.env,
                                capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(before, (self.deploy / 'compose.yaml').read_bytes())

    @unittest.skipUnless(Path('/var/run/docker.sock').is_socket() and
                         Path('/run/dbus/system_bus_socket').is_socket() and
                         Path('/run/systemd/system').is_dir(), 'Host Docker/systemd sockets needed')
    def test_systemd_monitoring_mounts(self):
        result = self.run_installer('existing\ny\ny\n\ny\n\n')
        self.assertEqual(result.returncode, 0, result.stderr)
        agent = self.config()['services']['agent']
        sources = {v['source'] for v in agent['volumes']}
        self.assertIn('/run/dbus/system_bus_socket', sources)
        self.assertIn('/run/systemd/system', sources)
        self.assertFalse(any(key.startswith('Controls__Systemd') for key in agent['environment']))

    def test_default_directory_and_port_prompts(self):
        self.env['HOME'] = str(self.root)
        result = subprocess.run(['bash', str(INSTALLER)], input='\n\nexisting\nn\n\n',
                                text=True, capture_output=True, env=self.env, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.root / 'webboard/compose.yaml').exists())
        self.assertIn('127.0.0.1:8080:8080', (self.root / 'webboard/compose.yaml').read_text())

    def test_invalid_port_does_not_create_directory(self):
        result = self.run_installer('', '--port', '0')
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.deploy.exists())

if __name__ == '__main__':
    unittest.main()
