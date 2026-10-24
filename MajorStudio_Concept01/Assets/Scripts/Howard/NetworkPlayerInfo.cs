namespace Game.Multiplayer
{
    public readonly struct NetworkPlayerInfo
    {
        public NetworkPlayerInfo(ulong clientId, bool isHost, bool isConnected)
        {
            ClientId = clientId;
            IsHost = isHost;
            IsConnected = isConnected;
        }

        public ulong ClientId { get; }
        public bool IsHost { get; }
        public bool IsConnected { get; }
    }
}
