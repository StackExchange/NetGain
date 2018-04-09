using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;

namespace StackExchange.NetGain
{
    public class Connection
    {
        private static long _nextId;
        internal long Id { get; } = Interlocked.Increment(ref _nextId);
        public object UserToken { get; set; }

        private IProtocolProcessor protocol;
        public Socket Socket { get; set; }
        public virtual void Reset()
        {
            protocol = null;
            writerCount = 0;
            HighPriority = false;
            flags = (int) (ConnectionFlags.IsAlive | ConnectionFlags.CanRead);
        }
        public Connection()
        {
            Reset();
        }
        public void SetProtocol(IProtocolProcessor protocol)
        {
            if (protocol == null) throw new InvalidEnumArgumentException("protocol");
            this.protocol = protocol;
        }
        public string GetProtocolName()
        {
            return protocol?.ToString() ?? "(none)";
        }

        private BufferStream incomingBuffer, outgoingBuffer;
        public int IncomingBufferedLength { get { return incomingBuffer == null ? 0 : (int)incomingBuffer.Length; } }
        public void AppendIncoming(NetContext context, byte[] buffer, int index, int count)
        {
            if(count != 0)
            {
                if (incomingBuffer == null) incomingBuffer = new BufferStream(context, context.Handler.MaxIncomingQuota);
                incomingBuffer.Position = incomingBuffer.Length;
                incomingBuffer.Write(buffer, index, count);
            }
        }
        public void DisableRead()
        {
            if(CanRead)
            {
                SetFlag(ConnectionFlags.CanRead, false);
                var socket = Socket;
                if(incomingBuffer != null)
                {
                    incomingBuffer.Dispose();
                    incomingBuffer = null;
                }
                if(socket != null)
                {
                    socket.Shutdown(SocketShutdown.Receive);
                }
            }
        }
        public bool CanRead
        {
            get { return GetFlag(ConnectionFlags.CanRead); }
        }
        [Flags]
        private enum ConnectionFlags
        {
            None = 0,
            IsAlive = 1,
            CanRead = 2,
            // 4, etc
        }

        private int flags;
        private bool GetFlag(ConnectionFlags flag)
        {
            return (Thread.VolatileRead(ref flags) & (int) flag) != 0;
        }
        private void SetFlag(ConnectionFlags flag, bool value)
        {
            int oldValue, newValue;
            do
            {
                oldValue = Thread.VolatileRead(ref flags);
                if (value)
                {
                    newValue = oldValue | ((int) flag);
                }
                else
                {
                    newValue = oldValue & ~((int) flag);
                }
                if (oldValue == newValue) return;
            } while (Interlocked.CompareExchange(ref flags, newValue, oldValue) != oldValue);

        }
        public bool IsAlive
        {
            get { return GetFlag(ConnectionFlags.IsAlive); }
        }
        public void GracefulShutdown(NetContext context)
        {
            var tmp = this.protocol;
            if (tmp != null) tmp.GracefulShutdown(context, this);
        }
        public void Shutdown(NetContext context)
        {
            bool wasAlive = GetFlag(ConnectionFlags.IsAlive);
            SetFlag(ConnectionFlags.IsAlive, false);
            if(wasAlive)
            {
                TcpHandler handler = context == null ? null : context.Handler;
                if (handler != null) handler.OnClosing(this);
            }
            
            var socket = Socket;
            try { socket.Shutdown(SocketShutdown.Send); }
            catch { /* swallow */ }
            try { socket.Close(); }
            catch { /* swallow */ }
            try { ((IDisposable)socket).Dispose(); }
            catch { /* swallow */ }
        }
        
        public int ReadOutgoing(NetContext context, byte[] buffer, int index, int count)
        {
            int bufferedLength = outgoingBuffer == null ? 0 : (int) outgoingBuffer.Length;

            if (bufferedLength == 0)
            {
                // nothing in the outbound buffer? then check to see if we have any new messages needing processing at the protocol layer, then the connection/handler layer
                IFrame frame;
                const int SANE_PACKET_SIZE = 512;
                while (bufferedLength < SANE_PACKET_SIZE &&
                    ((frame = protocol.GetOutboundFrame(context)) != null
                        ||
                        (context.Handler.RequestOutgoing(this) && (frame = protocol.GetOutboundFrame(context)) != null))
                       && GetFlag(ConnectionFlags.IsAlive))
                    // ^^^^ we try and get a frame from the protocol layer; if that is empty, we nudge the connection/handler layer to write, and then we
                    // check again for a fram from the protocol layer (the connection/handler can only queue frames); we then repeat this if necessary/appropriate
                    // to fill a packet
                {
                    if (outgoingBuffer == null) outgoingBuffer = new BufferStream(context, context.Handler.MaxOutgoingQuota);
                    outgoingBuffer.Position = outgoingBuffer.Length;
                    outgoingBuffer.IsReadonly = false;
                    frame.Write(context, this, outgoingBuffer);
                    outgoingBuffer.IsReadonly = true;
                    bufferedLength = (int) outgoingBuffer.Length;
                    if (frame.Flush) break; // send "as is"
                }
            }

            if (bufferedLength == 0)
            {
                Interlocked.Exchange(ref writerCount, 0); // nix and nada
                return 0;
            }

            int bytesRead = 0;
            if (outgoingBuffer != null)
            {
                outgoingBuffer.Position = 0;
                bytesRead = outgoingBuffer.Read(buffer, index, count);

                outgoingBuffer.Discard(bytesRead);
                if(outgoingBuffer.Length == 0)
                {
                    outgoingBuffer.Dispose();
                    outgoingBuffer = null;
                }
            }

            
            return bytesRead;
        }

