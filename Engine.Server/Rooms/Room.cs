using System;
using System.Collections.Generic;
using Engine.Shared.Networking;
using Engine.Shared.Rooms;

namespace Engine.Server.Rooms;

/// <summary>
/// A room instance that can organize sessions. Inherit <see cref="Room{TOptions}"/> to receive typed options.
/// </summary>
public abstract class Room : IDisposable
{
    [Dependency] protected readonly IRoomManager RoomManager = default!;

    public string RoomId { get; init; }

    private List<INetSession> _sessions = new();
    public IReadOnlyList<INetSession> Sessions => _sessions;

    /// <summary>
    /// The <see cref="RoomOptions"/> type clients must send to join this room.
    /// </summary>
    public virtual Type OptionsType => typeof(EmptyRoomOptions);

    /// <summary>
    /// Fired when a session joins the room.
    /// </summary>
    public event Action<INetSession, RoomOptions>? OnSessionJoin;

    /// <summary>
    /// Fired when a sessions leaves the room.
    /// </summary>
    public event Action<INetSession, string>? OnSessionLeave;

    private bool _isDisposing = false;

    public virtual void Init()
    {
    }

    public virtual void Update(float dt)
    {
        
    }

    public void Dispose()
    {
        if (_isDisposing)
            throw new Exception("The room is already disposing!");

        _isDisposing = true;
        OnDispose();
    }

    protected virtual void OnDispose()
    {
        foreach(var session in _sessions.ToArray()) // LeaveSession removes from the list
            RoomManager.LeaveSession(session, Loc.GetString("internal-room-is-disposing"));

        RoomManager.RoomDisposed(this);
    }

    public void Broadcast(NetMessage message)
    {
        for (int i = 0; i < _sessions.Count; i++)
            _sessions[i].SendMessage(message);
    }

    /// <summary>
    /// Called after a session joined. <paramref name="options"/> is already checked to be an <see cref="OptionsType"/>.
    /// </summary>
    protected virtual void OnJoinRaw(INetSession session, RoomOptions options)
    {

    }

    /// <summary>
    /// Called after a session left.
    /// </summary>
    protected virtual void OnLeave(INetSession session, string reason)
    {

    }

    internal void JoinRoom(INetSession session, RoomOptions options)
    {
        _sessions.Add(session);
        OnJoinRaw(session, options);
        OnSessionJoin?.Invoke(session, options);
    }

    internal void LeaveRoom(INetSession session, string reason)
    {
        _sessions.Remove(session);
        OnLeave(session, reason);
        OnSessionLeave?.Invoke(session, reason);
    }
}

/// <inheritdoc cref="Room"/>
public abstract class Room<TOptions> : Room where TOptions : RoomOptions
{
    public sealed override Type OptionsType => typeof(TOptions);

    protected sealed override void OnJoinRaw(INetSession session, RoomOptions options)
        => OnJoin(session, (TOptions)options);

    /// <summary>
    /// Called after a session joined. <paramref name="options"/> is already checked to be an <see cref="OptionsType"/>.
    /// </summary>
    protected virtual void OnJoin(INetSession session, TOptions options)
    {

    }
}
