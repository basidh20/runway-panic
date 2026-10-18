using UnityEngine;

namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Makes sure the crow debug overlay (<see cref="CrowDebugStats"/>, F4) exists once crows are in the scene.
    /// Editor and development builds only (checked at runtime with <see cref="Debug.isDebugBuild"/>).
    /// </summary>
    public sealed partial class CrowBrain
    {
        partial void EnsureDebugOverlay()
        {
            if (Debug.isDebugBuild)
                CrowDebugStats.EnsureExists();
        }
    }
}
