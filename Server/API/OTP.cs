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
/// </summary>
public sealed class OTP
{
    /// <summary>
    /// Coordenada geográfica simple.
    /// Se usa para origen y destino.
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

    /// <summary>
    /// Query GraphQL.
    /// 
    /// Pedimos:
    /// - duración total
    /// - distancia total
    /// - legs
    /// - modo de transporte
    /// - línea
    /// - paradas
    /// - headsign
    /// - geometría codificada
    /// </summary>
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
    /// Este método no transforma la ruta.
    /// Solo hace:
    /// - POST HTTP
    /// - validación de status code
    /// - validación de errores GraphQL
    /// </summary>
    public async Task<string> ConsultarAsync(Coordenada origen, Coordenada destino)
    {
        AppLogger.Info(
            "OTP",
            $"Consultando ruta origen={origen.Latitud:F6},{origen.Longitud:F6} destino={destino.Latitud:F6},{destino.Longitud:F6}");

        var bodyObject = new
        {
            query = Query,
            variables = new
            {
                dateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
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
            throw new Exception($"[OTP] Error HTTP {(int)response.StatusCode} {response.StatusCode}: {jsonResponse}");
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
    /// Método de compatibilidad.
    /// 
    /// Si alguna parte antigua del servidor sigue esperando solo duración,
    /// este método permite mantenerla sin romper todo.
    /// Internamente usa el parser nuevo.
    /// </summary>
    public int? ExtraerDuracion(string jsonResponse)
    {
        MeetingRouteResult? result = ExtraerResultadoRuta(jsonResponse);
        return result?.DurationSeconds;
    }

    /// <summary>
    /// Parser principal.
    /// 
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
            $"Primer itinerario: duration={duration}s ({duration / 60} min), distance={distance:F2}m, legs={legs.Count}, transfers={transferCount}");

        return new MeetingRouteResult
        {
            DurationSeconds = duration,
            DistanceMeters = distance,
            TransferCount = transferCount,
            Legs = legs
        };
    }

    /// <summary>
    /// Convierte un leg de OTP en un DTO propio.
    /// 
    /// Aquí se centraliza la lectura de:
    /// - modo
    /// - origen/destino
    /// - duración
    /// - distancia
    /// - línea
    /// - dirección
    /// - geometría
    /// </summary>
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

    /// <summary>
    /// Lee un string obligatorio de forma segura.
    /// Si no existe, devuelve string.Empty.
    /// </summary>
    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind != JsonValueKind.Null
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>
    /// Lee un string opcional.
    /// Si no existe o es null, devuelve null.
    /// </summary>
    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Lee un entero de forma segura.
    /// Si no existe, devuelve 0.
    /// </summary>
    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    /// <summary>
    /// Lee un double de forma segura.
    /// Si no existe, devuelve 0.
    /// </summary>
    private static double ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    /// <summary>
    /// Lee el name de fromPlace o toPlace.
    /// </summary>
    private static string ReadPlaceName(JsonElement leg, string placeProperty)
    {
        if (!leg.TryGetProperty(placeProperty, out JsonElement place) ||
            place.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return ReadString(place, "name");
    }
}