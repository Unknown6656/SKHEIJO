using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Unknown6656.Common;

namespace SKHEIJO
{
    public enum DisconnectReaseon
    {
        ServerShutdown,
        Kicked,
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

    // server -> client
    public sealed record CommunicationData_Disconnect(DisconnectReaseon Reason) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_ServerInformation(string ServerName, Guid[] Players) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_SucessError(bool Success, string? Message) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerJoined(Guid UUID) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerLeft(Guid UUID) : CommunicationData;

    // client -> server
    public sealed record CommunicationData_PlayerNameChangeRequest(string Name) : CommunicationData;

    // server -> client
    public sealed record CommunicationData_PlayerNameUpdate(Guid UUID, string Name) : CommunicationData;


    // TODO
    // public sealed record CommunicationData_
}
