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

            CmeDiagnostics.DebugLog("=== Daily Tick Started ===");
            CmeDiagnostics.DebugLog($"Mod Enabled: {settings.EnableMod}");
            CmeDiagnostics.DebugLog($"Debug Logging Enabled: {settings.EnableDebugLogging}");

            if (!settings.AllowPlayerClanPartiesJoinExternalArmies)
            {
                CmeDiagnostics.DebugLog("Enforcing external army restriction...");
                _externalArmyRestrictionService.EnforceExternalArmyRestriction(settings);
            }

            if (settings.AutoCreatePartyForIdleClanMembers)
            {
                CmeDiagnostics.DebugLog("Running auto-create party for idle clan members...");
                _idleClanMemberPartyAutomation.AutoCreatePartyForIdleClanMembers(settings);
            }

            if (settings.AutoReinforceParties)
            {
                CmeDiagnostics.DebugLog("Running auto-reinforce parties...");
                _clanLogisticsService.ReinforceLowStrengthParties(settings);
            }

            if (settings.AutoTransferExcessPrisoners)
            {
                CmeDiagnostics.DebugLog("Running auto-transfer excess prisoners...");
                _clanLogisticsService.TransferExcessPrisonersTodungeons(settings);
            }

            if (settings.ShowNotifications)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_DAILY_TICK}ClanManagerEnhanced daily check executed.").ToString()));
            }

            CmeDiagnostics.DebugLog("=== Daily Tick Completed ===");
        }

    }
}
