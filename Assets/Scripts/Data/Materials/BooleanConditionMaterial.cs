using System;
using System.Collections.Generic;
using System.Text;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Data.Structure;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using Arterra.Editor;
using UnityEditor;
#endif

#pragma warning disable CS1591
//When nothing you make is generic enough so you just build your own language  
//This may be what people call overdesigning

namespace Arterra.Data.Material {
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/BooleanConditionMaterial")]
    public class BooleanConditionMaterial : MaterialBehavior {
        [Serializable]
        public struct BooleanCheck {
            public StructureData.CheckInfo Condition;
            public uint _offset;
            public uint _material;

            public int3 Offset {
                readonly get => new int3(
                    DecodeSigned10((_offset >> 20) & 0x3FFu),
                    DecodeSigned10((_offset >> 10) & 0x3FFu),
                    DecodeSigned10(_offset & 0x3FFu));
                set {
                    int x = math.clamp(value.x, -512, 511);
                    int y = math.clamp(value.y, -512, 511);
                    int z = math.clamp(value.z, -512, 511);
                    _offset = ((uint)x & 0x3FFu) << 20 |
                        ((uint)y & 0x3FFu) << 10 |
                        ((uint)z & 0x3FFu);
                }
            }

            public uint material {
                readonly get => _material & 0x7FFFFFFF;
                set => _material = value & 0x7FFFFFFF | (_material & 0x80000000);
            }

            public bool HasMaterialCheck {
                readonly get => (_material & 0x80000000) != 0;
                set {
                    if (value) _material |= 0x80000000;
                    else _material &= 0x7FFFFFFF;
                }
            }

            private static int DecodeSigned10(uint value) {
                int raw = (int)(value & 0x3FFu);
                if ((raw & 0x200) != 0) raw |= unchecked((int)0xFFFFFC00);
                return raw;
            }

            public bool Satisfies(IMaterial material, int3 baseCoord) {
                int3 target = baseCoord + Offset;
                MapData sample = CPUMapManager.SampleMap(target);
                if (!Condition.Contains(sample)) return false;
                if (!HasMaterialCheck) return true;

                string key = material.RetrieveKey((int)this.material);
                if (string.IsNullOrEmpty(key)) return false;

                int checkMaterial = MaterialRegistry.RetrieveIndex(key);
                return checkMaterial >= 0 && sample.material == checkMaterial;
            }
        }

        public StructureData.CheckInfo SelfCondition;
        public Option<List<MapSamplePoint>> ConversionData;
        public Option<List<BooleanCheck>> Checks;
        [Range(0, 1)] public float RandomUpdateCheckChance;
        [Range(0, 1)] public float PropogateUpdateCheckChance;
        public bool DropsMaterial;
        [TextArea]
        public string Expression = "0";

        [SerializeField, HideInInspector] private string _normalizedExpression;
        [SerializeField, HideInInspector] private string _compileError;

        private BoolNode _ast;
        private static Catalogue<MaterialData> MaterialRegistry => Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        public bool HasCompileError => !string.IsNullOrEmpty(_compileError);
        public string CompileError => _compileError;
        public string NormalizedExpression => _normalizedExpression;

        public override void Preset(MaterialData self) {
            CompileExpression();
        }

