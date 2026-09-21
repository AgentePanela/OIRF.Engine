using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Rooms;

namespace Engine.Server.Rooms;

public interface IRoomManager
{
    /// <summary>
    /// All current available rooms.
    /// </summary>
    public IReadOnlyDictionary<string, Room> AvailableRooms { get; }

    /// <summary>
    /// All sessions that are in a room.
    /// </summary>
    public IReadOnlyDictionary<INetSession, Room> InRoomSessions { get; }

    internal void Init();

    internal void Update(float dt);

    internal void RoomDisposed(Room room);

    /// <summary>
    /// Creates and makes a room public.
    /// </summary>
    /// <param name="id">The identifier this room will use to sessions join it.</param>
    public T CreateRoom<T>(string? id = null) where T : Room, new();

    /// <summary>
    /// Make a client leave a room.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="reason"></param>
    public void LeaveSession(INetSession session, string reason = "");

    /// <summary>
    /// Make a session join a room, if its not already in one.
    /// </summary>
    public void JoinSession(INetSession session, Room room, RoomOptions options);

    /// <summary>
    /// Verify if a session is already in a room and returns the room instance.
    /// </summary>
    public bool IsInRoom(INetSession session, [NotNullWhen(true)] out Room? room);
}

internal sealed class RoomManager : IRoomManager
{
    [Dependency] private readonly INetManager _netMan = default!;

    private readonly Dictionary<string, Room> _rooms = new();
    private readonly Dictionary<INetSession, Room> _sessions = new();

    public IReadOnlyDictionary<string, Room> AvailableRooms => _rooms;
    public IReadOnlyDictionary<INetSession, Room> InRoomSessions => _sessions;
    

    public T CreateRoom<T>(string? id = null) where T : Room, new()
    {
        var room = new T()
        {
            RoomId = id ?? Guid.NewGuid().ToString()[..8],
        };

        _rooms.Add(room.RoomId, room);
        room.Init();
        Log.Debug($"New room! {room.RoomId} ({typeof(T).Name})");
        return room;
    }

    public void LeaveSession(INetSession session, string reason = "")
    {
        if (!IsInRoom(session, out var room))
            return;

        _sessions.Remove(session);
        room.LeaveRoom(session, reason);
        session.SendMessage(new RoomResonseMessage 
        { 
            responseType = RoomResonseMessage.ResponseType.Leaved,
            Reason = reason,
            RoomId = room.RoomId
        });
    }

    public void JoinSession(INetSession session, Room room, RoomOptions options)
    {
        if (IsInRoom(session, out _))
            return;

        if (!room.OptionsType.IsInstanceOfType(options))
            throw new ArgumentException($"Room {room.RoomId} expects {room.OptionsType.Name}, got {options.GetType().Name}.", nameof(options));

        if (room.Locked)
            return;

        if (room.MaxSessions is not null && room.Sessions.Count >= room.MaxSessions)
            return;

        _sessions.Add(session, room);
        room.JoinRoom(session, options);
        session.SendMessage(new RoomResonseMessage 
        { 
            responseType = RoomResonseMessage.ResponseType.Joined,
            RoomId = room.RoomId
        });
    }

    public bool IsInRoom(INetSession session, [NotNullWhen(true)] out Room? room)
    {
        if (!_sessions.TryGetValue(session, out room))
            return false;

        return true;
    }

    void IRoomManager.Update(float dt)
    {
        foreach((_, var room) in _rooms)
        {
            room.Update(dt);
        }
    }

    void IRoomManager.RoomDisposed(Room room)
    {
        _rooms.Remove(room.RoomId);
    }

    void IRoomManager.Init()
    {
        IoCManager.ResolveDependencies(this);
        _netMan.RegisterNetMessage<JoinRoomMessage>(OnRoomJoinMessage);
        _netMan.RegisterNetMessage<LeaveRoomMessage>(OnRoomLeaveMessage);
        _netMan.RegisterNetMessage<RoomListRequestMessage>(OnRoomListRequest);
    }

    private void OnRoomListRequest(RoomListRequestMessage message, INetSession? session)
    {
        if (session is null)
            return;

        var response = new RoomListResponseMessage();
        foreach (var room in _rooms.Values)
        {
            if (room.Hiddden)
                continue;

            response.Rooms.Add(new RoomInfo
            {
                RoomId = room.RoomId,
                RoomType = room.GetType().Name,
                Sessions = room.Sessions.Count,
                MaxSessions = room.MaxSessions,
                Locked = room.Locked
            });
        }

        session.SendMessage(response);
    }

    private void OnRoomJoinMessage(JoinRoomMessage message, INetSession? session)
    {
        if (session is null || IsInRoom(session, out _))
        {
            session?.SendMessage(new RoomResonseMessage
            {
                responseType = RoomResonseMessage.ResponseType.Error,
                Reason = Loc.GetString("internal-room-man-response-already-in-room")
            });
            return;
        }

        if (!_rooms.TryGetValue(message.RoomId, out var room))
        {
            session.SendMessage(new RoomResonseMessage
            {
                responseType = RoomResonseMessage.ResponseType.Error,
                Reason = Loc.GetString("internal-room-man-response-unknown-room-id")
            });
            return;
        }

        if (room.Locked)
        {
            session.SendMessage(new RoomResonseMessage
            {
                responseType = RoomResonseMessage.ResponseType.Error,
                Reason = Loc.GetString("internal-room-man-response-join-room-locked")
            });
            return;
        }

        if (room.MaxSessions is not null && room.Sessions.Count >= room.MaxSessions)
        {
            session.SendMessage(new RoomResonseMessage
            {
                responseType = RoomResonseMessage.ResponseType.Error,
                Reason = Loc.GetString("internal-room-man-response-join-room-full")
            });
            return;
        }

        var options = message.Options;
        if (options is null && room.OptionsType == typeof(EmptyRoomOptions))
            options = new EmptyRoomOptions();

        if (options is null || !room.OptionsType.IsInstanceOfType(options))
        {
            session.SendMessage(new RoomResonseMessage
            {
                responseType = RoomResonseMessage.ResponseType.Error,
                RoomId = room.RoomId,
                Reason = Loc.GetString("internal-room-man-response-invalid-options")
            });
            return;
        }

        JoinSession(session, room, options);
    }

    private void OnRoomLeaveMessage(LeaveRoomMessage message, INetSession? session)
    {
        if (session is null || !IsInRoom(session, out var room))
        {
            session?.SendMessage(new RoomResonseMessage
            {
                responseType = RoomResonseMessage.ResponseType.Error,
                Reason = Loc.GetString("internal-room-man-response-not-in-room")
            });
            return;
        }

        LeaveSession(session, message.Reason);
    }
}