using Server.Infrastructure;
using System.Text;
using System.Text.Json;

namespace Server.API;

/// <summary>
/// Cliente de integración con OpenTripPlanner (OTP).
///
/// Esta clase encapsula toda la comunicación HTTP/GraphQL contra OTP y evita
/// que el resto del servidor tenga que conocer la estructura interna de la
/// respuesta GraphQL.
///
/// Responsabilidades principales:
/// - Construir la query GraphQL de planificación de viaje.
/// - Enviar la petición HTTP al endpoint de OTP.
/// - Validar errores HTTP y errores GraphQL.
/// - Parsear la respuesta cruda de OTP.
/// - Transformar el primer itinerario válido en modelos internos del servidor.
///
/// No calcula el punto de encuentro.
/// No gestiona grupos ni sesiones.
/// No envía datos directamente al cliente MAUI.
/// </summary>
public sealed class OTP
{
    private const string LogContext = nameof(OTP);

    /// <summary>
    /// Endpoint GraphQL expuesto por OpenTripPlanner.
    ///
    /// En el entorno actual se espera que OTP esté ejecutándose localmente,
    /// normalmente dentro de Docker, exponiendo el puerto 8080.
    /// </summary>
    private const string OtpGraphQlUrl = "http://localhost:8080/otp/transmodel/v3";

    /// <summary>
    /// Query GraphQL usada para solicitar rutas entre dos coordenadas.
    ///
    /// Se solicitan:
    /// - Patrones de viaje disponibles.
    /// - Duración y distancia total.
    /// - Tramos individuales del itinerario.
    /// - Información de línea, modo de transporte, dirección y geometría.
    ///
    /// El resultado se parsea posteriormente a MeetingRouteResult y RouteLegDto.
    /// </summary>
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

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Recibe HttpClient por inyección de dependencias.
    ///
    /// Esto evita crear instancias manuales de HttpClient dentro de la clase
    /// y permite configurar timeouts, handlers o políticas externas.
    /// </summary>
    public OTP(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Coordenada geográfica simple usada como origen o destino de una ruta.
    ///
    /// Origen: ubicación real del usuario.
    /// Destino: punto de encuentro calculado por el servidor.
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

    /// <summary>
    /// Consulta OpenTripPlanner y devuelve la respuesta JSON cruda.
    ///
    /// Este método solo se encarga de la comunicación con OTP:
    /// - Prepara variables de la query.
    /// - Envía la petición HTTP.
    /// - Valida la respuesta.
    /// - Devuelve el JSON sin transformarlo.
    ///
    /// La transformación del JSON a modelos internos se hace en
    /// ExtraerResultadoRuta.
    /// </summary>
    public async Task<string> ConsultarAsync(Coordenada origen, Coordenada destino)
    {
        /*
         * OTP puede devolver tripPatterns vacío si se consulta en franjas
         * con poco o ningún servicio de transporte.
         *
         * Por eso se calcula una fecha controlada con GetQueryDateTime().
         */
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

        /*
         * GraphQL puede devolver HTTP 200 aunque exista un error lógico
         * dentro del campo "errors". Por eso se validan ambos niveles:
         * - Estado HTTP.
         * - Errores GraphQL dentro del JSON.
         */
        ValidateHttpResponse(response, jsonResponse);
        ValidateGraphQlResponse(jsonResponse);

        AppLogger.Debug(LogContext, "Consulta completada correctamente.");

        return jsonResponse;
    }

    /// <summary>
    /// Método de compatibilidad para obtener únicamente la duración.
    ///
    /// Se conserva para llamadas antiguas o casos donde solo interesa saber
    /// el tiempo total, pero el flujo principal debería usar
    /// ExtraerResultadoRuta para obtener duración, distancia, transbordos y legs.
    /// </summary>
    public int? ExtraerDuracion(string jsonResponse)
    {
        MeetingRouteResult? result = ExtraerResultadoRuta(jsonResponse);
        return result?.DurationSeconds;
    }

    /// <summary>
    /// Extrae el primer itinerario válido devuelto por OTP y lo transforma
    /// en un MeetingRouteResult.
    ///
    /// Devuelve null cuando OTP responde correctamente pero no encuentra
    /// ninguna ruta válida. Esto no se considera error técnico, sino ausencia
    /// funcional de itinerario.
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

        /*
         * OTP puede devolver varios itinerarios.
         * Actualmente se toma el primero, que suele ser el recomendado
         * por el motor de planificación.
         *
         * Mejora futura:
         * seleccionar por menor duración, menor número de transbordos
         * o generalizedCost.
         */
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

    /// <summary>
    /// Adapta una coordenada interna al formato esperado por la variable
    /// Location de la query GraphQL de OTP.
    /// </summary>
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

    /// <summary>
    /// Valida el estado HTTP de la respuesta.
    ///
    /// Detecta errores de infraestructura o endpoint:
    /// - OTP no disponible.
    /// - Endpoint incorrecto.
    /// - Error interno de OTP.
    /// - Request mal formada.
    /// </summary>
    private static void ValidateHttpResponse(HttpResponseMessage response, string jsonResponse)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        AppLogger.Error(LogContext, $"Error HTTP {(int)response.StatusCode} {response.StatusCode}");

        throw new Exception($"[OTP] Error HTTP {(int)response.StatusCode}: {jsonResponse}");
    }