        public override void PropogateMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord);
            if (prng.NextFloat() >= PropogateUpdateCheckChance) return;
            if (!SelfCondition.Contains(cur)) return;
            if (!Evaluate(GCoord)) return;
            PlaceResult(GCoord, prng);
        }

        public override void RandomMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord);
            if (prng.NextFloat() >= RandomUpdateCheckChance) return;
            if (!SelfCondition.Contains(cur)) return;
            if (!Evaluate(GCoord)) return;
            PlaceResult(GCoord, prng);
        }

        private void PlaceResult(int3 GCoord, Unity.Mathematics.Random prng) {
            List<MapSamplePoint> result = ConversionData.value;
            if (result == null) return;
            MapSamplePoint.PlaceRegion(result, this, GCoord, ref prng, DropsMaterial);
        }

        public bool Evaluate(int3 gCoord) {
            if (_ast == null) return false;
            List<BooleanCheck> checks = Checks.value;
            if (checks == null || checks.Count == 0) return false;

            bool[] results = new bool[checks.Count];
            for (int i = 0; i < checks.Count; i++)
                results[i] = checks[i].Satisfies(this, gCoord);

            return _ast.Evaluate(results);
        }

        private void OnValidate() {
            CompileExpression();
        }

        private void CompileExpression() {
            _ast = null;
            _compileError = string.Empty;
            _normalizedExpression = string.Empty;

            List<BooleanCheck> checks = Checks.value;
            int checkCount = checks == null ? 0 : checks.Count;
            if (string.IsNullOrWhiteSpace(Expression)) {
                _compileError = "Expression cannot be empty.";
                return;
            }

            try {
                Parser parser = new Parser(Expression, checkCount);
                _ast = parser.Parse();
                _normalizedExpression = _ast.ToExpression();
            } catch (Exception ex) {
                _compileError = ex.Message;
                _ast = null;
            }
        }

        private abstract class BoolNode {
            public abstract bool Evaluate(bool[] checks);
            public abstract string ToExpression();
        }

        private sealed class CheckNode : BoolNode {
            private readonly int _index;

            public CheckNode(int index) {
                _index = index;
            }

            public override bool Evaluate(bool[] checks) {
                return _index >= 0 && _index < checks.Length && checks[_index];
            }

            public override string ToExpression() {
                return _index.ToString();
            }
        }

        private sealed class NotNode : BoolNode {
            private readonly BoolNode _inner;

            public NotNode(BoolNode inner) {
                _inner = inner;
            }

            public override bool Evaluate(bool[] checks) {
                return !_inner.Evaluate(checks);
            }

            public override string ToExpression() {
                return "~" + Wrap(_inner);
            }
        }

        private sealed class BinaryNode : BoolNode {
            private readonly BoolNode _left;
            private readonly BoolNode _right;
            private readonly char _op;

            public BinaryNode(BoolNode left, BoolNode right, char op) {
                _left = left;
                _right = right;
                _op = op;
            }

            public override bool Evaluate(bool[] checks) {
                if (_op == '^') {
                    if (!_left.Evaluate(checks)) return false;
                    return _right.Evaluate(checks);
                }

                if (_left.Evaluate(checks)) return true;
                return _right.Evaluate(checks);
            }

            public override string ToExpression() {
                return Wrap(_left) + " " + _op + " " + Wrap(_right);
            }
        }

        private static string Wrap(BoolNode node) {
            if (node is CheckNode) return node.ToExpression();
            return "(" + node.ToExpression() + ")";
        }

        private enum TokenKind {
            Integer,
            And,
            Or,
            Not,
            LeftParen,
            RightParen,
            End,
        }

        private readonly struct Token {
            public readonly TokenKind Kind;
            public readonly int Value;
            public readonly int Position;

            public Token(TokenKind kind, int value, int position) {
                Kind = kind;
                Value = value;
                Position = position;
            }
        }

        // Right-leaning boolean grammar accepted by this parser:
        // S -> P
        // P -> U | U v P | U ^ P
        // U -> ~U | ( P ) | B
        // B -> integer index into Checks list

        private sealed class Parser {
            private readonly string _input;
            private readonly int _checkCount;
            private int _index;
            private Token _current;

            public Parser(string input, int checkCount) {
                _input = input;
                _checkCount = checkCount;
                _current = NextToken();
            }

            public BoolNode Parse() {
                BoolNode root = ParseRightExpression();
                Expect(TokenKind.End);
                return root;
            }

            private BoolNode ParseRightExpression() {
                BoolNode left = ParseUnaryOrPrimary();
                if (_current.Kind != TokenKind.And && _current.Kind != TokenKind.Or)
                    return left;

                char op = _current.Kind == TokenKind.And ? '^' : 'v';
                Advance();
                BoolNode right = ParseRightExpression();
                return new BinaryNode(left, right, op);
            }

            private BoolNode ParseUnaryOrPrimary() {
                if (_current.Kind == TokenKind.Not) {
                    Advance();
                    return new NotNode(ParseUnaryOrPrimary());
                }

                if (_current.Kind == TokenKind.LeftParen) {
                    Advance();
                    BoolNode inner = ParseRightExpression();
                    Expect(TokenKind.RightParen);
                    return inner;
                }

                if (_current.Kind == TokenKind.Integer) {
                    int index = _current.Value;
                    if (index < 0 || index >= _checkCount)
                        throw Error(_current.Position, "Check index " + index + " is out of range [0, " + math.max(_checkCount - 1, 0) + "].");

                    Advance();
                    return new CheckNode(index);
                }

                throw Error(_current.Position, "Expected integer check index, '~', or '('.");
            }

            private void Expect(TokenKind kind) {
                if (_current.Kind != kind)
                    throw Error(_current.Position, "Expected token " + kind + " but found " + _current.Kind + ".");
                Advance();
            }

            private void Advance() {
                _current = NextToken();
            }

            private Token NextToken() {
                while (_index < _input.Length && char.IsWhiteSpace(_input[_index]))
                    _index++;

                if (_index >= _input.Length)
                    return new Token(TokenKind.End, 0, _index);

                int pos = _index;
                char ch = _input[_index++];
                switch (ch) {
                    case '^':
                        return new Token(TokenKind.And, 0, pos);
                    case 'v':
                        return new Token(TokenKind.Or, 0, pos);
                    case '~':
                        return new Token(TokenKind.Not, 0, pos);
                    case '(':
                        return new Token(TokenKind.LeftParen, 0, pos);
                    case ')':
                        return new Token(TokenKind.RightParen, 0, pos);
                }

                if (!char.IsDigit(ch))
                    throw Error(pos, "Unexpected character '" + ch + "'.");

                int value = ch - '0';
                while (_index < _input.Length && char.IsDigit(_input[_index])) {
                    value = checked(value * 10 + (_input[_index] - '0'));
                    _index++;
                }

                return new Token(TokenKind.Integer, value, pos);
            }

            private Exception Error(int position, string message) {
                StringBuilder sb = new StringBuilder();
                sb.Append("Expression parse error at index ");
                sb.Append(position);
                sb.Append(": ");
                sb.Append(message);
                return new FormatException(sb.ToString());
            }
        }
    }

    [Serializable]
    public struct MapSamplePoint {
        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        public Entity.ProfileE check;
        public uint _material;
        public uint _offset;

        public int3 Offset {
            readonly get => new int3((int)(_offset >> 20) & 0x3FF, (int)(_offset >> 10) & 0x3FF, (int)(_offset & 0x3FF)) - new int3(512, 512, 512);
            set {
                value += new int3(512, 512, 512);
                _offset = (uint)((value.x & 0x3FF) << 20 | (value.y & 0x3FF) << 10 | (value.z & 0x3FF));
            }
        }

        public uint material {
            readonly get => _material & 0x7FFFFFFF;
            set => _material = value & 0x7FFFFFFF | (_material & 0x80000000);
        }

        public bool HasMaterialCheck {
            readonly get => (_material & 0x80000000) != 0;
            set {
                if (value) _material |= 0x80000000;
                else _material &= 0x7FFFFFFF;
            }
        }

        public bool SatisfiesCheck(IMaterial mat, int3 BaseCoord) {
            int3 GCoord = BaseCoord + Offset;
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            if (!check.bounds.Contains(mapData)) return false;
            if (!HasMaterialCheck) return true;
            int checkMat = matInfo.RetrieveIndex(
                mat.RetrieveKey((int)material));
            return mapData.material == checkMat;
        }

        //No flag means always place
        //Or designates start of section
        //And means place if we select section 
        //Ex flag means only change material
        public static void PlaceRegion(
            List<MapSamplePoint> checks, IMaterial mat,
            int3 BaseCoord, ref Unity.Mathematics.Random prng, bool DropItem = false
        ) {
            int sectionCount = 1;
            int sectionIndex = checks.Count;
            for (int i = 0; i < checks.Count; i++) {
                MapSamplePoint pt = checks[i];
                if (pt.check.OrFlag) {
                    float probability = 1/(float)sectionCount;
                    if(prng.NextFloat() < probability) sectionIndex = i;
                    sectionCount++;
                } else if(!pt.check.AndFlag) {
                    pt.PlaceEntry(mat, BaseCoord, ref prng, DropItem);
                }
            } for(int i = sectionIndex; i < checks.Count; i++) {
                MapSamplePoint pt = checks[i];
                if (i == sectionIndex) pt.PlaceEntry(mat, BaseCoord, ref prng, DropItem);
                else if (pt.check.OrFlag) break;
                else if (pt.check.AndFlag) pt.PlaceEntry(mat, BaseCoord, ref prng, DropItem);
            }
        } 

        public void PlaceEntry(IMaterial mat, int3 BaseCoord, ref Unity.Mathematics.Random prng, bool DropItem = false) {
            int3 GCoord = BaseCoord + Offset;
            MapData cur = CPUMapManager.SampleMap(GCoord);
            if (cur.IsNull) return;

            MapData newData = new MapData { data = 0 };

            if (HasMaterialCheck) {
                newData.material = matInfo.RetrieveIndex(mat.RetrieveKey((int)material));
            } else newData.material = cur.material;


            if (check.ExFlag) {
                newData.viscosity = cur.viscosity;
                newData.density = cur.density;
            } else {
                newData.viscosity = Mathf.RoundToInt(check.bounds.MinSolid +
                    prng.NextFloat() * (check.bounds.MaxSolid - check.bounds.MinSolid));
                int liquidDensity = Mathf.RoundToInt(check.bounds.MinLiquid +
                    prng.NextFloat() * (check.bounds.MaxLiquid - check.bounds.MinLiquid));
                newData.density = Mathf.Clamp(newData.viscosity + liquidDensity, 0, MapData.MaxDensity);
            }

            MapData deltaC = cur; MapData deltaN = newData;
            if (newData.material == cur.material) {
                //If it's the same material, recalculate cur as the delta viscosity and density
                deltaC.viscosity -= math.min(cur.SolidDensity, newData.SolidDensity);
                deltaC.density = deltaC.viscosity + deltaC.LiquidDensity
                    - math.min(cur.LiquidDensity, newData.LiquidDensity);

                deltaN.viscosity = math.max(cur.viscosity, newData.viscosity) - cur.viscosity;
                deltaN.density = deltaN.viscosity - cur.LiquidDensity
                    + math.max(cur.LiquidDensity, newData.LiquidDensity);
            }

            if (matInfo.Retrieve(cur.material).OnRemoving(GCoord, null))
                return;

            if (matInfo.Retrieve(newData.material).OnPlacing(GCoord, null))
                return;

            Item.IItem drop = matInfo.Retrieve(cur.material).OnRemoved(GCoord, deltaC);
            if (DropItem) GamePlay.UI.InventoryController.DropItem(drop, GCoord);
            matInfo.Retrieve(newData.material).OnPlaced(GCoord, deltaN);

            CPUMapManager.SetMap(newData, GCoord);
        }
    }
}


