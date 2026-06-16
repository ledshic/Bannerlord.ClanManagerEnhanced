using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace Bannerlord.ClanManagerEnhanced
{
    internal static class ClanPartyFilters
    {
        internal static bool ShouldCheckParty(MobileParty? party, Clan playerClan)
        {
            if (party == null || party.IsMainParty || party.LeaderHero == null)
            {
                return false;
            }

            if (party.LeaderHero.Clan != playerClan)
            {
                return false;
            }

            if (party.IsDisbanding || party.MapEvent != null)
            {
                return false;
            }

            return true;
        }
    }
}
