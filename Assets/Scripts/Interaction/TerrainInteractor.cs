using System;
using Arterra.Core.Storage;
using Unity.Mathematics;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Data.Material;
using static Arterra.Core.Storage.CPUMapManager;

namespace Arterra.GamePlay.Interaction {
    public static class TerrainInteractor {

        private const byte Solid = 0x1;
        private const byte Liquid = 0x2;
        private const byte Gas = 0x4;
        public static bool IsTouching(byte data) => (data & (Solid | Liquid)) != 0;
        public static bool TouchSolid(byte data) => (data & Solid) != 0;
        public static bool TouchLiquid(byte data) => (data & Liquid) != 0;
        public static bool TouchGas(byte data) => (data & Gas) != 0;
        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static int3x3 XZPlane => new(1, 0, 0, 0, 0, 1, 0, 1, 0); //xzy
        private static int3x3 XYPlane => new(1, 0, 0, 0, 1, 0, 0, 0, 1); //xyz
        private static int3x3 YZPlane => new(0, 0, 1, 1, 0, 0, 0, 1, 0); //zxy
        private static bool TrilinearBlend(ref Span<int> c, float3 d, out int corner) {
            float c00 = c[0] * (1 - d.x) + c[4] * d.x;
            float c01 = c[1] * (1 - d.x) + c[5] * d.x;
            float c10 = c[2] * (1 - d.x) + c[6] * d.x;
            float c11 = c[3] * (1 - d.x) + c[7] * d.x;

            corner = 0;
            float c0 = c00 * (1 - d.y) + c10 * d.y;
            float c1 = c01 * (1 - d.y) + c11 * d.y;
            float density = c0 * (1 - d.z) + c1 * d.z;
            if (density < IsoValue) return false;

            float mDens = 0;
            for (int i = 0; i < 8; i++) {
                int3 cn = new(i & 0x4, i & 0x2, i & 0x1);
                float cDens = (c[7 - i] - IsoValue) * math.abs(cn.x - d.x)
                    * math.abs(cn.y - d.y) * math.abs(cn.z - d.z);
                if (cDens < mDens) continue;
                mDens = cDens; corner = 7 - i;
            }
            return true;
        }

        private static byte TrilinearContact(float3 posGS, Entity caller, ref float friction) {
            //Calculate Density
            int3 l; int3 u;
            l.x = (int)Math.Floor(posGS.x); u.x = l.x + 1;
            l.y = (int)Math.Floor(posGS.y); u.y = l.y + 1;
            l.z = (int)Math.Floor(posGS.z); u.z = l.z + 1;
            float3 d = posGS - l;
            byte contact = 0;

            Action<object, object, object> touchMatL = null;
            Action<object, object, object> touchMatS = null;
            static int3 ccoord(int3 origin, int cI) => origin + new int3(cI & 0x4, cI & 0x2, cI & 0x1);

            Span<MapData> m = stackalloc MapData[8] {
                SampleMap(l),                       SampleMap(new int3(l.x, l.y, u.z)), SampleMap(new int3(l.x, u.y, l.z)),
                SampleMap(new int3(l.x, u.y, u.z)), SampleMap(new int3(u.x, l.y, l.z)), SampleMap(new int3(u.x, l.y, u.z)),
                SampleMap(new int3(u.x, u.y, l.z)), SampleMap(u)
            };

            Span<int> c = stackalloc int[8] {
                m[0].LiquidDensity, m[1].LiquidDensity, m[2].LiquidDensity,
                m[3].LiquidDensity, m[4].LiquidDensity, m[5].LiquidDensity,
                m[6].LiquidDensity, m[7].LiquidDensity
            };

            if (TrilinearBlend(ref c, d, out int corner)) {
                contact |= Liquid; //Collide with barrier(null)
                if (!m[corner].IsNull) {
                    int3 coord = ccoord(l, corner);
                    MaterialData mat = matInfo.Retrieve(m[corner].material);
                    mat.OnEntityTouchLiquid(coord, caller);
                    friction = math.max(mat.Roughness, friction);

                    touchMatL ??= caller?.eventCtrl.GetRaiseEventAction(Core.Events.GameEvent.Entity_TouchMatLiquid);
                    touchMatL?.Invoke(caller, m[corner], coord);
                }
            }


            c[0] = m[0].SolidDensity; c[1] = m[1].SolidDensity; c[2] = m[2].SolidDensity;
            c[3] = m[3].SolidDensity; c[4] = m[4].SolidDensity; c[5] = m[5].SolidDensity;
            c[6] = m[6].SolidDensity; c[7] = m[7].SolidDensity;

            if (TrilinearBlend(ref c, d, out corner)) {
                contact |= Solid; //Collide with barrier(null)
                if (!m[corner].IsNull) {
                    int3 coord = ccoord(l, corner);
                    MaterialData mat = matInfo.Retrieve(m[corner].material);
                    friction = math.max(mat.Roughness, friction);
                    mat.OnEntityTouchSolid(coord, caller);

                    touchMatS ??= caller?.eventCtrl.GetRaiseEventAction(Core.Events.GameEvent.Entity_TouchMatSolid);
                    touchMatS?.Invoke(caller, m[corner], coord);
                }
            } 

            contact |= IsTouching(contact) ? (byte)0 : Gas;
            return contact;
        }