#pragma warning restore CS1591

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(Arterra.Data.Material.BooleanConditionMaterial.BooleanCheck))]
public class BooleanCheckDrawer : PropertyDrawer {
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(line, label);

        EditorGUI.indentLevel++;

        SerializedProperty conditionProp = property.FindPropertyRelative("Condition");
        SerializedProperty offsetProp = property.FindPropertyRelative("_offset");
        SerializedProperty materialProp = property.FindPropertyRelative("_material");

        line.y += EditorGUIUtility.singleLineHeight;
        float conditionHeight = EditorGUI.GetPropertyHeight(conditionProp, true);
        Rect conditionRect = new Rect(line.x, line.y, line.width, conditionHeight);
        EditorGUI.PropertyField(conditionRect, conditionProp, true);

        line.y += conditionHeight + EditorGUIUtility.standardVerticalSpacing;
        int[] offset = new int[3] {
            DecodeSigned10((offsetProp.uintValue >> 20) & 0x3FFu),
            DecodeSigned10((offsetProp.uintValue >> 10) & 0x3FFu),
            DecodeSigned10(offsetProp.uintValue & 0x3FFu),
        };
        Rect offsetLabelRect = new Rect(line.x, line.y, EditorGUIUtility.labelWidth, line.height);
        Rect offsetRect = new Rect(offsetLabelRect.x + EditorGUIUtility.labelWidth, line.y,
            line.width - EditorGUIUtility.labelWidth, line.height);
        EditorGUI.LabelField(offsetLabelRect, "Offset");
        EditorGUI.MultiIntField(offsetRect, new GUIContent[] { new("x"), new("y"), new("z") }, offset);

