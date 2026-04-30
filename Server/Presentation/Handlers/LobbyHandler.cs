using NetUtils;
using Server.API;
using Server.Application.Services;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using System.Net.Sockets;
using System.Text.Json;
using static Server.Data.AppDbContext;

namespace Server.Presentation.Handlers;

public sealed class LobbyHandler
{
    private readonly GroupSessionManager _sessionManager;
    private readonly IMeetingRouteService _meetingRouteService;
    private readonly string _connectionString;

    private enum LobbyOpt
    {
        Refresh = 1,
        Exit = 2,
        Start = 3,
        SendLocation = 4,
        PollResult = 5
    }

    public LobbyHandler(
        GroupSessionManager sessionManager,
        IMeetingRouteService meetingRouteService,
        string connectionString)
    {
        _sessionManager = sessionManager;
        _meetingRouteService = meetingRouteService;
        _connectionString = connectionString;
    }

    public async Task<string?> PrepareCreateGroupAsync(Socket socket, User user)
    {
        AppLogger.Info("LobbyHandler", $"[User:{user.username}] Preparando creación de grupo...");

        using AppDbContext context = new AppDbContext(_connectionString);
        var createService = new CreateGroupService(context);

        var result = await createService.ExecuteAsync(socket, user);

        if (!result.Success)
        {
            AppLogger.Warn("LobbyHandler", $"[User:{user.username}] No se pudo crear el grupo.");
            return null;
        }

        var session = new GroupSession(result.GroupId, result.GroupCode, user.id);
        session.AddMember(user.id, user.username);
        _sessionManager.Add(session);

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{result.GroupCode}] [User:{user.username}] Sesión creada.");

        return result.GroupCode;
    }

    public string? PrepareJoinGroup(Socket socket, User user)
    {
        string groupCode = SocketTools.receiveString(socket);

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Intentando unirse...");

        bool success = _sessionManager.TryJoinGroup(groupCode, user.id, user.username);
        SocketTools.sendBool(socket, success);

        if (!success)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] Join fallido — grupo no encontrado o iniciado.");
            return null;
        }

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Unido correctamente.");

        return groupCode;
    }

    public async Task RunLobbyAsync(Socket socket, string groupCode, User user)
    {
        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Entrando al lobby.");

        while (true)
        {
            GroupSession? session = _sessionManager.Get(groupCode);

            if (session is null)
            {
                SocketTools.sendBool(socket, false);

                AppLogger.Warn(
                    "LobbyHandler",
                    $"[Group:{groupCode}] Sesión no encontrada. Terminando lobby.");

                return;
            }

            SendLobbyHeader(socket, session);

            int option;

            try
            {
                option = SocketTools.receiveInt(socket);
            }
            catch (SocketException)
            {
                AppLogger.Warn(
                    "LobbyHandler",
                    $"[Group:{groupCode}] [User:{user.username}] Desconexión detectada.");

                return;
            }

            switch (option)
            {
                case (int)LobbyOpt.Refresh:
                    AppLogger.Debug(
                        "LobbyHandler",
                        $"[Group:{groupCode}] Refresh. Members={session.MemberCount}, Started={session.HasStarted}");
                    break;

                case (int)LobbyOpt.Exit:
                    HandleExit(groupCode, session, user);
                    return;

                case (int)LobbyOpt.Start:
                    HandleStart(socket, groupCode, session, user);
                    break;

                case (int)LobbyOpt.SendLocation:
                    bool locationDone = await HandleSendLocationAsync(socket, groupCode, session, user);

                    if (locationDone)
                        return;

                    break;

                case (int)LobbyOpt.PollResult:
                    bool pollDone = await HandlePollResultAsync(socket, groupCode, session, user);

                    if (pollDone)
                        return;

                    break;

                default:
                    AppLogger.Warn(
                        "LobbyHandler",
                        $"[Group:{groupCode}] Opción desconocida: {option}");
                    break;
            }
        }
    }

    private static void SendLobbyHeader(Socket socket, GroupSession session)
    {
        SocketTools.sendBool(socket, true);
        SocketTools.sendInt(socket, session.MemberCount);
        SocketTools.sendBool(socket, session.HasStarted);
    }

    private void HandleExit(string groupCode, GroupSession session, User user)
    {
        session.RemoveMember(user.id);

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] Salida voluntaria.");

        if (session.MemberCount == 0)
        {
            _sessionManager.Remove(groupCode);

            AppLogger.Info(
                "LobbyHandler",
                $"[Group:{groupCode}] Sesión eliminada — sin miembros.");
        }
    }

    private static void HandleStart(Socket socket, string groupCode, GroupSession session, User user)
    {
        if (user.id != session.OwnerUserId)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Start rechazado: no es owner.");

            SocketTools.sendBool(socket, false);
            return;
        }

        bool started = session.Start();
        SocketTools.sendBool(socket, started);

        AppLogger.Info(
            "LobbyHandler",
            started
                ? $"[Group:{groupCode}] Grupo iniciado por [User:{user.username}]."
                : $"[Group:{groupCode}] El grupo ya estaba iniciado.");
    }

    private async Task<bool> HandleSendLocationAsync(
        Socket socket,
        string groupCode,
        GroupSession session,
        User user)
    {
        if (!session.HasStarted)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación enviada antes de Start.");

            SendPayload(socket, MeetingResultFactory.Error("El grupo aún no se ha iniciado."));
            return false;
        }

        var locationService = new ReceiveLocationService();
        bool allReceived = locationService.Execute(socket, session, user);

        if (!allReceived)
        {
            AppLogger.Info(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] Ubicación registrada. " +
                $"Progreso: {session.GetAllLocations().Count}/{session.MemberCount}");

            SendPayload(socket, MeetingResultFactory.Pending());
            return false;
        }

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] Todas las ubicaciones listas. Calculando ruta para [User:{user.username}].");

        await SendRouteResultAsync(socket, session, user);
        return true;
    }

    private async Task<bool> HandlePollResultAsync(
        Socket socket,
        string groupCode,
        GroupSession session,
        User user)
    {
        if (!session.AreAllLocationsReceived())
        {
            AppLogger.Debug(
                "LobbyHandler",
                $"[Group:{groupCode}] [User:{user.username}] PollResult: faltan ubicaciones. " +
                $"Progreso: {session.GetAllLocations().Count}/{session.MemberCount}");

            SendPayload(socket, MeetingResultFactory.Pending());
            return false;
        }

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{groupCode}] [User:{user.username}] PollResult: todas listas. Calculando ruta.");

        await SendRouteResultAsync(socket, session, user);
        return true;
    }

    private async Task SendRouteResultAsync(Socket socket, GroupSession session, User user)
    {
        MeetingResultTransportModel result =
            await _meetingRouteService.CalculateForUserAsync(session, user);

        SendPayload(socket, result);

        AppLogger.Debug(
            "LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] Resultado enviado al cliente.");
    }

    private static void SendPayload(Socket socket, MeetingResultTransportModel payload)
    {
        string json = JsonSerializer.Serialize(payload);
        SocketTools.sendString(json, socket);
    }
}