        private static bool BilinearBlend(ref Span<int> c, float2 d, out int corner) {
            float c0 = c[0] * (1 - d.x) + c[2] * d.x;
            float c1 = c[1] * (1 - d.x) + c[3] * d.x;

            corner = 0;
            float density = c0 * (1 - d.y) + c1 * d.y;
            if (density < IsoValue) return false;

            float mDens = 0;
            for (int i = 0; i < 4; i++) {
                int2 cn = new(i & 0x2, i & 0x1);
                float cDens = (c[3 - i] - IsoValue) * math.abs(cn.x - d.x) * math.abs(cn.y - d.y);
                if (cDens < mDens) continue;
                mDens = cDens;
                corner = 3 - i;
            }
            return true;
        }

        private static byte BilinearContact(float2 posGS, in int3x3 transform, int axis, Entity caller, ref float friction) {
            int2 l; int2 u;
            l.x = (int)Math.Floor(posGS.x); u.x = l.x + 1;
            l.y = (int)Math.Floor(posGS.y); u.y = l.y + 1;
            float2 d = posGS - l;
            byte contact = 0;

            Action<object, object, object> touchMatL = null;
            Action<object, object, object> touchMatS = null;

            Span<int3> coords = stackalloc int3[4] {
                math.mul(transform, new int3(l.x, l.y, axis)),
                math.mul(transform, new int3(l.x, u.y, axis)),
                math.mul(transform, new int3(u.x, l.y, axis)),
                math.mul(transform, new int3(u.x, u.y, axis))
            };

            Span<MapData> m = stackalloc MapData[4] {
                SampleMap(coords[0]), SampleMap(coords[1]),
                SampleMap(coords[2]), SampleMap(coords[3])
            };

            Span<int> c = stackalloc int[4] {
                m[0].LiquidDensity, m[1].LiquidDensity,
                m[2].LiquidDensity, m[3].LiquidDensity
            };

            if (BilinearBlend(ref c, d, out int corner)) {
                contact |= Liquid; //Collide with barrier(null)
                if (!m[corner].IsNull) {
                    MaterialData mat = matInfo.Retrieve(m[corner].material);
                    friction = math.max(mat.Roughness, friction);
                    mat.OnEntityTouchLiquid(coords[corner], caller);

                    touchMatL ??= caller?.eventCtrl.GetRaiseEventAction(Core.Events.GameEvent.Entity_TouchMatLiquid);
                    touchMatL?.Invoke(caller, m[corner], coords[corner]);
                }
            }

            c[0] = m[0].SolidDensity; c[1] = m[1].SolidDensity;
            c[2] = m[2].SolidDensity; c[3] = m[3].SolidDensity;

            if (BilinearBlend(ref c, d, out corner)) {
                contact |= Solid; //Collide with barrier(null)
                if (!m[corner].IsNull) {
                    MaterialData mat = matInfo.Retrieve(m[corner].material);
                    friction = math.max(mat.Roughness, friction);
                    mat.OnEntityTouchSolid(coords[corner], caller);

                    touchMatS ??= caller?.eventCtrl.GetRaiseEventAction(Core.Events.GameEvent.Entity_TouchMatSolid);
                    touchMatS?.Invoke(caller, m[corner], coords[corner]);
                }
            }

            contact |= IsTouching(contact) ? (byte)0 : Gas;
            return contact;
        }

