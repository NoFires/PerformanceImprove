"""Verify deployment/config and ensure analysed DLL files have not changed."""
import ast
import hashlib
import json
import tomllib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    sources = [ROOT / 'mcp/server.py', *(ROOT / 'scripts').glob('*.py')]
    for path in sources:
        ast.parse(path.read_text(encoding='utf-8'))
    inventory = json.loads((ROOT / 'analysis/inventory.json').read_text(encoding='utf-8'))
    game = Path(inventory['game_root'])
    paths = [(game / item['path'], item['sha256']) for item in inventory['assemblies']]
    paths.extend((game / 'Mods' / mod['folder'] / item['name'], item['sha256'])
                 for mod in inventory['mods'] for item in mod['dlls'])
    changed = []
    for path, expected in paths:
        with path.open('rb') as stream:
            actual = hashlib.file_digest(stream, 'sha256').hexdigest()
        if actual != expected:
            changed.append(str(path))
    config = tomllib.loads((Path.home() / '.codex/config.toml').read_text(encoding='utf-8'))
    server = config['mcp_servers']['wrath-analysis']
    assert Path(server['command']).is_file()
    assert Path(server['args'][-1]).read_bytes() == (ROOT / 'mcp/server.py').read_bytes()
    assert not changed, changed
    result = {'python_sources_parsed': len(sources), 'game_and_mod_dlls_checked': len(paths),
              'changed_dlls': changed, 'codex_toml_valid': True, 'deployed_mcp_matches_source': True}
    (ROOT / 'analysis/final-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
