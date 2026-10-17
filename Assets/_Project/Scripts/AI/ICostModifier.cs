using UnityEngine;

namespace RunwayPanic.AI
{
    /// <summary>
    /// Extra A* cost for entering a cell, so a species can avoid places without blocking them
    /// (crow danger marks, hawk fear field, gull debris). Several modifiers can be passed to one search.
    /// </summary>
    public interface ICostModifier
    {
        /// <summary>
        /// Extra cost (≥ 0) added to the step cost of entering the cell centred on <paramref name="worldPosition"/>.
        /// Called once per neighbour the search looks at, so keep it cheap.
        /// </summary>
        float GetExtraCost(Vector3 worldPosition);
    }
}
