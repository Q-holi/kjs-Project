"""Loopback bot test. Real sockets; does not launch any Unity clients."""
import argparse
import asyncio
from contextlib import suppress
import json
from pathlib import Path
import statistics
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from websockets.asyncio.client import connect
from websockets.asyncio.server import serve
from Server.server import World, encode


async def run(count: int, seconds: float) -> dict:
    world = World(max_players=count)
    server = await serve(world.handle, "127.0.0.1", 0, max_size=4096, close_timeout=1)
    url = f"ws://127.0.0.1:{server.sockets[0].getsockname()[1]}/world"
    ticker = asyncio.create_task(world.tick_loop())
    sockets = []
    readers = []
    state = [{"snapshots": 0, "full": 0, "chat": False, "last": None, "intervals": []}
             for _ in range(count)]
    try:
        async def join(i):
            ws = await connect(url, proxy=None)
            sockets.append(ws)
            await ws.send(encode({"type": "join", "version": 1, "name": f"Bot{i}"}))
            packet = json.loads(await asyncio.wait_for(ws.recv(), 5))
            assert packet["type"] == "welcome"
            return ws, packet["id"]

        clients = await asyncio.gather(*(join(i) for i in range(count)))
        ids = {pid for _, pid in clients}
        assert len(ids) == count

        async def read(i, ws):
            async for raw in ws:
                packet = json.loads(raw)
                if packet["type"] == "snapshot":
                    received = time.perf_counter()
                    if state[i]["last"] is not None:
                        state[i]["intervals"].append((received - state[i]["last"]) * 1000)
                    state[i]["last"] = received
                    state[i]["snapshots"] += 1
                    players = packet["players"]
                    if len(players) == count:
                        assert {p["id"] for p in players} == ids
                        state[i]["full"] += 1
                    assert all(-24 <= p["x"] <= 24 and -24 <= p["z"] <= 24 for p in players)
                elif packet["type"] == "chat" and packet["text"] == "64-client smoke 한글":
                    state[i]["chat"] = True

        readers = [asyncio.create_task(read(i, ws)) for i, (ws, _) in enumerate(clients)]
        started = time.perf_counter()
        seq = 0
        await clients[0][0].send(encode({"type": "chat", "text": "64-client smoke 한글"}))
        while time.perf_counter() - started < seconds:
            seq += 1
            direction = 1 if seq % 20 < 10 else -1
            await asyncio.gather(*(ws.send(encode({"type": "input", "seq": seq,
                                                   "dx": direction, "dz": 0})) for ws, _ in clients))
            await asyncio.sleep(0.1)
        for reader in readers:
            if reader.done():
                reader.result()  # Propagate any assertion or transport failure.
        assert all(s["full"] >= max(1, int(seconds * 10)) and s["chat"] for s in state), state
        intervals = sorted(value for s in state for value in s["intervals"])
        report = {
            "passed": True, "clients": count, "duration_seconds": round(time.perf_counter() - started, 3),
            "server_tick_rate": world.tick_rate, "input_rounds": seq,
            "min_snapshots_per_client": min(s["snapshots"] for s in state),
            "max_snapshots_per_client": max(s["snapshots"] for s in state),
            "full_roster_seen_by_all": True, "unicode_chat_seen_by_all": True,
            "snapshot_interval_median_ms": round(statistics.median(intervals), 3),
            "snapshot_interval_p95_ms": round(intervals[int(len(intervals) * 0.95)], 3),
            "scope": "Python WebSocket bots, loopback; not Unity rendering or WAN/MMORPG capacity."
        }
    finally:
        await asyncio.gather(*(ws.close() for ws in sockets))
        for reader in readers:
            reader.cancel()
        await asyncio.gather(*readers, return_exceptions=True)
        server.close()
        await server.wait_closed()
        ticker.cancel()
        with suppress(asyncio.CancelledError):
            await ticker
    assert not world.sessions
    report["players_after_disconnect"] = len(world.sessions)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--clients", type=int, default=64)
    parser.add_argument("--seconds", type=float, default=5)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if not 1 <= args.clients <= 256 or args.seconds < 1:
        parser.error("clients must be 1..256; seconds must be >= 1")
    result = asyncio.run(run(args.clients, args.seconds))
    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    print(rendered)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
