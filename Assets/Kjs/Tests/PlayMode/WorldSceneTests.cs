using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Kjs.Tests
{
    public sealed class WorldSceneTests
    {
        private Scene scene;

        [UnityTest]
        public IEnumerator SharedWorld_LoadsWithCameraAndSupportedMaterial()
        {
            yield return SceneManager.LoadSceneAsync("SharedWorld", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByName("SharedWorld");
            Assert.That(scene.IsValid(), Is.True);
            KjsDemo demo = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<KjsDemo>(out var found)) demo = found;
            Assert.That(demo, Is.Not.Null, "The serialized scene must reference KjsDemo correctly.");
            Assert.That(demo.View, Is.Not.Null);
            var camera = demo.GetComponentInChildren<Camera>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.orthographic, Is.True);
            var material = Resources.Load<Material>("KjsSurface");
            Assert.That(material, Is.Not.Null);
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.That(material.shader.isSupported, Is.True, "The player needs the built-in Standard shader.");
            Assert.That(demo.View.PlayerCount, Is.Zero, "Disconnected world should have no fake players.");
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }
    }

    public sealed class WorldNetworkTests
    {
        private WorldClient first;
        private WorldClient second;

        [UnityTest]
        public IEnumerator TwoUnityTransports_JoinMoveChatAndLeave()
        {
            string url = Environment.GetEnvironmentVariable("KJS_INTEGRATION_URL");
            if (string.IsNullOrEmpty(url))
                Assert.Ignore("Start Server/server.py and set KJS_INTEGRATION_URL=ws://127.0.0.1:7777/world.");
            first = new WorldClient();
            second = new WorldClient();
            var firstConnect = first.ConnectAsync(url, JsonUtility.ToJson(new JoinRequest { name = "Unity A" }));
            var secondConnect = second.ConnectAsync(url, JsonUtility.ToJson(new JoinRequest { name = "Unity B" }));
            yield return new WaitUntil(() => firstConnect.IsCompleted && secondConnect.IsCompleted);
            Assert.That(first.IsOpen && second.IsOpen, Is.True);

            ServerPacket welcome = null;
            yield return WaitFor(first, p => p.type == "welcome", p => welcome = p);
            string id = welcome.id;
            yield return WaitFor(second, p => p.type == "welcome", p => { });
            ServerPacket initial = null;
            yield return WaitFor(second, p => p.type == "snapshot" && p.players.Length >= 2, p => initial = p);
            float oldX = Array.Find(initial.players, p => p.id == id).x;
            var input = first.SendAsync(JsonUtility.ToJson(new InputRequest { seq = 1, dx = 1, dz = 0 }));
            yield return new WaitUntil(() => input.IsCompleted);
            Assert.That(input.Result, Is.True);
            yield return WaitFor(second, p => p.type == "snapshot" && Array.Exists(p.players,
                x => x.id == id && x.x > oldX), p => { });
            var chat = first.SendAsync(JsonUtility.ToJson(new ChatRequest { text = "Unity 한글 테스트" }));
            yield return new WaitUntil(() => chat.IsCompleted);
            Assert.That(chat.Result, Is.True);
            yield return WaitFor(second, p => p.type == "chat" && p.text == "Unity 한글 테스트", p => { });
            first.Dispose();
            yield return WaitFor(second, p => p.type == "left" && p.id == id, p => { });
        }

        private static IEnumerator WaitFor(WorldClient client, Predicate<ServerPacket> match,
                                            Action<ServerPacket> found)
        {
            float deadline = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < deadline)
            {
                while (client.TryDequeue(out var raw))
                {
                    var packet = JsonUtility.FromJson<ServerPacket>(raw);
                    if (!match(packet)) continue;
                    found(packet);
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Timed out waiting for a server packet. Verify the local server and firewall.");
        }

        [TearDown]
        public void Cleanup()
        {
            first?.Dispose();
            second?.Dispose();
        }
    }
}
