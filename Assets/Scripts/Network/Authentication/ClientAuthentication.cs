using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Arterra.Core.Network.Authentication {
    /// <summary>Unity identity-provider login. The game service exchanges its JWT for an Arterra login.</summary>
    public static class ClientAuthentication {
        private const string CachedPlayerKey = "Arterra.UnityPlayerId";
        private const string LocalProfileKey = "Arterra.LocalProfileId";
        private static Task initialization;

        public static AccountId LocalProfile {
            get {
                string id = PlayerPrefs.GetString(LocalProfileKey, "");
                if (string.IsNullOrEmpty(id)) {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(LocalProfileKey, id);
                    PlayerPrefs.Save();
                }
                return new AccountId("Local", id);
            }
        }
        // The cached identity is only for local saves, never evidence about a remote peer.
        public static AccountId LocalAccount => PlayerPrefs.HasKey(CachedPlayerKey)
            ? new AccountId("Unity", PlayerPrefs.GetString(CachedPlayerKey)) : LocalProfile;

        public static Task EnsureSignedInAsync() {
            if (initialization == null || initialization.IsFaulted || initialization.IsCanceled
                || (initialization.IsCompleted && (UnityServices.State != ServicesInitializationState.Initialized
                    || !AuthenticationService.Instance.IsSignedIn)))
                initialization = SignInAsync();
            return initialization;
        }

        private static async Task SignInAsync() {
            if (UnityServices.State != ServicesInitializationState.Initialized) {
                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Initializing Unity services.");
                await UnityServices.InitializeAsync();
                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Unity services initialized.");
            }
            if (!AuthenticationService.Instance.IsSignedIn) {
                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Signing in anonymously.");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Sign-in complete.");
            }
            PlayerPrefs.SetString(CachedPlayerKey, AuthenticationService.Instance.PlayerId);
            PlayerPrefs.Save();
        }

    }
}
