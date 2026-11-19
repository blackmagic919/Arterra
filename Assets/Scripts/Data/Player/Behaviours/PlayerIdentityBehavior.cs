using System;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Core.Events;
using Arterra.Core.Network;
using Arterra.Core.Network.Service;
using Arterra.Core.Storage;
using Arterra.GamePlay;
using Newtonsoft.Json;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class PlayerIdentityBehavior : SpeciesBehavior {
        [JsonProperty] private NetworkVariable<PlayerProfile> profile = new("PlayerProfile", NetworkPermission.Server);
        private CancellationTokenSource lifetime;
        private GameObject nameTag;
        private TMP_Text label;
        private bool identityPending;
        private bool inheritedProfile;
        private PlayerProfile resolvedProfile;

        [JsonIgnore] public PlayerProfile Profile => profile.HasValue ? profile.Value : null;
        [JsonIgnore] public Guid UserId => Profile?.UserId ?? Guid.Empty;

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 coord) =>
            Bind(self);

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 coord) =>
            Bind(self);

        private void Bind(BehaviorEntity.Animal self) {
            lifetime = new CancellationTokenSource();
            profile.Register(self);
            if (self.IsAuthority) {
                if (!string.IsNullOrWhiteSpace(Profile?.DisplayName)) resolvedProfile = Profile;
                self.eventCtrl.AddEventHandler(GameEvent.Entity_Respawn, TransferProfile);
                identityPending = true;
            }
        }

        private void TryResolveUser(BehaviorEntity.Animal self) {
            if (self.IsOwner) {
                // Hosting authenticates the service connection. Offline worlds need no new login.
                Guid? userId = NetworkManager.Service.UserId;
                if (!userId.HasValue) return;
                identityPending = false;
                SetUser(self, userId.Value);
                return;
            }

            foreach (var player in PlayerHandler.Players) {
                if (!ReferenceEquals(player.Value, self)) continue;
                if (!TransportManager.Instance.TryGetUser(player.Key, out var userId)) return;
                identityPending = false;
                SetUser(self, userId);
                return;
            }
            // An unloaded connection or a previous player body has no identity to resolve.
            identityPending = false;
        }

        /// <summary>Only the host assigns identities obtained from the authenticated connection.</summary>
        private void SetUser(BehaviorEntity.Animal self, Guid userId) {
            if (!self.IsAuthority) throw new UnauthorizedAccessException("Only the host assigns player identity.");
            if (userId == Guid.Empty) throw new ArgumentException("A player requires a service user ID.", nameof(userId));
            if (inheritedProfile && UserId == userId) {
                inheritedProfile = false;
                return;
            }
            inheritedProfile = false;
            lifetime.Cancel();
            lifetime.Dispose();
            lifetime = new CancellationTokenSource();
            if (UserId != userId) {
                resolvedProfile = null;
                profile.Value = new PlayerProfile { UserId = userId };
            }
            _ = RefreshAsync(self, userId, lifetime.Token);
        }

        private async Task RefreshAsync(BehaviorEntity.Animal self, Guid userId, CancellationToken token) {
            uint session = TransportManager.Instance.SessionVersion;
            int retrySeconds = 5;
            while (!token.IsCancellationRequested && self.active && session == TransportManager.Instance.SessionVersion) {
                try {
                    PlayerProfile value = await NetworkManager.Service.GetPlayerProfileAsync(userId, token);
                    if (token.IsCancellationRequested || !self.active || session != TransportManager.Instance.SessionVersion) return;
                    if (value == null) throw new InvalidOperationException("Game service returned an empty player profile.");
                    value.UserId = userId;
                    resolvedProfile = value;
                    profile.Value = value;
                    return;
                } catch (OperationCanceledException) {
                    return;
                } catch (Exception exception) {
                    if (!token.IsCancellationRequested) Debug.LogWarning($"Could not load player profile: {exception.Message}");
                }
                try { await Task.Delay(TimeSpan.FromSeconds(retrySeconds), token); }
                catch (OperationCanceledException) { return; }
                retrySeconds = Math.Min(retrySeconds * 2, 30);
            }
        }

        private void TransferProfile(object source, object target, object context) {
            if (target is not Guid id || !EntityManager.TryGetEntity(id, out var entity)
                || !entity.Is(out PlayerIdentityBehavior replacement)) return;
            identityPending = false;
            lifetime.Cancel();
            if (resolvedProfile == null) return;
            replacement.lifetime.Cancel();
            var value = new PlayerProfile {
                UserId = resolvedProfile.UserId,
                DisplayName = resolvedProfile.DisplayName
            };
            replacement.profile.Value = value;
            replacement.resolvedProfile = value;
            replacement.inheritedProfile = true;
        }

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            if (self.IsOwner || nameTag != null) return;
            var prefab = Resources.Load<GameObject>("Prefabs/GameUI/Stats/NameTagDisplay");
            nameTag = GameObject.Instantiate(prefab, self.controller.transform);
            label = nameTag.GetComponentInChildren<TMP_Text>(true);
            label.richText = false;
            nameTag.SetActive(false);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Sync && self.IsAuthority && identityPending)
                TryResolveUser(self);
            if (self.context != BehaviorEntity.UpdateContext.Visual || nameTag == null) return;
            string displayName = Profile?.DisplayName;
            bool visible = NetworkManager.SessionId.HasValue && TransportManager.Instance.IsRunning
                && !self.IsOwner && !string.IsNullOrWhiteSpace(displayName);
            nameTag.SetActive(visible);
            if (!visible) return;
            if (label.text != displayName) label.text = displayName;
            float3 top = self.origin + new float3(self.settings.collider.size.x / 2,
                self.settings.collider.size.y, self.settings.collider.size.z / 2);
            nameTag.transform.position = (Vector3)CPUMapManager.GSToWS(top) + Vector3.up * 0.25f;
            if (Camera.main != null) nameTag.transform.rotation = Camera.main.transform.rotation;
        }

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (nameTag != null) GameObject.Destroy(nameTag);
            nameTag = null;
            label = null;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Respawn, TransferProfile);
            identityPending = false;
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            DisposeVisual(self);
            profile.Dispose();
        }
    }
}
