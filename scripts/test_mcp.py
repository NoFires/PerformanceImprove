"""Exercise the installed server over the actual MCP stdio transport."""
import asyncio
import json
from pathlib import Path
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


async def main():
    params = StdioServerParameters(
        command=r'E:\Program\MCP\wrath-analysis\.venv\Scripts\python.exe',
        args=['-u', r'E:\Program\MCP\wrath-analysis\server.py'])
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            initialized = await session.initialize()
            catalog = await session.list_tools()
            results = []
            for name, args in [
                ('game_inventory', {}),
                ('list_types', {'contains': 'GameVersion', 'limit': 5}),
                ('decompile_type', {'type_name': 'Kingmaker.GameVersion', 'max_chars': 3000}),
                ('search_source', {'query': 'public void Tick()', 'limit': 3}),
                ('read_source', {'path': 'Assembly-CSharp/Kingmaker/Game.cs', 'start_line': 830, 'line_count': 10}),
            ]:
                result = await session.call_tool(name, args)
                assert not result.isError, (name, result)
                results.append({'tool': name, 'success': True})
            blocked = await session.call_tool('read_source', {'path': '../../outside.cs'})
            assert blocked.isError
            output = {'protocol': initialized.protocolVersion, 'server': initialized.serverInfo.model_dump(),
                      'tools': [t.name for t in catalog.tools], 'calls': results,
                      'path_escape_rejected': blocked.isError}
            target = Path(__file__).resolve().parents[1] / 'analysis/mcp-verification.json'
            target.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding='utf-8')
            print(json.dumps(output, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    asyncio.run(main())
