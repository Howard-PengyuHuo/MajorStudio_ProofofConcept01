using System;
using CarryOn.Taylor;
using Unity.Netcode;

namespace Game.Multiplayer
{
    public enum LobbyRole : byte { Unassigned, Traveler, Hacker }
    public enum SessionStage : byte { Lobby, ComicEntering, ComicNarration, ComicHold, CharacterSelection, Gameplay, SelectionEntering }

    public struct LobbyPlayerSelection : INetworkSerializable, IEquatable<LobbyPlayerSelection>
    {
        public ulong ClientId;
        public uint Version;
        public bool Connected;
        public LobbyRole Role;
        public int CutscenePage;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref Connected);
            serializer.SerializeValue(ref Role);
            serializer.SerializeValue(ref CutscenePage);
        }

        public bool Equals(LobbyPlayerSelection other) => ClientId == other.ClientId && Version == other.Version &&
            Connected == other.Connected && Role == other.Role && CutscenePage == other.CutscenePage;
    }

    // One server-written value keeps assignments, comic phase and countdown generation atomic.
    public struct LobbyRoleState : INetworkSerializable, IEquatable<LobbyRoleState>
    {
        public LobbyPlayerSelection Host, Client;
        public bool GameStarted;
        public SessionStage Stage;
        public int ComicIndex;
        public int NarrationSegmentIndex;
        public uint ComicSequence, CountdownGeneration;
        public double StageStartedAt, StageEndsAt, CountdownEndsAt;
        public bool CountdownActive;
        public bool MusicPlaying;
        public double MusicStartedAt;

        public bool HasDistinctRoles => Host.Connected && Client.Connected &&
            ((Host.Role == LobbyRole.Traveler && Client.Role == LobbyRole.Hacker) ||
             (Host.Role == LobbyRole.Hacker && Client.Role == LobbyRole.Traveler));

        public bool TryGetPlayer(ulong clientId, out LobbyPlayerSelection player)
        {
            if (Host.Connected && Host.ClientId == clientId) { player = Host; return true; }
            if (Client.Connected && Client.ClientId == clientId) { player = Client; return true; }
            player = default;
            return false;
        }

        public bool IsOccupied(LobbyRole role, ulong exceptClientId) => role != LobbyRole.Unassigned &&
            ((Host.Connected && Host.ClientId != exceptClientId && Host.Role == role) ||
             (Client.Connected && Client.ClientId != exceptClientId && Client.Role == role));

        public bool TryGetRole(ulong clientId, out PlayerRole role)
        {
            if (TryGetPlayer(clientId, out var player) && player.Role != LobbyRole.Unassigned)
            {
                role = player.Role == LobbyRole.Traveler ? PlayerRole.Traveler : PlayerRole.Hacker;
                return true;
            }
            role = default;
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Host);
            serializer.SerializeValue(ref Client);
            serializer.SerializeValue(ref GameStarted);
            serializer.SerializeValue(ref Stage);
            serializer.SerializeValue(ref ComicIndex);
            serializer.SerializeValue(ref NarrationSegmentIndex);
            serializer.SerializeValue(ref ComicSequence);
            serializer.SerializeValue(ref CountdownGeneration);
            serializer.SerializeValue(ref StageStartedAt);
            serializer.SerializeValue(ref StageEndsAt);
            serializer.SerializeValue(ref CountdownEndsAt);
            serializer.SerializeValue(ref CountdownActive);
            serializer.SerializeValue(ref MusicPlaying);
            serializer.SerializeValue(ref MusicStartedAt);
        }

        public bool Equals(LobbyRoleState other) => Host.Equals(other.Host) && Client.Equals(other.Client) &&
            GameStarted == other.GameStarted && Stage == other.Stage && ComicIndex == other.ComicIndex &&
            NarrationSegmentIndex == other.NarrationSegmentIndex &&
            ComicSequence == other.ComicSequence && CountdownGeneration == other.CountdownGeneration &&
            StageStartedAt.Equals(other.StageStartedAt) && StageEndsAt.Equals(other.StageEndsAt) &&
            CountdownEndsAt.Equals(other.CountdownEndsAt) && CountdownActive == other.CountdownActive &&
            MusicPlaying == other.MusicPlaying && MusicStartedAt.Equals(other.MusicStartedAt);
    }
}
