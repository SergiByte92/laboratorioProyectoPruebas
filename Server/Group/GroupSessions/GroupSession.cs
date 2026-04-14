using System.Collections.Concurrent;
using Server.UserRouting;

namespace Server.Group.GroupSessions
{
    /// <summary>
    /// Mantiene el estado activo de un grupo en memoria, incluyendo miembros,
    /// ubicaciones recibidas y control del ciclo de vida de la sesión.
    /// </summary>
    public sealed class GroupSession
    {
        public int GroupId { get; }
        public string GroupCode { get; }
        public int OwnerUserId { get; }
        public bool HasStarted { get; private set; }

        private readonly ConcurrentDictionary<int, GroupMember> _members = new();
        private readonly ConcurrentDictionary<int, UserLocation> _locations = new();

        public int MemberCount => _members.Count;
        public int LocationCount => _locations.Count;

        public GroupSession(int groupId, string groupCode, int ownerUserId)
        {
            GroupId = groupId;
            GroupCode = groupCode;
            OwnerUserId = ownerUserId;
            HasStarted = false;
        }

        public void AddMember(int userId, string username)
        {
            if (HasStarted)
                return;

            _members.TryAdd(userId, new GroupMember(userId, username));
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

        public bool Start() // Si ya esta iniciado, no se puede iniciar más veces
        {
            if (HasStarted)
                return false;

            HasStarted = true;
            return true;
        }

        public void AddOrUpdateLocation(UserLocation location)
        {
            if (!HasStarted)
                return;

            _locations[location.UserId] = location;
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