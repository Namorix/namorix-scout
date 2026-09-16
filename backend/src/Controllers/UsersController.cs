using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Namorix.Core.Grpc;
using Namorix.Core.Middleware;
using Namorix.Core.Responses;
using Namorix.Scout.Constants;
using Namorix.Scout.Dtos;

namespace Namorix.Scout.Controllers;

[ApiController]
[RequireAuth]
[Route("api/users")]
public sealed class UsersController(AddonChannelClient channel, ILogger<UsersController> logger)
    : ControllerBase
{
    // The desktop clamps to its own cap, so asking for this many is safe; it exists to say the
    // addon wants the whole page rather than the desktop's smaller default.
    private const int UserListLimit = 200;

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // The desktop's user table, proxied for the share picker. Any signed-in user of this addon
    // may read it — sharing a camera means naming someone, and the addon has no user table of
    // its own. Nothing here is per-camera, so there is no owner to scope it to.
    //
    // One page, and no narrowing on this side: the picker shows every name at once instead of
    // making the caller guess a substring. A desktop with more users than fits one page is a
    // case to revisit, not one this handles.
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var callerId = (long)CurrentUserId;

        try
        {
            var response = await channel.SearchUsersAsync(
                string.Empty, limit: UserListLimit, ct: ct);
            // The caller is dropped because the only consumer hands this to a share picker, and
            // granting yourself access to something you already own is the one answer the
            // server refuses outright (CameraService.AddShareAsync). The desktop stays generic
            // about who exists; the "who is worth offering" call belongs here.
            var users = response.Users
                .Where(u => u.UserId != callerId)
                .Select(u => new ScoutUserDto((int)u.UserId, u.Username, u.Name))
                .ToArray();
            return Ok(ApiResponse.Ok(users));
        }
        catch (Exception ex)
        {
            // The desktop being down is not this addon's failure to report as a bad request:
            // 503 says "try again", which is the only useful answer here.
            logger.LogWarning(ex, "Listing users from the desktop failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse.Fail(Error.DesktopUnreachable));
        }
    }
}
