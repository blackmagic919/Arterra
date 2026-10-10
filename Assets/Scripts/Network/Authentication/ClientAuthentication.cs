using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Arterra.Core.Network.Authentication {
    /// <summary>Unity-specific client login and session proof. No account tokens are sent to game hosts.</summary>
    public static class ClientAuthentication {
        internal const string ProofProperty = "arterra.connectionProof";
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
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            PlayerPrefs.SetString(CachedPlayerKey, AuthenticationService.Instance.PlayerId);
            PlayerPrefs.Save();
        }

        internal static async Task PublishProofAsync(ISession session, string challenge) {
            if (session.CurrentPlayer == null || session.CurrentPlayer.Id != AuthenticationService.Instance.PlayerId)
                throw new InvalidOperationException("Session does not belong to the signed-in player.");
            session.CurrentPlayer.SetProperty(ProofProperty, new PlayerProperty(challenge, VisibilityPropertyOptions.Member));
            await session.SaveCurrentPlayerDataAsync();
        }
    }
}
