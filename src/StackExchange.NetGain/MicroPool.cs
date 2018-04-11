using System;

namespace StackExchange.NetGain
{
    class MicroPool<T> where T : class
    {
        private readonly T[] buffer;
        private int readIndex, writeIndex, count;

        public MicroPool(int count)
        {
            buffer = new T[count];
        }
        public T TryGet()
        {
            lock(buffer)
            {
                if (count != 0)
                {
                    count--;
                    int index = readIndex++;
                    if (readIndex == buffer.Length) readIndex = 0;
                    return buffer[index];
                }
            }
            return null;
        }
        public virtual void PutBack(T value)
        {
            lock(buffer)
            {
                if(count != buffer.Length)
                {
                    count++;
                    int index = writeIndex++;
                    if (writeIndex == buffer.Length) writeIndex = 0;
                    buffer[index] = value;
                    return;
                }
            }
            // no space; drop on the floor
            var disp = value as IDisposable;
            if (disp != null) disp.Dispose();
        }
    }
}
