using System;
using System.Collections.Concurrent;
using Server.UserRouting;

namespace Server.Group.GroupSessions // Si peta, el servidor debería al encenderse cargar grupos activos en memoria
{
    public class GroupSessionManager // Supongo que tocara usar locks, pendiente de implementar
    {

        private readonly ConcurrentDictionary<string, GroupSession> _activeGroups = new();

        public void Add(GroupSession session)
        {

            _activeGroups[session.GroupCode] = session;
        }

        public GroupSession? Get(string code)
        {

            _activeGroups.TryGetValue(code, out var session);
            return session;
        }

        public bool TryJoinGroup(string groupCode, int userId, string username)
        {

            if (!_activeGroups.TryGetValue(groupCode, out var session))
                return false;

            session.AddMember(userId, username);
            return true;
        }

        public bool Remove(string groupCode)
        {
            if (_activeGroups.TryRemove(groupCode, out var session)) // lo bueno del out es que me permite usar posteriormente esa variable
            {
                Console.WriteLine($"[INFO] Sesión eliminada de memoria: {groupCode}");

                // OJO:
                // Aquí NO cerramos sockets, porque los estás manejando con hilos en otro sitio.
                // Tampoco hace falta Dispose si GroupSession no tiene recursos IDisposable propios.

                return true;
            }

            Console.WriteLine($"[WARN] No se encontró la sesión: {groupCode}");
            // Quizás aqui deberia actualizarse la base de datos a que no esta activo el grupo
            return false;
        }
    }
}