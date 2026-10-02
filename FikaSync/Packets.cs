using Fika.Core.Networking.LiteNetLib.Utils;
using UnityEngine;

namespace CloudSixFikaSync
{

    internal struct CloudStatePacket : INetSerializable
    {
        public Vector3 WindOffset;
        public Vector3 MacroOffset;
        public Vector2 WindDir;
        public float MacroEvolution;
        public float BottomHeight;
        public float TopHeight;

        public void Serialize(NetDataWriter writer)
        {
            writer.PutUnmanaged(WindOffset);
            writer.PutUnmanaged(MacroOffset);
            writer.PutUnmanaged(WindDir);
            writer.PutUnmanaged(MacroEvolution);
            writer.PutUnmanaged(BottomHeight);
            writer.PutUnmanaged(TopHeight);
        }

        public void Deserialize(NetDataReader reader)
        {
            WindOffset = reader.GetUnmanaged<Vector3>();
            MacroOffset = reader.GetUnmanaged<Vector3>();
            WindDir = reader.GetUnmanaged<Vector2>();
            MacroEvolution = reader.GetUnmanaged<float>();
            BottomHeight = reader.GetUnmanaged<float>();
            TopHeight = reader.GetUnmanaged<float>();
        }
    }

    // Client -> host: "my clouds just spawned, send me the current authoritative state" so a joiner
    // locks immediately instead of waiting up to one heartbeat for the periodic broadcast. No payload —
    // the sender is identified by the NetPeer FIKA hands the server-side handler.
    internal struct CloudStateRequestPacket : INetSerializable
    {
        public void Serialize(NetDataWriter writer) { }
        public void Deserialize(NetDataReader reader) { }
    }
}
