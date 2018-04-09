using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;

namespace StackExchange.NetGain.WebSockets
{
    public class WebSocketsSelectorProcessor : WebSocketsSelectorBase, IProtocolFactory
    {
        private static readonly WebSocketsSelectorProcessor @default = new WebSocketsSelectorProcessor();
        public static WebSocketsSelectorProcessor Default { get { return @default; } }
        IProtocolProcessor IProtocolFactory.GetProcessor() { return this; }
        Connection IProtocolFactory.CreateConnection(EndPoint endpoint) { return new WebSocketConnection(endpoint); }

        protected override void Send(NetContext context, Connection connection, object message)
        {
            // this would only happen when broadcasting from the server during the handshake;
            // frankly, I'm happy to just drop that on the floor as undelivered
        }
        protected override void InitializeInbound(NetContext context, Connection connection)
        {
            base.InitializeInbound(context, connection);
            // high priority while connecting; downgrade later
            connection.HighPriority = true;
        }

        protected virtual bool AllowClientsMissingConnectionHeaders { get {  return false; } }
        protected override int ProcessHeadersAndUpgrade(NetContext context, Connection conn, Stream input, Stream additionalData, int additionalOffset)
        {
            string requestLine;
            var headers = ParseHeaders(input, out requestLine);
            string host = headers["Host"];

            // context.Handler.WriteLog(host + ": " + requestLine, conn);

            //The "Request-URI" of the GET method [RFC2616] is used to identify the
            //endpoint of the WebSocket connection, both to allow multiple domains
            //to be served from one IP address and to allow multiple WebSocket
            //endpoints to be served by a single server.
            var connection = (WebSocketConnection) conn;
            connection.Host = host;
            connection.Origin = headers["Origin"] ?? headers["Sec-WebSocket-Origin"]; // Some early drafts used the latter, so we'll allow it as a fallback
                                                                                      // in particular, two drafts of version "8" used (separately) **both**,
                                                                                      // so we can't rely on the version for this (hybi-10 vs hybi-11).
                                                                                      // To make it even worse, hybi-00 used Origin, so it is all over the place!
            connection.Protocol = headers["Sec-WebSocket-Protocol"];
            connection.RequestLine = requestLine;
            if (string.IsNullOrEmpty(connection.Host))
            {
                //4.   The request MUST contain a |Host| header field whose value
                //contains /host/ plus optionally ":" followed by /port/ (when not
                //using the default port).
                throw new InvalidOperationException("host required");
            }

            bool looksGoodEnough = false;
            // mozilla sends "keep-alive, Upgrade"; let's make it more forgiving
            var connectionParts = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            if (headers.ContainsKey("Connection"))
            {
                // so for mozilla, this will be the set {"keep-alive", "Upgrade"}
                var parts = headers["Connection"].Split(Comma);
                foreach (var part in parts) connectionParts.Add(part.Trim());
            }
            if (connectionParts.Contains("Upgrade") && string.Equals(headers["Upgrade"], "websocket", StringComparison.InvariantCultureIgnoreCase))
            {
                //5.   The request MUST contain an |Upgrade| header field whose value
                //MUST include the "websocket" keyword.
                //6.   The request MUST contain a |Connection| header field whose value
                //MUST include the "Upgrade" token.
                looksGoodEnough = true;
            }

            if(!looksGoodEnough && AllowClientsMissingConnectionHeaders)
            {
                if((headers.ContainsKey("Sec-WebSocket-Version") && headers.ContainsKey("Sec-WebSocket-Key"))
                    || (headers.ContainsKey("Sec-WebSocket-Key1") && headers.ContainsKey("Sec-WebSocket-Key2")))
                {
                    looksGoodEnough = true;
                }
            }

            if(looksGoodEnough)
            {
                //9.   The request MUST include a header field with the name
                //|Sec-WebSocket-Version|.  The value of this header field MUST be
                WebSocketsProcessor newProtocol;
                string version = headers["Sec-WebSocket-Version"];
                byte[] body = null;
                int bodyRead = 0;
                bool processExtensions = false;
                if(version == null)
                {
                    if (headers.ContainsKey("Sec-WebSocket-Key1") && headers.ContainsKey("Sec-WebSocket-Key2"))
                    { // smells like hixie-76/hybi-00
                        newProtocol = new WebSocketsProcessor_Hixie76_00.Server();
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
                else
                {
                    switch(version)
                    {
                        
                        case "4": case "5": case "6": case "7": case "8": // these are all early drafts
                        case "13": // this is later drafts and RFC6455
                            newProtocol = new WebSocketsProcessor_RFC6455_13(false);
                            processExtensions = true;
                            break;
                        default:
                            // should issues a 400 "upgrade required" and specify Sec-WebSocket-Version - see 4.4
                            throw new InvalidOperationException(string.Format("Sec-WebSocket-Version {0} is not supported", version));
                    }
                }
                
                //if(headers.ContainsKey("x-forwarded-for"))
                //{
                //    context.Handler.WriteLog("forwarded for: " + headers["x-forwarded-for"], conn);
                //}
                
                if (processExtensions)
                {
                    AddMatchingExtensions(headers, connection, context.Extensions);
                }
                connection.HighPriority = false; // regular priority when we've got as far as switching protocols
                connection.SetProtocol(newProtocol);
                context.Handler.OnAuthenticate(connection, headers);
                newProtocol.CompleteHandshake(context, connection, input, requestLine, headers, body);
                context.Handler.OnAfterAuthenticate(connection);
                connection.PromptToSend(context);
                return bodyRead;
            }
            else
            {
                var responseHeaders = new StringDictionary();
                HttpStatusCode code;
                string body;

                if (!TryBasicResponse(context, headers, requestLine, responseHeaders, out code, out body))
                {
                    // does it look a lot like a proxy server?
                    bool isProxy = headers.ContainsKey("via");
                    if(!isProxy)
                    {
                        foreach(string key in headers.Keys)
                        {
                            if(key.EndsWith("-via"))
                            {
                                isProxy = true;
                                break;
                            }
                        }
                    }
                    if(isProxy)
                    {
                        throw new CloseSocketException(); // no need to log this
                    }


                    if ((failSockets++ % 100) == 0)
                    {
                        // DUMP HEADERS
                        var sb = new StringBuilder("Failing request: ").AppendLine(requestLine);
                        foreach (string key in headers.Keys)
                        {
                            sb.AppendFormat("{0}:\t{1}", key, headers[key]).AppendLine();
                        }
                        context?.Handler?.ErrorLog?.WriteLine(sb);
                    }
                    throw new InvalidOperationException("Request was not a web-socket upgrade request; connection: " + headers["Connection"] + ", upgrade: " + headers["Upgrade"]);
                }
                else
                {
                    var newProcessor = new BasicHttpResponseProcessor();
                    connection.SetProtocol(newProcessor);
                    var resp = new StringBuilder();
                    resp.Append("HTTP/1.0 ").Append((int)code).Append(' ').Append(code).AppendLine();
                    foreach (string key in responseHeaders.Keys)
                    {
                        string val = responseHeaders[key];
                        if(string.IsNullOrEmpty(val)) continue;
                        resp.Append(key).Append(": ").Append(val).AppendLine();
                    }
                    resp.Append("Connection: close").AppendLine();
                    resp.AppendLine();
                    if (!string.IsNullOrEmpty(body)) resp.AppendLine(body);
                    connection.Send(context, resp.ToString());
                    newProcessor.SendShutdown(context);
                    connection.PromptToSend(context);
                    throw new CloseSocketException();
                }
            }
        }
        
        uint failSockets = 0;
        
        protected virtual bool TryBasicResponse(NetContext context, StringDictionary requestHeaders, string requestLine, StringDictionary responseHeaders, out HttpStatusCode code, out string body)
        {
            body = null;
            code = HttpStatusCode.BadRequest;
            return false;
        }

    }


}
