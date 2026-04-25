using Server.API;
using Server.Group;
using Server.UserRouting;

namespace Server.Application.Services;

/// <summary>
/// Factory para construir respuestas de resultado del punto de encuentro.
///
/// Responsabilidad:
/// - Crear modelos MeetingResultTransportModel consistentes.
/// - Evitar duplicar payloads JSON dentro de LobbyHandler.
///
/// No serializa.
/// No escribe en sockets.
/// No consulta OTP.
/// No contiene lógica de protocolo TCP.
///
/// LobbyHandler decide CUÁNDO enviar una respuesta.
/// MeetingResultFactory decide CÓMO construir esa respuesta.
/// </summary>
public static class MeetingResultFactory
{
    /// <summary>
    /// Resultado pendiente.
    /// Se usa cuando todavía faltan ubicaciones de otros miembros.
    ///
    /// DurationSeconds = -1 indica al cliente que debe seguir haciendo polling.
    /// </summary>
    public static MeetingResultTransportModel Pending()
    {
        return new MeetingResultTransportModel
        {
            DurationSeconds = -1,
            HasValidRoute = false,
            MeetingPointName = "Pendiente",
            AddressText = "Esperando ubicaciones del resto del grupo",
            DistanceText = "Distancia no disponible",
            FairnessText = "Cálculo pendiente",
            Legs = new List<RouteLegDto>()
        };
    }

    /// <summary>
    /// Resultado de error técnico o de validación del flujo.
    ///
    /// statusCode recomendado:
    /// -2 = error técnico / flujo inválido
    /// </summary>
    public static MeetingResultTransportModel Error(string message, int statusCode = -2)
    {
        return new MeetingResultTransportModel
        {
            DurationSeconds = statusCode,
            HasValidRoute = false,
            MeetingPointName = "Error",
            AddressText = message,
            DistanceText = "Distancia no disponible",
            FairnessText = "No se pudo calcular el punto de encuentro",
            Legs = new List<RouteLegDto>()
        };
    }

    /// <summary>
    /// Resultado cuando el centroide se ha calculado correctamente,
    /// pero OTP no ha devuelto una ruta válida.
    ///
    /// DurationSeconds = -3 indica que existe punto de encuentro,
    /// pero no existe itinerario disponible.
    /// </summary>
    public static MeetingResultTransportModel NoRoute(
        double meetingLatitude,
        double meetingLongitude,
        UserLocation origin)
    {
        return new MeetingResultTransportModel
        {
            Latitude = meetingLatitude,
            Longitude = meetingLongitude,
            OriginLatitude = origin.Latitude,
            OriginLongitude = origin.Longitude,
            DurationSeconds = -3,
            HasValidRoute = false,
            MeetingPointName = "Punto de encuentro",
            AddressText = "No se encontró una ruta válida",
            DistanceText = "Distancia no disponible",
            FairnessText = "Centroide calculado, pero sin ruta disponible",
            Legs = new List<RouteLegDto>()
        };
    }

    /// <summary>
    /// Resultado válido con ruta calculada correctamente.
    /// Incluye punto de encuentro, origen del usuario, duración,
    /// distancia, transbordos y tramos de ruta.
    /// </summary>
    public static MeetingResultTransportModel Success(
        double meetingLatitude,
        double meetingLongitude,
        UserLocation origin,
        MeetingRouteResult route)
    {
        return new MeetingResultTransportModel
        {
            Latitude = meetingLatitude,
            Longitude = meetingLongitude,
            OriginLatitude = origin.Latitude,
            OriginLongitude = origin.Longitude,
            DurationSeconds = route.DurationSeconds,
            DistanceMeters = route.DistanceMeters,
            TransferCount = route.TransferCount,
            HasValidRoute = true,
            MeetingPointName = "Punto de encuentro",
            AddressText = "Ruta calculada correctamente",
            DistanceText = $"{route.DistanceMeters / 1000:0.0} km",
            FairnessText = route.TransferCount == 0
                ? "Ruta directa sin transbordos"
                : $"Ruta con {route.TransferCount} transbordo{(route.TransferCount == 1 ? "" : "s")}",
            Legs = route.Legs
        };
    }
}