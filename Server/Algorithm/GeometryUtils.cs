using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Algorithm
{
    internal class GeometryUtils
    {
        public struct GeographicLocation
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }

            // Un constructor opcional para crear puntos rápido
            public GeographicLocation(double lat, double lon)
            {
                Latitude = lat;
                Longitude = lon;
            }
        }
        public static GeographicLocation CalculateCentroid(List<GeographicLocation> locations)
        {
            double sumLat = 0;
            double sumLon = 0;
            int n = locations.Count;

            foreach (var loc in locations)
            {
                sumLat += loc.Latitude;
                sumLon += loc.Longitude;
            }

            return new GeographicLocation(sumLat / n, sumLon / n);
        }
    }
}
