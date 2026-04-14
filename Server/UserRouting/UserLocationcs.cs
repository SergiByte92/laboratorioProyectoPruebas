using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.UserRouting
{
    /// <summary>
    /// Representa la ubicación enviada por un usuario para el cálculo del punto de encuentro.
    /// </summary>
    public sealed class UserLocation  // de esta manera impido que se pueda heredar 
    {
        public int UserId { get; }
        public double Latitude { get; }
        public double Longitude { get; }

        public UserLocation(int userId, double latitude, double longitude)
        {
            UserId = userId;
            Latitude = latitude;
            Longitude = longitude;
        }
    }
}
