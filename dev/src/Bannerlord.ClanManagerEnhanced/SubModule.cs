using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.ClanManagerEnhanced
{
    public class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "Bannerlord.ClanManagerEnhanced";

        private Harmony? _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                Debug.Print($"[Bannerlord.ClanManagerEnhanced] SubModule loaded. Harmony patches applied. v{typeof(SubModule).Assembly.GetName().Version}");
            }
            catch (Exception ex)
            {
                Debug.Print($"[ClanManagerEnhanced] ERROR in OnSubModuleLoad: {ex}");
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=CME_INIT_FAIL}ClanManagerEnhanced failed to initialize Harmony. Check logs.").ToString(),
                    Colors.Red));
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
            }
            catch (Exception ex)
            {
                Debug.Print($"[ClanManagerEnhanced] ERROR in OnSubModuleUnloaded: {ex}");
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            if (game.GameType is Campaign)
            {
                var campaignStarter = (CampaignGameStarter)gameStarter;
                campaignStarter.AddBehavior(new ClanManagementBehavior());
                Debug.Print("[Bannerlord.ClanManagerEnhanced] ClanManagementBehavior registered for campaign.");
            }
        }
    }
}
