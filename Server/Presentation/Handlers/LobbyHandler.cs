using NetUtils;
using Server.Algorithm;
using Server.API;
using Server.Application.Services;
using Server.Data;
using Server.Group;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using Server.UserRouting;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using static Server.Data.AppDbContext;

namespace Server.Presentation.Handlers;

public sealed class LobbyHandler
{
    private readonly GroupSessionManager _sessionManager;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _otpSemaphore;
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
        HttpClient httpClient,
        SemaphoreSlim otpSemaphore,
        string connectionString)
    {
        _sessionManager = sessionManager;
        _httpClient = httpClient;
        _otpSemaphore = otpSemaphore;
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
        var locations = session.GetAllLocations();

        var points = locations
            .Select(location => new GeometryUtils.GeographicLocation(
                location.Latitude,
                location.Longitude))
            .ToList();

        GeometryUtils.GeographicLocation centroid = GeometryUtils.CalculateCentroid(points);

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] Centroide: " +
            $"{centroid.Latitude:F6}, {centroid.Longitude:F6}");

        UserLocation? userLocation = session.GetLocation(user.id);

        if (userLocation is null)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{session.GroupCode}] [User:{user.username}] Ubicación no encontrada.");

            SendPayload(socket, MeetingResultFactory.Error("No se encontró la ubicación del usuario actual."));
            return;
        }

        MeetingRouteResult? route;

        try
        {
            AppLogger.Info(
                "LobbyHandler",
                $"[Group:{session.GroupCode}] [User:{user.username}] Esperando turno OTP...");

            await _otpSemaphore.WaitAsync();

            try
            {
                AppLogger.Info(
                    "LobbyHandler",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Turno OTP adquirido.");

                var otp = new OTP(_httpClient);

                var origin = new OTP.Coordenada(
                    userLocation.Latitude,
                    userLocation.Longitude);

                var destination = new OTP.Coordenada(
                    centroid.Latitude,
                    centroid.Longitude);

                AppLogger.Info(
                    "LobbyHandler",
                    $"[User:{user.username}] OTP: " +
                    $"{userLocation.Latitude:F6},{userLocation.Longitude:F6} → " +
                    $"{centroid.Latitude:F6},{centroid.Longitude:F6}");

                var sw = Stopwatch.StartNew();
                string jsonResponse = await otp.ConsultarAsync(origin, destination);
                sw.Stop();

                AppLogger.Info(
                    "LobbyHandler",
                    $"[User:{user.username}] OTP respondió en {sw.ElapsedMilliseconds} ms.");

                route = otp.ExtraerResultadoRuta(jsonResponse);
            }
            finally
            {
                _otpSemaphore.Release();

                AppLogger.Debug(
                    "LobbyHandler",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Semáforo OTP liberado.");
            }
        }
        catch (TaskCanceledException ex)
        {
            AppLogger.Error(
                "LobbyHandler",
                $"[User:{user.username}] Timeout OTP. {ex.Message}");

            SendPayload(socket, MeetingResultFactory.Error("OTP tardó demasiado. Inténtalo de nuevo."));
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "LobbyHandler",
                $"[User:{user.username}] Error consultando OTP.\n{ex}");

            SendPayload(socket, MeetingResultFactory.Error("Error calculando la ruta en el servidor."));
            return;
        }

        if (route is null)
        {
            AppLogger.Warn(
                "LobbyHandler",
                $"[Group:{session.GroupCode}] [User:{user.username}] OTP sin itinerario disponible.");

            SendPayload(socket, MeetingResultFactory.NoRoute(
                centroid.Latitude,
                centroid.Longitude,
                userLocation));

            return;
        }

        AppLogger.Info(
            "LobbyHandler",
            $"[Group:{session.GroupCode}] [User:{user.username}] Ruta calculada: " +
            $"{route.DurationSeconds}s, {route.DistanceMeters:F0}m, " +
            $"{route.TransferCount} transbordos, {route.Legs.Count} tramos.");

        SendPayload(socket, MeetingResultFactory.Success(
            centroid.Latitude,
            centroid.Longitude,
            userLocation,
            route));

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