using Server.Algorithm;
using Server.API;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using Server.UserRouting;
using System.Diagnostics;
using static Server.Data.AppDbContext;

namespace Server.Application.Services;

/// <summary>
/// Servicio de aplicación encargado de calcular el resultado de encuentro
/// para un usuario concreto dentro de una sesión de grupo.
///
/// Responsabilidades:
/// - Obtener las ubicaciones registradas en la sesión.
/// - Calcular el centroide como punto de encuentro.
/// - Consultar OTP para obtener la ruta individual usuario → centroide.
/// - Limitar la concurrencia contra OTP mediante SemaphoreSlim.
/// - Construir el modelo de transporte que será enviado al cliente.
/// </summary>
public sealed class MeetingRouteService : IMeetingRouteService
{
    #region Constants

    private const string LogContext = nameof(MeetingRouteService);

    #endregion

    #region Dependencies

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Semáforo compartido para limitar consultas concurrentes a OTP.
    ///
    /// OTP puede tardar bastante en local, especialmente con Docker y varios
    /// emuladores. Limitar concurrencia evita saturación y timeouts.
    /// </summary>
    private readonly SemaphoreSlim _otpSemaphore;

    #endregion

    #region Constructor

    public MeetingRouteService(HttpClient httpClient, SemaphoreSlim otpSemaphore)
    {
        _httpClient = httpClient;
        _otpSemaphore = otpSemaphore;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Calcula la ruta individual de un usuario hacia el punto de encuentro
    /// del grupo.
    ///
    /// El punto de encuentro se calcula como centroide de todas las ubicaciones
    /// recibidas en la sesión. Después se consulta OTP para obtener la ruta
    /// desde la ubicación del usuario actual hasta ese centroide.
    /// </summary>
    public async Task<MeetingResultTransportModel> CalculateForUserAsync(
        GroupSession session,
        User user)
    {
        #region Meeting point calculation

        var locations = session.GetAllLocations();

        var points = locations
            .Select(location => new GeometryUtils.GeographicLocation(
                location.Latitude,
                location.Longitude))
            .ToList();

        GeometryUtils.GeographicLocation centroid =
            GeometryUtils.CalculateCentroid(points);

        AppLogger.Info(
            LogContext,
            $"[Group:{session.GroupCode}] [User:{user.username}] Centroide: " +
            $"{centroid.Latitude:F6}, {centroid.Longitude:F6}");

        #endregion

        #region Current user location

        UserLocation? userLocation = session.GetLocation(user.id);

        if (userLocation is null)
        {
            AppLogger.Warn(
                LogContext,
                $"[Group:{session.GroupCode}] [User:{user.username}] Ubicación no encontrada.");

            return MeetingResultFactory.Error(
                "No se encontró la ubicación del usuario actual.");
        }

        #endregion

        #region OTP route calculation

        MeetingRouteResult? route;

        try
        {
            AppLogger.Info(
                LogContext,
                $"[Group:{session.GroupCode}] [User:{user.username}] Esperando turno OTP...");

            await _otpSemaphore.WaitAsync();

            try
            {
                AppLogger.Info(
                    LogContext,
                    $"[Group:{session.GroupCode}] [User:{user.username}] Turno OTP adquirido.");

                var otp = new OTP(_httpClient);

                var origin = new OTP.Coordenada(
                    userLocation.Latitude,
                    userLocation.Longitude);

                var destination = new OTP.Coordenada(
                    centroid.Latitude,
                    centroid.Longitude);

                AppLogger.Info(
                    LogContext,
                    $"[User:{user.username}] OTP: " +
                    $"{userLocation.Latitude:F6},{userLocation.Longitude:F6} → " +
                    $"{centroid.Latitude:F6},{centroid.Longitude:F6}");

                var sw = Stopwatch.StartNew();

                string jsonResponse = await otp.ConsultarAsync(origin, destination);

                sw.Stop();

                AppLogger.Info(
                    LogContext,
                    $"[User:{user.username}] OTP respondió en {sw.ElapsedMilliseconds} ms.");

                route = otp.ExtraerResultadoRuta(jsonResponse);
            }
            finally
            {
                _otpSemaphore.Release();

                AppLogger.Debug(
                    LogContext,
                    $"[Group:{session.GroupCode}] [User:{user.username}] Semáforo OTP liberado.");
            }
        }
        catch (TaskCanceledException ex)
        {
            AppLogger.Error(
                LogContext,
                $"[User:{user.username}] Timeout OTP. {ex.Message}");

            return MeetingResultFactory.Error(
                "OTP tardó demasiado. Inténtalo de nuevo.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                LogContext,
                $"[User:{user.username}] Error consultando OTP.\n{ex}");

            return MeetingResultFactory.Error(
                "Error calculando la ruta en el servidor.");
        }

        #endregion

        #region Result mapping

        if (route is null)
        {
            AppLogger.Warn(
                LogContext,
                $"[Group:{session.GroupCode}] [User:{user.username}] OTP sin itinerario disponible.");

            return MeetingResultFactory.NoRoute(
                centroid.Latitude,
                centroid.Longitude,
                userLocation);
        }

        AppLogger.Info(
            LogContext,
            $"[Group:{session.GroupCode}] [User:{user.username}] Ruta calculada: " +
            $"{route.DurationSeconds}s, {route.DistanceMeters:F0}m, " +
            $"{route.TransferCount} transbordos, {route.Legs.Count} tramos.");

        return MeetingResultFactory.Success(
            centroid.Latitude,
            centroid.Longitude,
            userLocation,
            route);

        #endregion
    }

    #endregion
}