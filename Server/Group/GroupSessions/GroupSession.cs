using System.Collections.Concurrent;
using Server.UserRouting;

namespace Server.Group.GroupSessions
{
    /// <summary>
    /// Mantiene el estado activo de un grupo en memoria.
    /// 
    /// Responsabilidades:
    /// - miembros activos,
    /// - owner,
    /// - estado iniciado/no iniciado,
    /// - ubicaciones recibidas.
    /// 
    /// Nota: esta clase no almacena resultados de rutas. Cada usuario calcula
    /// su propia ruta hacia el centroide común.
    /// </summary>
    public sealed class GroupSession
    {
        private readonly object _stateLock = new();
        private readonly ConcurrentDictionary<int, GroupMember> _members = new();
        private readonly ConcurrentDictionary<int, UserLocation> _locations = new();

        public int GroupId { get; }
        public string GroupCode { get; }
        public int OwnerUserId { get; }
        public bool HasStarted { get; private set; }

        public int MemberCount => _members.Count;
        public int LocationCount => _locations.Count;

        public GroupSession(int groupId, string groupCode, int ownerUserId)
        {
            GroupId = groupId;
            GroupCode = groupCode;
            OwnerUserId = ownerUserId;
            HasStarted = false;
        }

        public bool AddMember(int userId, string username)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(username))
                return false;

            lock (_stateLock)
            {
                if (HasStarted)
                    return false;

                return _members.TryAdd(userId, new GroupMember(userId, username));
            }
        }

        public GroupMember? GetMember(int userId)
        {
            _members.TryGetValue(userId, out var member);
            return member;
        }

        public bool RemoveMember(int userId)
        {
            _locations.TryRemove(userId, out _);
            return _members.TryRemove(userId, out _);
        }

        public bool Start()
        {
            lock (_stateLock)
            {
                if (HasStarted)
                    return false;

                if (MemberCount == 0)
                    return false;

                HasStarted = true;
                return true;
            }
        }

        public bool AddOrUpdateLocation(UserLocation location)
        {
            if (location.UserId <= 0)
                return false;

            lock (_stateLock)
            {
                if (!HasStarted)
                    return false;

                if (!_members.ContainsKey(location.UserId))
                    return false;

                _locations[location.UserId] = location;
                return true;
            }
        }

        public UserLocation? GetLocation(int userId)
        {
            _locations.TryGetValue(userId, out var location);
            return location;
        }

        public IReadOnlyCollection<UserLocation> GetAllLocations()
        {
            return _locations.Values.ToList().AsReadOnly();
        }

        public bool AreAllLocationsReceived()
        {
            return MemberCount > 0 && LocationCount == MemberCount;
        }
    }
}
