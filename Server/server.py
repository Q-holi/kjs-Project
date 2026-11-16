"""Small server-authoritative shared world. Run: python Server/server.py.

No Unity process is required on the server. See docs/protocol.md for the wire
contract. This is a multiplayer starter, not a production MMORPG backend.
"""

from __future__ import annotations

import argparse
import asyncio
from contextlib import suppress
from dataclasses import dataclass, field
import json
import logging
import math
import time
import unicodedata
import uuid

from websockets.asyncio.server import ServerConnection, serve
from websockets.exceptions import ConnectionClosed

LOG = logging.getLogger("kjs.world")


def encode(packet: dict) -> str:
    return json.dumps(packet, ensure_ascii=False, separators=(",", ":"), allow_nan=False)


def decode(raw: str | bytes) -> dict:
    if not isinstance(raw, str):
        raise ValueError("text_frames_required")
    def invalid_constant(value: str):
        raise ValueError("non_finite_number")
    packet = json.loads(raw, parse_constant=invalid_constant)
    if not isinstance(packet, dict):
        raise ValueError("object_required")
    return packet


def clean_text(value: object, limit: int) -> str:
    if not isinstance(value, str):
        raise ValueError("text_required")
    # Keep Korean and other Unicode text; strip control/format characters.
    return "".join(c for c in value if not unicodedata.category(c).startswith("C")).strip()[:limit]


@dataclass
class Player:
    id: str
    name: str
    x: float = 0.0
    z: float = 0.0
    yaw: float = 0.0
    seq: int = -1
    dx: float = 0.0
    dz: float = 0.0
    input_at: float = 0.0

    def wire(self) -> dict:
        return {"id": self.id, "name": self.name, "x": round(self.x, 4),
                "z": round(self.z, 4), "yaw": round(self.yaw, 2), "seq": self.seq}


@dataclass
class Session:
    ws: ServerConnection
    player: Player
    outgoing: asyncio.Queue = field(default_factory=lambda: asyncio.Queue(maxsize=8))
    tokens: float = 120.0
    token_at: float = field(default_factory=time.monotonic)
    chat_at: float = -math.inf
    slow: bool = False

    def allow_packet(self, now: float) -> bool:
        self.tokens = min(120.0, self.tokens + (now - self.token_at) * 60.0)
        self.token_at = now
        if self.tokens < 1:
            return False
        self.tokens -= 1
        return True


