using System;
using System.Collections.Specialized;
using System.IO;

namespace StackExchange.NetGain
{
    public abstract class HttpProcessor : ProtocolProcessor
    {

        protected static int FindHeadersLength(Stream stream)
        {
            int b, index = 0, newLineCount = 0;
            while ((b = stream.ReadByte()) >= 0)
            {
                index++;
                switch (b)
                {
                    case (byte)'\r':
                        // skip, to allow "[CR][LF][CR][LF]" or "[LF][LF]"
                        continue;
                    case (byte)'\n':
                        if (++newLineCount == 2) return index;
                        break;
                    default:
                        newLineCount = 0;
                        break;
                }

            }
            return -1;
        }
        protected static StringDictionary ParseHeaders(Stream stream, out string requestLine)
        {
            using (var reader = new StreamReader(stream))
            {
                string lastKey = null, line;
                requestLine = reader.ReadLine();
                var headers = new StringDictionary();
                //In compliance with [RFC2616], header fields in the handshake may be
                //sent by the client in any order, so the order in which different
                //header fields are received is not significant.
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if (line[0] == ' ' || line[0] == '\t')
                    {
                        //HTTP/1.1 header field values can be folded onto multiple lines if the
                        //continuation line begins with a space or horizontal tab. All linear
                        //white space, including folding, has the same semantics as SP. A
                        //recipient MAY replace any linear white space with a single SP before
                        //interpreting the field value or forwarding the message downstream.
                        headers[lastKey] += " " + line.TrimStart();
                    }
                    else
                    {
                        int i = line.IndexOf(':');
                        if (i <= 0) throw new InvalidOperationException();

                        // most HTTP headers are repeatable, so Foo=abc\r\nFoo=def\r\n
                        // is identical to Foo=abc, def\r\n
                        lastKey = line.Substring(0, i);
                        string value = line.Substring(i + 1, line.Length - (i + 1)).Trim();
                        if(headers.ContainsKey(lastKey))
                        {
                            value = headers[lastKey] + ", " + value;
                        }
                        headers[lastKey] = value;                        
                    }
                }
                return headers;
            }
        }
    }
}