        private static bool LinearBlend(int c0, int c1, float t, out int corner) {
            float density = c0 * (1 - t) + c1 * t;
            corner = 0;

            if (density < IsoValue) return false;
            if (c0 * (1 - t) > c1 * t) corner = 0;
            else corner = 1;
            return true;
        }
        private static byte LinearContact(float posGS, in int3x3 transform, in int2 plane, Entity caller, ref float friction) {
            int t0 = (int)Math.Floor(posGS);
            int t1 = t0 + 1;

            int3 c0 = math.mul(transform, new int3(plane.x, plane.y, t0));
            int3 c1 = math.mul(transform, new int3(plane.x, plane.y, t1));
            MapData m0 = SampleMap(c0);
            MapData m1 = SampleMap(c1);
            float td = posGS - t0;
            byte contact = 0;

            if (LinearBlend(m0.LiquidDensity, m1.LiquidDensity, td, out int corner)) {
                MapData pt = corner != 0 ? m1 : m0;
                int3 c = corner != 0 ? c1 : c0;
                contact |= Liquid;

                if (pt.IsNull) return contact;
                MaterialData mat = matInfo.Retrieve(pt.material);
                friction = math.max(mat.Roughness, friction);
                mat.OnEntityTouchLiquid(c, caller);

                caller?.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_TouchMatLiquid, caller, pt, c);
            } else if (LinearBlend(m0.SolidDensity, m1.SolidDensity, td, out corner)) {
                MapData pt = corner != 0 ? m1 : m0;
                int3 c = corner != 0 ? c1 : c0;
                contact |= Solid;

                if (pt.IsNull) return contact;
                MaterialData mat = matInfo.Retrieve(pt.material);
                friction = math.max(mat.Roughness, friction);
                mat.OnEntityTouchSolid(c, caller);

                caller?.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_TouchMatSolid, caller, pt, c);
            }

