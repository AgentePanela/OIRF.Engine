using System;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Rooms;

namespace Engine.Client.Rooms;

/// <summary>
/// Manages the client-side multiplayer room managing.
/// </summary>
public interface IRoomManager
{
    /// <summary>
    /// The id of the room this client is currently in, or null if it is in none.
    /// </summary>
    public string? CurrentRoomId { get; }

    /// <summary>
    /// True if a join request was sent and the server has not answered yet.
    /// </summary>
    public bool IsJoining { get; }

    /// <summary>
    /// True if the client is currently inside a room.
    /// </summary>
    [MemberNotNullWhen(true, nameof(CurrentRoomId))]
    public bool IsInRoom { get; }

    /// <summary>
    /// Fired when the server confirms this client joined a room. (room id).
    /// </summary>
    public event Action<string>? OnJoined;

    /// <summary>
    /// Fired when this client leaves a room (requested, kicked, etc...) |
    /// (room id, reason)
    /// </summary>
    public event Action<string, string>? OnLeft;

    /// <summary>
    /// Fired when the server return a error message.
    /// </summary>
    public event Action<string>? OnError;

    internal void Init();

    /// <summary>
    /// Asks the server to join a room. The result arrives through <see cref="OnJoined"/> or <see cref="OnError"/>.
    /// </summary>
    /// <returns>False if the request was not sent (not connected, already in a room or already joining).</returns>
    /// <param name="options">The options type the server room expects (see <c>Room&lt;TOptions&gt;</c>), null for rooms without options.</param>
    public bool JoinRoom(string roomId, RoomOptions? options = null);

    /// <summary>
    /// Asks the server to leave the current room. The result arrives through <see cref="OnLeft"/> or <see cref="OnError"/>.
    /// </summary>
    /// <returns>False if the request was not sent (not connected or not in a room).</returns>
    public bool LeaveRoom(string reason = "");
}

internal sealed class RoomManager : IRoomManager
{
    [Dependency] private readonly INetManager _netMan = default!;

    public string? CurrentRoomId { get; private set; }
    public bool IsJoining { get; private set; }

    [MemberNotNullWhen(true, nameof(CurrentRoomId))]
    public bool IsInRoom => CurrentRoomId is not null;

    public event Action<string>? OnJoined;
    public event Action<string, string>? OnLeft;
    public event Action<string>? OnError;

    void IRoomManager.Init()
    {
        IoCManager.ResolveDependencies(this);
        _netMan.RegisterNetMessage<RoomResonseMessage>(OnRoomResponse);
        _netMan.OnDisconnected += (_, _) => Reset();
    }

    public bool JoinRoom(string roomId, RoomOptions? options = null)
    {
        if (!_netMan.IsClient || _netMan.MySession is not { } session || IsInRoom || IsJoining)
            return false;

        IsJoining = true;
        session.SendMessage(new JoinRoomMessage
        {
            RoomId = roomId,
            Options = options
        });
        return true;
    }

    public bool LeaveRoom(string reason = "")
    {
        if (!_netMan.IsClient || _netMan.MySession is not { } session || !IsInRoom)
            return false;

        session.SendMessage(new LeaveRoomMessage { Reason = reason });
        return true;
    }

    private void OnRoomResponse(RoomResonseMessage message, INetSession? session)
    {
        switch (message.responseType)
        {
            case RoomResonseMessage.ResponseType.Joined:
                IsJoining = false;
                CurrentRoomId = message.RoomId;
                Log.Debug($"Joined room {message.RoomId}");
                OnJoined?.Invoke(message.RoomId);
                break;

            case RoomResonseMessage.ResponseType.Leaved:
                Reset();
                Log.Debug($"Left room {message.RoomId}: {message.Reason}");
                OnLeft?.Invoke(message.RoomId, message.Reason);
                break;

            case RoomResonseMessage.ResponseType.Error:
                IsJoining = false;
                Log.Warn($"Room request refused: {message.Reason}");
                OnError?.Invoke(message.Reason);
                break;
        }
    }

    private void Reset()
    {
        IsJoining = false;
        CurrentRoomId = null;
    }
}
