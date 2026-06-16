using System;
using TaleWorlds.Library;

namespace Bannerlord.ClanManagerEnhanced
{
    internal static class InformationManager
    {
        private static bool? _messageOutputEnabled;
        private static DateTime _lastSettingsCheckUtc = DateTime.MinValue;

        internal static void DisplayMessage(InformationMessage message)
        {
            if (!IsMessageOutputEnabled())
            {
                return;
            }

            TaleWorlds.Library.InformationManager.DisplayMessage(message);
        }

        private static bool IsMessageOutputEnabled()
        {
            var now = DateTime.UtcNow;
            if (_messageOutputEnabled == null || (now - _lastSettingsCheckUtc).TotalSeconds > 30)
            {
                try
                {
                    var settings = ClanManagerSettings.Instance;
                    _messageOutputEnabled = settings?.ShowNotifications ?? false;
                    _lastSettingsCheckUtc = now;
                }
                catch
                {
                    _messageOutputEnabled = false;
                }
            }

            return _messageOutputEnabled == true;
        }
    }
}