            contact |= IsTouching(contact) ? (byte)0 : Gas;
            return contact;
        }

        private static byte UnitContact(int3 posGS, Entity caller, ref float friction) {
            MapData m = SampleMap(posGS);
            if (m.LiquidDensity >= IsoValue) {
                if (m.IsNull) return Liquid;
                MaterialData mat = matInfo.Retrieve(m.material);
                friction = math.max(mat.Roughness, friction);
                mat.OnEntityTouchLiquid(posGS, caller);

                caller?.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_TouchMatSolid, caller, m, posGS);
                return Liquid;
            } else if (m.SolidDensity >= IsoValue) {
                if (m.IsNull) return Solid;
                MaterialData mat = matInfo.Retrieve(m.material);
                friction = math.max(mat.Roughness, friction);
                mat.OnEntityTouchSolid(posGS, caller);

                caller?.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_TouchMatSolid, caller, m, posGS);
                return Solid;
            }
            return 0;
        }

        private static byte SampleUnitContact(float3 originGS, float3 boundsGS, Entity caller, ref float friction) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            int3 minC = (int3)math.ceil(min);
            int3 maxC = (int3)math.floor(max);
            byte contacted = 0;

            int3 coord = int3.zero;
            for (coord.x = minC.x; coord.x <= maxC.x; coord.x++) {
                for (coord.y = minC.y; coord.y <= maxC.y; coord.y++) {
                    for (coord.z = minC.z; coord.z <= maxC.z; coord.z++) {
                        contacted |= UnitContact(coord, caller, ref friction);
                    }
                }
            }
            return contacted;
        }


        private static byte SampleFaceContact(float3 originGS, float3 boundsGS, Entity caller, ref float friction) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            int3 minC = (int3)math.ceil(min);
            int3 maxC = (int3)math.floor(max);
            byte contacted = 0;

            //3*2 = 6 faces
            for (int x = minC.x; x <= maxC.x; x++) {
                for (int y = minC.y; y <= maxC.y; y++) {
                    contacted |= LinearContact(min.z, XYPlane, new int2(x, y), caller, ref friction);
                    contacted |= LinearContact(max.z, XYPlane, new int2(x, y), caller, ref friction);
                }
            }

            for (int x = minC.x; x <= maxC.x; x++) {
                for (int z = minC.z; z <= maxC.z; z++) {
                    contacted |= LinearContact(min.y, XZPlane, new int2(x, z), caller, ref friction);
                    contacted |= LinearContact(max.y, XZPlane, new int2(x, z), caller, ref friction);
                }
            }

            for (int y = minC.y; y <= maxC.y; y++) {
                for (int z = minC.z; z <= maxC.z; z++) {
                    contacted |= LinearContact(min.x, YZPlane, new int2(y, z), caller, ref friction);
                    contacted |= LinearContact(max.x, YZPlane, new int2(y, z), caller, ref friction);
                }
            }
            return contacted;
        }

        private static byte SampleEdgeContact(float3 originGS, float3 boundsGS, Entity caller, ref float friction) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            int3 minC = (int3)math.ceil(min);
            int3 maxC = (int3)math.floor(max);
            byte contacted = 0;

            //3*4 = 12 edges
            for (int x = minC.x; x <= maxC.x; x++) {
                for (int i = 0; i < 4; i++) {
                    int2 index = new(i % 2, i / 2 % 2);
                    float2 corner = min.yz * index + max.yz * (1 - index);
                    contacted |= BilinearContact(corner, YZPlane, x, caller, ref friction);
                }
            }

            for (int y = minC.y; y <= maxC.y; y++) {
                for (int i = 0; i < 4; i++) {
                    int2 index = new(i % 2, i / 2 % 2);
                    float2 corner = min.xz * index + max.xz * (1 - index);
                    contacted |= BilinearContact(corner, XZPlane, y, caller, ref friction);
                }
            }

            for (int z = minC.z; z <= maxC.z; z++) {
                for (int i = 0; i < 4; i++) {
                    int2 index = new(i % 2, i / 2 % 2);
                    float2 corner = min.xy * index + max.xy * (1 - index);
                    contacted |= BilinearContact(corner, XYPlane, z, caller, ref friction);
                }
            }
            return contacted;
        }

        private static byte SampleCornerContact(float3 originGS, float3 boundsGS, Entity caller, ref float friction) {
            float3 min = math.min(originGS, originGS + boundsGS);
            float3 max = math.max(originGS, originGS + boundsGS);
            byte contacted = 0;

            //8 corners
            for (int i = 0; i < 8; i++) {
                int3 index = new(i % 2, i / 2 % 2, i / 4);
                float3 corner = min * index + max * (1 - index);
                contacted |= TrilinearContact(corner, caller, ref friction);
            }
            return contacted;
        }

        public static byte SampleContact(float3 originGS, float3 boundsGS, out float friction, Entity caller = null) {
            byte contacted = 0; friction = 0;
            contacted |= SampleCornerContact(originGS, boundsGS, caller, ref friction);
            contacted |= SampleEdgeContact(originGS, boundsGS, caller, ref friction);
            contacted |= SampleFaceContact(originGS, boundsGS, caller, ref friction);
            contacted |= SampleUnitContact(originGS, boundsGS, caller, ref friction);
            return contacted;
        }


        public static void DetectMapInteraction(float3 originGS, Action<float> OnInSolid = null, Action<float> OnInLiquid = null, Action<float> OnInGas = null) {
            static (float, float) TrilinearBlend(float3 posGS) {
                //Calculate Density
                int x0 = (int)Math.Floor(posGS.x); int x1 = x0 + 1;
                int y0 = (int)Math.Floor(posGS.y); int y1 = y0 + 1;
                int z0 = (int)Math.Floor(posGS.z); int z1 = z0 + 1;

                MapData c000 = SampleMap(new int3(x0, y0, z0));
                MapData c100 = SampleMap(new int3(x1, y0, z0));
                MapData c010 = SampleMap(new int3(x0, y1, z0));
                MapData c110 = SampleMap(new int3(x1, y1, z0));
                MapData c001 = SampleMap(new int3(x0, y0, z1));
                MapData c101 = SampleMap(new int3(x1, y0, z1));
                MapData c011 = SampleMap(new int3(x0, y1, z1));
                MapData c111 = SampleMap(new int3(x1, y1, z1));
                if (c000.IsNull) c000.data &= 0xFFFF0000;
                if (c100.IsNull) c100.data &= 0xFFFF0000;
                if (c010.IsNull) c010.data &= 0xFFFF0000;
                if (c110.IsNull) c110.data &= 0xFFFF0000;
                if (c001.IsNull) c001.data &= 0xFFFF0000;
                if (c101.IsNull) c101.data &= 0xFFFF0000;
                if (c011.IsNull) c011.data &= 0xFFFF0000;
                if (c111.IsNull) c111.data &= 0xFFFF0000;

                float xd = posGS.x - x0;
                float yd = posGS.y - y0;
                float zd = posGS.z - z0;

                float c00 = c000.density * (1 - xd) + c100.density * xd;
                float c01 = c001.density * (1 - xd) + c101.density * xd;
                float c10 = c010.density * (1 - xd) + c110.density * xd;
                float c11 = c011.density * (1 - xd) + c111.density * xd;

                float c0 = c00 * (1 - yd) + c10 * yd;
                float c1 = c01 * (1 - yd) + c11 * yd;
                float density = c0 * (1 - zd) + c1 * zd;

                c00 = c000.viscosity * (1 - xd) + c100.viscosity * xd;
                c01 = c001.viscosity * (1 - xd) + c101.viscosity * xd;
                c10 = c010.viscosity * (1 - xd) + c110.viscosity * xd;
                c11 = c011.viscosity * (1 - xd) + c111.viscosity * xd;

                c0 = c00 * (1 - yd) + c10 * yd;
                c1 = c01 * (1 - yd) + c11 * yd;
                float viscosity = c0 * (1 - zd) + c1 * zd;
                return (density, viscosity);
            }

            (float density, float viscoity) = TrilinearBlend(originGS);
            if (viscoity > CPUMapManager.IsoValue) OnInSolid?.Invoke(viscoity);
            else if (density - viscoity > CPUMapManager.IsoValue) OnInLiquid?.Invoke(density - viscoity);
            else OnInGas?.Invoke(density);

            //int3 coordGS = (int3)math.round(centerGS);
            //int material = SampleMap(coordGS).material;
        }
    }
}
