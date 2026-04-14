using Microsoft.EntityFrameworkCore;
using NetUtils;
using Server.Data;
using Server.Group.GroupCode;
using System.Net.Sockets;
using static Server.Data.AppDbContext;

namespace Server.Group
{
    /// <summary>
    /// Orquesta la creación de un grupo a partir de los datos recibidos del cliente,
    /// validando la entrada, generando un código único y persistiendo la información
    /// necesaria en la base de datos.
    /// </summary>
    internal class CreateGroupService
    {
        private readonly AppDbContext _context;

        public CreateGroupService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(bool Success, int GroupId, string GroupCode)> ExecuteAsync(Socket socket, User currentUser)
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
                return (false, 0, string.Empty);
            }

            string groupCode = await GroupCodeGenerator.CreateUniqueGroupCode(_context);

            using var transaction = await _context.Database.BeginTransactionAsync();

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
                await _context.SaveChangesAsync();

                var userInGroup = new AppDbContext.UserGroup
                {
                    userId = currentUser.id,
                    groupId = groupAdd.id
                };

                _context.UsersGroups.Add(userInGroup);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                SocketTools.sendBool(socket, true);
                SocketTools.sendString(groupCode, socket);

                return (true, groupAdd.id, groupCode);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                SocketTools.sendBool(socket, false);

                Console.WriteLine("Error creando grupo:");
                Console.WriteLine(ex);

                return (false, 0, string.Empty);
            }
        }
    }
}