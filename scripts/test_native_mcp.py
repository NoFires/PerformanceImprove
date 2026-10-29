"""Exercise both native-tool bridges over real MCP stdio, without the game."""
import asyncio
import json
import re
import subprocess
import sys
from pathlib import Path
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

ROOT = Path(__file__).resolve().parents[1]
SERVERS = {
    'ghidra': StdioServerParameters(command=r'E:\Program\MCP\ghidra-mcp\.venv\Scripts\python.exe',
                                   args=['-u', r'E:\Program\MCP\ghidra-mcp\launcher.py']),
    'x64dbg': StdioServerParameters(command=r'E:\Program\Development\node\node.exe',
                                   args=[r'E:\Program\MCP\x64dbg-mcp\node_modules\x64dbg-mcp-server\dist\index.js'],
                                   env={'X64DBG_MCP_TIMEOUT': '15000'}),
}

async def check(name, params):
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            initialized = await session.initialize()
            catalog = await session.list_tools()
            result = {'protocol': initialized.protocolVersion, 'server': initialized.serverInfo.model_dump(),
                      'tools': [tool.model_dump() for tool in catalog.tools], 'calls': []}
            async def call(tool, args):
                response = await session.call_tool(tool, args)
                assert not response.isError, response
                result['calls'].append({'tool':tool, 'arguments':args, 'result':response.model_dump()})
                return response
            if name == 'x64dbg':
                state = await call('x64dbg_debug', {'action': {'action': 'state', 'include_health': True}})
                if '--exercise' in sys.argv:
                    assert json.loads(state.content[0].text)['state']['state'] == 'stopped', 'Debugger must be idle'
                    await call('x64dbg_command', {'action':{'action':'execute','command':r'init "E:\Program\MCP\TestTargets\native_mcp_smoke.exe"'}})
                    try:
                        for _ in range(20):
                            await asyncio.sleep(0.5)
                            response = await session.call_tool('x64dbg_modules', {'action':{'action':'list'}})
                            if 'native_mcp_smoke' in str(response):
                                result['calls'].append({'tool':'x64dbg_modules', 'result':response.model_dump()})
                                break
                        else:
                            raise AssertionError('Smoke target was not loaded')
                        await call('x64dbg_command', {'action':{'action':'evaluate','expression':'cip'}})
                    finally:
                        await call('x64dbg_debug', {'action':{'action':'stop_debug'}})
            elif '--exercise' in sys.argv:
                await call('load_program', {'file':r'E:\Program\MCP\TestTargets\native_mcp_smoke.exe'})
                await call('run_analysis', {})
                metadata = await call('get_metadata', {})
                assert 'native_mcp_smoke' in str(metadata), metadata
                symbols = subprocess.check_output([r'E:\Program\C Compiler\MSYS2\ucrt64\bin\nm.exe',
                                                    r'E:\Program\MCP\TestTargets\native_mcp_smoke.exe'], text=True)
                address = re.search(r'([0-9a-fA-F]+) T ArithmeticProbe', symbols).group(1)
                decompiled = await call('decompile_function', {'address':'0x'+address})
                assert 'ArithmeticProbe' in str(decompiled) and 'return' in str(decompiled), decompiled
            (ROOT/'analysis'/f'{name}-mcp-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
            print(json.dumps({'name':name, 'tools':len(catalog.tools), 'calls':result['calls']}), flush=True)
            if name == 'ghidra' and '--exercise' not in sys.argv:
                for tool in catalog.tools:
                    if tool.name in ('check_connection','load_program','run_analysis','list_functions','get_metadata','decompile_function','create_project'):
                        print(tool.model_dump_json(), flush=True)

async def main():
    results = await asyncio.gather(*(check(name, params) for name, params in SERVERS.items()), return_exceptions=True)
    for result in results:
        if isinstance(result, BaseException):
            raise result

if __name__ == '__main__':
    asyncio.run(main())
