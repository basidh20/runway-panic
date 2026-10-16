namespace RunwayPanic.Core
{
    /// <summary>
    /// Top-level game states driven by the GameManager.
    /// Values are explicit so serialized data keeps its meaning if the list is reordered.
    /// </summary>
    public enum GameState
    {
        MainMenu = 0,
        Playing = 1,
        Shopping = 2,
        Paused = 3,
        GameOver = 4,
        Victory = 5
    }
}