        int x = math.clamp(offset[0], -512, 511);
        int y = math.clamp(offset[1], -512, 511);
        int z = math.clamp(offset[2], -512, 511);
        offsetProp.uintValue = ((uint)x & 0x3FFu) << 20 |
            ((uint)y & 0x3FFu) << 10 |
            ((uint)z & 0x3FFu);

        uint packed = materialProp.uintValue;
        bool hasMaterialCheck = (packed & 0x80000000u) != 0;

        line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        Rect indentedLine = EditorGUI.IndentedRect(line);
        float labelWidth = EditorGUIUtility.labelWidth;
        float fieldWidth = indentedLine.width / 2f;

        Rect hasMatRect = new Rect(indentedLine.x, indentedLine.y, fieldWidth, indentedLine.height);
        EditorGUI.LabelField(hasMatRect, "HasMaterial");
        hasMatRect.x += labelWidth;
        hasMaterialCheck = EditorGUI.Toggle(hasMatRect, hasMaterialCheck);

        if (hasMaterialCheck) {
            Rect materialRect = new Rect(indentedLine.x + fieldWidth, indentedLine.y, fieldWidth, indentedLine.height);
            RegistryReferenceDrawer.SetupRegistries();
            RegistryReferenceDrawer materialDrawer = new RegistryReferenceDrawer { BitMask = 0x7FFFFFFF, BitShift = 0 };
            materialDrawer.DrawRegistryDropdown(materialRect, materialProp, new GUIContent("Material"),
                Config.TEMPLATE.Generation.Materials.value.MaterialDictionary);
        }

