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
    public sealed record CommunicationData_Notification(string Message, bool Success = true) : CommunicationData;

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
    public sealed record CommunicationData_PlayerInfo(bool Exists, string? Name, bool IsAdmin, bool IsServer, bool IsInGame) : CommunicationData
    {
        public static CommunicationData_PlayerInfo NotFound { get; } = new(false, null, false, false, false);
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
        GameWaitingFor WaitingFor,
        CommunicationData_GameUpdate.GameUpdatePlayerData[] Players,
        int CurrentPlayer,
        Card? EgoDrawnCard,
        int MaxPlayers,
        Guid FinalRoundInitiator
    ) : CommunicationData
    {
        public sealed record GameUpdatePlayerData(Guid UUID, int Columns, int Rows, Card?[] Cards, Card? DrawnCard, int LeaderBoardIndex, int Points);
    }

    // client -> server
    public sealed record CommunicationData_GameJoinRequest : CommunicationData;

    // client -> server
    public sealed record CommunicationData_GameLeaveRequest : CommunicationData;

    // client -> server
    public sealed record CommunicationData_AdminServerStop : CommunicationData_AdminCommand;

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

    // client -> server
    // server -> client
    public sealed record CommunicationData_AdminInitialBoardSize(int Columns, int Rows) : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_AdminRequestWinAnimation(Guid UUID) : CommunicationData_AdminCommand;

    // client -> server
    public sealed record CommunicationData_GameDraw(Pile Pile) : CommunicationData;

    // client -> server
    public sealed record CommunicationData_GameSwap(int Row, int Column) : CommunicationData;

    // client -> server
    public sealed record CommunicationData_GameDiscard : CommunicationData;

    // client -> server
    public sealed record CommunicationData_GameUncover(int Row, int Column) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerWin(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_FinalRound(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_LeaderBoard(CommunicationData_LeaderBoard.LeaderBoardEntry[] LeaderBoard)
        : CommunicationData
    {
        public sealed record LeaderBoardEntry(Guid UUID, int Points);
    }

    // server -> client
    public sealed record CommunicationData_AnimateMoveCard(Guid UUID, CardLocation From, CardLocation To, Card Card, Card? Behind) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_AnimateFlipCard(Guid UUID, int Row, int Column, Card Card) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_AnimateColumnDeletion(Guid UUID, int Column, Card[] Cards) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_ServerHighScores(ServerConfig.HighScore[] HighScores) : CommunicationData;

    // client -> server
    public sealed record CommunicationData_SendChatMessage(string Content) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_ChatMessageMention(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_ChatMessages(params CommunicationData_ChatMessages.ChatMessage[] Messages)
        : CommunicationData
    {
        public sealed record ChatMessage(Guid UUID, DateTime Time, string Content);
    }
}