class World:
    def __init__(self, max_players: int = 64, tick_rate: int = 20):
        if not 1 <= max_players <= 256 or not 1 <= tick_rate <= 60:
            raise ValueError("max_players must be 1..256 and tick_rate 1..60")
        self.max_players = max_players
        self.tick_rate = tick_rate
        self.half_extent = 24.0
        self.speed = 5.0
        self.input_timeout = 0.30
        self.sessions: dict[str, Session] = {}
        self.tick_id = 0

    def _enqueue(self, session: Session, packet: str):
        if session.slow:
            return
        try:
            session.outgoing.put_nowait(packet)
        except asyncio.QueueFull:
            session.slow = True
            # A slow consumer must not stall the world or grow memory indefinitely.
            asyncio.create_task(session.ws.close(1013, "slow_consumer"))

    def broadcast(self, packet: dict):
        payload = encode(packet)
        for session in tuple(self.sessions.values()):
            self._enqueue(session, payload)

    async def _sender(self, session: Session):
        try:
            while True:
                payload = await session.outgoing.get()
                await asyncio.wait_for(session.ws.send(payload), timeout=2.0)
        except (ConnectionClosed, TimeoutError):
            await session.ws.close(1013, "send_failed")

    def apply_packet(self, session: Session, packet: dict, now: float):
        player = session.player
        kind = packet.get("type")
        if kind == "input":
            seq, dx, dz = packet.get("seq"), packet.get("dx"), packet.get("dz")
            if (type(seq) is not int or not 0 <= seq <= 2_147_483_647
                    or type(dx) not in (float, int) or type(dz) not in (float, int)):
                raise ValueError("invalid_input")
            if not math.isfinite(dx) or not math.isfinite(dz) or abs(dx) > 1 or abs(dz) > 1:
                raise ValueError("invalid_input")
            if seq <= player.seq:
                return
            magnitude = math.hypot(dx, dz)
            if magnitude > 1.0:
                dx, dz = dx / magnitude, dz / magnitude
            player.seq, player.dx, player.dz, player.input_at = seq, dx, dz, now
            # Client positions, dt and speed are never used.
        elif kind == "chat":
            text = clean_text(packet.get("text"), 140)
            if not text or now - session.chat_at < 0.5:
                return
            session.chat_at = now
            self.broadcast({"type": "chat", "id": player.id, "name": player.name, "text": text})
        elif kind == "ping":
            self._enqueue(session, encode({"type": "pong", "tick": self.tick_id}))
        else:
            raise ValueError("unknown_type")

    def step(self, dt: float, now: float):
        # Fixed step: a lag spike never permits a large teleport.
        dt = min(max(dt, 0.0), 1.0 / self.tick_rate)
        for session in self.sessions.values():
            p = session.player
            if now - p.input_at > self.input_timeout:
                p.dx = p.dz = 0.0
            p.x = max(-self.half_extent, min(self.half_extent, p.x + p.dx * self.speed * dt))
            p.z = max(-self.half_extent, min(self.half_extent, p.z + p.dz * self.speed * dt))
            if p.dx or p.dz:
                p.yaw = math.degrees(math.atan2(p.dx, p.dz))
        self.tick_id += 1

    async def tick_loop(self):
        period = 1.0 / self.tick_rate
        while True:
            start = time.monotonic()
            self.step(period, start)
            if self.sessions:
                self.broadcast({"type": "snapshot", "tick": self.tick_id,
                                "players": [s.player.wire() for s in self.sessions.values()]})
            await asyncio.sleep(max(0.0, period - (time.monotonic() - start)))

    async def handle(self, ws: ServerConnection):
        session = None
        sender = None
        try:
            if ws.request.path != "/world":
                await ws.close(1008, "use_/world")
                return
            first = decode(await asyncio.wait_for(ws.recv(), timeout=5.0))
            if (first.get("type") != "join" or type(first.get("version")) is not int
                    or first.get("version") != 1):
                raise ValueError("join_version_1_required")
            name = clean_text(first.get("name"), 20)
            if not name:
                raise ValueError("name_required")
            # No await between capacity check and registration: atomic on this loop.
            if len(self.sessions) >= self.max_players:
                await ws.send(encode({"type": "error", "message": "server_full"}))
                await ws.close(1013, "server_full")
                return
            player = Player(uuid.uuid4().hex, name)
            angle = len(self.sessions) * 2.39996
            player.x, player.z = 3.0 * math.cos(angle), 3.0 * math.sin(angle)
            session = Session(ws, player)
            self.sessions[player.id] = session
            self._enqueue(session, encode({"type": "welcome", "id": player.id, "version": 1,
                                          "tickRate": self.tick_rate, "speed": self.speed,
                                          "worldHalfExtent": self.half_extent,
                                          "maxPlayers": self.max_players}))
            sender = asyncio.create_task(self._sender(session))
            LOG.info("join id=%s players=%d", player.id, len(self.sessions))
            async for raw in ws:
                now = time.monotonic()
                if not session.allow_packet(now):
                    await ws.close(1008, "rate_limit")
                    break
                self.apply_packet(session, decode(raw), now)
        except (ValueError, OverflowError, RecursionError) as exc:
            # Do not echo untrusted exception text or raw packets.
            LOG.debug("invalid packet: %s", type(exc).__name__)
            await ws.close(1008, "invalid_packet")
        except TimeoutError:
            await ws.close(1008, "join_timeout")
        except ConnectionClosed:
            pass
        finally:
            if session:
                self.sessions.pop(session.player.id, None)
                self.broadcast({"type": "left", "id": session.player.id})
                LOG.info("leave id=%s players=%d", session.player.id, len(self.sessions))
            if sender:
                sender.cancel()
                with suppress(asyncio.CancelledError, ConnectionClosed):
                    await sender


async def run(host: str, port: int, max_players: int):
    world = World(max_players=max_players)
    async with serve(world.handle, host, port, max_size=4096, max_queue=8,
                     ping_interval=20, ping_timeout=20, close_timeout=2):
        LOG.info("kjs world ws://%s:%d/world max_players=%d", host, port, max_players)
        await world.tick_loop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1", help="Use 0.0.0.0 for LAN testing")
    parser.add_argument("--port", type=int, default=7777)
    parser.add_argument("--max-players", type=int, default=64)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    try:
        asyncio.run(run(args.host, args.port, args.max_players))
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
