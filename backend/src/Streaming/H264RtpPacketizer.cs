namespace Namorix.Scout.Streaming;

internal static class H264RtpPacketizer
{
    public const int MaxPayloadBytes = 1200;

    public static void PacketizeNal(ReadOnlySpan<byte> nal, int maxPayloadBytes, List<byte[]> packets)
    {
        if (nal.IsEmpty)
            return;

        if (nal.Length <= maxPayloadBytes)
        {
            packets.Add([.. nal]);
            return;
        }

        var nalHeader = nal[0];
        var nalType = nalHeader & 0x1F;
        if (nalType is 0 or 24 or 25 or 26 or 27 or 28 or 29 or 30 or 31)
            return;

        var fuIndicator = (byte)((nalHeader & 0xE0) | 28);
        var payload = nal[1..];
        var offset = 0;
        var first = true;

        while (offset < payload.Length)
        {
            var chunkLength = Math.Min(payload.Length - offset, maxPayloadBytes - 2);
            var end = offset + chunkLength >= payload.Length;
            var fuHeader = (byte)((first ? 0x80 : 0) | (end ? 0x40 : 0) | nalType);

            var packet = new byte[2 + chunkLength];
            packet[0] = fuIndicator;
            packet[1] = fuHeader;
            payload.Slice(offset, chunkLength).CopyTo(packet.AsSpan(2));
            packets.Add(packet);

            offset += chunkLength;
            first = false;
        }
    }
}
