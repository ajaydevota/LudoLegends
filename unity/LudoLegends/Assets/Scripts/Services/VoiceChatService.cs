using System;
using UnityEngine;

namespace LudoLegends.Client
{
    /// <summary>
    /// Provider boundary for in-room voice chat.
    ///
    /// Voice is optional and completely decoupled from gameplay. The game server only
    /// issues a short-lived, room-scoped token (/api/voice/token); the audio itself flows
    /// through the voice provider and never touches the game server's bandwidth.
    ///
    /// To enable it:
    ///   1. import the Photon Voice 2 package and add PHOTON_VOICE to Scripting Define Symbols
    ///   2. import Photon Realtime (shared by Fusion/Voice) and set the App Id
    ///   3. configure the server's custom-auth webhook (docs/voice-and-notifications.md)
    /// Until then every call is a no-op, and the UI hides the microphone button because
    /// app-config.json has voiceEnabled=false.
    /// </summary>
    public interface IVoiceProvider
    {
        bool IsConnected { get; }

        void Join(string channel, string token, string displayName);

        void Leave();

        void SetMicrophoneEnabled(bool enabled);

        void MutePlayer(string playerId, bool muted);

        void MuteAll(bool muted);
    }

    public static class VoiceChatService
    {
        private static IVoiceProvider _provider;

        public static bool MicrophoneEnabled { get; private set; } = true;

        public static bool GlobalMute { get; private set; }

        public static bool Available
        {
            get { return _provider != null && AppConfig.Current.voiceEnabled; }
        }

        public static string CurrentChannel { get; private set; }

        public static void UseProvider(IVoiceProvider provider)
        {
            _provider = provider;
        }

        /// <summary>Called when the local player enters a room with voice enabled.</summary>
        public static void JoinRoom(string channel, string token, string displayName)
        {
            if (!Available)
            {
                return;
            }

            try
            {
                _provider.Join(channel, token, displayName);
                CurrentChannel = channel;
                MicrophoneEnabled = true;
                _provider.SetMicrophoneEnabled(true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Voice join failed: " + ex.Message);
            }
        }

        /// <summary>Always called when leaving a room, disconnecting or being backgrounded.</summary>
        public static void LeaveRoom()
        {
            if (_provider == null)
            {
                CurrentChannel = null;
                return;
            }

            try
            {
                _provider.Leave();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Voice leave failed: " + ex.Message);
            }

            CurrentChannel = null;
        }

        public static void ToggleMicrophone()
        {
            if (!Available)
            {
                return;
            }

            MicrophoneEnabled = !MicrophoneEnabled;
            _provider.SetMicrophoneEnabled(MicrophoneEnabled);
        }

        public static void MutePlayer(string playerId, bool muted)
        {
            if (Available)
            {
                _provider.MutePlayer(playerId, muted);
            }
        }

        public static void SetGlobalMute(bool muted)
        {
            GlobalMute = muted;
            if (Available)
            {
                _provider.MuteAll(muted);
            }
        }
    }
}
