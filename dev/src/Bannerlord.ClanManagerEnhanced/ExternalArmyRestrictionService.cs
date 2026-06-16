using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    internal sealed class ExternalArmyRestrictionService
    {
        private const double BlockedArmyNoticeCooldownSeconds = 30.0;

        private static DateTime _lastBlockedArmyNoticeUtc = DateTime.MinValue;

        private static readonly MethodInfo? LeaveArmyMethod = typeof(MobileParty).GetMethod(
            "LeaveArmy",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        private static readonly MethodInfo? RemovePartyMethod = typeof(Army).GetMethod(
            "RemoveParty",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(MobileParty) },
            null);

        internal void EnforceExternalArmyRestriction(ClanManagerSettings settings)
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_PLAYER_CLAN_NULL_EXT_ARMY}Player clan is null, skipping external army enforcement.").ToString()));
                return;
            }

            var blockedCount = 0;

            foreach (var party in MobileParty.All)
            {
                if (!ClanPartyFilters.ShouldCheckParty(party, playerClan))
                {
                    continue;
                }

                var army = party.Army;
                if (army == null)
                {
                    continue;
                }

                if (army.LeaderParty != null && army.LeaderParty.IsMainParty)
                {
                    var allowOwnArmyText = new TextObject("{=CME_DBG_EXT_ARMY_ALLOW_OWN}Party {PARTY} is in player's own army, allowing");
                    allowOwnArmyText.SetTextVariable("PARTY", party.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(allowOwnArmyText.ToString()));
                    continue;
                }

                if (TryForceLeaveArmy(party, army))
                {
                    blockedCount++;
                    var forcedLeaveText = new TextObject("{=CME_DBG_EXT_ARMY_FORCED_LEAVE}Forced party {PARTY} to leave external army");
                    forcedLeaveText.SetTextVariable("PARTY", party.Name.ToString());
                    InformationManager.DisplayMessage(new InformationMessage(forcedLeaveText.ToString()));
                }
            }

            var blockedSummaryText = new TextObject("{=CME_DBG_EXT_ARMY_BLOCKED_SUMMARY}External army enforcement: blocked {COUNT} parties");
            blockedSummaryText.SetTextVariable("COUNT", blockedCount);
            InformationManager.DisplayMessage(new InformationMessage(blockedSummaryText.ToString()));

            if (blockedCount > 0 && settings.ShowNotifications && ShouldShowBlockedArmyNotice())
            {
                var text = new TextObject("{=CME_BLOCK_EXT_ARMY_JOIN}Blocked {COUNT} player clan party(ies) from staying in a non-player army.");
                text.SetTextVariable("COUNT", blockedCount);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
                _lastBlockedArmyNoticeUtc = DateTime.UtcNow;
            }
        }

        private static bool ShouldShowBlockedArmyNotice()
        {
            var now = DateTime.UtcNow;
            return (now - _lastBlockedArmyNoticeUtc).TotalSeconds >= BlockedArmyNoticeCooldownSeconds;
        }

        private static bool TryForceLeaveArmy(MobileParty party, Army army)
        {
            try
            {
                if (LeaveArmyMethod != null)
                {
                    LeaveArmyMethod.Invoke(party, null);
                    return true;
                }

                if (RemovePartyMethod != null)
                {
                    RemovePartyMethod.Invoke(army, new object[] { party });
                    return true;
                }
            }
            catch (Exception ex)
            {
                var removeArmyErrorText = new TextObject("{=CME_ERR_EXT_ARMY_REMOVE_FAILED}[ERROR] Failed to remove party from army: {DETAIL}");
                removeArmyErrorText.SetTextVariable("DETAIL", ex.ToString());
                InformationManager.DisplayMessage(new InformationMessage(removeArmyErrorText.ToString()));
            }

            return false;
        }
    }
}