        materialProp.uintValue = (materialProp.uintValue & 0x7FFFFFFF) | (hasMaterialCheck ? 0x80000000u : 0u);

        EditorGUI.indentLevel--;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        SerializedProperty conditionProp = property.FindPropertyRelative("Condition");

        float height = 0f;
        height += EditorGUIUtility.singleLineHeight; // label
        height += EditorGUIUtility.standardVerticalSpacing;
        height += EditorGUI.GetPropertyHeight(conditionProp, true); // condition
        height += EditorGUIUtility.standardVerticalSpacing;
        height += EditorGUIUtility.singleLineHeight; // offset
        height += EditorGUIUtility.standardVerticalSpacing;
        height += EditorGUIUtility.singleLineHeight; // has material match + material dropdown row
        return height;
    }

    private static int DecodeSigned10(uint value) {
        int raw = (int)(value & 0x3FFu);
        if ((raw & 0x200) != 0)
            raw |= unchecked((int)0xFFFFFC00);
        return raw;
    }
}

/// <summary> A utility class to override serialization of <see cref="StructureData.CheckInfo"/> into a Unity Inspector format.
/// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
[CustomPropertyDrawer(typeof(Arterra.Data.Material.MapSamplePoint))]
public class StructCheckDrawer : PropertyDrawer {
    private static readonly Dictionary<string, bool> _foldouts = new();
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        string path = property.propertyPath;
        if (!_foldouts.ContainsKey(path))
            _foldouts[path] = false;

