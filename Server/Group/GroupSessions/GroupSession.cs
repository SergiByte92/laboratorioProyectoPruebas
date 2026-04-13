using System.Collections.Concurrent;
using Server.UserRouting;

namespace Server.Group.GroupSessions
{
    public sealed class GroupSession
    {
        public int GroupId { get; }
        public string GroupCode { get; }
        public int OwnerUserId { get; }

        private readonly ConcurrentDictionary<int, GroupMember> _members = new();
        private readonly ConcurrentDictionary<int, UserLocation> _locations = new();

        public int MemberCount => _members.Count;
        public int LocationCount => _locations.Count;

        public GroupSession(int groupId, string groupCode, int ownerUserId)
        {
            GroupId = groupId;
            GroupCode = groupCode;
            OwnerUserId = ownerUserId;
        }

        public void AddMember(int userId, string username)
        {
            _members.TryAdd(userId, new GroupMember(userId, username));
        }

        public GroupMember? GetMember(int userId)
        {
            _members.TryGetValue(userId, out var member);
            return member;
        }

        public bool RemoveMember(int userId)
        {
            _locations.TryRemove(userId, out _); // si sale, también quitamos su ubicación
            return _members.TryRemove(userId, out _);
        }

        public void AddOrUpdateLocation(UserLocation location)
        {
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