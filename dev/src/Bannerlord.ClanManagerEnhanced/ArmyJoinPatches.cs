using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace Bannerlord.ClanManagerEnhanced
{
    /// <summary>
    /// Blocks player clan parties from joining non-player armies when the setting is disabled.
    /// This is a pre-emptive guard; behavior-level enforcement remains as a safety net.
    /// </summary>
    [HarmonyPatch(typeof(Army), "AddParty")]
    public static class ArmyJoinPatches
    {
        public static bool Prefix(Army __instance, MobileParty __0)
        {
            var settings = ClanManagerSettings.Instance;
            if (settings == null || !settings.EnableMod || settings.AllowPlayerClanPartiesJoinExternalArmies)
            {
                return true;
            }

            var party = __0;
            if (party == null || party.IsMainParty || party.LeaderHero == null)
            {
                return true;
            }

            var playerClan = Clan.PlayerClan;
            if (playerClan == null || party.LeaderHero.Clan != playerClan)
            {
                return true;
            }

            // Allowed: player-led army can still call player clan parties.
            if (__instance?.LeaderParty != null && __instance.LeaderParty.IsMainParty)
            {
                return true;
            }

            return false;
        }
    }
}
