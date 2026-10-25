using UnityEngine.Networking;

namespace LegacyMapExpansion
{
    internal sealed class MapChallenge : MessageBase
    {
        public MapChallenge() { }
        internal string Nonce, Identity;
        internal int Protocol;
        public override void Serialize(NetworkWriter writer) { writer.Write(Protocol); writer.Write(Nonce); writer.Write(Identity); }
        public override void Deserialize(NetworkReader reader) { Protocol = reader.ReadInt32(); Nonce = reader.ReadString(); Identity = reader.ReadString(); }
    }
    internal sealed class MapProof : MessageBase
    {
        public MapProof() { }
        internal string Nonce, Identity;
        internal int Protocol;
        internal bool Ready;
        public override void Serialize(NetworkWriter writer) { writer.Write(Protocol); writer.Write(Nonce); writer.Write(Identity); writer.Write(Ready); }
        public override void Deserialize(NetworkReader reader) { Protocol = reader.ReadInt32(); Nonce = reader.ReadString(); Identity = reader.ReadString(); Ready = reader.ReadBoolean(); }
    }
    internal sealed class MapResult : MessageBase
    {
        public MapResult() { }
        internal string Nonce, Reason;
        internal bool Accepted;
        public override void Serialize(NetworkWriter writer) { writer.Write(Nonce); writer.Write(Accepted); writer.Write(Reason); }
        public override void Deserialize(NetworkReader reader) { Nonce = reader.ReadString(); Accepted = reader.ReadBoolean(); Reason = reader.ReadString(); }
    }
}
