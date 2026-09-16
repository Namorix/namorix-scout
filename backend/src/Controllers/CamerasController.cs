using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Namorix.Core.Middleware;
using Namorix.Core.Responses;
using Namorix.Scout.Constants;
using Namorix.Scout.Dtos;
using Namorix.Scout.Services;

namespace Namorix.Scout.Controllers;

[ApiController]
[RequireAuth]
[Route("api/cameras")]
public sealed class CamerasController(CameraService cameras) : ControllerBase
{
    // Every camera route is scoped to the caller. A camera owned by someone else reads as
    // missing rather than forbidden, so the answers do not reveal that the row exists.
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse.Ok(await cameras.ListAsync(CurrentUserId, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var camera = await cameras.GetAsync(id, CurrentUserId, ct);
        return camera is null
            ? NotFound(ApiResponse.Fail(Error.CameraNotFound))
            : Ok(ApiResponse.Ok(camera));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CameraUpsertRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(ApiResponse.Fail(Error.InvalidCameraInput));

        try
        {
            return Ok(ApiResponse.Ok(await cameras.CreateAsync(CurrentUserId, request, ct)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail(Error.InvalidCameraInput, ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] CameraUpsertRequest? request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(ApiResponse.Fail(Error.InvalidCameraInput));

        ScCameraDto? camera;
        try
        {
            camera = await cameras.UpdateAsync(id, CurrentUserId, request, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail(Error.InvalidCameraInput, ex.Message));
        }

        return camera is null
            ? NotFound(ApiResponse.Fail(Error.CameraNotFound))
            : Ok(ApiResponse.Ok(camera));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await cameras.DeleteAsync(id, CurrentUserId, ct)
            ? Ok(ApiResponse.Ok())
            : NotFound(ApiResponse.Fail(Error.CameraNotFound));
}
