using Server.Algorithm;
using Server.API;
using Server.Group.GroupSessions;
using Server.Infrastructure;
using Server.UserRouting;
using System.Diagnostics;
using static Server.Data.AppDbContext;

namespace Server.Application.Services;

public sealed class MeetingRouteService : IMeetingRouteService
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _otpSemaphore;

    public MeetingRouteService(HttpClient httpClient, SemaphoreSlim otpSemaphore)
    {
        _httpClient = httpClient;
        _otpSemaphore = otpSemaphore;
    }

    public async Task<MeetingResultTransportModel> CalculateForUserAsync(
        GroupSession session,
        User user)
    {
        var locations = session.GetAllLocations();

        var points = locations
            .Select(location => new GeometryUtils.GeographicLocation(
                location.Latitude,
                location.Longitude))
            .ToList();

        GeometryUtils.GeographicLocation centroid =
            GeometryUtils.CalculateCentroid(points);

        AppLogger.Info(
            "MeetingRouteService",
            $"[Group:{session.GroupCode}] [User:{user.username}] Centroide: " +
            $"{centroid.Latitude:F6}, {centroid.Longitude:F6}");

        UserLocation? userLocation = session.GetLocation(user.id);

        if (userLocation is null)
        {
            AppLogger.Warn(
                "MeetingRouteService",
                $"[Group:{session.GroupCode}] [User:{user.username}] Ubicación no encontrada.");

            return MeetingResultFactory.Error(
                "No se encontró la ubicación del usuario actual.");
        }

        MeetingRouteResult? route;

        try
        {
            AppLogger.Info(
                "MeetingRouteService",
                $"[Group:{session.GroupCode}] [User:{user.username}] Esperando turno OTP...");

            await _otpSemaphore.WaitAsync();

            try
            {
                AppLogger.Info(
                    "MeetingRouteService",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Turno OTP adquirido.");

                var otp = new OTP(_httpClient);

                var origin = new OTP.Coordenada(
                    userLocation.Latitude,
                    userLocation.Longitude);

                var destination = new OTP.Coordenada(
                    centroid.Latitude,
                    centroid.Longitude);

                AppLogger.Info(
                    "MeetingRouteService",
                    $"[User:{user.username}] OTP: " +
                    $"{userLocation.Latitude:F6},{userLocation.Longitude:F6} → " +
                    $"{centroid.Latitude:F6},{centroid.Longitude:F6}");

                var sw = Stopwatch.StartNew();

                string jsonResponse = await otp.ConsultarAsync(origin, destination);

                sw.Stop();

                AppLogger.Info(
                    "MeetingRouteService",
                    $"[User:{user.username}] OTP respondió en {sw.ElapsedMilliseconds} ms.");

                route = otp.ExtraerResultadoRuta(jsonResponse);
            }
            finally
            {
                _otpSemaphore.Release();

                AppLogger.Debug(
                    "MeetingRouteService",
                    $"[Group:{session.GroupCode}] [User:{user.username}] Semáforo OTP liberado.");
            }
        }
        catch (TaskCanceledException ex)
        {
            AppLogger.Error(
                "MeetingRouteService",
                $"[User:{user.username}] Timeout OTP. {ex.Message}");

            return MeetingResultFactory.Error(
                "OTP tardó demasiado. Inténtalo de nuevo.");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "MeetingRouteService",
                $"[User:{user.username}] Error consultando OTP.\n{ex}");

            return MeetingResultFactory.Error(
                "Error calculando la ruta en el servidor.");
        }

        if (route is null)
        {
            AppLogger.Warn(
                "MeetingRouteService",
                $"[Group:{session.GroupCode}] [User:{user.username}] OTP sin itinerario disponible.");

            return MeetingResultFactory.NoRoute(
                centroid.Latitude,
                centroid.Longitude,
                userLocation);
        }

        AppLogger.Info(
            "MeetingRouteService",
            $"[Group:{session.GroupCode}] [User:{user.username}] Ruta calculada: " +
            $"{route.DurationSeconds}s, {route.DistanceMeters:F0}m, " +
            $"{route.TransferCount} transbordos, {route.Legs.Count} tramos.");

        return MeetingResultFactory.Success(
            centroid.Latitude,
            centroid.Longitude,
            userLocation,
            route);
    }
}