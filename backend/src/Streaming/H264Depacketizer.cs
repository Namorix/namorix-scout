namespace Namorix.Scout.Streaming;

internal sealed class H264Depacketizer(Action<VideoFrame> onFrame)
{
    private readonly List<byte[]> _nals = [];
    private readonly List<byte> _fu = [];

    public void Reset()
    {
        _nals.Clear();
        _fu.Clear();
    }

    public void ParseRtp(ReadOnlySpan<byte> rtp)
    {
        if (rtp.Length < 12)
            return;

        var marker = (rtp[1] & 0x80) != 0;
        var timestamp = (uint)((rtp[4] << 24) | (rtp[5] << 16) | (rtp[6] << 8) | rtp[7]);

        var csrcCount = rtp[0] & 0x0F;
        var offset = 12 + csrcCount * 4;
        if ((rtp[0] & 0x10) != 0)
        {
            if (rtp.Length < offset + 4)
                return;
            offset += 4 + (((rtp[offset + 2] << 8) | rtp[offset + 3]) * 4);
        }

        if (rtp.Length <= offset)
            return;

        ParsePayload(rtp[offset..]);

        if (!marker || _nals.Count == 0)
            return;

        var nals = _nals.ToArray();
        _nals.Clear();
        onFrame(new VideoFrame
        {
            RtpTimestamp = timestamp,
            IsKeyFrame = nals.Any(n => (n[0] & 0x1F) == 5),
            Nals = nals,
            Bytes = nals.Sum(n => n.Length),
        });
    }

    private void ParsePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            return;

        var nalType = payload[0] & 0x1F;
        switch (nalType)
        {
            case >= 1 and <= 23:
                AddNal([.. payload]);
                break;
            case 24:
            {
                var i = 1;
                while (i + 2 <= payload.Length)
                {
                    var size = (payload[i] << 8) | payload[i + 1];
                    i += 2;
                    if (size <= 0 || i + size > payload.Length)
                        break;
                    AddNal([.. payload.Slice(i, size)]);
                    i += size;
                }

                break;
            }
            case 28 or 29:
            {
                if (payload.Length < 2)
                    return;

                var fuHeader = payload[1];
                var isStart = (fuHeader & 0x80) != 0;
                var isEnd = (fuHeader & 0x40) != 0;

                if (isStart)
                {
                    _fu.Clear();
                    _fu.Add((byte)((payload[0] & 0xE0) | (fuHeader & 0x1F)));
                }
                else if (_fu.Count == 0)
                {
                    return;
                }

                foreach (var b in payload[2..])
                    _fu.Add(b);

                if (isEnd)
                {
                    AddNal([.. _fu]);
                    _fu.Clear();
                }

                break;
            }
        }
    }

    private void AddNal(byte[] nal)
    {
        if (nal.Length > 0)
            _nals.Add(nal);
    }
}
