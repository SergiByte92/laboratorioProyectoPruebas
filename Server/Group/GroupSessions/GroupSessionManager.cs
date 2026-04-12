using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Server.Group.GroupSessions
{
    public class GroupSessionManager
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


    }
}
