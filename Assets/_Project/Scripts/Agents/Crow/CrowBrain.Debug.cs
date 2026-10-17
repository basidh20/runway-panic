namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Makes sure the crow debug overlay (<see cref="CrowDebugStats"/>, F4) exists once crows are in the scene.
    /// Editor and development builds only; in a release build the hook is empty and the compiler removes it.
    /// </summary>
    public sealed partial class CrowBrain
    {
        partial void EnsureDebugOverlay()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrowDebugStats.EnsureExists();
#endif
        }
    }
}
