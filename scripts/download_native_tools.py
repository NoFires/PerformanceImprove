"""Download pinned upstream releases to E:, verify GitHub SHA256 digests, extract."""
import concurrent.futures
import hashlib
import json
import urllib.request
import zipfile
from pathlib import Path

CACHE = Path(r'E:\Program\Caches\ReverseEngineering')
TARGETS = [
    ('NationalSecurityAgency/ghidra', 'Ghidra_12.1.2_build', 'ghidra_', Path(r'E:\Program\ReverseEngineering\Ghidra')),
    ('x64dbg/x64dbg', '2026.05.27', 'snapshot_', Path(r'E:\Program\ReverseEngineering\x64dbg\2026-05-27')),
    ('bethington/ghidra-mcp', 'v6.0.0', 'GhidraMCP-', Path(r'E:\Program\MCP\ghidra-mcp\extension')),
    ('bethington/ghidra-mcp', 'v6.0.0', 'ghidra_mcp_bridge-', None),
    ('bromoket/x64dbg_mcp', 'v2.3.0', 'x64dbg_mcp.dp64', None),
    ('bromoket/x64dbg_mcp', 'v2.3.0', 'x64dbg_mcp.dp32', None),
]

def fetch(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent':'PF-local-RE-setup'}), timeout=120)

def install(spec):
    repo, tag, prefix, dest = spec
    with fetch(f'https://api.github.com/repos/{repo}/releases/tags/{tag}') as response:
        release = json.load(response)
    asset = next(a for a in release['assets'] if a['name'].startswith(prefix))
    archive = CACHE / asset['name']
    expected = asset['digest'].removeprefix('sha256:')
    if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
        partial = archive.with_suffix(archive.suffix+'.partial')
        with fetch(asset['browser_download_url']) as source, partial.open('wb') as output:
            while chunk := source.read(4*1024*1024):
                output.write(chunk)
        assert hashlib.sha256(partial.read_bytes()).hexdigest() == expected, archive
        partial.replace(archive)
    if dest:
        dest.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as package:
            for member in package.infolist():
                assert (dest/member.filename).resolve().is_relative_to(dest.resolve())
            package.extractall(dest)
    result = dict(repo=repo, tag=tag, url=asset['browser_download_url'], archive=str(archive), sha256=expected,
                  digest_verified=True, destination=str(dest) if dest else None)
    print(json.dumps(result), flush=True)
    return result

if __name__ == '__main__':
    CACHE.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(install, TARGETS))
    target = Path(__file__).resolve().parents[1]/'analysis/native-tools-downloads.json'
    target.write_text(json.dumps(results, indent=2), encoding='utf-8')
