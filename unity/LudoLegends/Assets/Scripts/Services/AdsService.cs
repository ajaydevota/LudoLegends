using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// Ad policy gate. Ads are NOT implemented in this build: there is no AdMob SDK call
    /// anywhere in the project, no ad unit is configured, and no button pretends otherwise.
    ///
    /// What exists is the rule set a future integration must obey, so gameplay is never
    /// interrupted at the wrong moment:
    ///   * ads stay off unless adsEnabled is true AND a unit id is configured AND the player
    ///     has not purchased the "remove ads" entitlement;
    ///   * an interstitial may only be requested between turns - never while the dice is
    ///     rolling, a token is animating, or a match is in progress;
    ///   * rewarded ads can never grant coins, because coins are only ever awarded by
    ///     verified server logic; a modified client cannot mint currency by faking a view.
    ///
    /// To add real ads: import Google Mobile Ads, set the unit ids in
    /// Assets/Resources/app-config.json, then call the plugin from these three methods.
    /// The gating logic below stays exactly as is.
    /// </summary>
    public static class AdsService
    {
        private static bool _roundActive;

        /// <summary>Whether the policy currently allows any ad request at all.</summary>
        public static bool Enabled
        {
            get
            {
                AppConfig config = AppConfig.Current;
                return config.adsEnabled &&
                       !string.IsNullOrEmpty(config.admobBannerUnitId) &&
                       !(AppSession.Current.Profile?.adsRemoved ?? false);
            }
        }

        /// <summary>True only between turns, when it is safe to cover the screen.</summary>
        public static bool CanShowNow(bool diceAnimating, bool tokenAnimating, bool matchInProgress)
        {
            return Enabled && !_roundActive && !matchInProgress && !diceAnimating && !tokenAnimating;
        }

        /// <summary>Set while a match is running so no ad can appear mid-game.</summary>
        public static void SetRoundActive(bool active)
        {
            _roundActive = active;
        }

        /// <summary>Called by menu screens; a no-op until an ad provider is integrated.</summary>
        public static void SetBannerVisible(bool visible)
        {
            if (!Enabled || visible == false)
            {
                return;
            }

            Debug.Log("Ads are enabled in config but no ad provider is integrated; staying ad-free.");
        }

        /// <summary>Called between matches; a no-op until an ad provider is integrated.</summary>
        public static void ShowInterstitial()
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log("Interstitial requested, but no ad provider is integrated; nothing was shown.");
        }
    }
}
