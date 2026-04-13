namespace Server.UserRouting
{
    internal class UserRouteResult
    {
        public string UserId { get; set; }
        public double StartLatitud { get; set; }
        public double StartLongitud { get; set; }
        public double DestinationLatitud { get; set; }
        public double DestinationLongitud { get; set; }
        public int DurationSeconds { get; set; }
        public double DistanceMeters { get; set; }

        public UserRouteResult(
            string userId,
            double startLatitud,
            double startLongitud,
            double destinationLatitud,
            double destinationLongitud,
            int durationSeconds,
            double distanceMeters)
        {
            UserId = userId;
            StartLatitud = startLatitud;
            StartLongitud = startLongitud;
            DestinationLatitud = destinationLatitud;
            DestinationLongitud = destinationLongitud;
            DurationSeconds = durationSeconds;
            DistanceMeters = distanceMeters;
        }
    }
}