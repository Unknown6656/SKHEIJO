using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System;

using Unknown6656.Common;

namespace SKHEIJO
{
    public enum DisconnectReaseon
    {
        ServerShutdown = 0,
        Kicked = 1,
        // TODO : ?
    }

    public abstract record CommunicationData
    {
        internal static Dictionary<string, Type> KnownDerivativeTypes { get; }

        static CommunicationData()
        {
            KnownDerivativeTypes = Assembly.GetExecutingAssembly()
                                           .GetTypes()
                                           .Where(t => t.IsAssignableTo(typeof(CommunicationData)) && t != typeof(CommunicationData))
                                           .ToDictionary(t => t.Name, LINQ.id);
        }
    }

    public abstract record CommunicationData_AdminCommand : CommunicationData;


    // server -> client
    public sealed record CommunicationData_Disconnect(DisconnectReaseon Reason) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_ServerInformation(string ServerName, Guid[] Players) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_SuccessError(bool Success, string? Message) : CommunicationData
    {
        public static CommunicationData_SuccessError OK { get; } = new(true, null);
    }

    // server -> client
    public sealed record CommunicationData_Notification(string Message) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerJoined(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerLeft(Guid UUID) : CommunicationData;

    // client -> server
    public sealed record CommunicationData_PlayerNameChangeRequest(string Name) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerInfoChanged(Guid UUID) : CommunicationData;

    // client -> server
    public sealed record CommunicationData_PlayerQueryInfo(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerInfo(bool Exists, string? Name, bool IsAdmin, bool IsInGame) : CommunicationData
    {
        public static CommunicationData_PlayerInfo NotFound { get; } = new(false, null, false, false);
    }

    // server -> client
    public sealed record CommunicationData_PlayerJoinedGame(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerLeftGame(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_GameUpdate(
        int DrawPileSize,
        int DiscardPileSize,
        Card? DiscardCard,
        GameState State,
        CommunicationData_GameUpdate.GameUpdatePlayerData[] Players,
        int CurrentPlayer,
        Card? EgoDrawnCard,
        int MaxPlayers
    ) : CommunicationData
    {
        public sealed record GameUpdatePlayerData(Guid UUID, int Columns, int Rows, Card?[] Cards, bool HasDrawn);
    }

    // client -> server
    public sealed record CommunicationData_GameJoinRequest : CommunicationData;

    // client -> server
    public sealed record CommunicationData_GameLeaveRequest : CommunicationData;

    // client -> server
    public sealed record CommunicationData_AdminGameStart : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminGameStop : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminGameReset : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminKickPlayer(Guid UUID) : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminRemovePlayerFromGame(Guid UUID) : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminMakeAdmin(Guid UUID) : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminMakeRegular(Guid UUID) : CommunicationData_AdminCommand;
}
