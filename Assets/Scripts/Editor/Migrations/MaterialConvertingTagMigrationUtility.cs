using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Arterra.Editor {
    /// <summary>
    /// Migrates IMaterialConverting tag data to ProfileE-based neighbor bounds.
    ///
    /// This script scans .asset YAML files under Assets/, finds managed references of
    /// ConvertibleTag, ConvertibleToolTag, and ConverterToolTag, and rewrites:
    /// _neighborBounds:
    ///   data: <old>
    /// into:
    /// _neighborBounds:
    ///   bounds:
    ///     data: <old>
    ///   flags: 2
    ///
    /// If already migrated, it enforces flags: 2 (OR behavior).
    /// </summary>
    public static class MaterialConvertingTagMigrationUtility {
        private const string MenuPath = "Arterra/Utilities/Migrate IMaterialConverting NeighborBounds to ProfileE";
        private const uint OrFlag = 0x2;

        private static readonly HashSet<string> SupportedManagedTypes = new() {
            "Arterra.Configuration.ConvertibleTag",
            "Arterra.Configuration.ConvertibleToolTag",
            "Arterra.Configuration.ConverterToolTag",
        };

        [MenuItem(MenuPath)]
        public static void MigrateNeighborBoundsToProfileE() {
            string assetsRoot = Application.dataPath;
            string[] files = Directory.GetFiles(assetsRoot, "*.asset", SearchOption.AllDirectories);

            int scannedAssets = files.Length;
            int migratedTags = 0;
            List<string> touchedAssets = new();

            try {
                foreach (string fullPath in files) {
                    if (!TryMigrateAssetYaml(fullPath, out int changedTagCount))
                        continue;

                    string assetPath = FullPathToAssetPath(fullPath);
                    touchedAssets.Add(assetPath);
                    migratedTags += changedTagCount;

                    if ((touchedAssets.Count % 25) == 0) {
                        Debug.Log($"[Tag Migration] Updated {touchedAssets.Count} assets so far...");
                    }
                }

                if (touchedAssets.Count > 0) {
                    AssetDatabase.Refresh();
                    AssetDatabase.ForceReserializeAssets(touchedAssets);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                Debug.Log(
                    $"[Tag Migration] Complete. Scanned assets: {scannedAssets}, " +
                    $"updated assets: {touchedAssets.Count}, migrated tags: {migratedTags}.");

                if (touchedAssets.Count > 0) {
                    Debug.Log("[Tag Migration] Updated asset list:\n" + string.Join("\n", touchedAssets));
                }
                else {
                    Debug.Log("[Tag Migration] No assets required migration.");
                }
            }
            catch (Exception ex) {
                Debug.LogError("[Tag Migration] Failed: " + ex);
                throw;
            }
        }

        private static bool TryMigrateAssetYaml(string fullPath, out int changedTagCount) {
            changedTagCount = 0;

            string[] lines = File.ReadAllLines(fullPath);
            if (lines.Length == 0)
                return false;

            bool changed = false;
            bool inSupportedType = false;
            List<string> output = new(lines.Length + 8);

            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("type: {class: ", StringComparison.Ordinal)) {
                    inSupportedType = IsSupportedTypeLine(trimmed);
                    output.Add(line);
                    continue;
                }

                if (!inSupportedType || trimmed != "_neighborBounds:") {
                    output.Add(line);
                    continue;
                }

                string indent = line.Substring(0, line.Length - trimmed.Length);
                string childIndent = indent + "  ";
                string grandChildIndent = childIndent + "  ";

                if (i + 1 < lines.Length) {
                    string next = lines[i + 1].TrimStart();
                    if (next.StartsWith("data:", StringComparison.Ordinal)) {
                        string dataValue = next.Substring("data:".Length).Trim();

                        output.Add(line);
                        output.Add(childIndent + "bounds:");
                        output.Add(grandChildIndent + "data: " + dataValue);
                        output.Add(childIndent + "flags: " + OrFlag);

                        i += 1;
                        changed = true;
                        changedTagCount++;
                        continue;
                    }
                }

                output.Add(line);

                int lookahead = i + 1;
                bool hasBounds = false;
                bool wroteFlags = false;
                bool localChanged = false;

                while (lookahead < lines.Length) {
                    string la = lines[lookahead];
                    string laTrim = la.TrimStart();

                    if (GetIndentLength(la) <= indent.Length) {
                        break;
                    }

                    if (laTrim == "bounds:") {
                        hasBounds = true;
                        output.Add(la);
                        lookahead++;
                        continue;
                    }

                    if (laTrim.StartsWith("flags:", StringComparison.Ordinal)) {
                        output.Add(childIndent + "flags: " + OrFlag);
                        wroteFlags = true;
                        if (!laTrim.Equals("flags: " + OrFlag, StringComparison.Ordinal)) {
                            localChanged = true;
                        }
                        lookahead++;
                        continue;
                    }

                    output.Add(la);
                    lookahead++;
                }

                if (hasBounds && !wroteFlags) {
                    output.Add(childIndent + "flags: " + OrFlag);
                    localChanged = true;
                }

                if (localChanged) {
                    changed = true;
                    changedTagCount++;
                }

                i = lookahead - 1;
            }

            if (!changed)
                return false;

            string normalized = string.Join("\n", output);
            File.WriteAllText(fullPath, normalized + "\n", new UTF8Encoding(false));

            return true;
        }

        private static int GetIndentLength(string line) {
            int idx = 0;
            while (idx < line.Length && line[idx] == ' ') idx++;
            return idx;
        }

        private static bool IsSupportedTypeLine(string trimmedTypeLine) {
            foreach (string typeName in SupportedManagedTypes) {
                int lastDot = typeName.LastIndexOf('.');
                string className = lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
                if (trimmedTypeLine.Contains("class: " + className + ",", StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        private static string FullPathToAssetPath(string fullPath) {
            string p = fullPath.Replace('\\', '/');
            string root = Application.dataPath.Replace('\\', '/');
            if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
                return p;
            }
            return "Assets" + p.Substring(root.Length);
        }
    }
}
