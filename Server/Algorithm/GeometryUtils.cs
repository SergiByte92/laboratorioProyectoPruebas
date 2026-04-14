namespace Server.Algorithm
{
    /// <summary>
    /// Contiene operaciones geométricas reutilizables para trabajar con coordenadas
    /// y apoyar el cálculo del punto óptimo.
    /// </summary>
    internal static class GeometryUtils
    {
        public readonly struct GeographicLocation
        {
            public double Latitude { get; }
            public double Longitude { get; }

            public GeographicLocation(double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
            }
        }

        public static GeographicLocation CalculateCentroid(IEnumerable<GeographicLocation> locations)
        {
            var points = locations.ToList();

            if (points.Count == 0)
                throw new InvalidOperationException("No se puede calcular el centroide sin ubicaciones.");

            double sumLat = 0;
            double sumLon = 0;

            foreach (var point in points)
            {
                sumLat += point.Latitude;
                sumLon += point.Longitude;
            }

            return new GeographicLocation(sumLat / points.Count, sumLon / points.Count);
        }
    }
}