using System.Net.Sockets;
using NetUtils;
using Server.Data;
using Server.Group.GroupSessions;
using Server.UserRouting;

namespace Server.Group
{
    /// <summary>
    /// Recibe las coordenadas enviadas por un usuario y las almacena en la sesión.
    /// No lanza excepción ante datos inválidos: devuelve un resultado controlado
    /// para que LobbyHandler pueda responder con JSON y mantener sincronizado el protocolo.
    /// </summary>
    internal sealed class ReceiveLocationService
    {
        public ReceiveLocationResult Execute(Socket socket, GroupSession session, AppDbContext.User currentUser)
        {
            double latitude = SocketTools.receiveDouble(socket);
            double longitude = SocketTools.receiveDouble(socket);

            if (!IsValidCoordinate(latitude, longitude))
            {
                return ReceiveLocationResult.Fail("Las coordenadas recibidas no son válidas.");
            }

            if (session.GetMember(currentUser.id) is null)
            {
                return ReceiveLocationResult.Fail("El usuario no pertenece a la sesión actual.");
            }

            UserLocation location = new UserLocation(currentUser.id, latitude, longitude);

            bool stored = session.AddOrUpdateLocation(location);

            if (!stored)
            {
                return ReceiveLocationResult.Fail("No se pudo registrar la ubicación en la sesión actual.");
            }

            Console.WriteLine($"[INFO] Ubicación recibida de user {currentUser.id}: {latitude}, {longitude}");
            Console.WriteLine($"[INFO] Progreso ubicaciones: {session.LocationCount}/{session.MemberCount}");

            return ReceiveLocationResult.Ok(session.AreAllLocationsReceived());
        }

        private static bool IsValidCoordinate(double latitude, double longitude)
        {
            return !double.IsNaN(latitude)
                && !double.IsInfinity(latitude)
                && !double.IsNaN(longitude)
                && !double.IsInfinity(longitude)
                && latitude >= -90
                && latitude <= 90
                && longitude >= -180
                && longitude <= 180;
        }
    }

    internal sealed record ReceiveLocationResult(
        bool Success,
        bool AllLocationsReceived,
        string? ErrorMessage)
    {
        public static ReceiveLocationResult Ok(bool allLocationsReceived)
        {
            return new ReceiveLocationResult(true, allLocationsReceived, null);
        }

        public static ReceiveLocationResult Fail(string errorMessage)
        {
            return new ReceiveLocationResult(false, false, errorMessage);
        }
    }
}
