using Server.API;
using Server.Group;
using Server.UserRouting;

namespace Server.Application.Services;

/// <summary>
/// Factory para construir respuestas de resultado del punto de encuentro.
///
/// Responsabilidades:
/// - Crear modelos <see cref="MeetingResultTransportModel"/> consistentes.
/// - Centralizar los estados especiales del resultado.
/// - Evitar duplicar payloads JSON dentro de <c>LobbyHandler</c>.
///
/// Esta clase no serializa.
/// Esta clase no escribe en sockets.
/// Esta clase no consulta OTP.
/// Esta clase no contiene lógica de protocolo TCP.
///
/// <c>LobbyHandler</c> decide cuándo enviar una respuesta.
/// <c>MeetingResultFactory</c> decide cómo construir esa respuesta.
/// </summary>
public static class MeetingResultFactory
{
    #region Status codes

    /// <summary>
    /// Indica que el cálculo todavía está pendiente.
    /// El cliente debe seguir haciendo polling.
    /// </summary>
    private const int PendingStatusCode = -1;

    /// <summary>
    /// Indica error técnico, error de validación o flujo inválido.
    /// </summary>
    private const int ErrorStatusCode = -2;

    /// <summary>
    /// Indica que el punto de encuentro existe, pero OTP no encontró ruta válida.
    /// </summary>
    private const int NoRouteStatusCode = -3;

    #endregion

    #region Public factory methods

    /// <summary>
    /// Construye un resultado pendiente.
    ///
    /// Se usa cuando el usuario ya ha enviado su ubicación, pero todavía faltan
    /// ubicaciones de otros miembros del grupo.
    ///
    /// <c>DurationSeconds = -1</c> indica al cliente que debe seguir haciendo polling.
    /// </summary>
    public static MeetingResultTransportModel Pending()
    {
        return new MeetingResultTransportModel
        {
            DurationSeconds = PendingStatusCode,
            HasValidRoute = false,
            MeetingPointName = "Pendiente",
            AddressText = "Esperando ubicaciones del resto del grupo",
            DistanceText = "Distancia no disponible",
            FairnessText = "Cálculo pendiente",
            Legs = new List<RouteLegDto>()
        };
    }

    /// <summary>
    /// Construye un resultado de error técnico o de validación del flujo.
    ///
    /// Se usa, por ejemplo, cuando:
    /// - El grupo todavía no se ha iniciado.
    /// - La ubicación recibida no es válida.
    /// - OTP falla o tarda demasiado.
    /// - Se produce un error inesperado en el servidor.
    ///
    /// Por defecto usa <c>DurationSeconds = -2</c>.
    /// </summary>
    public static MeetingResultTransportModel Error(
        string message,
        int statusCode = ErrorStatusCode)
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
    /// Construye un resultado sin ruta válida.
    ///
    /// Se usa cuando el centroide se ha calculado correctamente, pero OTP no ha
    /// devuelto ningún itinerario disponible entre el origen del usuario y el
    /// punto de encuentro.
    ///
    /// <c>DurationSeconds = -3</c> indica al cliente que existe punto de encuentro,
    /// pero no existe ruta disponible.
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
            DurationSeconds = NoRouteStatusCode,
            HasValidRoute = false,
            MeetingPointName = "Punto de encuentro",
            AddressText = "No se encontró una ruta válida",
            DistanceText = "Distancia no disponible",
            FairnessText = "Centroide calculado, pero sin ruta disponible",
            Legs = new List<RouteLegDto>()
        };
    }

    /// <summary>
    /// Construye un resultado válido con ruta calculada correctamente.
    ///
    /// Incluye:
    /// - Coordenadas del punto de encuentro.
    /// - Coordenadas de origen del usuario.
    /// - Duración total.
    /// - Distancia total.
    /// - Número de transbordos.
    /// - Tramos individuales de ruta.
    ///
    /// Este es el payload final que el cliente MAUI puede representar en el mapa.
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
            FairnessText = BuildFairnessText(route.TransferCount),
            Legs = route.Legs
        };
    }

    #endregion

    #region Text builders

    /// <summary>
    /// Construye el texto descriptivo asociado al número de transbordos.
    /// </summary>
    private static string BuildFairnessText(int transferCount)
    {
        return transferCount == 0
            ? "Ruta directa sin transbordos"
            : $"Ruta con {transferCount} transbordo{(transferCount == 1 ? "" : "s")}";
    }

    #endregion
}