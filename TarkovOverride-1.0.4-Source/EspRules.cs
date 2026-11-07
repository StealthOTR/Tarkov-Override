using System;
namespace SPTPracticeMenu
{
    public enum EspKind { USEC, BEAR, SCAV, Boss, Guard, Raider, Rogue, Cultist, Bloodhound, Infected, Friendly, Other }
    public enum EspBoxStyle { FullBox, CornerBrackets, Marker }
    public enum EspDisplayPreset { Simple, Detailed, Custom }
    public static class EspRules
    {
        public static bool BossRole(string role)
        {return !string.IsNullOrEmpty(role)&&(role.StartsWith("boss",StringComparison.OrdinalIgnoreCase)||role=="followerBigPipe"||role=="followerBirdEye"||role=="gifter");}
        public static EspKind Kind(string role,string side,bool friendly)
        {
            role=role??"";
            if(friendly) return EspKind.Friendly;
            if(BossRole(role)) return EspKind.Boss;
            if(role.StartsWith("follower",StringComparison.OrdinalIgnoreCase)||role=="tagillaHelperAgro") return EspKind.Guard;
            if(role=="pmcBot") return EspKind.Raider;
            if(role=="arenaFighter"||role=="arenaFighterEvent") return EspKind.Bloodhound;
            if(role.StartsWith("infected",StringComparison.OrdinalIgnoreCase)) return EspKind.Infected;
            if(role=="exUsec") return EspKind.Rogue;
            if(role.StartsWith("sectant",StringComparison.OrdinalIgnoreCase)||role.StartsWith("sectact",StringComparison.OrdinalIgnoreCase)) return EspKind.Cultist;
            if(role=="pmcUSEC"||string.Equals(side,"Usec",StringComparison.OrdinalIgnoreCase)) return EspKind.USEC;
            if(role=="pmcBEAR"||string.Equals(side,"Bear",StringComparison.OrdinalIgnoreCase)) return EspKind.BEAR;
            if(string.Equals(side,"Savage",StringComparison.OrdinalIgnoreCase)||role=="assault"||role=="marksman") return EspKind.SCAV;
            return EspKind.Other;
        }
        public static bool VisibilityAllowed(bool visibleOnly,bool hiddenOnly,bool known,bool visible)
        {
            // Hidden-only wins if an old/manual configuration enabled both switches.
            if(hiddenOnly) return known&&!visible;
            if(visibleOnly) return known&&visible;
            return true;
        }
    }
}
