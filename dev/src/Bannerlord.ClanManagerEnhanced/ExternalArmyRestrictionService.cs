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
                CmeDiagnostics.DebugLog("PlayerClan is null, skipping external army enforcement");
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
                    CmeDiagnostics.DebugLog($"Party {party.Name} is in player's own army, allowing");
                    continue;
                }

                if (TryForceLeaveArmy(party, army))
                {
                    blockedCount++;
                    CmeDiagnostics.DebugLog($"Forced party {party.Name} to leave external army");
                }
            }

            CmeDiagnostics.DebugLog($"External army enforcement: blocked {blockedCount} parties");

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
                CmeDiagnostics.DebugLog($"[ERROR] Failed to remove party from army: {ex}");
            }

            return false;
        }
    }
}
