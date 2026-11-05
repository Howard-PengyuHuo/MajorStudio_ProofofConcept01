using System;
using CarryOn.Taylor;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Multiplayer
{
    public struct NetworkGovernmentDocument : INetworkSerializable, IEquatable<NetworkGovernmentDocument>
    {
        public FixedString512Bytes Name, Nationality, Position;
        public TaskCategory Category;
        public NetworkGovernmentDocument(GovernmentDocument document)
        {
            Name = NetworkIdentity.Text(document.Name);
            Nationality = NetworkIdentity.Text(document.Nationality);
            Position = NetworkIdentity.Text(document.Position);
            Category = document.Category;
        }
        public GovernmentDocument ToDocument() => new()
        { Name = Name.ToString(), Nationality = Nationality.ToString(), Position = Position.ToString(), Category = Category };
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Nationality);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Category);
        }
        public bool Equals(NetworkGovernmentDocument other) => Name.Equals(other.Name) &&
            Nationality.Equals(other.Nationality) && Position.Equals(other.Position) && Category == other.Category;
    }
}
