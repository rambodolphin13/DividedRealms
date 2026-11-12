using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Discord 2026-11-12: opening the account vault gave "Oversized packet detected and discarded (code: 0x02) (size: 2097)".
    /// MAX_ITEMS_PER_PACKET (30) counts items, not bytes, so the 1.89+ inventory update is now cut by size as well.
    /// </summary>
    [TestFixture]
    public class UT_InventoryUpdateSplit
    {
        private const int Header = 3 + 6; // length + code, then six header bytes

        private static List<(GSTCPPacketOut Packet, int Count, bool First)> Split(IEnumerable<int> itemSizes, int maxLength = 2048)
        {
            var sent = new List<(GSTCPPacketOut, int, bool)>();
            bool nextFirst = true;
            PacketLib189.WriteSplitInventoryUpdate(itemSizes,
                first =>
                {
                    nextFirst = first;
                    var pak = new GSTCPPacketOut().Init(0x02);
                    pak.Write(new byte[6], 0, 6);
                    return pak;
                },
                (pak, size) => pak.Write(new byte[size], 0, size),
                (pak, count) => sent.Add((pak, count, nextFirst)),
                maxLength);
            return sent;
        }

        [Test]
        public void ThirtyLongItemsSplitUnderTheLimit()
        {
            // 30 items of 70 bytes = 2,100 bytes of items, the size of the reported vault packet.
            var sent = Split(Enumerable.Repeat(70, 30));

            Assert.That(sent.Count, Is.EqualTo(2));
            Assert.That(sent.All(s => s.Packet.Length <= 2048), Is.True);
            Assert.That(sent.Sum(s => s.Count), Is.EqualTo(30));
            Assert.That(sent[0].Packet.Length, Is.EqualTo(Header + 70 * sent[0].Count));
            Assert.That(sent[0].First, Is.True);
            Assert.That(sent[1].First, Is.False, "only the first packet opens the window");
        }

        [Test]
        public void SmallUpdateStaysOnePacket()
        {
            var sent = Split(Enumerable.Repeat(30, 30));

            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Count, Is.EqualTo(30));
            Assert.That(sent[0].Packet.Length, Is.EqualTo(Header + 900));
        }

        [Test]
        public void EmptyUpdateStillSendsOneHeader()
        {
            var sent = Split(Enumerable.Empty<int>());

            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Count, Is.EqualTo(0));
            Assert.That(sent[0].Packet.Length, Is.EqualTo(Header));
        }

        [Test]
        public void CountNeverPassesOneByte()
        {
            var sent = Split(Enumerable.Repeat(1, 600));

            Assert.That(sent.Select(s => s.Count), Is.EqualTo(new[] { 255, 255, 90 }));
        }

        [Test]
        public void CutItemIsWrittenAgainWhole()
        {
            var sent = Split(new[] { 1000, 1000, 1000 });

            Assert.That(sent.Select(s => s.Count), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(sent[0].Packet.Length, Is.EqualTo(Header + 2000));
            Assert.That(sent[1].Packet.Length, Is.EqualTo(Header + 1000));
        }
    }
}
