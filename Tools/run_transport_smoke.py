"""Start an isolated Python world and run the actual C# WorldClient against it."""
import asyncio
from contextlib import suppress
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from websockets.asyncio.server import serve
from Server.server import World


async def main():
    world = World()
    server = await serve(world.handle, "127.0.0.1", 0, max_size=4096)
    port = server.sockets[0].getsockname()[1]
    ticker = asyncio.create_task(world.tick_loop())
    try:
        dotnet = os.environ.get("DOTNET_EXECUTABLE", "dotnet")
        precompiled = os.environ.get("KJS_TRANSPORT_SMOKE_DLL")
        arguments = ([precompiled] if precompiled else
                     ["run", "--project", "Tools/TransportSmoke/TransportSmoke.csproj",
                      "--configuration", "Release", "--"])
        process = await asyncio.create_subprocess_exec(
            dotnet, *arguments, f"ws://127.0.0.1:{port}/world",
            cwd=Path(__file__).resolve().parents[1])
        result = await asyncio.wait_for(process.wait(), 90)
        if result:
            raise SystemExit(result)
    finally:
        if 'process' in locals() and process.returncode is None:
            process.kill()
            await process.wait()
        server.close()
        await server.wait_closed()
        ticker.cancel()
        with suppress(asyncio.CancelledError):
            await ticker
    if world.sessions:
        raise AssertionError("C# clients weren't cleaned up")


if __name__ == "__main__":
    asyncio.run(main())
