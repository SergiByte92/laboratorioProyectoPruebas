using System.Net.Sockets;
using NetUtils;
using Server.Data;
using Server.Group.GroupSessions;
using Server.UserRouting;

namespace Server.Group
{
    /// <summary>
    /// Recibe las coordenadas enviadas por un usuario y las almacena en la sesión
    /// del grupo correspondiente para su posterior procesamiento.
    /// </summary>
    internal sealed class ReceiveLocationService
    {
        public bool Execute(Socket socket, GroupSession session, AppDbContext.User currentUser)
        {
            double latitude = SocketTools.receiveDouble(socket);
            double longitude = SocketTools.receiveDouble(socket);

            UserLocation location = new UserLocation(currentUser.id, latitude, longitude);

            session.AddOrUpdateLocation(location);

            Console.WriteLine($"[INFO] Ubicación recibida de user {currentUser.id}: {latitude}, {longitude}");
            Console.WriteLine($"[INFO] Progreso ubicaciones: {session.LocationCount}/{session.MemberCount}");

            return session.AreAllLocationsReceived();
        }
    }
}