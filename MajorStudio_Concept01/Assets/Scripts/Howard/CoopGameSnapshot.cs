using System;
using CarryOn.Taylor;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Multiplayer
{
    public struct NetworkIdentity : INetworkSerializable, IEquatable<NetworkIdentity>
    {
        public FixedString512Bytes Name, Nationality, Age, LicenseNumber;

        public NetworkIdentity(IdentityCard card)
        {
            Name = Text(card.Name);
            Nationality = Text(card.Nationality);
            Age = Text(card.Age);
            LicenseNumber = Text(card.LicenseNumber);
        }

        public IdentityCard ToCard() => new()
        {
            Name = Name.ToString(), Nationality = Nationality.ToString(),
            Age = Age.ToString(), LicenseNumber = LicenseNumber.ToString()
        };

        public static FixedString512Bytes Text(string value, int characterLimit = 64)
        {
            value ??= string.Empty;
            int length = Math.Min(value.Length, characterLimit);
            if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
            return new FixedString512Bytes(value.Substring(0, length));
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Nationality);
            serializer.SerializeValue(ref Age);
            serializer.SerializeValue(ref LicenseNumber);
        }

        public bool Equals(NetworkIdentity other) => Name.Equals(other.Name) && Nationality.Equals(other.Nationality)
            && Age.Equals(other.Age) && LicenseNumber.Equals(other.LicenseNumber);
    }

    // One atomic value: the Interview phase can never arrive ahead of its final printed identity.
    public struct CoopGameSnapshot : INetworkSerializable, IEquatable<CoopGameSnapshot>
    {
        public uint Round, Revision, ShakeSequence;
        public ulong TravelerId, HackerId;
        public EntryPhase Phase;
        public double PreparationEndsAt;
        public NetworkIdentity Identity, PrintedIdentity;
        public bool HasPrintedIdentity, IsFinished, EntryApproved;
        public int QuestionIndex, Reputation;
        public string ResultReason;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref Revision);
            serializer.SerializeValue(ref ShakeSequence);
            serializer.SerializeValue(ref TravelerId);
            serializer.SerializeValue(ref HackerId);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref PreparationEndsAt);
            serializer.SerializeValue(ref Identity);
            serializer.SerializeValue(ref PrintedIdentity);
            serializer.SerializeValue(ref HasPrintedIdentity);
            serializer.SerializeValue(ref IsFinished);
            serializer.SerializeValue(ref EntryApproved);
            serializer.SerializeValue(ref QuestionIndex);
            serializer.SerializeValue(ref Reputation);
            serializer.SerializeValue(ref ResultReason);
        }

        public bool Equals(CoopGameSnapshot other) => Round == other.Round && Revision == other.Revision
            && ShakeSequence == other.ShakeSequence && TravelerId == other.TravelerId && HackerId == other.HackerId
            && Phase == other.Phase && PreparationEndsAt.Equals(other.PreparationEndsAt)
            && Identity.Equals(other.Identity) && PrintedIdentity.Equals(other.PrintedIdentity)
            && HasPrintedIdentity == other.HasPrintedIdentity && IsFinished == other.IsFinished
            && EntryApproved == other.EntryApproved && QuestionIndex == other.QuestionIndex
            && Reputation == other.Reputation && string.Equals(ResultReason, other.ResultReason, StringComparison.Ordinal);
    }
}
