namespace Game.Multiplayer
{
    public enum ConnectionState
    {
        Disconnected,
        Initializing,
        CreatingRoom,
        JoiningRoom,
        Connecting,
        Connected,
        Disconnecting,
        Error
    }
}
