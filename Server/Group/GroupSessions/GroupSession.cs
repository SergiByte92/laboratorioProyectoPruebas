using System.Collections.Concurrent;

namespace Server.Group.GroupSessions
{
    public sealed class GroupSession
    {
        public int GroupId { get; }
        public string GroupCode { get; }
        public int OwnerUserId { get; }

        private readonly ConcurrentDictionary<int, MemberSubmission> _members = new();

        public int MemberCount => _members.Count;

        public GroupSession(int groupId, string groupCode, int ownerUserId)
        {
            GroupId = groupId;
            GroupCode = groupCode;
            OwnerUserId = ownerUserId;
        }

        public void AddMember(int userId, string username)
        {
            _members.TryAdd(userId, new MemberSubmission(userId, username));
        }

        public MemberSubmission? GetMember(int userId)
        {
            _members.TryGetValue(userId, out var member);
            return member;
        }

        public bool RemoveMember(int userId)
        {
            return _members.TryRemove(userId, out _);
        }
    }
}