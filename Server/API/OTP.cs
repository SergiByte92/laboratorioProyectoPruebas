using Server.Infrastructure;
using System.Text;
using System.Text.Json;

namespace Server.API;

/// <summary>
/// Cliente de integración con OpenTripPlanner (OTP).
/// 
/// Responsabilidades:
/// - Construir y enviar la consulta GraphQL.
/// - Validar errores HTTP y GraphQL.
/// - Parsear la respuesta de OTP a modelos internos de ruta.
/// </summary>
public sealed class OTP
{
    #region Constants

    private const string LogContext = nameof(OTP);

    private const string OtpGraphQlUrl = "http://localhost:8080/otp/transmodel/v3";

    private const string TripQuery = @"
query trip($dateTime: DateTime, $from: Location!, $to: Location!) {
  trip(dateTime: $dateTime, from: $from, to: $to) {
    tripPatterns {
      aimedStartTime
      aimedEndTime
      expectedEndTime
      expectedStartTime
      duration
      distance
      generalizedCost
      legs {
        id
        mode
        aimedStartTime
        aimedEndTime
        expectedEndTime
        expectedStartTime
        realtime
        distance
        duration
        generalizedCost
        fromPlace {
          name
          quay {
            id
          }
        }
        toPlace {
          name
          quay {
            id
          }
        }
        fromEstimatedCall {
          destinationDisplay {
            frontText
          }
        }
        line {
          publicCode
          name
          id
          presentation {
            colour
          }
        }
        authority {
          name
          id
        }
        pointsOnLink {
          points
        }
        interchangeTo {
          staySeated
        }
        interchangeFrom {
          staySeated
        }
      }
      systemNotices {
        tag
      }
    }
  }
}";

    #endregion

    #region Dependencies

    private readonly HttpClient _httpClient;

    #endregion

    #region Constructor

    public OTP(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    #endregion

    #region Models

    /// <summary>
    /// Coordenada geográfica simple usada como origen o destino de una ruta.
    /// </summary>
    public sealed class Coordenada
    {
        public double Latitud { get; set; }
        public double Longitud { get; set; }

        public Coordenada(double latitud, double longitud)
        {
            Latitud = latitud;
            Longitud = longitud;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Consulta OTP y devuelve el JSON crudo.
    /// 
    /// Usa una fecha de consulta controlada para evitar respuestas vacías
    /// durante franjas sin servicio de transporte.
    /// </summary>
    public async Task<string> ConsultarAsync(Coordenada origen, Coordenada destino)
    {
        string queryDateTime = GetQueryDateTime();

        AppLogger.Info(
            LogContext,
            $"Consultando ruta origen={origen.Latitud:F6},{origen.Longitud:F6} " +
            $"destino={destino.Latitud:F6},{destino.Longitud:F6} " +
            $"dateTime={queryDateTime}");

        var body = new
        {
            query = TripQuery,
            variables = new
            {
                dateTime = queryDateTime,
                from = CreateLocation(origen),
                to = CreateLocation(destino)
            }
        };

        string jsonBody = JsonSerializer.Serialize(body);

        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(OtpGraphQlUrl, content);

        string jsonResponse = await response.Content.ReadAsStringAsync();

        AppLogger.Info(LogContext, $"HTTP Status={(int)response.StatusCode} {response.StatusCode}");

        ValidateHttpResponse(response, jsonResponse);
        ValidateGraphQlResponse(jsonResponse);

        AppLogger.Debug(LogContext, "Consulta completada correctamente.");

        return jsonResponse;
    }

    /// <summary>
    /// Método de compatibilidad para obtener únicamente la duración de la ruta.
    /// </summary>
    public int? ExtraerDuracion(string jsonResponse)
    {
        MeetingRouteResult? result = ExtraerResultadoRuta(jsonResponse);
        return result?.DurationSeconds;
    }

    /// <summary>
    /// Extrae el primer itinerario válido de OTP y lo convierte a MeetingRouteResult.
    /// 
    /// Devuelve null cuando OTP responde correctamente pero no encuentra ruta.
    /// </summary>
    public MeetingRouteResult? ExtraerResultadoRuta(string jsonResponse)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonResponse);

        ValidateGraphQlDocument(doc);

        JsonElement trip = GetTripElement(doc);

        if (trip.ValueKind == JsonValueKind.Null)
        {
            AppLogger.Warn(LogContext, "'trip' es null. Sin ruta disponible.");
            return null;
        }

        JsonElement patterns = GetTripPatterns(trip);

        int patternCount = patterns.GetArrayLength();
        AppLogger.Info(LogContext, $"Itinerarios encontrados: {patternCount}");

        if (patternCount == 0)
        {
            AppLogger.Warn(LogContext, "tripPatterns vacío. Sin ruta disponible.");
            return null;
        }

        JsonElement firstPattern = patterns[0];

        int duration = ReadInt(firstPattern, "duration");
        double distance = ReadDouble(firstPattern, "distance");
        List<RouteLegDto> legs = ParseLegs(firstPattern);

        int transferCount = CalculateTransferCount(legs);

        AppLogger.Info(
            LogContext,
            $"Primer itinerario: duration={duration}s ({duration / 60} min), " +
            $"distance={distance:F2}m, legs={legs.Count}, transfers={transferCount}");

        return new MeetingRouteResult
        {
            DurationSeconds = duration,
            DistanceMeters = distance,
            TransferCount = transferCount,
            Legs = legs
        };
    }

    #endregion

    #region GraphQL Request Builders

    private static object CreateLocation(Coordenada coordenada)
    {
        return new
        {
            coordinates = new
            {
                latitude = coordenada.Latitud,
                longitude = coordenada.Longitud
            }
        };
    }

    #endregion

    #region Response Validation

    private static void ValidateHttpResponse(HttpResponseMessage response, string jsonResponse)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        AppLogger.Error(LogContext, $"Error HTTP {(int)response.StatusCode} {response.StatusCode}");

        throw new Exception($"[OTP] Error HTTP {(int)response.StatusCode}: {jsonResponse}");
    }

