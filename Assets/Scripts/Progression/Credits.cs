using System.Globalization;

namespace RallyX.Progression
{
    /// <summary>How credit amounts are written in the game.</summary>
    public static class Credits
    {
        /// <summary>e.g. "12,500 CR".</summary>
        public static string Format(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + " CR";
    }
}
