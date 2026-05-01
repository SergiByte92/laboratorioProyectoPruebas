using System.Collections.Concurrent;

namespace Server.Group.GroupSessions
{
    /// <summary>
    /// Gestiona las sesiones de grupo activas en memoria.
    /// La búsqueda por código usa ConcurrentDictionary para soportar varios clientes.
    /// </summary>
    public class GroupSessionManager
    {
        private readonly ConcurrentDictionary<string, GroupSession> _activeGroups = new();

        public void Add(GroupSession session)
        {
            _activeGroups[session.GroupCode] = session;
        }

        public GroupSession? Get(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            string normalizedCode = NormalizeGroupCode(code);
            _activeGroups.TryGetValue(normalizedCode, out var session);
            return session;
        }

        public bool TryJoinGroup(string groupCode, int userId, string username)
        {
            if (string.IsNullOrWhiteSpace(groupCode))
                return false;

            string normalizedCode = NormalizeGroupCode(groupCode);

            if (!_activeGroups.TryGetValue(normalizedCode, out var session))
                return false;

            return session.AddMember(userId, username);
        }

        public bool Remove(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode))
                return false;

            string normalizedCode = NormalizeGroupCode(groupCode);

            if (_activeGroups.TryRemove(normalizedCode, out _))
            {
                Console.WriteLine($"[INFO] Sesión eliminada de memoria: {normalizedCode}");
                return true;
            }

            Console.WriteLine($"[WARN] No se encontró la sesión: {normalizedCode}");
            return false;
        }

        private static string NormalizeGroupCode(string code)
        {
            return code.Trim().ToUpperInvariant();
        }
    }
}
