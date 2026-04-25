using Server.API;
using Server.Group.GroupSessions;
using Server.UserRouting;
using static Server.Data.AppDbContext;

namespace Server.Application.Services;

public interface IMeetingRouteService
{
    Task<MeetingResultTransportModel> CalculateForUserAsync(
        GroupSession session,
        User user);
}