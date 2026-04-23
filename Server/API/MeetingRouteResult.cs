namespace Server.API;

/// <summary>
/// Resultado interno del servidor tras parsear la respuesta de OTP.
/// 
/// Este modelo todavía no es el contrato final con el cliente.
/// Primero se obtiene desde OTP y después se convierte a MeetingResultTransportModel.
/// </summary>
public sealed class MeetingRouteResult
{
    /// <summary>
    /// Duración total del itinerario en segundos.
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Distancia total del itinerario en metros.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Número de transbordos calculado a partir de los tramos de transporte público.
    /// </summary>
    public int TransferCount { get; set; }

    /// <summary>
    /// Lista de tramos del itinerario.
    /// </summary>
    public List<RouteLegDto> Legs { get; set; } = new();
}