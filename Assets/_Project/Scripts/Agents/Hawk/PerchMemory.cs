using System.Collections.Generic;

namespace RunwayPanic.Agents.Hawk
{
    /// <summary>
    /// "I got shot here before": a value 0..1 per perch that is raised when the hawk is hit there (H5) and fades over
    /// time. <see cref="PerchSelector"/> subtracts it from the perch's score, so the hawk avoids perches where the player
    /// found it. One instance per hawk (each hawk remembers its own experiences).
    /// </summary>
    public sealed class PerchMemory
    {
        // Parallel lists, not a Dictionary: values change every frame while iterating, and there are only ~8 perches.
        private readonly List<BirdPoint> perches = new List<BirdPoint>(16);
        private readonly List<float> values = new List<float>(16);

        /// <summary>Recently-shot value of <paramref name="perch"/> (0 = never, 1 = just shot there twice or more).</summary>
        public float Get(BirdPoint perch)
        {
            int index = perches.IndexOf(perch);
            return index >= 0 ? values[index] : 0f;
        }

        /// <summary>Raises the value of <paramref name="perch"/> by <paramref name="amount"/>, capped at 1.</summary>
        public void Mark(BirdPoint perch, float amount)
        {
            if (perch == null)
                return;

            int index = perches.IndexOf(perch);
            if (index < 0)
            {
                perches.Add(perch);
                values.Add(0f);
                index = perches.Count - 1;
            }

            values[index] = values[index] + amount > 1f ? 1f : values[index] + amount;
        }

        /// <summary>Lowers every value by <paramref name="amount"/> (call with decay per second × deltaTime), never below 0.</summary>
        public void Decay(float amount)
        {
            for (int i = 0; i < values.Count; i++)
                if (values[i] > 0f)
                    values[i] = values[i] - amount > 0f ? values[i] - amount : 0f;
        }

        /// <summary>Forgets every perch (e.g. at the start of a wave).</summary>
        public void Clear()
        {
            perches.Clear();
            values.Clear();
        }
    }
}
