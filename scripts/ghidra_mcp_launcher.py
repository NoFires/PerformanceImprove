"""Local stdio entry point: ensure the loopback headless backend, then bridge."""
import os
import runpy
import subprocess
import time
import urllib.request
from pathlib import Path

ROOT = Path(r'E:\Program\MCP\ghidra-mcp')
GHIDRA = Path(r'E:\Program\ReverseEngineering\Ghidra\ghidra_12.1.2_PUBLIC')
URL = 'http://127.0.0.1:8089'

def healthy():
    try:
        with urllib.request.urlopen(URL+'/check_connection', timeout=2) as response:
            return 'GhidraMCP' in response.read().decode()
    except (OSError, TimeoutError):
        return False

def ensure_backend():
    if healthy():
        return
    (ROOT/'logs').mkdir(exist_ok=True)
    env = os.environ.copy()
    env['JAVA_HOME'] = r'E:\Program\java21'
    env['PATH'] = env['JAVA_HOME']+r'\bin;'+env['PATH']
    temp = Path(r'E:\Program\Caches\Ghidra\tmp')
    temp.mkdir(parents=True, exist_ok=True)
    env['TEMP'] = env['TMP'] = str(temp)
    cmd = [os.environ.get('COMSPEC', 'cmd.exe'), '/d', '/c', str(GHIDRA/'support'/'launch.bat'),
           'fg', 'jdk', 'GhidraMCP', '2G',
           '-Djava.awt.headless=true -XX:ParallelGCThreads=2 -Djdk.net.unixdomain.tmpdir=E:/Program/Caches/Ghidra/tmp',
           'com.xebyte.headless.GhidraMCPHeadlessServer', '--bind', '127.0.0.1', '--port', '8089']
    with (ROOT/'logs'/'headless.log').open('ab') as log:
        process = subprocess.Popen(cmd, env=env, cwd=GHIDRA, stdin=subprocess.DEVNULL,
                                   stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW)
    (ROOT/'logs'/'launcher-pid.txt').write_text(str(process.pid))
    deadline = time.monotonic()+90
    while time.monotonic() < deadline:
        if healthy():
            return
        if process.poll() is not None:
            raise RuntimeError('Ghidra backend exited; inspect '+str(ROOT/'logs'/'headless.log'))
        time.sleep(0.5)
    raise TimeoutError('Ghidra backend startup exceeded 90 seconds')

if __name__ == '__main__':
    ensure_backend()
    os.environ['GHIDRA_MCP_URL'] = URL
    os.environ['GHIDRA_DEBUGGER_TOOLS'] = '0'
    runpy.run_module('bridge_mcp_ghidra', run_name='__main__')
