using Microsoft.EntityFrameworkCore;
using NetUtils;
using Server.Data;
using Server.Group.GroupCode;
using System.Net.Sockets;
using static Server.Data.AppDbContext;

namespace Server.Group
{
    internal class CreateGroupService
    {
        private readonly AppDbContext _context;

        public CreateGroupService(AppDbContext context)
        {
            _context = context;
        }

        public async Task ExecuteAsync(Socket socket, User currentUser)
        {
            string groupName = SocketTools.receiveString(socket);
            string groupLabel = SocketTools.receiveString(socket);
            string groupDescription = SocketTools.receiveString(socket);
            string groupMethod = SocketTools.receiveString(socket);

            if (string.IsNullOrWhiteSpace(groupName) ||
                string.IsNullOrWhiteSpace(groupLabel) ||
                string.IsNullOrWhiteSpace(groupMethod))
            {
                SocketTools.sendBool(socket, false);
                return;
            }

            string groupCode = await GroupCodeGenerator.CreateUniqueGroupCode(_context);

            using var transaction = _context.Database.BeginTransaction();

            try
            {
                var groupAdd = new AppDbContext.Group
                {
                    code = groupCode,
                    name = groupName,
                    label = groupLabel,
                    description = groupDescription,
                    method = groupMethod,
                    userId = currentUser.id,
                    isActive = true,
                    created_at = DateTime.UtcNow
                };

                _context.Groups.Add(groupAdd);
                _context.SaveChanges();

                var userInGroup = new AppDbContext.UserGroup
                {
                    userId = currentUser.id,
                    groupId = groupAdd.id
                };

                _context.UsersGroups.Add(userInGroup);
                _context.SaveChanges();

                transaction.Commit();

                SocketTools.sendBool(socket, true);
                SocketTools.sendString(groupCode, socket);
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                SocketTools.sendBool(socket, false);
                Console.WriteLine("Error creando grupo:");
                Console.WriteLine(ex);
            }
        }
    }
}