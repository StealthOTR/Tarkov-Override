using System;

namespace SPTPracticeMenu
{
    public static class EnemyLabel
    {
        public static string Get(string role, string side, string nickname)
        {
            role = role ?? "";
            // Boss roles take precedence over faction; the Goons' other two members use follower roles.
            if (role.StartsWith("boss", StringComparison.OrdinalIgnoreCase) ||
                role == "followerBigPipe" || role == "followerBirdEye" || role == "gifter")
                return string.IsNullOrWhiteSpace(nickname) ? role : nickname;
            if (role == "pmcUSEC") return "USEC";
            if (role == "pmcBEAR") return "BEAR";
            if (role == "pmcBot") return "RAIDER";
            if (role == "exUsec") return "ROGUE";
            if (role.StartsWith("follower", StringComparison.OrdinalIgnoreCase) || role == "tagillaHelperAgro") return "BOSS GUARD";
            if (role.StartsWith("sectant", StringComparison.OrdinalIgnoreCase) || role.StartsWith("sectact", StringComparison.OrdinalIgnoreCase)) return "CULTIST";
            if (role.StartsWith("infected", StringComparison.OrdinalIgnoreCase)) return "INFECTED";
            if (role == "arenaFighter" || role == "arenaFighterEvent") return "BLOODHOUND";
            if (string.Equals(side, "Usec", StringComparison.OrdinalIgnoreCase)) return "USEC";
            if (string.Equals(side, "Bear", StringComparison.OrdinalIgnoreCase)) return "BEAR";
            return "SCAV";
        }
    }
}