        private int writerCount;

        public bool IsWriting { get { return Thread.VolatileRead(ref writerCount) != 0; } }
        public void PromptToSend(NetContext context)
        {
            if(Interlocked.CompareExchange(ref writerCount, 1, 0) == 0)
            { 
                // then **we** are the writer
                context.Handler.StartSending(this);
            }
        }

        public bool ProcessBufferedData(NetContext context, out int messageCount)
        {
            messageCount = 0;
            
            if (incomingBuffer != null && incomingBuffer.Length > 0)
            {
                int toDiscard;
                do
                {
                    if (!CanRead) break; // stop processing data
#if VERBOSE
                    long inboundLength = incomingBuffer.Length;
                    Debug.WriteLine(string.Format("[{0}]\tprocessing with {1} bytes available", context.Handler, inboundLength));
#endif
                    incomingBuffer.Position = 0;
                    toDiscard = protocol.ProcessIncoming(context, this, incomingBuffer);
                    if (toDiscard > 0)
                    {
                        messageCount++;
                        // could be null if our processing shut it down!
                        if(incomingBuffer != null)
                        {
                            if(toDiscard == incomingBuffer.Length)
                            {
                                incomingBuffer.Dispose();
                                incomingBuffer = null;
                            } else
                            {
                                incomingBuffer.Discard(toDiscard);
                            }
                            
                        } 
#if VERBOSE
                        Debug.WriteLine(string.Format("[{0}]\tprocessed {1} bytes; {2} remaining", context.Handler, toDiscard, inboundLength - toDiscard));
#endif
                    } else if(toDiscard < 0)
                    {
                        if(incomingBuffer != null)
                        {
                            incomingBuffer.Dispose();
                            incomingBuffer = null;
                        }
                        
                        // stop reading! (for example, protocol error, graceful shutdown)
                        return false;
                    }
                    else
                    {
#if VERBOSE
                        Debug.WriteLine(string.Format("[{0}]\tincomplete", context.Handler));
#endif
                    }
                } while (toDiscard > 0 && incomingBuffer != null && incomingBuffer.Length > 0);
            }
            if(incomingBuffer != null && incomingBuffer.Length == 0)
            {
                incomingBuffer.Dispose();
                incomingBuffer = null;
            }
            return true;
        }

        internal void InitializeClientHandshake(NetContext context)
        {
            protocol.InitializeClientHandshake(context, this);
        }

        public void Send(NetContext context, object message)
        {
            protocol.Send(context, this, message);
            PromptToSend(context);
        }

        private long lastSeen;
        public DateTime LastSeen
        {
            get { return DateTime.FromBinary(Interlocked.Read(ref lastSeen)); }
        }
        public void Seen()
        {
            Interlocked.Exchange(ref lastSeen, DateTime.UtcNow.ToBinary());
        }

        private static int nextConnectionId;
        volatile int connectionId;

        private string GetLogPath()
        {
            return "conn-" + connectionId + ".bin";
        }
        internal void ResetLogOutput()
        {
            string path = GetLogPath();
            if(File.Exists(path)) File.Delete(path);
        }
        internal void LogOutput(byte[] buffer, int offset, int count)
        {
            using(var file = new FileStream(GetLogPath(), FileMode.Append, FileAccess.Write))
            {
                file.Write(buffer, offset, count);
            }
        }
        public override string ToString()
        {
            return "connection:" + connectionId;
        }

        internal static string GetIdent(SocketAsyncEventArgs args)
        {
            try
            {
                Connection connection = args == null ? null : args.UserToken as Connection;
                return GetIdent(connection);
            } catch
            {
                return "n/a";
            }
        }
        internal static string GetAuditTimestamp()
        {
            return DateTime.UtcNow.ToString("HH:mm:ss");
        }
        internal static string GetIdent(Connection connection)
        {
            var builder = new StringBuilder(GetAuditTimestamp()).Append(' ');
            if(connection == null) builder.Append("??conn??");
            else
            {
                int id = connection.connectionId;
                builder.Append(((byte) (id >> 24)).ToString("x2"))
                    .Append(((byte) (id >> 16)).ToString("x2"))
                    .Append(((byte) (id >> 8)).ToString("x2"))
                    .Append(((byte) (id)).ToString("x2"));
            }
            return builder.ToString();
        }

        internal static string GetConnectIdent(System.Net.EndPoint endpoint)
        {
            return GetAuditTimestamp() + " " + (endpoint == null ? "??connect??" : endpoint.ToString());
        }

        internal static string GetHeartbeatIdent()
        {
            return GetAuditTimestamp() + " heartbeat";
        }

        internal static string GetLogIdent()
        {
            return GetAuditTimestamp();
        }

        internal void Prepare()
        {
            Reset();
            Seen();
            connectionId = Interlocked.Increment(ref nextConnectionId);
        }

        public bool HighPriority { get; set; }
    }
}
