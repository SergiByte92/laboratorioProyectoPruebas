using System.Collections.Generic;

namespace Server.Group.GroupSessions
{
    public sealed class GroupSession
    {
        public int GroupId { get; }
        public string GroupCode { get; }
        public int OwnerUserId { get; }

        private readonly Dictionary<int, MemberSubmission> _members = new();

        public IReadOnlyCollection<MemberSubmission> Members => _members.Values;

        public GroupSession(int groupId, string groupCode, int ownerUserId)
        {
            GroupId = groupId;
            GroupCode = groupCode;
            OwnerUserId = ownerUserId;
        }

        public void AddMember(int userId, string username)
        {
            if (!_members.ContainsKey(userId))
            {
                _members[userId] = new MemberSubmission(userId, username);
            }
        }

        public MemberSubmission? GetMember(int userId)
        {
            _members.TryGetValue(userId, out var member);
            return member;
        }
    }
}