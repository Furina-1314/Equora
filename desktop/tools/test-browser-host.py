"""Exercise the shipped native-messaging host against isolated policy files."""
import datetime as dt
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[2]
exe = root / 'desktop/Equora.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/BrowserHost/com.equora.nativehost.exe'
with tempfile.TemporaryDirectory(prefix='equora-host-') as directory:
    data = Path(directory)
    def write(name, value):
        (data / name).write_text(json.dumps(value), encoding='utf-8')
    def heartbeat(focusing=True, age=0, allow=False):
        now = dt.datetime.now().astimezone()
        write('restriction-heartbeat.json', {'updatedAt': (now-dt.timedelta(seconds=age)).isoformat(), 'focusing': focusing,
              'allowUntil': (now+dt.timedelta(minutes=15)).isoformat() if allow else None})
    rule = {'id': '1', 'kind': 'website', 'name': 'Test', 'target': 'example.com', 'enabled': True, 'duringFocus': True, 'dailyMinutes': 1}
    write('restrictions.json', {'enabled': True, 'rules': [rule]})
    heartbeat()
    env = dict(os.environ, EQUORA_TEST_DATA_DIRECTORY=directory)
    with subprocess.Popen([str(exe)], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE) as host:
        nonce = 0
        def query(**extra):
            global nonce
            nonce += 1
            payload = json.dumps({'type': 'query', 'protocolVersion': 1, 'nonce': nonce, **extra}).encode()
            host.stdin.write(struct.pack('<I', len(payload)) + payload)
            host.stdin.flush()
            header = host.stdout.read(4)
            assert len(header) == 4, 'Host exited before sending a response'
            message = json.loads(host.stdout.read(struct.unpack('<I', header)[0]))
            assert message['type'] == 'state', message
            return message['state']
        assert query()['blockedDomains'] == ['example.com']
        heartbeat(False)
        assert query()['blockedDomains'] == []
        time.sleep(0.15)
        state = query(usage={'domain': 'sub.example.com', 'seconds': 1})
        assert 0 < state['usedSeconds']['example.com'] <= 1
        now = dt.datetime.now().astimezone()
        write('website-usage.json', {'date': now.strftime('%Y-%m-%d'), 'seconds': {'example.com': 60}})
        assert query()['blockedDomains'] == ['example.com']
        heartbeat(allow=True)
        assert query()['blockedDomains'] == []
        heartbeat(age=20)
        state = query()
        assert not state['enabled'] and state['blockedDomains'] == []
        host.stdin.close()
        assert host.wait(timeout=5) == 0
print('PASS: shipped host framing, focus/pause, subdomain usage, daily quota, temporary unlock, stale lease.')
