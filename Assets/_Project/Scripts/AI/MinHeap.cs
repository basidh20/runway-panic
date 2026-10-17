using System;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Binary min-heap keyed by a float priority: the A* open list. <see cref="Pop"/> always returns the
    /// item with the lowest priority in O(log n). Storage only grows, so a reused heap stops allocating
    /// once it has reached its working size.
    /// </summary>
    public sealed class MinHeap<T>
    {
        private T[] items;
        private float[] priorities;

        /// <summary>Creates an empty heap with room for <paramref name="initialCapacity"/> items.</summary>
        public MinHeap(int initialCapacity = 256)
        {
            initialCapacity = Math.Max(1, initialCapacity);
            items = new T[initialCapacity];
            priorities = new float[initialCapacity];
        }

        /// <summary>Number of items in the heap.</summary>
        public int Count { get; private set; }

        /// <summary>Adds <paramref name="item"/> with <paramref name="priority"/> (lower comes out first).</summary>
        public void Push(T item, float priority)
        {
            if (Count == items.Length)
            {
                Array.Resize(ref items, items.Length * 2);
                Array.Resize(ref priorities, priorities.Length * 2);
            }

            // Sift up: move the new item toward the root while it beats its parent.
            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (priorities[parent] <= priority)
                    break;
                items[i] = items[parent];
                priorities[i] = priorities[parent];
                i = parent;
            }

            items[i] = item;
            priorities[i] = priority;
        }

        /// <summary>Removes and returns the item with the lowest priority. The heap must not be empty.</summary>
        public T Pop()
        {
            if (Count == 0)
                throw new InvalidOperationException("Pop on an empty heap.");

            T top = items[0];
            Count--;
            if (Count == 0)
            {
                items[0] = default;
                return top;
            }

            // Sift down: put the last item at the root and sink it below any smaller child.
            T last = items[Count];
            float lastPriority = priorities[Count];
            items[Count] = default;

            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= Count)
                    break;
                if (child + 1 < Count && priorities[child + 1] < priorities[child])
                    child++;
                if (lastPriority <= priorities[child])
                    break;
                items[i] = items[child];
                priorities[i] = priorities[child];
                i = child;
            }

            items[i] = last;
            priorities[i] = lastPriority;
            return top;
        }

        /// <summary>Empties the heap and keeps its storage.</summary>
        public void Clear()
        {
            Array.Clear(items, 0, Count);
            Count = 0;
        }
    }
}
