using System;

namespace StackExchange.NetGain
{
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
