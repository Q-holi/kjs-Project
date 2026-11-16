using System;

namespace Kjs
{
    [Serializable]
    public sealed class JoinRequest
    {
        public string type = "join";
        public int version = 1;
        public string name;
    }

    [Serializable]
    public sealed class InputRequest
    {
        public string type = "input";
        public int seq;
        public float dx;
        public float dz;
    }

    [Serializable]
    public sealed class ChatRequest
    {
        public string type = "chat";
        public string text;
    }

    [Serializable]
    public sealed class PlayerSnapshot
    {
        public string id;
        public string name;
        public float x;
        public float z;
        public float yaw;
        public int seq;
    }

    [Serializable]
    public sealed class ServerPacket
    {
        public string type;
        public string id;
        public int version;
        public int tick;
        public int tickRate;
        public int maxPlayers;
        public float speed;
        public float worldHalfExtent;
        public string name;
        public string text;
        public string message;
        public PlayerSnapshot[] players;
    }
}
