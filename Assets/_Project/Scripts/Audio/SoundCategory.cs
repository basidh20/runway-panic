namespace RunwayPanic.Audio
{
    /// <summary>Volume group a sound belongs to (see <see cref="SoundPlayer.SetVolume"/>).</summary>
    public enum SoundCategory
    {
        Music = 0,
        Ambience = 1,
        Weapons = 2,
        Player = 3,
        Birds = 4,
        World = 5,
        UI = 6
    }
}
