using System;
using CarryOn.Taylor;
using Unity.Netcode;

namespace Game.Multiplayer
{
    public enum LobbyRole : byte { Unassigned, Traveler, Hacker }

    public struct LobbyPlayerSelection : INetworkSerializable, IEquatable<LobbyPlayerSelection>
    {
        public ulong ClientId;
        public uint Version;
        public bool Connected, Ready;
        public LobbyRole Role;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref Connected);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Role);
        }

        public bool Equals(LobbyPlayerSelection other) => ClientId == other.ClientId && Version == other.Version &&
            Connected == other.Connected && Ready == other.Ready && Role == other.Role;
    }

    // A single server-written value keeps role occupancy, readiness and the start lock atomic.
    public struct LobbyRoleState : INetworkSerializable, IEquatable<LobbyRoleState>
    {
        public LobbyPlayerSelection Host, Client;
        public bool GameStarted;

        public bool BothReady => Host.Connected && Client.Connected && Host.Ready && Client.Ready &&
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
        }

        public bool Equals(LobbyRoleState other) => Host.Equals(other.Host) && Client.Equals(other.Client) &&
            GameStarted == other.GameStarted;
    }
}
