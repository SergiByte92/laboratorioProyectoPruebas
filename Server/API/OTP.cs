using Server.Infrastructure;
using System.Text;
using System.Text.Json;

namespace Server.API;

/// <summary>
/// Cliente de integración con OpenTripPlanner.
///
/// Responsabilidades:
/// - Construir la query GraphQL.
/// - Enviar la petición HTTP.
/// - Validar errores HTTP y GraphQL.
/// - Parsear duración, distancia, legs, líneas y transbordos.
///
/// CAMBIO: GetQueryDateTime evita que OTP devuelva tripPatterns vacío
/// cuando la consulta llega fuera del horario de servicio de transporte
/// (p.ej. de madrugada). En ese caso, avanza la hora de consulta al día
/// siguiente a las 09:00 para obtener siempre un itinerario real.
/// </summary>
public sealed class OTP
{
    /// <summary>
    /// Coordenada geográfica simple.
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

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Endpoint GraphQL de OTP.
    /// </summary>
    private const string Url = "http://localhost:8080/otp/transmodel/v3";

    private const string Query = @"
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

    public OTP(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Consulta OTP y devuelve el JSON crudo.
    ///
    /// Usa GetQueryDateTime() en lugar de DateTime.UtcNow para evitar
    /// recibir tripPatterns vacío durante horario nocturno sin servicio.
    /// </summary>
    public async Task<string> ConsultarAsync(Coordenada origen, Coordenada destino)
    {
        string queryDateTime = GetQueryDateTime();

        AppLogger.Info(
            "OTP",
            $"Consultando ruta origen={origen.Latitud:F6},{origen.Longitud:F6} " +
            $"destino={destino.Latitud:F6},{destino.Longitud:F6} " +
            $"dateTime={queryDateTime}");

        var bodyObject = new
        {
            query = Query,
            variables = new
            {
                dateTime = queryDateTime,
                from = new
                {
                    coordinates = new
                    {
                        latitude = origen.Latitud,
                        longitude = origen.Longitud
                    }
                },
                to = new
                {
                    coordinates = new
                    {
                        latitude = destino.Latitud,
                        longitude = destino.Longitud
                    }
                }
            }
        };

        string jsonBody = JsonSerializer.Serialize(bodyObject);

        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PostAsync(Url, content);

        string jsonResponse = await response.Content.ReadAsStringAsync();

        AppLogger.Info("OTP", $"HTTP Status={(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Error("OTP", $"Error HTTP {(int)response.StatusCode} {response.StatusCode}");
            throw new Exception($"[OTP] Error HTTP {(int)response.StatusCode}: {jsonResponse}");
        }

        using JsonDocument doc = JsonDocument.Parse(jsonResponse);

        if (doc.RootElement.TryGetProperty("errors", out JsonElement errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            AppLogger.Error("OTP", $"GraphQL errors detectados: {errors}");
            throw new Exception($"[OTP] Error GraphQL: {errors}");
        }

        AppLogger.Debug("OTP", "Consulta completada correctamente.");
        return jsonResponse;
    }

    /// <summary>
    /// Método de compatibilidad: devuelve solo la duración.
    /// Internamente usa el parser completo.
    /// </summary>
    public int? ExtraerDuracion(string jsonResponse)
    {
        MeetingRouteResult? result = ExtraerResultadoRuta(jsonResponse);
        return result?.DurationSeconds;
    }

    /// <summary>
    /// Parser principal.
    /// Extrae el primer itinerario de OTP y lo convierte a MeetingRouteResult.
    /// Devuelve null cuando OTP responde bien pero no encuentra ruta.
    /// </summary>
    public MeetingRouteResult? ExtraerResultadoRuta(string jsonResponse)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonResponse);

        if (doc.RootElement.TryGetProperty("errors", out JsonElement errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            throw new Exception($"[OTP] Error GraphQL al extraer ruta: {errors}");
        }

        if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
            throw new Exception("[OTP] No se encontró 'data' en la respuesta.");

        if (!data.TryGetProperty("trip", out JsonElement trip) ||
            trip.ValueKind == JsonValueKind.Null)
        {
            AppLogger.Warn("OTP", "'trip' es null. Sin ruta disponible.");
            return null;
        }

        if (!trip.TryGetProperty("tripPatterns", out JsonElement patterns) ||
            patterns.ValueKind != JsonValueKind.Array)
        {
            throw new Exception("[OTP] 'tripPatterns' no existe o no es un array.");
        }

        int patternCount = patterns.GetArrayLength();
        AppLogger.Info("OTP", $"Itinerarios encontrados: {patternCount}");

        if (patternCount == 0)
        {
            AppLogger.Warn("OTP", "tripPatterns vacío. Sin ruta disponible.");
            return null;
        }

        JsonElement firstPattern = patterns[0];

        int duration = ReadInt(firstPattern, "duration");
        double distance = ReadDouble(firstPattern, "distance");

        List<RouteLegDto> legs = new();

        if (firstPattern.TryGetProperty("legs", out JsonElement legsElement) &&
            legsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement leg in legsElement.EnumerateArray())
            {
                legs.Add(ParseLeg(leg));
            }
        }

        int transitLegs = legs.Count(l =>
            !string.Equals(l.Mode, "WALK", StringComparison.OrdinalIgnoreCase));

        int transferCount = Math.Max(0, transitLegs - 1);

        AppLogger.Info(
            "OTP",
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

    // ── Helpers de parseo ─────────────────────────────────────────────────────

    private static RouteLegDto ParseLeg(JsonElement leg)
    {
        string mode = ReadString(leg, "mode");
        int duration = ReadInt(leg, "duration");
        double distance = ReadDouble(leg, "distance");
        string fromName = ReadPlaceName(leg, "fromPlace");
        string toName = ReadPlaceName(leg, "toPlace");

        string? publicCode = null;
        string? lineName = null;

        if (leg.TryGetProperty("line", out JsonElement line) &&
            line.ValueKind != JsonValueKind.Null)
        {
            publicCode = ReadNullableString(line, "publicCode");
            lineName = ReadNullableString(line, "name");
        }

        string? headsign = null;

        if (leg.TryGetProperty("fromEstimatedCall", out JsonElement call) &&
            call.ValueKind != JsonValueKind.Null &&
            call.TryGetProperty("destinationDisplay", out JsonElement display) &&
            display.ValueKind != JsonValueKind.Null)
        {
            headsign = ReadNullableString(display, "frontText");
        }

        string? encodedPolyline = null;

        if (leg.TryGetProperty("pointsOnLink", out JsonElement pointsOnLink) &&
            pointsOnLink.ValueKind != JsonValueKind.Null)
        {
            encodedPolyline = ReadNullableString(pointsOnLink, "points");
        }

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

    private static string ReadPlaceName(JsonElement leg, string placeProperty)
    {
        if (!leg.TryGetProperty(placeProperty, out JsonElement place) ||
            place.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return ReadString(place, "name");
    }

    // ── Fecha/hora para la consulta ───────────────────────────────────────────

    /// <summary>
    /// Calcula la fecha y hora que se enviará a OTP.
    ///
    /// Problema: si la consulta llega entre las 00:00 y las 06:00 (hora de
    /// Barcelona), OTP devuelve tripPatterns vacío porque no hay servicio
    /// nocturno en la mayoría de líneas. Esto no es un error técnico, pero
    /// el resultado parece un fallo desde el punto de vista del usuario.
    ///
    /// Solución: si estamos en franja nocturna (00:00–05:59 hora local),
    /// avanzamos la hora de consulta al día siguiente a las 09:00, que
    /// garantiza servicio completo de transporte público.
    ///
    /// El ID de zona horaria se intenta en el orden:
    ///   1. "Europe/Madrid"       → Linux / macOS
    ///   2. "Romance Standard Time" → Windows (alias de Europe/Madrid en CLDR)
    ///   3. Offset fijo UTC+1     → fallback si ninguno de los anteriores existe
    /// </summary>
    private static string GetQueryDateTime()
    {
        DateTime utcNow = DateTime.UtcNow;

        TimeZoneInfo zone = ResolveBarcelonaTimeZone();
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);

        AppLogger.Debug("OTP", $"Hora local Barcelona para consulta: {localNow:HH:mm:ss}");

        bool isNightHours = localNow.Hour < 6;

        DateTime queryLocal = isNightHours
            ? localNow.Date.AddDays(1).AddHours(9)  // mañana a las 09:00
            : localNow;

        if (isNightHours)
        {
            AppLogger.Info("OTP",
                $"Franja nocturna detectada ({localNow:HH:mm}). " +
                $"Consultando OTP con fecha futura: {queryLocal:yyyy-MM-dd HH:mm}");
        }

        DateTime queryUtc = TimeZoneInfo.ConvertTimeToUtc(queryLocal, zone);
        return queryUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }

    /// <summary>
    /// Resuelve la zona horaria de Barcelona con fallback progresivo.
    /// Necesario porque el ID varía entre Linux/macOS y Windows.
    /// </summary>
    private static TimeZoneInfo ResolveBarcelonaTimeZone()
    {
        // Linux / macOS
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"); }
        catch { /* continúa */ }

        // Windows
        try { return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); }
        catch { /* continúa */ }

        // Fallback: UTC+1 fijo (sin ajuste de verano, pero válido para desarrollo)
        AppLogger.Warn("OTP", "No se pudo resolver la zona horaria de Barcelona. Usando UTC+1 fijo.");
        return TimeZoneInfo.CreateCustomTimeZone(
            "CET-Fallback",
            TimeSpan.FromHours(1),
            "CET Fallback",
            "CET Fallback");
    }
}