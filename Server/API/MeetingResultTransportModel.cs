namespace Server.API;

/// <summary>
/// Contrato JSON que el servidor envía al cliente MAUI.
/// 
/// Sustituye al contrato antiguo:
///     double latitude
///     double longitude
///     int duration
/// 
/// Ahora se manda todo como un único string JSON:
///     SocketTools.sendString(json, socket)
/// </summary>
public sealed class MeetingResultTransportModel
{
    /// <summary>
    /// Latitud del punto de encuentro.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitud del punto de encuentro.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Latitud del usuario actual.
    /// Sirve para pintar origen -> destino en el cliente.
    /// </summary>
    public double OriginLatitude { get; set; }

    /// <summary>
    /// Longitud del usuario actual.
    /// </summary>
    public double OriginLongitude { get; set; }

    /// <summary>
    /// Duración total en segundos.
    /// Valores especiales:
    /// -2 = error técnico
    /// -3 = OTP no encontró ruta
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Distancia total en metros.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Número de transbordos.
    /// </summary>
    public int TransferCount { get; set; }

    /// <summary>
    /// Indica si OTP encontró una ruta válida.
    /// </summary>
    public bool HasValidRoute { get; set; }

    /// <summary>
    /// Nombre que mostrará el mapa.
    /// </summary>
    public string MeetingPointName { get; set; } = "Punto de encuentro";

    /// <summary>
    /// Texto secundario para el bottom sheet.
    /// </summary>
    public string AddressText { get; set; } = "Dirección no disponible";

    /// <summary>
    /// Distancia formateada para UI.
    /// </summary>
    public string DistanceText { get; set; } = "Distancia no disponible";

    /// <summary>
    /// Texto de equilibrio/fairness mostrado al usuario.
    /// </summary>
    public string FairnessText { get; set; } = "Equilibrio no disponible";

    /// <summary>
    /// Tramos detallados del itinerario.
    /// Aquí es donde viajan los transbordos.
    /// </summary>
    public List<RouteLegDto> Legs { get; set; } = new();
}