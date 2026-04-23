namespace Server.API;

/// <summary>
/// Representa un tramo individual del itinerario devuelto por OTP.
/// 
/// Ejemplos de tramo:
/// - WALK: caminar hasta una parada
/// - BUS: trayecto en autobús
/// - RAIL: tren
/// - SUBWAY: metro
/// 
/// Este modelo se serializa y viaja al cliente MAUI dentro del resultado final.
/// </summary>
public sealed class RouteLegDto
{
    /// <summary>
    /// Modo de transporte del tramo.
    /// Ejemplo: WALK, BUS, RAIL, SUBWAY.
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del punto/parada de origen del tramo.
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del punto/parada de destino del tramo.
    /// </summary>
    public string ToName { get; set; } = string.Empty;

    /// <summary>
    /// Duración del tramo en segundos.
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Distancia del tramo en metros.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Código público de la línea.
    /// Ejemplo: H12, L3, R2.
    /// Solo aplica a transporte público.
    /// </summary>
    public string? PublicCode { get; set; }

    /// <summary>
    /// Nombre descriptivo de la línea.
    /// </summary>
    public string? LineName { get; set; }

    /// <summary>
    /// Dirección del transporte.
    /// Ejemplo: "Direcció Zona Universitària".
    /// </summary>
    public string? Headsign { get; set; }

    /// <summary>
    /// Geometría codificada del tramo.
    /// Más adelante sirve para pintar la ruta real en el mapa.
    /// </summary>
    public string? EncodedPolyline { get; set; }
}