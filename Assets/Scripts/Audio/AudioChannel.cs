namespace RallyX.Audio
{
    /// <summary>
    /// A volume the player sets in the settings. Every sound plays on <see cref="Music"/>, <see cref="Effects"/> or
    /// <see cref="Vehicle"/>; <see cref="Master"/> scales all of them.
    /// </summary>
    public enum AudioChannel
    {
        Master,

        /// <summary>Menu and race music.</summary>
        Music,

        /// <summary>Menu sounds, the countdown and race announcements.</summary>
        Effects,

        /// <summary>Engines, tyres, wind, impacts and landings.</summary>
        Vehicle,
    }
}