        Rect foldoutRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        _foldouts[path] = EditorGUI.Foldout(foldoutRect, _foldouts[path], label, true); // triangle on left, label is clickable

        if (!_foldouts[path]) return;

        EditorGUI.indentLevel++;

        // Draw check bounds normally
        SerializedProperty checkProp = property.FindPropertyRelative("check");
        Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        rect.y += EditorGUIUtility.singleLineHeight;
        rect = EditorGUI.IndentedRect(rect);

        EditorGUI.PropertyField(rect, checkProp);
        rect.y += EditorGUI.GetPropertyHeight(checkProp);

        SerializedProperty materialProp = property.FindPropertyRelative("_material");
        SerializedProperty offsetProp = property.FindPropertyRelative("_offset");

        uint matInfo = materialProp.uintValue;
        bool hasCheck = (matInfo & 0x80000000) != 0;
        int[] offset = new int[3]{
            (int)((offsetProp.intValue >> 20) & 0x3FF) - 512,
            (int)((offsetProp.intValue >> 10) & 0x3FF) - 512,
            (int)(offsetProp.intValue & 0x3FF) - 512
        };

        float labelWidth = EditorGUIUtility.labelWidth;
        float fieldWidth = rect.width / 2f;

        Rect hasMatRect = new(rect.x, rect.y, fieldWidth, rect.height);
        EditorGUI.LabelField(hasMatRect, "HasMaterial");
        hasMatRect.x += labelWidth;
        hasCheck = EditorGUI.Toggle(hasMatRect, hasCheck);

        if (hasCheck) {
            Rect materialRect = new(rect.x + fieldWidth, rect.y, fieldWidth, rect.height);
            RegistryReferenceDrawer.SetupRegistries();
            RegistryReferenceDrawer materialDrawer = new RegistryReferenceDrawer { BitMask = 0x7FFFFFFF, BitShift = 0 };
            materialDrawer.DrawRegistryDropdown(materialRect, materialProp, new GUIContent("Material"),
                Config.TEMPLATE.Generation.Materials.value.MaterialDictionary);
        }

        rect.y += EditorGUIUtility.singleLineHeight;
        Rect offsetLabelRect = new(rect.x, rect.y, labelWidth, rect.height);
        Rect offsetRect = new(offsetLabelRect.x + labelWidth, rect.y, rect.width - labelWidth, rect.height);
        EditorGUI.LabelField(offsetLabelRect, "Offset ");
        EditorGUI.MultiIntField(offsetRect, new GUIContent[] { new("x"), new("y"), new("z") }, offset);
        rect.y += EditorGUIUtility.singleLineHeight;

        materialProp.uintValue = (materialProp.uintValue & 0x7FFFFFFF) | (hasCheck ? 0x80000000u : 0u);
        offsetProp.intValue = ((offset[0] + 512) & 0x3FF) << 20 |
            ((offset[1] + 512) & 0x3FF) << 10 | ((offset[2] + 512) & 0x3FF);

        EditorGUI.indentLevel--;

    }

    /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        bool isExpanded = _foldouts.TryGetValue(property.propertyPath, out bool val) && val;
        if (!isExpanded) return EditorGUIUtility.singleLineHeight;

        SerializedProperty checkProp = property.FindPropertyRelative("check");
        return EditorGUI.GetPropertyHeight(checkProp) + EditorGUIUtility.singleLineHeight * 3;
    }
}
#endif