"""Real-loopback protocol tests and deterministic movement regression tests."""
import asyncio
from contextlib import suppress
import json
import math
import time
import unittest

from websockets.asyncio.client import connect
from websockets.asyncio.server import serve
from websockets.exceptions import ConnectionClosed

from Server.server import Player, Session, World, encode


async def receive_type(ws, kind, predicate=lambda p: True):
    async with asyncio.timeout(4):
        while True:
            packet = json.loads(await ws.recv())
            if packet["type"] == kind and predicate(packet):
                return packet


class MovementTests(unittest.TestCase):
    def setUp(self):
        self.world = World()
        self.player = Player("test", "Tester")
        self.session = Session(None, self.player)
        self.world.sessions["test"] = self.session

    def test_diagonal_speed_and_teleport_fields(self):
        self.world.apply_packet(self.session, {"type": "input", "seq": 1, "dx": 1, "dz": 1,
                                               "x": 9999, "z": 9999, "speed": 9999, "dt": 9999}, 10)
        self.world.step(500, 10)
        self.assertAlmostEqual(math.hypot(self.player.x, self.player.z), 5 / 20)

    def test_world_boundary_and_stale_sequence(self):
        self.player.x = 23.99
        self.world.apply_packet(self.session, {"type": "input", "seq": 2, "dx": 1, "dz": 0}, 10)
        self.world.apply_packet(self.session, {"type": "input", "seq": 1, "dx": -1, "dz": 0}, 10)
        self.world.step(0.05, 10)
        self.assertEqual(self.player.x, 24)
        self.assertEqual(self.player.seq, 2)

    def test_missing_inputs_stop_player(self):
        self.world.apply_packet(self.session, {"type": "input", "seq": 0, "dx": 1, "dz": 0}, 10)
        self.world.step(0.05, 10)
        before = self.player.x
        self.world.step(0.05, 10.31)
        self.assertEqual(self.player.x, before)

    def test_nonfinite_and_boolean_input_rejected(self):
        for value in [math.inf, math.nan, True, "1", 2]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.world.apply_packet(self.session, {"type": "input", "seq": 0,
                                                       "dx": value, "dz": 0}, 10)


class ProtocolTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.world = World(max_players=32)
        self.server = await serve(self.world.handle, "127.0.0.1", 0,
                                  max_size=4096, max_queue=8, close_timeout=0.5)
        self.url = f"ws://127.0.0.1:{self.server.sockets[0].getsockname()[1]}/world"
        self.tick_task = asyncio.create_task(self.world.tick_loop())
        self.clients = []

    async def asyncTearDown(self):
        await asyncio.gather(*(ws.close() for ws in self.clients))
        self.server.close()
        await self.server.wait_closed()
        self.tick_task.cancel()
        with suppress(asyncio.CancelledError):
            await self.tick_task
        self.assertEqual(len(self.world.sessions), 0)

    async def join(self, name="Tester"):
        ws = await connect(self.url, proxy=None)
        self.clients.append(ws)
        await ws.send(encode({"type": "join", "version": 1, "name": name}))
        first = json.loads(await asyncio.wait_for(ws.recv(), 3))
        self.assertEqual(first["type"], "welcome", "welcome must precede all snapshots")
        return ws, first["id"]

    async def assert_closed(self, ws, code):
        with self.assertRaises(ConnectionClosed) as raised:
            while True:
                await asyncio.wait_for(ws.recv(), 3)
        self.assertEqual(raised.exception.rcvd.code, code)

    async def test_32_clients_see_each_other_and_unicode_chat(self):
        clients = await asyncio.gather(*(self.join(f"사용자{i}") for i in range(32)))
        ids = {pid for _, pid in clients}
        self.assertEqual(len(ids), 32)
        states = await asyncio.gather(*(receive_type(ws, "snapshot", lambda p: len(p["players"]) == 32)
                                        for ws, _ in clients))
        for state in states:
            self.assertEqual({p["id"] for p in state["players"]}, ids)
        await clients[0][0].send(encode({"type": "chat", "text": "안녕하세요!\n함께 테스트"}))
        messages = await asyncio.gather(*(receive_type(ws, "chat") for ws, _ in clients))
        for message in messages:
            self.assertEqual(message["text"], "안녕하세요!함께 테스트")
            self.assertEqual(message["id"], clients[0][1])

    async def test_movement_observed_by_second_client(self):
        ws, pid = await self.join("Mover")
        observer, _ = await self.join("Observer")
        initial = await receive_type(observer, "snapshot", lambda p: len(p["players"]) == 2)
        old = next(p for p in initial["players"] if p["id"] == pid)
        await ws.send(encode({"type": "input", "seq": 1, "dx": 1, "dz": 0}))
        changed = await receive_type(observer, "snapshot", lambda p: any(
            x["id"] == pid and x["x"] > old["x"] for x in p["players"]))
        new = next(p for p in changed["players"] if p["id"] == pid)
        self.assertGreater(new["x"], old["x"])
        self.assertLessEqual(new["x"] - old["x"], 1.5)
        self.assertEqual(new["seq"], 1)

    async def test_disconnect_cleanup_and_new_identity(self):
        first, pid = await self.join()
        observer, _ = await self.join()
        await first.close()
        await receive_type(observer, "left", lambda p: p["id"] == pid)
        state = await receive_type(observer, "snapshot", lambda p: len(p["players"]) == 1)
        self.assertNotIn(pid, {p["id"] for p in state["players"]})
        _, new_id = await self.join()
        self.assertNotEqual(pid, new_id)

    async def test_capacity_rejects_extra_client(self):
        self.world.max_players = 1
        await self.join()
        extra = await connect(self.url, proxy=None)
        self.clients.append(extra)
        await extra.send(encode({"type": "join", "version": 1, "name": "Extra"}))
        packet = json.loads(await extra.recv())
        self.assertEqual(packet, {"type": "error", "message": "server_full"})
        await self.assert_closed(extra, 1013)
        self.assertEqual(len(self.world.sessions), 1)

    async def test_invalid_packet_does_not_break_other_clients(self):
        bad, _ = await self.join()
        survivor, _ = await self.join()
        await bad.send('{"type":"input","seq":1,"dx":NaN,"dz":0}')
        await self.assert_closed(bad, 1008)
        await survivor.send(encode({"type": "ping"}))
        await receive_type(survivor, "pong")

    async def test_wrong_protocol_version(self):
        ws = await connect(self.url, proxy=None)
        self.clients.append(ws)
        await ws.send(encode({"type": "join", "version": 99, "name": "Tester"}))
        await self.assert_closed(ws, 1008)

    async def test_binary_and_oversized_packets(self):
        binary, _ = await self.join()
        await binary.send(b"invalid")
        await self.assert_closed(binary, 1008)
        large, _ = await self.join()
        await large.send("a" * 5000)
        await self.assert_closed(large, 1009)

    async def test_chat_rate_limit_and_length(self):
        ws, _ = await self.join()
        observer, _ = await self.join()
        await ws.send(encode({"type": "chat", "text": "가" * 200}))
        chat = await receive_type(observer, "chat")
        self.assertEqual(len(chat["text"]), 140)
        await ws.send(encode({"type": "chat", "text": "too soon"}))
        await ws.send(encode({"type": "ping"}))
        await receive_type(ws, "pong")
        # Incoming ping was processed after chat; no second broadcast was queued.
        await observer.send(encode({"type": "ping"}))
        while True:
            packet = json.loads(await asyncio.wait_for(observer.recv(), 3))
            self.assertNotEqual(packet["type"], "chat")
            if packet["type"] == "pong":
                break

    async def test_packet_flood_disconnects_sender(self):
        ws, _ = await self.join()
        with suppress(ConnectionClosed):
            for _ in range(200):
                await ws.send(encode({"type": "ping"}))
        # Queue overflow may close a non-reading flooder first with 1013.
        with self.assertRaises(ConnectionClosed) as raised:
            while True:
                await asyncio.wait_for(ws.recv(), 3)
        self.assertIn(raised.exception.rcvd.code, (1008, 1013))


if __name__ == "__main__":
    unittest.main()
