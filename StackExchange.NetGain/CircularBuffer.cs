using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using StackExchange.NetGain.Logging;

namespace StackExchange.NetGain
{
    internal class ConnectionSet : IEnumerable<Connection>
    {
        private Connection[] connections = new Connection[10];
        private readonly ILog log = LogManager.Current.GetLogger<ConnectionSet>();

        private int count;
        public int Count
        {
            get { return Thread.VolatileRead(ref count); }
        }
        public void Add(Connection connection)
        {
            lock(this)
            {
                count++;
                for(int i = 0 ; i < connections.Length ;i++)
                {
                    if(connections[i] == null)
                    {
                        connections[i] = connection;
                        return;
                    }
                }
                int oldLen = connections.Length;
                Array.Resize(ref connections, oldLen * 2);
                connections[oldLen] = connection;
#if VERBOSE
                log.Debug("resized ConnectionSet: {0}", connections.Length);
#endif
            }
        }
        public bool Remove(Connection connection)
        {
            lock (this)
            {
                for (int i = 0; i < connections.Length; i++)
                {
                    if((object)connections[i] == (object)connection)
                    {
                        connections[i] = null;
                        count--;
                        return true;
                    }
                }
            }
            return false;
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        public IEnumerator<Connection> GetEnumerator()
        {
            // lock-free, best-efforts enumeration
            var snapshot = connections;
            for(int i = 0 ; i < snapshot.Length ; i++)
            {
                var conn = snapshot[i];
                if (conn != null) yield return conn;
            }
        }
    }

    public class CircularBuffer<T>
    {
        private int count, origin;
        public int Count { get { return count; } }

        private T[] data = new T[10];
        public void Push(T value)
        {
            if (count == data.Length)
            { 
                // grow the array, re-normalizing to zero
                var newArr = new T[data.Length * 2];
                int split = count - origin;
                Array.Copy(data, origin, newArr, 0, split);
                Array.Copy(data, 0, newArr, split, count - split);
                origin = 0;
                data = newArr;
            }
            int idx = (origin + count++) % data.Length;
            data[idx] = value;
        }
        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= count)
                {
                    throw new ArgumentOutOfRangeException("index");
                }
                return data[(origin + index) % data.Length];
            }
            set
            {
                if (index == count) Push(value);
                else if (index < 0 || index > count) throw new ArgumentOutOfRangeException("index");
                else data[(origin + index) % data.Length] = value;
            }
        }
        public T Pop()
        {
            if (count == 0) throw new InvalidOperationException();
            T value = data[origin];
            count--;
            origin = (origin + 1) % data.Length;
            return value;
        }

        public void Reset()
        {
            count = origin = 0;
        }
    }
}
