using UnityEngine;

namespace Arterra.Engine.Rendering {
    public struct FrustumLightVolumeCpuData {
        public Matrix4x4 wsToFs;
        public Matrix4x4 fsToWs;
    }

    public static class FrustumLightVolumeBuilder {
        const float DirectionEpsilon = 1e-6f;
        const float RectExtentEpsilon = 1e-5f;

        public static FrustumLightVolumeCpuData Build(Camera camera, Vector3 lightDirection, float atmosphereRadius) {
            Vector3 cameraPos = camera.transform.position;
            Vector3 dir = lightDirection.sqrMagnitude > DirectionEpsilon ? lightDirection.normalized : Vector3.up;

            Matrix4x4 lightToWorld = BuildLightToWorld(cameraPos, dir);
            Matrix4x4 worldToLight = lightToWorld.inverse;
            Vector3 cameraLS = worldToLight.MultiplyPoint3x4(cameraPos);

            Vector3[] shellPointsLS = CollectFrustumShellPointsLS(camera, worldToLight, atmosphereRadius);

            Vector2 minXY = new Vector2(cameraLS.x, cameraLS.y);
            Vector2 maxXY = minXY;
            for (int i = 0; i < shellPointsLS.Length; i++) {
                Vector2 p = new Vector2(shellPointsLS[i].x, shellPointsLS[i].y);
                minXY = Vector2.Min(minXY, p);
                maxXY = Vector2.Max(maxXY, p);
            }

            Matrix4x4 lsToFsRect = BuildLSToFSMatrix(minXY, maxXY);

            float footprintPlaneDepthLS = cameraLS.z;
            for (int i = 0; i < shellPointsLS.Length; i++) {
                footprintPlaneDepthLS = Mathf.Min(footprintPlaneDepthLS, shellPointsLS[i].z);
            }

            Matrix4x4 lsDepthOffset = Matrix4x4.identity;
            lsDepthOffset.m23 = -footprintPlaneDepthLS;
            Matrix4x4 fsDepthOffset = Matrix4x4.identity;
            fsDepthOffset.m23 = footprintPlaneDepthLS;

            Matrix4x4 wsToFs = lsDepthOffset * lsToFsRect * worldToLight;
            Matrix4x4 fsToWs = lightToWorld * lsToFsRect.inverse * fsDepthOffset;

            return new FrustumLightVolumeCpuData {
                wsToFs = wsToFs,
                fsToWs = fsToWs
            };
        }

        public static void DrawFrustumShadowFootprintGizmos(Camera camera, Vector3 lightDirection, float atmosphereRadius, Color color) {
            if (camera == null) {
                return;
            }

            FrustumLightVolumeCpuData volume = Build(camera, lightDirection, atmosphereRadius);
            Matrix4x4 fsToWs = volume.fsToWs;

            Vector3 p00 = fsToWs.MultiplyPoint3x4(new Vector3(0.0f, 0.0f, 0.0f));
            Vector3 p10 = fsToWs.MultiplyPoint3x4(new Vector3(1.0f, 0.0f, 0.0f));
            Vector3 p11 = fsToWs.MultiplyPoint3x4(new Vector3(1.0f, 1.0f, 0.0f));
            Vector3 p01 = fsToWs.MultiplyPoint3x4(new Vector3(0.0f, 1.0f, 0.0f));

            Color prevColor = Gizmos.color;
            Gizmos.color = color;
            Gizmos.DrawLine(p00, p10);
            Gizmos.DrawLine(p10, p11);
            Gizmos.DrawLine(p11, p01);
            Gizmos.DrawLine(p01, p00);

            float marker = Mathf.Max(atmosphereRadius * 0.01f, 1.0f);
            Gizmos.DrawWireSphere(p00, marker);
            Gizmos.DrawWireSphere(p10, marker);
            Gizmos.DrawWireSphere(p11, marker);
            Gizmos.DrawWireSphere(p01, marker);
            Gizmos.color = prevColor;
        }

        static Matrix4x4 BuildLSToFSMatrix(Vector2 minXY, Vector2 maxXY) {
            Vector2 extent = maxXY - minXY;
            extent.x = Mathf.Max(extent.x, RectExtentEpsilon);
            extent.y = Mathf.Max(extent.y, RectExtentEpsilon);

            Matrix4x4 lsToFs = Matrix4x4.identity;
            lsToFs.m00 = 1.0f / extent.x;
            lsToFs.m03 = -minXY.x / extent.x;
            lsToFs.m11 = 1.0f / extent.y;
            lsToFs.m13 = -minXY.y / extent.y;
            return lsToFs;
        }

        static Matrix4x4 BuildLightToWorld(Vector3 origin, Vector3 forward) {
            Vector3 upHint = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;
            Vector3 right = Vector3.Cross(upHint, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, up);
            return Matrix4x4.TRS(origin, rotation, Vector3.one);
        }

        static Vector3[] CollectFrustumShellPointsLS(Camera camera, Matrix4x4 worldToLight, float atmosphereRadius) {
            Vector3[] points = new Vector3[4];
            int index = 0;
            Vector3 camPos = camera.transform.position;

            for (int y = 0; y <= 1; y++) {
                for (int x = 0; x <= 1; x++) {
                    Ray ray = camera.ViewportPointToRay(new Vector3(x, y, 0.0f));
                    Vector3 shellPointWS = camPos + ray.direction.normalized * atmosphereRadius;
                    Vector3 shellPointLS = worldToLight.MultiplyPoint3x4(shellPointWS);
                    points[index++] = shellPointLS;
                }
            }

            return points;
        }

    }
}
