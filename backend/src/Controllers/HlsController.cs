using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Namorix.Core.Middleware;
using Namorix.Core.Responses;
using Namorix.Scout.Constants;
using Namorix.Scout.Services;
using Namorix.Scout.Streaming;

namespace Namorix.Scout.Controllers;

// The remote-viewing path: the same H.264 the relay already carries, muxed into fMP4 segments a
// browser can play over plain HTTPS. Everything here is a plain GET so the browser fetches it
// like any other asset, cookies and all - no WebRTC, no UDP, nothing for a NAT to block.
[ApiController]
[RequireAuth]
[Route("api/cameras")]
public sealed class HlsController(HlsPackagerRegistry packagers, CameraService cameras) : ControllerBase
{
    private const string PlaylistContentType = "application/vnd.apple.mpegurl";

    // RFC 8216 wants the target duration to cover every segment in the playlist, and a camera
    // running at 29.97fps cannot land its cuts on exactly SegmentMilliseconds. The extra second
    // is slack for that; it also slows the player's reload cadence, which is the reason not to
    // round it up further.
    private const int TargetDurationSeconds = HlsPackager.SegmentMilliseconds / 1000 + 1;

    private int CurrentUserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("{id:guid}/live.m3u8")]
    public async Task<IActionResult> Playlist(Guid id, CancellationToken ct)
    {
        // Access is settled before the packager is reached: a camera shared with the caller
        // opens like their own, and anything else reads as missing, never as forbidden.
        if (await cameras.GetAsync(id, CurrentUserId, ct) is null)
            return NotFound(ApiResponse.Fail(Error.CameraNotFound));

        var packager = packagers.Acquire(id);
        if (packager is null)
            return Conflict(ApiResponse.Fail(Error.CameraOffline));

        // The window slides on its own, so a cached playlist is a player pinned to a segment
        // that has already been dropped from the buffer.
        Response.Headers.CacheControl = "no-store";
        return Content(BuildPlaylist(packager), PlaylistContentType);
    }

    [HttpGet("{id:guid}/init.mp4")]
    public async Task<IActionResult> Initialization(Guid id, CancellationToken ct)
    {
        if (await cameras.GetAsync(id, CurrentUserId, ct) is null)
            return NotFound(ApiResponse.Fail(Error.CameraNotFound));

        var packager = packagers.Acquire(id);
        // Nothing has been muxed yet, which for a live source means the camera has not sent a
        // frame - the same condition as the playlist arriving empty.
        var init = packager?.InitSegment;
        if (init is null)
            return NotFound(ApiResponse.Fail(Error.CameraOffline));

        Response.Headers.CacheControl = "no-store";
        return File(init, "video/mp4");
    }

    [HttpGet("{id:guid}/seg{sequence:int}.m4s")]
    public async Task<IActionResult> Segment(Guid id, int sequence, CancellationToken ct)
    {
        if (await cameras.GetAsync(id, CurrentUserId, ct) is null)
            return NotFound(ApiResponse.Fail(Error.CameraNotFound));

        var packager = packagers.Acquire(id);
        var segment = packager?.Segments.FirstOrDefault(candidate => candidate.Sequence == sequence);
        // A player that fell behind the window asks for a segment already dropped: it re-reads
        // the playlist and carries on, so a miss is not worth keeping the segment around for.
        if (segment is null)
            return NotFound(ApiResponse.Fail(Error.StreamNotFound));

        // Numbered URI, but not immutable: sequence numbers start over whenever the packager is
        // rebuilt, so the same name can later describe different video.
        Response.Headers.CacheControl = "no-store";
        return File(segment.Data, "video/mp4");
    }

    private static string BuildPlaylist(HlsPackager packager)
    {
        var segments = packager.Segments;
        var playlist = new StringBuilder();
        playlist.Append("#EXTM3U\n");
        // Version 7 is the floor for the EXT-X-MAP an fMP4 stream needs.
        playlist.Append("#EXT-X-VERSION:7\n");
        playlist.Append(CultureInfo.InvariantCulture, $"#EXT-X-TARGETDURATION:{TargetDurationSeconds}\n");
        playlist.Append(CultureInfo.InvariantCulture,
            $"#EXT-X-MEDIA-SEQUENCE:{(segments.Count > 0 ? segments[0].Sequence : 0)}\n");
        // Relative to this playlist, so the player asks for it on the route above.
        playlist.Append("#EXT-X-MAP:URI=\"init.mp4\"\n");

        foreach (var segment in segments)
        {
            // Nominal, because the packager only cuts on elapsed time - nothing downstream of it
            // can read a real duration back out of a fragment.
            playlist.Append(CultureInfo.InvariantCulture,
                $"#EXTINF:{HlsPackager.SegmentMilliseconds / 1000.0:F3},\n");
            playlist.Append(CultureInfo.InvariantCulture, $"seg{segment.Sequence}.m4s\n");
        }

        return playlist.ToString();
    }
}
