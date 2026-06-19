using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace Bannerlord.ClanManagerEnhanced
{
    /// <summary>
    /// Filters player clan parties out of non-player lords' army recruitment candidates.
    /// This aligns with the game's army creation decision stage (possibleArmyMembers),
    /// so external armies stop searching player clan parties up front.
    /// </summary>
    [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), nameof(DefaultArmyManagementCalculationModel.CanLordCreateArmy))]
    public static class ArmyRecruitmentCandidatePatches
    {
        public static void Postfix(MobileParty leaderParty, ref MBList<MobileParty> possibleArmyMembers, ref bool __result)
        {
            var settings = ClanManagerSettings.Instance;
            if (settings == null || !settings.EnableMod)
            {
                return;
            }

            var playerClan = Clan.PlayerClan;
            if (playerClan == null || leaderParty?.LeaderHero?.Clan == null)
            {
                return;
            }

            // Keep player's own army flow intact; only filter external lords.
            if (leaderParty.IsMainParty || leaderParty.LeaderHero.Clan == playerClan)
            {
                return;
            }

            if (possibleArmyMembers == null || possibleArmyMembers.Count == 0)
            {
                return;
            }

            for (var i = possibleArmyMembers.Count - 1; i >= 0; i--)
            {
                var candidate = possibleArmyMembers[i];
                if (candidate?.LeaderHero?.Clan == playerClan)
                {
                    possibleArmyMembers.RemoveAt(i);
                }
            }

            // If there is no candidate left, prevent army creation from continuing as successful.
            if (possibleArmyMembers.Count == 0)
            {
                __result = false;
            }
        }
    }
}
