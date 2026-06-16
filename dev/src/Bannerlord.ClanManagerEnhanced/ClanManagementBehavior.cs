using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    public class ClanManagementBehavior : CampaignBehaviorBase
    {
        private readonly ExternalArmyRestrictionService _externalArmyRestrictionService = new ExternalArmyRestrictionService();
        private readonly IdleClanMemberPartyAutomation _idleClanMemberPartyAutomation = new IdleClanMemberPartyAutomation();
        private readonly ClanLogisticsService _clanLogisticsService = new ClanLogisticsService();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTick()
        {
            var settings = ClanManagerSettings.Instance;
            if (settings == null || !settings.EnableMod)
            {
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_DAILY_TICK_STARTED}=== Daily Tick Started ===").ToString()));

            var modEnabledText = new TextObject("{=CME_DBG_MOD_ENABLED}Mod Enabled: {VALUE}");
            modEnabledText.SetTextVariable("VALUE", settings.EnableMod ? "true" : "false");
            InformationManager.DisplayMessage(new InformationMessage(modEnabledText.ToString()));

            var debugEnabledText = new TextObject("{=CME_DBG_DEBUG_ENABLED}Message Output Enabled: {VALUE}");
            debugEnabledText.SetTextVariable("VALUE", settings.ShowNotifications ? "true" : "false");
            InformationManager.DisplayMessage(new InformationMessage(debugEnabledText.ToString()));

            if (settings.ShowNotifications)
            {
                LogClanMemberStatus();
            }

            if (!settings.AllowPlayerClanPartiesJoinExternalArmies)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_ENFORCE_EXT_ARMY}Enforcing external army restriction...").ToString()));
                _externalArmyRestrictionService.EnforceExternalArmyRestriction(settings);
            }

            if (settings.AutoCreatePartyForIdleClanMembers)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_RUN_AUTO_CREATE}Running auto-create party for idle clan members...").ToString()));
                _idleClanMemberPartyAutomation.AutoCreatePartyForIdleClanMembers(settings);
            }

            if (settings.AutoReinforceParties)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_RUN_REINFORCE}Running auto-reinforce parties...").ToString()));
                _clanLogisticsService.ReinforceLowStrengthParties(settings);
            }

            if (settings.AutoTransferExcessPrisoners)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DBG_RUN_TRANSFER_PRISONERS}Running auto-transfer excess prisoners...").ToString()));
                _clanLogisticsService.TransferExcessPrisonersTodungeons(settings);
            }

            if (settings.ShowNotifications)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DAILY_TICK}ClanManagerEnhanced daily check executed.").ToString()));
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_DAILY_TICK_COMPLETED}=== Daily Tick Completed ===").ToString()));
        }

        private static void LogClanMemberStatus()
        {
            var playerClan = Clan.PlayerClan;
            if (playerClan == null)
            {
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_MEMBER_SCAN_START}=== Clan Member Status Scan ===").ToString()));

            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero.Clan != playerClan)
                {
                    continue;
                }

                var location = hero.CurrentSettlement?.Name?.ToString()
                    ?? hero.PartyBelongedTo?.Name?.ToString()
                    ?? "unknown location";

                TextObject status;
                if (hero == Hero.MainHero)
                {
                    status = new TextObject("{=CME_DBG_STATUS_PLAYER}player");
                }
                else if (hero.IsPrisoner)
                {
                    status = new TextObject("{=CME_DBG_STATUS_PRISONER}prisoner");
                }
                else if (hero.GovernorOf != null)
                {
                    status = new TextObject("{=CME_DBG_STATUS_GOVERNOR}governor of {SETTLEMENT}");
                    status.SetTextVariable("SETTLEMENT", hero.GovernorOf.Name.ToString());
                }
                else if (hero.PartyBelongedTo != null)
                {
                    if (hero.PartyBelongedTo.IsMainParty)
                    {
                        status = new TextObject("{=CME_DBG_STATUS_IN_PLAYER_PARTY}in player party");
                    }
                    else
                    {
                        status = new TextObject("{=CME_DBG_STATUS_IN_PARTY}in party: {PARTY}");
                        status.SetTextVariable("PARTY", hero.PartyBelongedTo.Name.ToString());
                    }
                }
                else if (hero.CurrentSettlement != null)
                {
                    status = new TextObject("{=CME_DBG_STATUS_IDLE_SETTLEMENT}idle in settlement");
                }
                else
                {
                    status = new TextObject("{=CME_DBG_STATUS_UNKNOWN}unknown");
                }

                var memberLine = new TextObject("{=CME_DBG_MEMBER_LINE}[MEMBER] {NAME} | {LOCATION} | {STATUS}");
                memberLine.SetTextVariable("NAME", hero.Name.ToString());
                memberLine.SetTextVariable("LOCATION", location);
                memberLine.SetTextVariable("STATUS", status.ToString());
                InformationManager.DisplayMessage(new InformationMessage(memberLine.ToString()));
            }

            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=CME_DBG_MEMBER_SCAN_END}=== Clan Member Status Scan END ===").ToString()));
        }

    }
}
