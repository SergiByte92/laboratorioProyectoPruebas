using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.API
{
    public sealed class OTP
    {
        // --- 1. CLASES DE DATOS INTERNAS ---
        // Al estar dentro de OTP, las usas como OTP.Coordenada
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

        // --- 2. CONFIGURACIÓN ---
        private readonly HttpClient _httpClient;
        private const string Url = "http://localhost:8080/otp/transmodel/v3";

        // 1. Nueva Query con soporte para modos
        private const string Query = @"
        query trip($from: Location!, $to: Location!, $modes: [TransportModeInput]!) {
          trip(from: $from, to: $to, transportModes: $modes) {
            tripPatterns {
              duration
              legs {
                mode
                duration
                fromPlace { quay { id name coordinates { latitude longitude } } }
                toPlace { quay { id name coordinates { latitude longitude } } }
                intermediateQuays { id name coordinates { latitude longitude } }
              }
            }
          }
        }";

        public OTP(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // --- 3. MÉTODOS DE CONSULTA Y EXTRACCIÓN ---

        // PASO A: Consultar el servidor (Envío y Recepción)
        public async Task<string> ConsultarAsync(Coordenada origen, Coordenada destino, string modoTransporte)
        {
            var bodyObject = new
            {
                query = Query,
                variables = new
                {
                    from = new { coordinates = new { latitude = origen.Latitud, longitude = origen.Longitud } },
                    to = new { coordinates = new { latitude = destino.Latitud, longitude = destino.Longitud } },
                    // Aquí inyectamos el modo que nos dice el usuario
                    modes = new[] { new { mode = modoTransporte } }
                }
            };

            string jsonBody = JsonSerializer.Serialize(bodyObject);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.PostAsync(Url, content);
            string jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Error en OTP: {response.StatusCode}. Detalle: {jsonResponse}");

            return jsonResponse;
        }

        // PASO B: Sacar la duración (Lógica de procesado)
        public int ExtraerDuracion(string jsonResponse)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);
            if (!doc.RootElement.TryGetProperty("data", out JsonElement data)) return 0;
            if (!data.TryGetProperty("trip", out JsonElement trip) || trip.ValueKind == JsonValueKind.Null) return 0;

            var patterns = trip.GetProperty("tripPatterns");
            if (patterns.GetArrayLength() == 0) return 0;

            return patterns[0].GetProperty("duration").GetInt32();
        }

        // PASO C: Sacar el recorrido completo
        public List<NodoRuta> ExtraerRecorrido(string jsonResponse)
        {
            var nodos = new List<NodoRuta>();
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (!doc.RootElement.TryGetProperty("data", out JsonElement data)) return nodos;
            var trip = data.GetProperty("trip");
            if (trip.ValueKind == JsonValueKind.Null) return nodos;

            var patterns = trip.GetProperty("tripPatterns");
            if (patterns.GetArrayLength() == 0) return nodos;

            foreach (JsonElement leg in patterns[0].GetProperty("legs").EnumerateArray())
            {
                AgregarNodoSiNoExiste(nodos, ExtraerNodoDePlace(leg, "fromPlace"));

                if (leg.TryGetProperty("intermediateQuays", out JsonElement intermedias))
                {
                    foreach (JsonElement quay in intermedias.EnumerateArray())
                        AgregarNodoSiNoExiste(nodos, ExtraerNodoDeQuay(quay));
                }

                AgregarNodoSiNoExiste(nodos, ExtraerNodoDePlace(leg, "toPlace"));
            }
            return nodos;
        }

        // --- 4. MÉTODOS PRIVADOS DE APOYO ---
        private static void AgregarNodoSiNoExiste(List<NodoRuta> nodos, NodoRuta? nodo)
        {
            if (nodo == null) return;
            if (!nodos.Any(n => !string.IsNullOrWhiteSpace(n.Id) && n.Id == nodo.Id))
                nodos.Add(nodo);
        }

        private static NodoRuta? ExtraerNodoDePlace(JsonElement leg, string placeKey)
        {
            if (!leg.TryGetProperty(placeKey, out JsonElement place)) return null;
            if (!place.TryGetProperty("quay", out JsonElement quay) || quay.ValueKind == JsonValueKind.Null) return null;
            return ExtraerNodoDeQuay(quay);
        }

        private static NodoRuta? ExtraerNodoDeQuay(JsonElement quay)
        {
            var nodo = new NodoRuta();
            if (quay.TryGetProperty("id", out JsonElement id)) nodo.Id = id.GetString() ?? "";
            if (quay.TryGetProperty("name", out JsonElement name)) nodo.Nombre = name.GetString() ?? "";

            if (quay.TryGetProperty("coordinates", out JsonElement coords))
            {
                nodo.Latitud = coords.GetProperty("latitude").GetDouble();
                nodo.Longitud = coords.GetProperty("longitude").GetDouble();
            }
            return nodo;
        }
    }
}