using System;
using System.Collections.Generic;
using Arterra.Data.Entity;
using Newtonsoft.Json;
using Unity.Mathematics;
using static Arterra.Data.Entity.EntitySetting;

namespace Arterra.Data.Entity.Behavior {
    public enum TransitionCheck {
        DestinationProfile,
        DestinationBody,
        SweepProfile,
        SweepBody,
        SweepBodyAndDestinationProfile
    }

    [Serializable]
    public struct PathTransition {
        public int3 Delta;
        public int NextGroup;
        public Optional<TransitionCheck> Check;
        public Optional<TransitionMovement> Movement;
    }

    [Serializable]
    public class PathTransitionGroup {
        // Disable for intermediate states, such as the apex of a hop.
        public bool CanEndPath = true;
        public List<PathTransition> Transitions = new();
    }

    [Serializable]
    public class TransitionPathfinderSettings : IBehaviorSetting {
        [JsonIgnore] public static string Name => "Transition Pathfinder";
        public int StartGroup;
        public List<PathTransitionGroup> Groups = CreateWalkingGroups();

        private static List<PathTransitionGroup> CreateWalkingGroups() {
            PathTransitionGroup walking = new();
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    for (int z = -1; z <= 1; z++) {
                        if (x == 0 && y == 0 && z == 0) continue;
                        walking.Transitions.Add(new PathTransition {
                            Delta = new int3(x, y, z),
                            NextGroup = 0,
                            Check = new Optional<TransitionCheck>(TransitionCheck.DestinationProfile)
                        });
                    }
                }
            }
            return new List<PathTransitionGroup> { walking };
        }

        public object Clone() {
            TransitionPathfinderSettings clone = new() { StartGroup = StartGroup, Groups = new() };
            foreach (PathTransitionGroup group in Groups) {
                clone.Groups.Add(new PathTransitionGroup {
                    CanEndPath = group.CanEndPath,
                    Transitions = new List<PathTransition>(group.Transitions)
                });
            }
            return clone;
        }
    }

    /// <summary>A pathfinder whose edges and movement states are defined by transition groups.</summary>
    public class TransitionPathfinderBehavior : PathFinderBehavior {
        [JsonIgnore] private TransitionPathfinderSettings settings;
        private delegate bool ProfileCheck(int3 position, bool bodyOnly, bool useExFlag);

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> dependencies) {
            base.AddSettingsDependencies(dependencies);
            dependencies.TryAdd(typeof(TransitionPathfinderSettings), new TransitionPathfinderSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 origin) {
            base.Initialize(self, setting, origin);
            BindSettings(setting);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 origin) {
            base.Deserialize(self, setting, ref origin);
            BindSettings(setting);
        }

        private void BindSettings(BehaviorEntity.AnimalSetting setting) {
            if (!setting.Is(out settings))
                throw new InvalidOperationException("Transition Pathfinder requires TransitionPathfinderSettings.");
            if (settings.Groups == null || settings.StartGroup < 0 || settings.StartGroup >= settings.Groups.Count)
                throw new InvalidOperationException("Transition Pathfinder requires a valid starting group.");
            foreach (PathTransitionGroup group in settings.Groups) {
                if (group?.Transitions == null)
                    throw new InvalidOperationException("Transition Pathfinder groups cannot be null.");
                foreach (PathTransition transition in group.Transitions) {
                    if (transition.NextGroup < 0 || transition.NextGroup >= settings.Groups.Count)
                        throw new InvalidOperationException("Transition Pathfinder has an invalid destination group.");
                }
            }
        }

        protected override PathData SearchPath(in int3 origin, in int3 end, int distance, in ProfileInfo profile, in EntityJob.Context context, out int cost) {
            return Search(origin, end, distance, MakeCheck(profile, context), null, false, out cost, out _);
        }

        protected override PathData SearchPath(in int3 origin, in int3 end, int distance, uint3 bounds, List<MatProfileE> profile, out int cost) {
            return Search(origin, end, distance, MakeCheck(bounds, profile), null, false, out cost, out _);
        }

        protected override PathData SearchPathOrApproachTarget(in int3 origin, in int3 end, int distance, in ProfileInfo profile, in EntityJob.Context context, out int cost) {
            return SearchPath(origin, end, distance, profile, context, out cost);
        }

        protected override PathData SearchPathOrApproachTarget(in int3 origin, in int3 end, int distance, uint3 bounds, List<MatProfileE> profile, out int cost) {
            return SearchPath(origin, end, distance, bounds, profile, out cost);
        }

        protected override PathData SearchMatchAlongRay(in int3 origin, in float3 direction, int distance, in ProfileInfo profile, in ProfileInfo target, in EntityJob.Context context, out int cost, out bool reached) {
            return Search(origin, RayEnd(direction, distance), distance, MakeCheck(profile, context), MakeCheck(target, context), true, out cost, out reached);
        }

        protected override PathData SearchMatchAlongRay(in int3 origin, in float3 direction, int distance, uint3 bounds, List<MatProfileE> profile, uint3 targetBounds, List<MatProfileE> target, out int cost, out bool reached) {
            return Search(origin, RayEnd(direction, distance), distance, MakeCheck(bounds, profile), MakeCheck(targetBounds, target), true, out cost, out reached);
        }

        protected override PathData SearchPathAlongRay(in int3 origin, ref float3 direction, int distance, in ProfileInfo profile, in EntityJob.Context context, out int cost) {
            PathData path = Search(origin, RayEnd(direction, distance), distance, MakeCheck(profile, context), null, true, out cost, out _);
            UpdateDirection(path, ref direction);
            return path;
        }

        protected override PathData SearchPathAlongRay(in int3 origin, ref float3 direction, int distance, uint3 bounds, List<MatProfileE> profile, out int cost) {
            PathData path = Search(origin, RayEnd(direction, distance), distance, MakeCheck(bounds, profile), null, true, out cost, out _);
            UpdateDirection(path, ref direction);
            return path;
        }

        private static int3 RayEnd(float3 direction, int distance) {
            float magnitude = math.cmax(math.abs(direction));
            return magnitude == 0 ? int3.zero : (int3)(direction / magnitude * distance);
        }

        private static void UpdateDirection(PathData path, ref float3 direction) {
            int3 displacement = int3.zero;
            for (int i = 0; i < path.Length; i++) displacement += path[i].Delta;
            if (math.any(displacement != 0)) direction = math.normalizesafe((float3)displacement);
        }

        private static ProfileCheck MakeCheck(ProfileInfo profile, EntityJob.Context context) {
            return (position, body, ex) => VerifyProfile(position, profile, context, ex, body);
        }

        private static ProfileCheck MakeCheck(uint3 bounds, List<MatProfileE> profile) {
            return (position, body, ex) => VerifyMatProfile(position, bounds, profile, ex, body);
        }

        public static bool ValidateStep(PathStep step, int3 start, ProfileInfo profile, EntityJob.Context context) {
            if (step.Group < 0) return VerifyProfile(start + step.Delta, profile, context);
            return ValidateStep(step, start, MakeCheck(profile, context));
        }

        public static bool ValidateStep(PathStep step, int3 start, uint3 bounds, List<MatProfileE> profile) {
            if (step.Group < 0) return VerifyMatProfile(start + step.Delta, bounds, profile);
            return ValidateStep(step, start, MakeCheck(bounds, profile));
        }

        private static bool ValidateStep(PathStep step, int3 start, ProfileCheck profile) {
            if (step.Group < 0) return profile(start + step.Delta, false, true);
            if (!step.Check.Enabled) return true;
            return CheckTransition(start, start + step.Delta, step.Check.Value, profile);
        }

        private static bool CheckTransition(int3 start, int3 end, TransitionCheck check, ProfileCheck profile) {
            switch (check) {
                case TransitionCheck.DestinationProfile:
                    return profile(end, false, true);
                case TransitionCheck.DestinationBody:
                    return profile(end, true, true);
                case TransitionCheck.SweepProfile:
                    return Sweep(start, end, false, profile);
                case TransitionCheck.SweepBody:
                    return Sweep(start, end, true, profile);
                case TransitionCheck.SweepBodyAndDestinationProfile:
                    return profile(end, false, true) && Sweep(start, end, true, profile);
                default:
                    return false;
            }
        }

        // Supercover traversal between voxel centers. At corner/edge crossings, check
        // every touched neighbor, not just the diagonal voxel. Body mode omits y = 0.
        private static bool Sweep(int3 start, int3 end, bool body, ProfileCheck profile) {
            int3 delta = end - start;
            int3 direction = math.sign(delta);
            float3 inverse = new float3(
                delta.x == 0 ? float.PositiveInfinity : 1f / math.abs(delta.x),
                delta.y == 0 ? float.PositiveInfinity : 1f / math.abs(delta.y),
                delta.z == 0 ? float.PositiveInfinity : 1f / math.abs(delta.z));
            float3 nextCrossing = inverse * 0.5f;
            int3 current = start;
            if (!profile(current, body, true)) return false;
            while (math.any(current != end)) {
                float crossing = math.cmin(nextCrossing);
                bool3 crossed = nextCrossing <= crossing + 1E-6f;
                int mask = (crossed.x ? 1 : 0) | (crossed.y ? 2 : 0) | (crossed.z ? 4 : 0);
                for (int subset = mask; subset != 0; subset = (subset - 1) & mask) {
                    int3 offset = new int3((subset & 1) != 0 ? direction.x : 0,
                        (subset & 2) != 0 ? direction.y : 0, (subset & 4) != 0 ? direction.z : 0);
                    if (!profile(current + offset, body, true)) return false;
                }
                if (crossed.x) { current.x += direction.x; nextCrossing.x += inverse.x; }
                if (crossed.y) { current.y += direction.y; nextCrossing.y += inverse.y; }
                if (crossed.z) { current.z += direction.z; nextCrossing.z += inverse.z; }
            }
            return true;
        }

        private struct SearchNode {
            public int4 State;
            public int Cost;
            public int Parent;
            public PathStep Step;
        }

        private PathData Search(int3 origin, int3 destination, int distance, ProfileCheck profile,
            ProfileCheck target, bool alongRay, out int expanded, out bool reached) {
            if (distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));

            destination = math.clamp(destination, -distance, distance);

            List<SearchNode> nodes = new();
            Dictionary<int4, int> indices = new();

            // Sequence breaks ties deterministically and keeps multiple entries for improved nodes.
            Utils.PriorityQueue<(int Node, int Cost), (int Score, int Sequence)> open = new();
            int sequence = 0;

            int4 start = new int4(int3.zero, settings.StartGroup);
            nodes.Add(new SearchNode { State = start, Parent = -1 });
            indices.Add(start, 0);
            open.Enqueue((0, 0), (Get3DDistance(math.abs(destination)), sequence++));

            int best = 0;
            int bestDistance = int.MaxValue;
            expanded = 0;
            reached = false;

            while (open.Count > 0) {
                var entry = open.Dequeue();
                SearchNode current = nodes[entry.Node];
                if (entry.Cost != current.Cost) continue;

                expanded++;

                int remaining = Get3DDistance(math.abs(destination - current.State.xyz));
                PathTransitionGroup group = settings.Groups[current.State.w];

                if (group.CanEndPath) {
                    if (remaining < bestDistance) {
                        best = entry.Node;
                        bestDistance = remaining;
                    }

                    if (target != null && target(origin + current.State.xyz, false, false)) {
                        best = entry.Node;
                        reached = true;
                        break;
                    }

                    if (target == null && remaining == 0) {
                        best = entry.Node;
                        break;
                    }

                    if (alongRay && current.Cost >= distance * 10) {
                        best = entry.Node;
                        break;
                    }
                }

                for (int i = 0; i < group.Transitions.Count; i++) {
                    PathTransition transition = group.Transitions[i];
                    int3 position = current.State.xyz + transition.Delta;
                    if (math.any(math.abs(position) > distance)) continue;

                    int4 state = new int4(position, transition.NextGroup);
                    int cost = current.Cost + Get3DDistance(math.abs(transition.Delta));

                    bool exists = indices.TryGetValue(state, out int index);
                    if (exists && nodes[index].Cost <= cost) continue;

                    if (transition.Check.Enabled && !CheckTransition(origin + current.State.xyz,
                        origin + position, transition.Check.Value, profile)) continue;

                    SearchNode next = new SearchNode {
                        State = state,
                        Cost = cost,
                        Parent = entry.Node,
                        Step = new PathStep {
                            Delta = transition.Delta,
                            Group = current.State.w,
                            TransitionIndex = i,
                            NextGroup = transition.NextGroup,
                            Check = transition.Check,
                            Movement = transition.Movement
                        }
                    };

                    if (exists) {
                        nodes[index] = next;
                    } else {
                        index = nodes.Count;
                        indices.Add(state, index);
                        nodes.Add(next);
                    }

                    open.Enqueue((index, cost), (cost + Get3DDistance(math.abs(destination - position)), sequence++));
                }
            }

            List<PathStep> path = new();

            for (int index = best; nodes[index].Parent >= 0; index = nodes[index].Parent)
                path.Add(nodes[index].Step);

            path.Reverse();

            return new TransitionPath(path.ToArray());
        }
    }
}
