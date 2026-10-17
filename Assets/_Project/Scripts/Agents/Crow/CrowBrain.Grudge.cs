namespace RunwayPanic.Agents.Crow
{
    /// <summary>
    /// Makes sure the scene's danger map exists as soon as the first crow starts, so no death goes unrecorded.
    /// </summary>
    public sealed partial class CrowBrain
    {
        partial void EnsureDangerMap()
        {
            CrowDangerMap.GetOrCreate(config);
        }
    }
}