    private static void ValidateGraphQlResponse(string jsonResponse)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
        ValidateGraphQlDocument(doc);
    }

    private static void ValidateGraphQlDocument(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("errors", out JsonElement errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            AppLogger.Error(LogContext, $"GraphQL errors detectados: {errors}");
            throw new Exception($"[OTP] Error GraphQL: {errors}");
        }
    }

    #endregion

    #region GraphQL Document Navigation

    private static JsonElement GetTripElement(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
        {
            throw new Exception("[OTP] No se encontró 'data' en la respuesta.");
        }

        if (!data.TryGetProperty("trip", out JsonElement trip))
        {
            throw new Exception("[OTP] No se encontró 'trip' en la respuesta.");
        }

        return trip;
    }

    private static JsonElement GetTripPatterns(JsonElement trip)
    {
        if (!trip.TryGetProperty("tripPatterns", out JsonElement patterns) ||
            patterns.ValueKind != JsonValueKind.Array)
        {
            throw new Exception("[OTP] 'tripPatterns' no existe o no es un array.");
        }

        return patterns;
    }

    #endregion

    #region Route Parsing

    private static List<RouteLegDto> ParseLegs(JsonElement pattern)
    {
        List<RouteLegDto> legs = new();

        if (!pattern.TryGetProperty("legs", out JsonElement legsElement) ||
            legsElement.ValueKind != JsonValueKind.Array)
        {
            return legs;
        }

        foreach (JsonElement leg in legsElement.EnumerateArray())
        {
            legs.Add(ParseLeg(leg));
        }

        return legs;
    }

    private static RouteLegDto ParseLeg(JsonElement leg)
    {
        string mode = ReadString(leg, "mode");
        int duration = ReadInt(leg, "duration");
        double distance = ReadDouble(leg, "distance");
        string fromName = ReadPlaceName(leg, "fromPlace");
        string toName = ReadPlaceName(leg, "toPlace");

        string? publicCode = ReadLineProperty(leg, "publicCode");
        string? lineName = ReadLineProperty(leg, "name");
        string? headsign = ReadHeadsign(leg);
        string? encodedPolyline = ReadEncodedPolyline(leg);

        return new RouteLegDto
        {
            Mode = mode,
            FromName = fromName,
            ToName = toName,
            DurationSeconds = duration,
            DistanceMeters = distance,
            PublicCode = publicCode,
            LineName = lineName,
            Headsign = headsign,
            EncodedPolyline = encodedPolyline
        };
    }

    private static int CalculateTransferCount(List<RouteLegDto> legs)
    {
        int transitLegs = legs.Count(leg =>
            !string.Equals(leg.Mode, "WALK", StringComparison.OrdinalIgnoreCase));

        return Math.Max(0, transitLegs - 1);
    }

    #endregion

    #region Leg Field Readers

    private static string? ReadLineProperty(JsonElement leg, string propertyName)
    {
        if (!leg.TryGetProperty("line", out JsonElement line) ||
            line.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadNullableString(line, propertyName);
    }

    private static string? ReadHeadsign(JsonElement leg)
    {
        if (!leg.TryGetProperty("fromEstimatedCall", out JsonElement call) ||
            call.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!call.TryGetProperty("destinationDisplay", out JsonElement display) ||
            display.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadNullableString(display, "frontText");
    }

    private static string? ReadEncodedPolyline(JsonElement leg)
    {
        if (!leg.TryGetProperty("pointsOnLink", out JsonElement pointsOnLink) ||
            pointsOnLink.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadNullableString(pointsOnLink, "points");
    }

    private static string ReadPlaceName(JsonElement leg, string placeProperty)
    {
        if (!leg.TryGetProperty(placeProperty, out JsonElement place) ||
            place.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return ReadString(place, "name");
    }

    #endregion

    #region Json Primitive Readers

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind != JsonValueKind.Null
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    #endregion

    #region Query Date Handling

    /// <summary>
    /// Calcula la fecha y hora que se enviará a OTP.
    /// 
    /// Si la consulta se realiza entre las 00:00 y las 05:59 en hora local
    /// de Barcelona, se consulta el día siguiente a las 09:00 para evitar
    /// respuestas vacías por falta de servicio nocturno.
    /// </summary>
    private static string GetQueryDateTime()
    {
        DateTime utcNow = DateTime.UtcNow;

        TimeZoneInfo zone = ResolveBarcelonaTimeZone();
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);

        AppLogger.Debug(LogContext, $"Hora local Barcelona para consulta: {localNow:HH:mm:ss}");

        bool isNightHours = localNow.Hour < 6;

        DateTime queryLocal = isNightHours
            ? localNow.Date.AddDays(1).AddHours(9)
            : localNow;

        if (isNightHours)
        {
            AppLogger.Info(
                LogContext,
                $"Franja nocturna detectada ({localNow:HH:mm}). " +
                $"Consultando OTP con fecha futura: {queryLocal:yyyy-MM-dd HH:mm}");
        }

        DateTime queryUtc = TimeZoneInfo.ConvertTimeToUtc(queryLocal, zone);

        return queryUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    /// <summary>
    /// Resuelve la zona horaria de Barcelona con fallback compatible
    /// entre Linux/macOS y Windows.
    /// </summary>
    private static TimeZoneInfo ResolveBarcelonaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        }
        catch
        {
            // Continúa con fallback Windows.
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        }
        catch
        {
            // Continúa con fallback fijo.
        }

        AppLogger.Warn(
            LogContext,
            "No se pudo resolver la zona horaria de Barcelona. Usando UTC+1 fijo.");

        return TimeZoneInfo.CreateCustomTimeZone(
            "CET-Fallback",
            TimeSpan.FromHours(1),
            "CET Fallback",
            "CET Fallback");
    }

    #endregion
}