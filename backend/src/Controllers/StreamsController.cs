using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Namorix.Core.Middleware;
using Namorix.Core.Responses;
using Namorix.Scout.Constants;
using Namorix.Scout.Dtos;
using Namorix.Scout.Services;
using Namorix.Scout.Streaming;

namespace Namorix.Scout.Controllers;

[ApiController]
[RequireAuth]
[Route("api/streams")]
public sealed class StreamsController(WebRtcRelayService relay, CameraService cameras) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpPost("{cameraId:guid}/offer")]
    public async Task<IActionResult> Offer(Guid cameraId, CancellationToken ct)
    {
        // The relay is addressed by camera id alone, so access is settled before it is reached:
        // a camera shared with the caller opens like their own, and anything else reads as
        // missing — the same answer as an unknown id.
        if (await cameras.GetAsync(cameraId, CurrentUserId, ct) is null)
            return NotFound(ApiResponse.Fail(Error.CameraNotFound));

        RtcOffer? offer;
        try
        {
            offer = await relay.CreateAsync(cameraId, ct);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail(Error.StreamOfferFailed, ex.Message));
        }

        return offer is null
            ? Conflict(ApiResponse.Fail(Error.CameraOffline))
            : Ok(ApiResponse.Ok(offer));
    }

    [HttpPost("{sessionId:guid}/answer")]
    public IActionResult Answer(Guid sessionId, [FromBody] StreamAnswerRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Sdp))
            return BadRequest(ApiResponse.Fail(Error.StreamAnswerFailed));

        if (relay.Find(sessionId) is null)
            return NotFound(ApiResponse.Fail(Error.StreamNotFound));

        try
        {
            return relay.SetRemoteAnswer(sessionId, request.Sdp)
                ? Ok(ApiResponse.Ok())
                : BadRequest(ApiResponse.Fail(Error.StreamAnswerFailed));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail(Error.StreamAnswerFailed, ex.Message));
        }
    }

    [HttpPost("{sessionId:guid}/ice")]
    public IActionResult AddIce(Guid sessionId, [FromBody] StreamIceRequest? request)
    {
        if (relay.Find(sessionId) is null)
            return NotFound(ApiResponse.Fail(Error.StreamNotFound));

        if (request is null || string.IsNullOrWhiteSpace(request.Candidate))
            return BadRequest(ApiResponse.Fail(Error.InvalidStreamInput));

        relay.AddRemoteIce(sessionId, request.Candidate);
        return Ok(ApiResponse.Ok());
    }

    [HttpGet("{sessionId:guid}/ice")]
    public IActionResult Ice(Guid sessionId)
    {
        if (relay.Find(sessionId) is null)
            return NotFound(ApiResponse.Fail(Error.StreamNotFound));

        return Ok(ApiResponse.Ok(new StreamIceResult(relay.DrainIce(sessionId))));
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Stop(Guid sessionId)
    {
        await relay.StopAsync(sessionId);
        return Ok(ApiResponse.Ok());
    }
}
