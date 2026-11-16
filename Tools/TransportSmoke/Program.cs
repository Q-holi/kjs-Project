using System;
using System.Text.Json;
using System.Threading.Tasks;
using Kjs;

internal static class Program
{
    private static async Task<JsonElement> WaitFor(WorldClient client, Func<JsonElement, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            while (client.TryDequeue(out string raw))
            {
                using var document = JsonDocument.Parse(raw);
                if (match(document.RootElement)) return document.RootElement.Clone();
            }
            await Task.Delay(10);
        }
        string events = "";
        while (client.TryDequeueEvent(out string message)) events += message + " ";
        throw new Exception("Packet timeout. " + events);
    }

    private static string Kind(JsonElement p) => p.GetProperty("type").GetString();
    private static float Position(JsonElement state, string id)
    {
        foreach (var player in state.GetProperty("players").EnumerateArray())
            if (player.GetProperty("id").GetString() == id) return player.GetProperty("x").GetSingle();
        throw new Exception("Player missing from snapshot.");
    }

    public static async Task<int> Main(string[] args)
    {
        string url = args.Length > 0 ? args[0] : "ws://127.0.0.1:7777/world";
        using var first = new WorldClient();
        using var second = new WorldClient();
        await Task.WhenAll(first.ConnectAsync(url, "{\"type\":\"join\",\"version\":1,\"name\":\"CSharp A\"}"),
                           second.ConnectAsync(url, "{\"type\":\"join\",\"version\":1,\"name\":\"CSharp B\"}"));
        var welcome = await WaitFor(first, p => Kind(p) == "welcome");
        var other = await WaitFor(second, p => Kind(p) == "welcome");
        string id = welcome.GetProperty("id").GetString();
        if (id == other.GetProperty("id").GetString()) throw new Exception("Duplicate IDs.");
        var initial = await WaitFor(second, p => Kind(p) == "snapshot" && p.GetProperty("players").GetArrayLength() == 2);
        float x = Position(initial, id);
        if (!await first.SendAsync("{\"type\":\"input\",\"seq\":1,\"dx\":1,\"dz\":0}"))
            throw new Exception("Input send failed.");
        await WaitFor(second, p => Kind(p) == "snapshot" && Position(p, id) > x);
        string text = "C# 한글 통신 테스트";
        await first.SendAsync(JsonSerializer.Serialize(new { type = "chat", text }));
        await WaitFor(second, p => Kind(p) == "chat" && p.GetProperty("text").GetString() == text);
        first.Dispose();
        await WaitFor(second, p => Kind(p) == "left" && p.GetProperty("id").GetString() == id);
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, clients = 2,
            checks = new[] { "welcome", "unique_ids", "shared_snapshot", "remote_movement", "unicode_chat", "leave" },
            runtime = Environment.Version.ToString(), note = "Same WorldClient.cs; .NET runtime, not Unity Editor." }));
        return 0;
    }
}
