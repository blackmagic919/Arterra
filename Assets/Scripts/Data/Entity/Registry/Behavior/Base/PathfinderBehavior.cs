using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class PathFinderBehavior : IBehavior {
        public PathFinder.PathInfo pathFinder;
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            pathFinder.hasPath = false;
            self.Register(this);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            self.Register(this);
        }

        public void OnDrawGizmos(BehaviorEntity.Animal self) {
            Gizmos.color = self.info.entityType % 2 == 0 ? Color.red : Color.blue;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(self.position), self.settings.collider.size * 2);
            PathFinder.PathInfo finder = pathFinder; //copy so we don't modify the original
            if (finder.hasPath) {
                int ind = finder.currentInd;
                while (ind != finder.path.Length) {
                    int dir = finder.path[ind];
                    int3 dest = finder.currentPos + new int3((dir / 9) - 1, (dir / 3 % 3) - 1, (dir % 3) - 1);
                    Gizmos.DrawLine(CPUMapManager.GSToWS(finder.currentPos),
                                    CPUMapManager.GSToWS(dest));
                    finder.currentPos = dest;
                    ind++;
                }
            }
        }
    }
}