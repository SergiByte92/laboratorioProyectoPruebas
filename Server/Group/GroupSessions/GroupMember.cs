using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Group.GroupSessions
{
    public sealed class GroupMember
    {
        public int UserId { get; }
        public string Username { get; }

        public GroupMember(int userId, string username)
        {
            UserId = userId;
            Username = username;
        }
    }
}