    /// <summary>
    /// Valida si la respuesta GraphQL contiene errores lógicos en el campo "errors".
    ///
    /// En GraphQL puede existir HTTP 200 con errores dentro del body.
    /// </summary>
    private static void ValidateGraphQlResponse(string jsonResponse)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonResponse);
        ValidateGraphQlDocument(doc);
    }

    /// <summary>
    /// Revisa el documento JSON ya parseado y lanza excepción si GraphQL
    /// devolvió errores.
    /// </summary>
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

    /// <summary>
    /// Obtiene el nodo data.trip de la respuesta GraphQL.
    ///
    /// Si la estructura no coincide con lo esperado, se considera error
    /// de contrato con OTP.
    /// </summary>
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

    /// <summary>
    /// Obtiene el array tripPatterns desde data.trip.
    ///
    /// tripPatterns contiene los itinerarios candidatos devueltos por OTP.
    /// </summary>
    private static JsonElement GetTripPatterns(JsonElement trip)
    {
        if (!trip.TryGetProperty("tripPatterns", out JsonElement patterns) ||
            patterns.ValueKind != JsonValueKind.Array)
        {
            throw new Exception("[OTP] 'tripPatterns' no existe o no es un array.");
        }

        return patterns;
    }

    /// <summary>
    /// Extrae todos los tramos del itinerario seleccionado.
    ///
    /// Cada leg representa una fase del trayecto:
    /// caminar, metro, bus, tren, transbordo, etc.
    /// </summary>
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

    /// <summary>
    /// Convierte un leg de OTP en RouteLegDto.
    ///
    /// Se extraen datos comunes como modo, duración, distancia, origen/destino,
    /// línea de transporte, dirección y geometría codificada.
    /// </summary>
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

    /// <summary>
    /// Calcula el número aproximado de transbordos.
    ///
    /// Se cuentan únicamente los tramos de transporte público, ignorando WALK.
    /// Si hay una sola línea de transporte, hay 0 transbordos.
    /// Si hay dos líneas de transporte, hay 1 transbordo, etc.
    /// </summary>
    private static int CalculateTransferCount(List<RouteLegDto> legs)
    {
        int transitLegs = legs.Count(leg =>
            !string.Equals(leg.Mode, "WALK", StringComparison.OrdinalIgnoreCase));

        return Math.Max(0, transitLegs - 1);
    }

    /// <summary>
    /// Lee una propiedad dentro del nodo line de un leg.
    ///
    /// En tramos WALK normalmente no existe line, por lo que se devuelve null.
    /// </summary>
    private static string? ReadLineProperty(JsonElement leg, string propertyName)
    {
        if (!leg.TryGetProperty("line", out JsonElement line) ||
            line.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadNullableString(line, propertyName);
    }

    /// <summary>
    /// Lee la dirección o cabecera de la línea desde fromEstimatedCall.
    ///
    /// Ejemplo conceptual:
    /// - "Zona Universitària"
    /// - "Badalona Pompeu Fabra"
    /// </summary>
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

    /// <summary>
    /// Lee la geometría codificada del tramo.
    ///
    /// OTP devuelve puntos de ruta como polyline codificada. El cliente puede
    /// usarla para dibujar el trazado real del tramo en el mapa.
    /// </summary>
    private static string? ReadEncodedPolyline(JsonElement leg)
    {
        if (!leg.TryGetProperty("pointsOnLink", out JsonElement pointsOnLink) ||
            pointsOnLink.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadNullableString(pointsOnLink, "points");
    }

    /// <summary>
    /// Lee el nombre de un lugar dentro del leg:
    /// - fromPlace.name
    /// - toPlace.name
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

    /// <summary>
    /// Lee un string obligatorio de forma defensiva.
    ///
    /// Si la propiedad no existe o es null, devuelve string.Empty para evitar
    /// excepciones durante el parseo de campos no críticos.
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
    ///
    /// Devuelve null cuando la propiedad no existe o es null.
    /// </summary>
    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Lee un entero de forma defensiva.
    ///
    /// Devuelve 0 si la propiedad no existe o no es numérica.
    /// </summary>
    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    /// <summary>
    /// Lee un double de forma defensiva.
    ///
    /// Devuelve 0 si la propiedad no existe o no es numérica.
    /// </summary>
    private static double ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    /// <summary>
    /// Calcula la fecha y hora que se enviará a OTP.
    ///
    /// Si la consulta se realiza entre las 00:00 y las 05:59 en hora local
    /// de Barcelona, se consulta el día siguiente a las 09:00 para evitar
    /// respuestas vacías por falta de servicio nocturno.
    ///
    /// El valor final se devuelve en UTC con formato ISO-8601:
    /// yyyy-MM-ddTHH:mm:ssZ
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
    /// Resuelve la zona horaria de Barcelona con compatibilidad entre sistemas.
    ///
    /// Linux/macOS suelen usar identificadores IANA como Europe/Madrid.
    /// Windows usa identificadores propios como Romance Standard Time.
    ///
    /// Si ninguno está disponible, se usa un fallback UTC+1.
    /// </summary>
    private static TimeZoneInfo ResolveBarcelonaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        }
        catch
        {
            // Fallback para Windows.
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        }
        catch
        {
            // Fallback final si el entorno no reconoce ninguna zona.
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
}