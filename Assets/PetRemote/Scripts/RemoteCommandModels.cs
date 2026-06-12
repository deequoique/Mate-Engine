using System;

namespace PetRemote
{
    [Serializable]
    public class RemoteCommand
    {
        public string type;
        public string name;
        public float? strength;
        public int? holdMs;
        public string url;
        public string text;
        public string corner;
    }

    [Serializable]
    public class JoinAckResponse
    {
        public bool ok;
        public string error;
        public RemotePeers peers;
    }

    [Serializable]
    public class RemotePeers
    {
        public bool controller;
        public bool pet;
    }

    [Serializable]
    public class MotionMeta
    {
        public string id;
        public string label;
        public bool loop;
    }

    [Serializable]
    public class MotionManifestEntry
    {
        public string id;
        public string label;
        public string file;
        public bool loop;
        public string fallback;
    }

    [Serializable]
    public class RoomKickedPayload
    {
        public string reason;
    }
}
