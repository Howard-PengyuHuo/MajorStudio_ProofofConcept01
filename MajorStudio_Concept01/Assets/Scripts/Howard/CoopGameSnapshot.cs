using System;
using CarryOn.Taylor;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Multiplayer
{
    public struct NetworkIdentity : INetworkSerializable, IEquatable<NetworkIdentity>
    {
        public FixedString512Bytes Name, BirthPlace, IssueDate, PassportCode, ExpiryDate, Birthday;

        public NetworkIdentity(IdentityCard card)
        {
            Name = Text(card.Name);
            BirthPlace = Text(card.BirthPlace);
            IssueDate = Text(card.IssueDate);
            PassportCode = Text(card.PassportCode);
            ExpiryDate = Text(card.ExpiryDate);
            Birthday = Text(card.Birthday);
        }

        public IdentityCard ToCard() => new()
        {
            Name = Name.ToString(), BirthPlace = BirthPlace.ToString(),
            IssueDate = IssueDate.ToString(), PassportCode = PassportCode.ToString(), ExpiryDate = ExpiryDate.ToString(),
            Birthday = Birthday.ToString()
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
            serializer.SerializeValue(ref BirthPlace);
            serializer.SerializeValue(ref IssueDate);
            serializer.SerializeValue(ref PassportCode);
            serializer.SerializeValue(ref ExpiryDate);
            serializer.SerializeValue(ref Birthday);
        }

        public bool Equals(NetworkIdentity other) => Name.Equals(other.Name) && BirthPlace.Equals(other.BirthPlace)
            && IssueDate.Equals(other.IssueDate) && PassportCode.Equals(other.PassportCode) && ExpiryDate.Equals(other.ExpiryDate)
            && Birthday.Equals(other.Birthday);
    }

    // One atomic value: the Interview phase can never arrive ahead of its final printed identity.
    public struct CoopGameSnapshot : INetworkSerializable, IEquatable<CoopGameSnapshot>
    {
        public uint Round, Revision, ShakeSequence;
        public ulong TravelerId, HackerId;
        public EntryPhase Phase;
        public double PreparationEndsAt;
        public double QuestionEndsAt;
        public double IntroductionEndsAt;
        public bool DocumentMismatch;
        public NetworkIdentity Identity, PrintedIdentity;
        public NetworkGovernmentDocument Government, PrintedGovernment;
        public bool HasPrintedGovernment;
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
            serializer.SerializeValue(ref QuestionEndsAt);
            serializer.SerializeValue(ref IntroductionEndsAt);
            serializer.SerializeValue(ref DocumentMismatch);
            serializer.SerializeValue(ref Identity);
            serializer.SerializeValue(ref PrintedIdentity);
            serializer.SerializeValue(ref Government);
            serializer.SerializeValue(ref PrintedGovernment);
            serializer.SerializeValue(ref HasPrintedGovernment);
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
            && QuestionEndsAt.Equals(other.QuestionEndsAt)
            && IntroductionEndsAt.Equals(other.IntroductionEndsAt)
            && DocumentMismatch == other.DocumentMismatch
            && Identity.Equals(other.Identity) && PrintedIdentity.Equals(other.PrintedIdentity)
            && Government.Equals(other.Government) && PrintedGovernment.Equals(other.PrintedGovernment)
            && HasPrintedGovernment == other.HasPrintedGovernment
            && HasPrintedIdentity == other.HasPrintedIdentity && IsFinished == other.IsFinished
            && EntryApproved == other.EntryApproved && QuestionIndex == other.QuestionIndex
            && Reputation == other.Reputation && string.Equals(ResultReason, other.ResultReason, StringComparison.Ordinal);
    }
}
