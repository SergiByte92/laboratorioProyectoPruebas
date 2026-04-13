using System;
using System.Collections.Concurrent;

namespace Server.Group.GroupSessions // Si peta, el servidor debería al encenderse cargar grupos activos en memoria
{
    public class GroupSessionManager // Supongo que tocara usar locks, pendiente de implementar
    {
        // Diccionario thread-safe:
        // key   = código del grupo
        // value = objeto GroupSession (la sesión activa en memoria)
        private readonly ConcurrentDictionary<string, GroupSession> _activeGroups = new();

        // Añade una sesión al diccionario
        public void Add(GroupSession session)
        {
            // Guarda la sesión en memoria usando el GroupCode como clave . De esta manera se pasa la referencia.
            _activeGroups[session.GroupCode] = session;
        }

        // Obtiene una sesión por su código
        public GroupSession? Get(string code)
        {
            // Si existe, la deja en la variable session
            _activeGroups.TryGetValue(code, out var session);
            return session;
        }

        // Intenta unir un usuario a un grupo existente aaa
        public bool TryJoinGroup(string groupCode, int userId, string username)
        {
            // Busca la sesión por código
            if (!_activeGroups.TryGetValue(groupCode, out var session))
                return false;

            // Si existe, añade el miembro al grupo
            session.AddMember(userId, username);
            return true;
        }

        // Elimina una sesión del diccionario cuando ya no debe estar activa
        public bool Remove(string groupCode)
        {
            // TryRemove elimina la entrada y además devuelve el objeto eliminado
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