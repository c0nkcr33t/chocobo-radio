using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ChocoboRadio;

internal sealed class OggPacketReader(OggPageReader pages)
{
    private readonly MemoryStream pending = new();
    private OggPage? page;
    private int segmentIndex;
    private int bodyOffset;
    private bool packetContinues;
    private bool packetBeginsStream;
    private uint packetSerial;
    private OggPacket? pushedBack;

    public async ValueTask<OggPacket?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (pushedBack != null)
        {
            var packet = pushedBack;
            pushedBack = null;
            return packet;
        }

        while (true)
        {
            // A previous call may have returned a packet before consuming every
            // segment in its page. Only request another page after all segments
            // in the current one have been consumed.
            if (page == null || segmentIndex == page.LacingValues.Length)
            {
                var nextPage = await pages.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (nextPage == null)
                {
                    if (packetContinues)
                        throw new EndOfStreamException("Ogg stream ended partway through a packet.");

                    return null;
                }

                // Header flag 0x01 says this page begins with the remainder of
                // a packet from the preceding page. It must agree with the
                // state established by the last lacing value we processed.
                if (nextPage.IsContinuation != packetContinues)
                    throw new InvalidDataException("Invalid Ogg packet continuation flag.");

                if (packetContinues && nextPage.SerialNumber != packetSerial)
                    throw new InvalidDataException("An Ogg packet changed logical stream serial number.");

                page = nextPage;
                segmentIndex = 0;
                bodyOffset = 0;
            }

            // This is the start of a new packet. Remember page-level facts now
            // because the packet might continue onto another page.
            if (!packetContinues)
            {
                packetSerial = page.SerialNumber;
                packetBeginsStream = page.IsBeginningOfStream && segmentIndex == 0;
            }

            var segmentLength = page.LacingValues[segmentIndex];
            pending.Write(page.Body, bodyOffset, segmentLength);
            bodyOffset += segmentLength;
            segmentIndex++;

            // A value of 255 means another segment belongs to this packet.
            // Any smaller value terminates it, including zero.
            packetContinues = segmentLength == 255;
            if (packetContinues) continue;

            var packet = new OggPacket(
                packetSerial,
                pending.ToArray(),
                packetBeginsStream,
                page.IsEndOfStream && segmentIndex == page.LacingValues.Length);

            pending.SetLength(0);
            pending.Position = 0;
            return packet;
        }
    }

    public void PushBack(OggPacket packet)
    {
        if (pushedBack != null) throw new InvalidOperationException("Only one Ogg packet can be pushed back.");
        pushedBack = packet;
    }
}
