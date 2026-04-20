using Server.Infrastructure;
using System.Text;
using System.Text.Json;

namespace Server.API
{
    public sealed class OTP
    {
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

        public sealed class NodoRuta
        {
            public string Id { get; set; } = "";
            public string Nombre { get; set; } = "";
        }

        private readonly HttpClient _httpClient;
        private const string Url = "http://localhost:8080/otp/transmodel/v3";

        private const string Query = @"
query trip($dateTime: DateTime, $from: Location!, $to: Location!) {
  trip(dateTime: $dateTime, from: $from, to: $to) {
    previousPageCursor
    nextPageCursor
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

            // Solo dejar esto si estás depurando algo muy concreto
            // AppLogger.Debug("OTP", $"Request body: {jsonBody}");

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

        public int? ExtraerDuracion(string jsonResponse)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.TryGetProperty("errors", out JsonElement errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                throw new Exception($"[OTP] Error GraphQL al extraer duración: {errors}");
            }

            if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
            {
                throw new Exception("[OTP] ExtraerDuracion: no se encontró 'data' en la respuesta.");
            }

            if (!data.TryGetProperty("trip", out JsonElement trip) ||
                trip.ValueKind == JsonValueKind.Null)
            {
                AppLogger.Warn("OTP", "'trip' es null. Sin ruta disponible.");
                return null;
            }

            if (!trip.TryGetProperty("tripPatterns", out JsonElement patterns) ||
                patterns.ValueKind != JsonValueKind.Array)
            {
                throw new Exception("[OTP] ExtraerDuracion: 'tripPatterns' no existe o no es un array.");
            }

            int count = patterns.GetArrayLength();
            AppLogger.Info("OTP", $"Itinerarios encontrados: {count}");

            if (count == 0)
            {
                AppLogger.Warn("OTP", "tripPatterns vacío. Sin ruta disponible.");
                return null;
            }

            JsonElement firstPattern = patterns[0];

            if (!firstPattern.TryGetProperty("duration", out JsonElement durationElement))
            {
                throw new Exception("[OTP] ExtraerDuracion: el primer tripPattern no contiene 'duration'.");
            }

            int duration = durationElement.GetInt32();

            double? distance = null;
            if (firstPattern.TryGetProperty("distance", out JsonElement distanceElement) &&
                distanceElement.ValueKind != JsonValueKind.Null)
            {
                distance = distanceElement.GetDouble();
            }

            int legsCount = 0;
            if (firstPattern.TryGetProperty("legs", out JsonElement legs) &&
                legs.ValueKind == JsonValueKind.Array)
            {
                legsCount = legs.GetArrayLength();
            }

            AppLogger.Info(
                "OTP",
                $"Primer itinerario: duration={duration}s ({duration / 60} min), distance={(distance?.ToString("F2") ?? "N/A")}m, legs={legsCount}");

            return duration;
        }

        public List<NodoRuta> ExtraerRecorrido(string jsonResponse)
        {
            var nodos = new List<NodoRuta>();

            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.TryGetProperty("errors", out JsonElement errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                throw new Exception($"[OTP] Error GraphQL al extraer recorrido: {errors}");
            }

            if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
                return nodos;

            if (!data.TryGetProperty("trip", out JsonElement trip) ||
                trip.ValueKind == JsonValueKind.Null)
                return nodos;

            if (!trip.TryGetProperty("tripPatterns", out JsonElement patterns) ||
                patterns.ValueKind != JsonValueKind.Array ||
                patterns.GetArrayLength() == 0)
                return nodos;

            if (!patterns[0].TryGetProperty("legs", out JsonElement legs) ||
                legs.ValueKind != JsonValueKind.Array)
                return nodos;

            foreach (JsonElement leg in legs.EnumerateArray())
            {
                AgregarNodoSiNoExiste(nodos, ExtraerNodoDePlace(leg, "fromPlace"));
                AgregarNodoSiNoExiste(nodos, ExtraerNodoDePlace(leg, "toPlace"));
            }

            AppLogger.Debug("OTP", $"Recorrido extraído con {nodos.Count} nodos.");
            return nodos;
        }

        public async Task<int?> ObtenerDuracionAsync(Coordenada origen, Coordenada destino)
        {
            string json = await ConsultarAsync(origen, destino);
            return ExtraerDuracion(json);
        }

        private static void AgregarNodoSiNoExiste(List<NodoRuta> nodos, NodoRuta? nodo)
        {
            if (nodo == null) return;
            if (!string.IsNullOrWhiteSpace(nodo.Id) && !nodos.Any(n => n.Id == nodo.Id))
                nodos.Add(nodo);
        }

        private static NodoRuta? ExtraerNodoDePlace(JsonElement leg, string placeKey)
        {
            if (!leg.TryGetProperty(placeKey, out JsonElement place))
                return null;

            var nodo = new NodoRuta();

            if (place.TryGetProperty("name", out JsonElement name))
                nodo.Nombre = name.GetString() ?? "";

            if (place.TryGetProperty("quay", out JsonElement quay) &&
                quay.ValueKind != JsonValueKind.Null &&
                quay.TryGetProperty("id", out JsonElement id))
            {
                nodo.Id = id.GetString() ?? "";
            }

            return nodo;
        }
    }
}