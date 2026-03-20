using System.Text;
using System.Text.Json;

namespace API
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
        public double Latitud { get; set; }
        public double Longitud { get; set; }
    }

    public sealed class OTP
    {
        private readonly HttpClient _httpClient;
        private const string Url = "http://localhost:8080/otp/transmodel/v3";

        private const string Query = @"
query trip($from: Location!, $to: Location!) {
  trip(from: $from, to: $to) {
    tripPatterns {
      duration
      legs {
        mode
        duration
        fromPlace {
          name
          quay {
            id
            name
            coordinates {
              latitude
              longitude
            }
          }
        }
        toPlace {
          name
          quay {
            id
            name
            coordinates {
              latitude
              longitude
            }
          }
        }
        intermediateQuays {
          id
          name
          coordinates {
            latitude
            longitude
          }
        }
      }
    }
  }
}";

        public OTP(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Método 1:
        /// Envía la consulta a OTP y devuelve el JSON crudo de respuesta.
        /// </summary>
        public async Task<string> ConsultarAsync(Coordenada origen, Coordenada destino)
        {
            var bodyObject = new
            {
                query = Query,
                variables = new
                {
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

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error HTTP al consultar OTP: {jsonResponse}");
            }

            return jsonResponse;
        }

        /// <summary>
        /// Método 2:
        /// Extrae la duración total en segundos de la primera ruta devuelta por OTP.
        /// </summary>
        public int ExtraerDuracion(string jsonResponse)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("data", out JsonElement data))
                return 0;

            if (!data.TryGetProperty("trip", out JsonElement trip) || trip.ValueKind == JsonValueKind.Null)
                return 0;

            if (!trip.TryGetProperty("tripPatterns", out JsonElement tripPatterns) || tripPatterns.GetArrayLength() == 0)
                return 0;

            JsonElement primeraRuta = tripPatterns[0];

            if (!primeraRuta.TryGetProperty("duration", out JsonElement durationElement))
                return 0;

            return durationElement.GetInt32();
        }

        /// <summary>
        /// Método 3:
        /// Extrae el recorrido completo como lista ordenada de nodos.
        /// Incluye fromPlace, intermediateQuays y toPlace.
        /// </summary>
        public List<NodoRuta> ExtraerRecorrido(string jsonResponse)
        {
            var nodos = new List<NodoRuta>();

            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("data", out JsonElement data))
                return nodos;

            if (!data.TryGetProperty("trip", out JsonElement trip) || trip.ValueKind == JsonValueKind.Null)
                return nodos;

            if (!trip.TryGetProperty("tripPatterns", out JsonElement tripPatterns) || tripPatterns.GetArrayLength() == 0)
                return nodos;

            JsonElement primeraRuta = tripPatterns[0];

            if (!primeraRuta.TryGetProperty("legs", out JsonElement legs))
                return nodos;

            foreach (JsonElement leg in legs.EnumerateArray())
            {
                NodoRuta? nodoInicio = ExtraerNodoDePlace(leg, "fromPlace");
                AgregarNodoSiNoExiste(nodos, nodoInicio);

                if (leg.TryGetProperty("intermediateQuays", out JsonElement intermedias))
                {
                    foreach (JsonElement quay in intermedias.EnumerateArray())
                    {
                        NodoRuta? nodoIntermedio = ExtraerNodoDeQuay(quay);
                        AgregarNodoSiNoExiste(nodos, nodoIntermedio);
                    }
                }

                NodoRuta? nodoFin = ExtraerNodoDePlace(leg, "toPlace");
                AgregarNodoSiNoExiste(nodos, nodoFin);
            }

            return nodos;
        }

        private static void AgregarNodoSiNoExiste(List<NodoRuta> nodos, NodoRuta? nodo)
        {
            if (nodo == null)
                return;

            bool existe = nodos.Any(n =>
                !string.IsNullOrWhiteSpace(n.Id) &&
                n.Id == nodo.Id);

            if (!existe)
            {
                nodos.Add(nodo);
            }
        }

        private static NodoRuta? ExtraerNodoDePlace(JsonElement leg, string placeKey)
        {
            if (!leg.TryGetProperty(placeKey, out JsonElement place))
                return null;

            if (!place.TryGetProperty("quay", out JsonElement quay) || quay.ValueKind == JsonValueKind.Null)
                return null;

            return ExtraerNodoDeQuay(quay);
        }

        private static NodoRuta? ExtraerNodoDeQuay(JsonElement quay)
        {
            if (quay.ValueKind == JsonValueKind.Null)
                return null;

            string id = "";
            string nombre = "";
            double latitud = 0;
            double longitud = 0;

            if (quay.TryGetProperty("id", out JsonElement idElement))
                id = idElement.GetString() ?? "";

            if (quay.TryGetProperty("name", out JsonElement nombreElement))
                nombre = nombreElement.GetString() ?? "";

            if (quay.TryGetProperty("coordinates", out JsonElement coordinates))
            {
                if (coordinates.TryGetProperty("latitude", out JsonElement latitudElement))
                    latitud = latitudElement.GetDouble();

                if (coordinates.TryGetProperty("longitude", out JsonElement longitudElement))
                    longitud = longitudElement.GetDouble();
            }

            return new NodoRuta
            {
                Id = id,
                Nombre = nombre,
                Latitud = latitud,
                Longitud = longitud
            };
        }
    }
}