using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Localization;

namespace Bannerlord.ClanManagerEnhanced
{
    public sealed class ClanManagerSettings : AttributeGlobalSettings<ClanManagerSettings>
    {
        public override string Id => "Bannerlord.ClanManagerEnhanced_v1";

        public override string DisplayName
        {
            get
            {
                var ver = typeof(ClanManagerSettings).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                return new TextObject("{=CME_MainDisplay}Clan Manager Enhanced {VERSION}",
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "VERSION", ver }
                    }).ToString();
            }
        }

        public override string FolderName => "Bannerlord.ClanManagerEnhanced";
        public override string FormatType => "json";

        [SettingPropertyBool(
            "{=CME_EnableMod}Enable Mod",
            RequireRestart = false,
            HintText = "{=CME_EnableModHint}Master toggle. When off, no automated clan management actions will run.")]
        [SettingPropertyGroup("{=CME_General}General", GroupOrder = 0)]
        public bool EnableMod { get; set; } = true;

        [SettingPropertyBool(
            "{=CME_ShowNotifs}Show Notifications",
            RequireRestart = false,
            HintText = "{=CME_ShowNotifsHint}Display information messages when ClanManagerEnhanced performs daily checks.")]
        [SettingPropertyGroup("{=CME_General}General")]
        public bool ShowNotifications { get; set; } = false;

        [SettingPropertyBool(
            "{=CME_AllowExternalArmyJoin}Allow Player Clan Parties Join External Armies",
            RequireRestart = false,
            HintText = "{=CME_AllowExternalArmyJoinHint}If disabled, player clan parties can only stay in the player's own army and will leave non-player armies.")]
        [SettingPropertyGroup("{=CME_General}General")]
        public bool AllowPlayerClanPartiesJoinExternalArmies { get; set; } = true;

        [SettingPropertyBool(
            "{=CME_AutoCreateIdleClanParty}Auto Create Party For Idle Clan Members",
            RequireRestart = false,
            HintText = "{=CME_AutoCreateIdleClanPartyHint}Daily check: if a clan member is idle in a town tavern and party slots are available, automatically create a clan party for them.")]
        [SettingPropertyGroup("{=CME_General}General")]
        public bool AutoCreatePartyForIdleClanMembers { get; set; } = true;

        [SettingPropertyBool(
            "{=CME_PartyReinforce}Auto Reinforce Low-Strength Parties",
            RequireRestart = false,
            HintText = "{=CME_PartyReinforceHint}Daily check: extract troops from overgarrisoned castles to reinforce parties below strength threshold.")]
        [SettingPropertyGroup("{=CME_Reinforcement}Troop Reinforcement", GroupOrder = 1)]
        public bool AutoReinforceParties { get; set; } = true;

        [SettingPropertyFloatingInteger(
            "{=CME_PartyStrengthThreshold}Party Troop Strength Threshold %",
            0f, 100f,
            "0.0",
            RequireRestart = false,
            HintText = "{=CME_PartyStrengthThresholdHint}Parties below this percentage of max capacity will seek reinforcement (0-100%).")]
        [SettingPropertyGroup("{=CME_Reinforcement}Troop Reinforcement")]
        public float PartyTroopThresholdPercent { get; set; } = 50f;

        [SettingPropertyFloatingInteger(
            "{=CME_CastleOvergarrison}Castle Overgarrison Threshold %",
            0f, 100f,
            "0.0",
            RequireRestart = false,
            HintText = "{=CME_CastleOvergarrisonHint}Castles with garrison above this percentage of max capacity will provide troops (0-100%).")]
        [SettingPropertyGroup("{=CME_Reinforcement}Troop Reinforcement")]
        public float CastleOvergarrisonThresholdPercent { get; set; } = 50f;

        [SettingPropertyFloatingInteger(
            "{=CME_ReinforcePercent}Reinforcement Amount %",
            0f, 100f,
            "0.0",
            RequireRestart = false,
            HintText = "{=CME_ReinforcePercentHint}Percentage of party max capacity to extract per reinforcement (0-100%, rounded up).")]
        [SettingPropertyGroup("{=CME_Reinforcement}Troop Reinforcement")]
        public float ReinforcePercentOfPartyCapacity { get; set; } = 30f;

        [SettingPropertyBool(
            "{=CME_PrisonerTransfer}Auto Transfer Excess Prisoners to Dungeons",
            RequireRestart = false,
            HintText = "{=CME_PrisonerTransferHint}Daily check: transfer prisoners from overloaded parties to clan castle dungeons.")]
        [SettingPropertyGroup("{=CME_Prisoners}Prisoner Management", GroupOrder = 2)]
        public bool AutoTransferExcessPrisoners { get; set; } = true;

        [SettingPropertyFloatingInteger(
            "{=CME_PrisonerThreshold}Party Prisoner Capacity Threshold %",
            0f, 100f,
            "0.0",
            RequireRestart = false,
            HintText = "{=CME_PrisonerThresholdHint}Parties above this percentage of prisoner capacity will transfer excess to dungeons (0-100%).")]
        [SettingPropertyGroup("{=CME_Prisoners}Prisoner Management")]
        public float PartyPrisonerThresholdPercent { get; set; } = 50f;
    }
}
