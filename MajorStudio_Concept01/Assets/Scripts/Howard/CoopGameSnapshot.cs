using System;
using CarryOn.Taylor;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Multiplayer
{
    public struct NetworkIdentity : INetworkSerializable, IEquatable<NetworkIdentity>
    {
        public string FirstName, LastName;
        public FixedString512Bytes Name, BirthPlace, IssueDate, PassportCode, ExpiryDate, Birthday;

        public NetworkIdentity(IdentityCard card)
        {
            FirstName = card.FirstName ?? "";
            LastName = card.LastName ?? "";
            Name = Text(card.Name, 129);
            BirthPlace = Text(card.BirthPlace);
            IssueDate = Text(card.IssueDate);
            PassportCode = Text(card.PassportCode);
            ExpiryDate = Text(card.ExpiryDate);
            Birthday = Text(card.Birthday);
        }

        public IdentityCard ToCard() => new()
        {
            FirstName = FirstName, LastName = LastName,
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
            if (serializer.IsWriter) { FirstName ??= ""; LastName ??= ""; }
            serializer.SerializeValue(ref FirstName);
            serializer.SerializeValue(ref LastName);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref BirthPlace);
            serializer.SerializeValue(ref IssueDate);
            serializer.SerializeValue(ref PassportCode);
            serializer.SerializeValue(ref ExpiryDate);
            serializer.SerializeValue(ref Birthday);
        }

        public bool Equals(NetworkIdentity other) => string.Equals(FirstName ?? "", other.FirstName ?? "", StringComparison.Ordinal) && string.Equals(LastName ?? "", other.LastName ?? "", StringComparison.Ordinal) && Name.Equals(other.Name) && BirthPlace.Equals(other.BirthPlace)
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
        public double DenialEndsAt;
        public int InterviewSeed;
        public bool DocumentMismatch, ReviewingPurpose;
        public FixedString512Bytes PurposeLine;
        public string LastSubmittedAnswer;
        public NetworkIdentity Identity, PrintedIdentity;
        public NetworkGovernmentDocument Government, PrintedGovernment;
        public bool HasPrintedGovernment;
        public bool HasPrintedIdentity, IsFinished, EntryApproved;
        public int QuestionIndex, Reputation;
        public int AcceptedAnswerCount;
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
            serializer.SerializeValue(ref DenialEndsAt);
            serializer.SerializeValue(ref InterviewSeed);
            serializer.SerializeValue(ref DocumentMismatch);
            serializer.SerializeValue(ref ReviewingPurpose);
            serializer.SerializeValue(ref PurposeLine);
            if (serializer.IsWriter && LastSubmittedAnswer == null) LastSubmittedAnswer = string.Empty;
            serializer.SerializeValue(ref LastSubmittedAnswer);
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
            serializer.SerializeValue(ref AcceptedAnswerCount);
            serializer.SerializeValue(ref ResultReason);
        }

        public bool Equals(CoopGameSnapshot other) => Round == other.Round && Revision == other.Revision
            && ShakeSequence == other.ShakeSequence && TravelerId == other.TravelerId && HackerId == other.HackerId
            && Phase == other.Phase && PreparationEndsAt.Equals(other.PreparationEndsAt)
            && QuestionEndsAt.Equals(other.QuestionEndsAt)
            && IntroductionEndsAt.Equals(other.IntroductionEndsAt)
            && DenialEndsAt.Equals(other.DenialEndsAt) && InterviewSeed == other.InterviewSeed
            && DocumentMismatch == other.DocumentMismatch && ReviewingPurpose == other.ReviewingPurpose && PurposeLine.Equals(other.PurposeLine)
            && string.Equals(LastSubmittedAnswer, other.LastSubmittedAnswer, StringComparison.Ordinal)
            && Identity.Equals(other.Identity) && PrintedIdentity.Equals(other.PrintedIdentity)
            && Government.Equals(other.Government) && PrintedGovernment.Equals(other.PrintedGovernment)
            && HasPrintedGovernment == other.HasPrintedGovernment
            && HasPrintedIdentity == other.HasPrintedIdentity && IsFinished == other.IsFinished
            && EntryApproved == other.EntryApproved && QuestionIndex == other.QuestionIndex
            && Reputation == other.Reputation && AcceptedAnswerCount == other.AcceptedAnswerCount && string.Equals(ResultReason, other.ResultReason, StringComparison.Ordinal);
    }
}
