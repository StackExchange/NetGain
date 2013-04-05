
using NUnit.Framework;
using StackExchange.NetGain;
using StackExchange.NetGain.WebSockets;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace StackExchange.NetGain.Tests
{
    [TestFixture]
    public class DeflateFrameTests
    {
        static WebSocketsFrame CreateFrame(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var frame = new WebSocketsFrame { OpCode = WebSocketsFrame.OpCodes.Text, Payload = new MemoryStream(bytes), PayloadLength = bytes.Length };
            return frame;
        }
        static string ReadFrameMessage(WebSocketsFrame frame)
        {
            var ms = new MemoryStream();
            frame.Payload.CopyTo(ms);
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        [Test]
        public void DeflateRoundTrip()
        {
            var handler = new TcpHandler();
            var ctx = new NetContext(delegate { }, handler);

            var extn = PerFrameDeflate.Default.CreateExtension("deflate-frame");
            var conn = new WebSocketConnection(new IPEndPoint(IPAddress.Loopback, 20000));

            string[] messages = {
                "This extension uses one reserved bit to indicate whether DEFLATE is applied to the frame or not.  We call this \"COMP\" bit.",
                "Hello",
                "This extension operates only on data frames, and only on the \"Application data\" therein (it does not affect the \"Extension data\" portion of the \"Payload data\").",
                "world", "Hello",
                "To send a frame with DEFLATE applied, an endpoint MUST use the following algorithm.",
                "world",
                "Apply DEFLATE [RFC1951] to all the octets in the \"Application data\" part of the frame.  Multiple blocks MAY be used.  Any type of block MAY be used.  Both block with \"BFINAL\" set to 0 and 1 MAY be used.",
                "If the resulting data does not end with an empty block with no compression (\"BTYPE\" set to 0), append an empty block with no compression to the tail.",
                "Remove 4 octets (that are 0x00 0x00 0xff 0xff) from the tail.",
                "Hello",
                "Build a frame by putting the resulting octets in the \"Application data\" part instead of the original octets.  The payload length field of the frame MUST be the sum of the size of the \"Extension data\" part and these resulting octets.  \"COMP\" bit MUST be set to 1."
            };

            var frames = Array.ConvertAll(messages, CreateFrame);

            int initialSize = frames.Sum(x => x.PayloadLength);
            Assert.IsTrue(frames.All(x => !x.Reserved1), "no COMP initially");
            var munged = frames.SelectMany(f => extn.ApplyOutgoing(ctx, conn, f)).ToArray();
            Assert.AreEqual(frames.Length, munged.Length, "compress: 1 in, 1 out");
            
            Assert.IsTrue(frames.Any(x => x.Reserved1), "some COMP after compress");
            Assert.IsTrue(frames.Any(x => !x.Reserved1), "some non-COMP after compress");

            for (int i = 0; i < munged.Length; i++)
            {
                var ms = new MemoryStream();
                munged[i].Payload.Position = 0;
                munged[i].Payload.CopyTo(ms);
                ms.Position = 0;
                munged[i].Payload = new MemoryStream(ms.ToArray()); // read-only, deliberately
            }
            int mungedSize = frames.Sum(x => x.PayloadLength);

            var unmunged = munged.SelectMany(f => extn.ApplyIncoming(ctx, conn, f)).ToArray();

            Assert.AreEqual(unmunged.Length, unmunged.Length, "inflate: 1 in, 1 out");
            Assert.IsTrue(unmunged.All(x => !x.Reserved1), "no COMP after inflate");

            int unmungedSize = unmunged.Sum(x => x.PayloadLength);

            Console.WriteLine("Uncompressed: {0} bytes; compressed: {1} bytes; inflated {2} bytes", initialSize, mungedSize, unmungedSize);

            string[] finalMessages = Array.ConvertAll(unmunged, ReadFrameMessage);

            Assert.IsTrue(finalMessages.SequenceEqual(messages), "Equal messages");
        }

        [Test]
        public void ExamplesFromSpecification_1_2()
        {
            var extn = PerFrameDeflate.Default.CreateExtension("deflate-frame");
            Assert.AreEqual("Hello", DecompressFrame(extn, 0xf2, 0x48, 0xcd, 0xc9, 0xc9, 0x07, 0x00), "Example 1");
            Assert.AreEqual("Hello", DecompressFrame(extn, 0xf2, 0x00, 0x11, 0x00, 0x00), "Example 2");
        }
        [Test]
        public void ExamplesFromSpecification_3()
        {
            Assert.AreEqual("Hello", DecompressFrame(null, 0x00, 0x05, 0x00, 0xfa, 0xff, 0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x00), "Example 3");
        }
        [Test]
        public void ExamplesFromSpecification_4()
        {
            Assert.AreEqual("Hello", DecompressFrame(null, 0xf3, 0x48, 0xcd, 0xc9, 0xc9, 0x07, 0x00, 0x00), "Example 4");
        }

        [Test]
        public void CompressFrame()
        {
            var handler = new TcpHandler();
            var ctx = new NetContext(delegate { }, handler);

            IExtensionFactory factory = new PerFrameDeflate(0, false);
            var extn = factory.CreateExtension("deflate-frame");
            var data = Encoding.UTF8.GetBytes("Hello");
            var frame = new WebSocketsFrame
            {
                OpCode = WebSocketsFrame.OpCodes.Text,
                Payload = new MemoryStream(data),
                PayloadLength = data.Length,
                Reserved1 = false
            };
            var connection = new WebSocketConnection(new IPEndPoint(IPAddress.Loopback, 20000));
            var encoded = extn.ApplyOutgoing(ctx, connection, frame).Single();
            var ms = new MemoryStream();
            encoded.Payload.CopyTo(ms);
            string hex = BitConverter.ToString(ms.GetBuffer(), 0, (int)ms.Length);
            Assert.AreEqual("F2-48-CD-C9-C9-07-00", hex);

            // unrelated decoder
            extn = PerFrameDeflate.Default.CreateExtension("deflate-frame");
            var decoded = extn.ApplyIncoming(ctx, connection, frame).Single();

            ms = new MemoryStream();
            decoded.Payload.Position = 0;
            decoded.Payload.CopyTo(ms);
            string s = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            Assert.AreEqual("Hello", s);
        }

        [Test]
        public void ExamplesFromSpecification_5()
        {
            Assert.AreEqual("Hello", DecompressFrame(null, 0xf2, 0x48, 0x05, 0x00, 0x00, 0x00, 0xff, 0xff, 0xca, 0xc9, 0xc9, 0x07, 0x00), "Example 5");
        }

        byte[] ParseBits(string hex)
        {
            if (hex == null) return null;
            if (hex.Length == 0) return new byte[0];
            int bytes = ((hex.Length - 2) / 3) + 1;
            var raw = new byte[bytes];
            int offset = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = (byte)Convert.ToInt32(hex.Substring(offset, 2), 16);
                offset += 3;
            }
            string test = BitConverter.ToString(raw);
            if (!string.Equals(test, hex, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            return raw;
        }
        [Test]
        public void ParseFrame()
        {
            byte[] wtfFromGoogle = ParseBits("C1-FE-06-6C-AD-C6-27-9F-E1-48-76-95-6F-F6-37-DB-02-F4-DF-B2-08-B8-5F-97-0C-51-AF-F9-98-67-41-8C-DB-F1-F1-43-13-E7-05-6B-DE-26-9A-06-F4-74-77-5D-00-AE-6A-17-A7-4B-8E-23-B6-BA-6A-36-6C-2E-4C-57-E2-9E-07-8B-24-AA-B6-C0-DD-2A-46-32-3D-54-AA-83-74-91-12-F6-BE-50-64-CA-DD-4C-1C-8E-A2-31-A8-03-D7-79-52-40-24-F9-09-4F-2A-62-28-6C-2E-C0-5A-26-63-75-B9-2E-28-A8-1C-D9-48-53-D4-59-12-C5-3A-2B-31-91-CB-D3-3D-1B-2A-5C-9D-CE-A3-C8-CC-46-8D-E4-D9-CA-6B-F7-27-4C-58-FB-41-92-FE-21-FA-31-02-10-A5-28-DF-71-35-B0-51-48-DE-17-26-F9-FC-43-53-51-64-B3-04-84-95-65-EB-F0-81-38-DC-F7-87-50-C6-07-8D-79-F4-FC-81-04-BF-62-1D-C5-A5-CD-BC-72-07-67-BE-E5-AE-09-9C-7D-0E-B7-7F-DA-E3-81-D8-57-9E-ED-EE-DE-7F-34-06-4A-79-14-64-73-AC-84-0C-C6-91-6D-06-83-04-7F-CF-66-1B-8E-EC-98-7B-E9-70-10-C4-90-B3-71-FD-39-DE-71-08-0C-79-54-A4-DF-A5-85-88-4D-8E-2E-E1-5B-08-BD-6A-23-EF-C3-41-BF-4F-86-4F-B8-F1-19-F0-F5-0F-FD-2F-72-56-E4-01-69-BE-14-D6-E3-97-CE-EA-3D-74-69-24-8B-81-0F-2B-C1-3E-C8-3B-0A-89-61-83-DD-0C-AB-DD-FA-54-49-9B-2C-A5-69-01-45-6F-BE-C6-9A-8D-A0-22-95-B1-F0-01-E6-C7-2D-F3-68-EA-EB-B5-F2-F2-1C-85-0C-9C-9F-2C-2D-17-92-D3-DB-DA-46-FB-8F-70-46-95-CF-5A-AE-54-F7-E5-8C-FC-5B-48-3A-1F-42-A2-B8-2A-CC-C0-28-7E-46-0C-27-4D-7E-2D-D4-15-BC-93-0E-C7-97-15-9F-F9-F3-3E-87-29-5C-8D-CA-23-20-BF-23-BB-7B-BE-93-A8-22-58-C5-69-2E-FF-92-10-BF-E7-D2-D6-64-03-2B-0F-18-07-81-23-07-70-5B-62-9B-08-2E-65-DB-B4-76-AA-CC-DE-FD-EB-CD-6D-8B-11-F4-FD-6A-E8-8F-A1-EB-CB-AA-BD-86-94-71-4C-F3-23-BF-79-14-71-38-43-68-9A-D3-7C-09-48-00-72-86-07-53-A9-82-A0-D2-3B-0F-BF-55-85-46-24-7B-48-8B-C5-FD-8F-F6-E4-01-23-D7-87-1B-F4-42-2F-2C-94-E1-02-9B-4D-F8-9A-20-B1-3C-B9-C3-D5-FE-F2-75-07-D9-93-F0-5B-E7-8D-76-58-8E-68-F8-7A-B3-F6-FB-0B-28-61-F4-56-41-05-4C-4E-63-DF-13-73-08-94-44-4A-D6-4C-B5-07-B0-A0-99-53-B4-76-B1-86-CA-A3-AF-B9-3B-32-06-F8-CD-DA-01-43-45-34-AE-68-5F-B3-37-28-3F-DC-79-C7-EF-F4-F1-A8-D9-9E-68-B1-B9-C1-BC-61-01-45-E1-F7-64-79-05-62-71-55-86-E3-8B-21-B7-F9-E0-AC-A6-89-73-97-0B-1C-93-97-EA-34-9B-4B-E7-A7-68-8D-4D-B5-A8-45-EE-4F-EE-4F-A0-7C-D8-FA-74-16-45-8D-91-6B-D1-A5-D3-83-CF-98-2B-ED-DB-A2-22-85-37-D7-24-91-71-8D-84-9A-91-8A-3D-F1-4C-76-C9-22-2E-9B-73-94-F9-9C-BE-74-B6-B6-62-6C-C5-A3-43-9A-2B-11-A0-3A-FA-56-23-AB-62-4A-FB-AA-CC-DF-87-4E-3A-33-7E-D9-3D-08-85-AF-79-FE-B7-3A-DA-23-DD-8D-4E-79-1A-AE-4A-2E-AE-D7-C7-FE-27-7D-4B-01-71-3C-3F-6F-FB-41-CF-4F-13-1A-31-26-3C-DC-27-22-25-16-6C-CC-4A-71-9B-B6-BE-9F-D0-3C-21-0D-81-36-ED-2E-F2-75-7A-2C-E4-58-45-A8-46-D6-11-A1-2A-0B-6B-1F-55-E0-C4-6D-29-73-80-B4-76-7D-34-20-3F-DA-F5-FC-7C-F3-C5-FA-6F-6D-9D-ED-D9-1B-F3-7E-A1-5C-F8-EF-68-21-E8-68-3D-5E-7D-B9-63-00-F5-35-89-2D-28-DB-5A-97-FD-DC-BA-92-A8-27-0D-2F-62-98-22-49-CB-8A-D9-20-EF-1B-8B-EE-92-CB-FA-01-D3-47-F8-EA-C4-E5-50-54-ED-54-74-1D-67-84-77-6A-BA-D2-EF-FC-58-60-8B-9A-46-53-AD-A3-85-2B-87-95-13-56-23-6A-CA-B8-62-07-93-AE-07-88-1B-33-D2-83-C7-56-C5-82-E5-5F-24-28-45-C9-17-67-76-EF-AA-AD-61-73-5F-AA-C7-91-C2-55-56-CB-B0-41-E9-9B-AF-99-08-25-37-81-65-5A-D1-B1-BB-35-53-B2-0A-22-DB-80-5B-1E-BB-0E-6D-C9-F6-BB-18-74-4A-DA-1B-C3-98-D9-4C-72-ED-A2-A9-E9-C2-4F-39-C4-2C-41-C0-1A-C1-E4-CD-43-EB-D8-31-2F-7E-14-25-B2-A6-97-3C-CF-EE-D6-83-FC-B1-CA-D8-F3-ED-90-7E-3D-B1-88-86-68-CD-FF-14-C7-76-05-42-BF-01-0A-D2-DB-4C-79-0D-CF-A5-83-17-74-5B-FF-1A-C4-01-A7-1F-98-5E-DF-BA-09-96-23-6B-9C-92-6C-7B-4F-AE-95-82-8F-35-04-E5-9D-4C-31-40-92-6D-22-73-AD-65-6B-43-E0-60-19-E1-A5-ED-44-83-2B-87-3F-15-F3-C3-42-11-8F-93-CD-BE-97-A2-CC-96-01-86-22-8D-2C-4C-83-CE-22-19-48-8C-A5-A4-CE-0F-A0-03-F8-40-5B-3D-17-AC-DD-61-8B-77-48-14-A1-A4-DA-BF-6B-D2-42-65-3D-79-46-16-B3-11-45-45-C8-D4-BC-30-58-05-2B-77-12-18-5F-D9-07-67-19-6F-9F-0B-26-12-97-C3-81-94-97-B4-69-FE-F7-E2-C1-B4-B8-56-CD-29-D3-F8-CC-7A-3D-90-C3-36-D2-70-3B-29-6A-8D-B9-52-C1-BB-D7-84-63-A0-A2-D7-D2-F0-8F-13-56-5A-6C-E7-C6-47-39-6A-DF-AF-F2-88-A7-07-B1-B0-3C-44-A9-D7-D7-99-91-4E-CC-04-2F-0D-CB-A0-0C-BE-B6-DA-A9-22-13-23-70-F7-08-95-36-E1-16-99-02-D9-9F-A9-ED-14-F5-B5-5B-B6-56-E3-AF-77-24-17-0F-7B-03-CB-E4-04-F9-AE-97-0E-DE-80-34-23-B1-1D-9D-CA-22-4E-00-4E-F8-F1-6F-89-B0-0E-7F-32-06-AA-EF-5C-2D-42-10-A1-01-36-25-38-33-CD-EF-B5-48-71-61-5F-6D-2B-98-11-4A-31-DD-61-E3-2D-FA-60-8A-44-9D-9E-C3-FE-1C-30-52-2F-2B-05-E3-C5-AE-52-D6-5C-01-CD-1A-A9-4B-FE-E9-72-E0-A4-2C-B1-9D-3B-53-F1-C4-06-68-91-B2-9C-C1-EE-FF-D8-BC-FD-82-53-25-3D-2B-D5-56-79-F3-5C-60-00-EB-BB-1F-65-55-95-BF-29-0A-9E-A4-36-FE-13-FE-8D-4D-FF-88-A5-3D-DD-0E-51-39-3E-CF-DD-63-F4-13-14-C4-28-DC-75-A3-F1-E9-F5-04-4A-16-B7-02-66-78-70-12-29-4C-13-E4-71-87-63-C4-48-9E-D6-ED-CF-12-7F-9E-D9-E8-7B-5C-D2-83-14-29-D6-7D-F1-F7-9B-32-D1-A4-16-F5-DF-73-F9-37-B2-3E-F5-DE-2F-E2-0F-7C-E3-1E-9F-02-A6-AD-44-17-6F-EE-2C-B8-B5-E6-84-85-EF-BD-A4-DB-22-9E-C3-45-DB-B2-16-53-06-63-34-13-86-D6-6E-5F-7C-BD-A2-89-92-E7-C9-8E-F3-40-84-1B-35-4C-78-CD-9B-B7-B0-23-17-EB-2D-EC-59-2F-AC-AB-93-C9-48-53-1E-4E-D1-64-FE-EE-D3-51-F7-BE-7A-50-30-AD-D4-1D-AD-E7-C8-13-C8-19-0E-EC-C1-CD-79-56-9C-8D-3E-83-2B-95-23-C5-A0-16-20-01-A3-F9-C8-7B-FD-57-3B-87-07-4F-8A-00-FE-83-84-09-53-E5-50-D2-0C-F1-F0-EC-92");
            int headerLength;
            var frame = WebSocketsFrame.TryParseFrameHeader(wtfFromGoogle, wtfFromGoogle.Length, out headerLength);
            Assert.IsNotNull(frame);
            Assert.AreEqual(wtfFromGoogle.Length, headerLength + frame.PayloadLength);
        }

        static string DecompressFrame(IExtension extn, params byte[] data)
        {
            var handler = new TcpHandler();
            var ctx = new NetContext(delegate { }, handler);
            
            if (extn == null) extn = PerFrameDeflate.Default.CreateExtension("deflate-frame");
            var frame = new WebSocketsFrame {
                OpCode = WebSocketsFrame.OpCodes.Text,
                Payload = new MemoryStream(data),
                PayloadLength = data.Length,
                Reserved1 = true
            };
            var connection = new WebSocketConnection(new IPEndPoint(IPAddress.Loopback, 20000));
            var decoded = extn.ApplyIncoming(ctx, connection, frame).Single();

            return ReadFrameMessage(decoded);
        }

    }
}
