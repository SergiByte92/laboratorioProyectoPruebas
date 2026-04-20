using System.Text;
using System.Text.Json;

namespace Server.API
{
    /// <summary>
    /// Proporciona acceso a los servicios de OpenTripPlanner necesarios para obtener
    /// información de rutas y desplazamientos entre ubicaciones.
    /// </summary>
    public sealed class OTP
    {
        // ── CLASES DE DATOS ──────────────────────────────────────────────────

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

        // ── MODOS DE TRANSPORTE ───────────────────────────────────────────────
        // Valores válidos para la API Transmodel de OTP2.
        // Se pueden combinar: ej. { WALK, BUS } significa ir a pie hasta la parada
        // y luego coger el bus.
        public static class Modo
        {
            public const string APie = "foot";   // a pie (WALK)
            public const string Bus = "BUS";
            public const string Metro = "SUBWAY";
            public const string Tren = "RAIL";
            public const string Tranvia = "TRAM";
            public const string Ferry = "FERRY";
        }

        // ── CONFIGURACIÓN ────────────────────────────────────────────────────

        private readonly HttpClient _httpClient;
        private const string Url = "http://localhost:8080/otp/transmodel/v3";

        // La query acepta múltiples modos. Si pasas [WALK, BUS] OTP calcula
        // la ruta combinando ambos (ir a pie hasta parada + coger bus).
        private const string Query = @"
        query trip($from: Location!, $to: Location!, $modes: [TransportModeInput]!) {
          trip(from: $from, to: $to, transportModes: $modes) {
            tripPatterns {
              duration
              legs {
                mode
                duration
                fromPlace { quay { id name coordinates { latitude longitude } } }
                toPlace   { quay { id name coordinates { latitude longitude } } }
                intermediateQuays { id name coordinates { latitude longitude } }
              }
            }
          }
        }";

        public OTP(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        // ── MÉTODOS PÚBLICOS ─────────────────────────────────────────────────

        /// <summary>
        /// Consulta OTP y devuelve el JSON completo de la respuesta.
        /// modos: array de strings del tipo Modo.* ej. ["foot"] o ["WALK","BUS"]
        /// </summary>
        public async Task<string> ConsultarAsync(
            Coordenada origen,
            Coordenada destino,
            params string[] modos)
        {
            Console.WriteLine($"[OTP] Consultando ruta...");
            Console.WriteLine($"[OTP]   Origen  : {origen.Latitud}, {origen.Longitud}");
            Console.WriteLine($"[OTP]   Destino : {destino.Latitud}, {destino.Longitud}");
            Console.WriteLine($"[OTP]   Modos   : {string.Join(", ", modos)}");

            // Construimos el array de modos para la query GraphQL
            var modesArray = modos.Select(m => new { mode = m }).ToArray();

            var bodyObject = new
            {
                query = Query,
                variables = new
                {
                    from = new { coordinates = new { latitude = origen.Latitud, longitude = origen.Longitud } },
                    to = new { coordinates = new { latitude = destino.Latitud, longitude = destino.Longitud } },
                    modes = modesArray
                }
            };

            string jsonBody = JsonSerializer.Serialize(bodyObject);
            Console.WriteLine($"[OTP] Body enviado: {jsonBody}");

            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.PostAsync(Url, content);

            string jsonResponse = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[OTP] HTTP Status : {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine($"[OTP] Respuesta   : {jsonResponse}");

            if (!response.IsSuccessStatusCode)
                throw new Exception($"[OTP] Error HTTP {response.StatusCode}: {jsonResponse}");

            return jsonResponse;
        }

        /// <summary>
        /// Extrae la duración en segundos del primer tripPattern.
        /// Devuelve 0 si OTP no encontró ruta (tripPatterns vacío).
        /// </summary>
        public int ExtraerDuracion(string jsonResponse)
        {
            using JsonDocument doc = JsonDocument.Parse(jsonResponse);

            if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
            {
                Console.WriteLine("[OTP] ExtraerDuracion: no se encontró 'data' en la respuesta.");
                return 0;
            }

            if (!data.TryGetProperty("trip", out JsonElement trip) ||
                trip.ValueKind == JsonValueKind.Null)
            {
                Console.WriteLine("[OTP] ExtraerDuracion: 'trip' es null. OTP no encontró ruta.");
                return 0;
            }

            var patterns = trip.GetProperty("tripPatterns");
            int count = patterns.GetArrayLength();

            Console.WriteLine($"[OTP] ExtraerDuracion: tripPatterns encontrados = {count}");

            if (count == 0)
            {
                Console.WriteLine("[OTP] ExtraerDuracion: tripPatterns vacío. Sin ruta disponible para estos parámetros.");
                return 0;
            }

            int duration = patterns[0].GetProperty("duration").GetInt32();
            Console.WriteLine($"[OTP] ExtraerDuracion: duración del primer patrón = {duration}s ({duration / 60} min)");

            return duration;
        }

        /// <summary>
        /// Extrae el recorrido completo (lista de nodos/paradas) del primer tripPattern.
        /// </summary>
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

            Console.WriteLine($"[OTP] ExtraerRecorrido: {nodos.Count} nodos extraídos.");
            return nodos;
        }

        // ── MÉTODOS PRIVADOS ─────────────────────────────────────────────────

        private static void AgregarNodoSiNoExiste(List<NodoRuta> nodos, NodoRuta? nodo)
        {
            if (nodo == null) return;
            if (!nodos.Any(n => !string.IsNullOrWhiteSpace(n.Id) && n.Id == nodo.Id))
                nodos.Add(nodo);
        }

        private static NodoRuta? ExtraerNodoDePlace(JsonElement leg, string placeKey)
        {
            if (!leg.TryGetProperty(placeKey, out JsonElement place)) return null;
            if (!place.TryGetProperty("quay", out JsonElement quay) ||
                quay.ValueKind == JsonValueKind.Null) return null;
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