using System;
using System.Collections.Generic;
using System.Text;
using Arterra.Configuration;
using Arterra.GamePlay;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arterra.Data.Entity.Behavior {
    public class PlayerStatsBehavior : SpeciesBehavior {
        private const int HistoryCapacity = 120;
        private const string InputLayer = "2.0::Subscene";
        private readonly Queue<EntityPopulationSample> history = new();
        private GameObject screen;
        private TMP_Text statistics;
        private TMP_Text populationTitle;
        private TMP_Text legend;
        private EntityPopulationGraph graph;
        private readonly List<TagRegistry.Tags> categoryTags = new();
        private bool[] includedTypes;
        private int currentCategory;
        private float nextSample;
        private BehaviorEntity.Animal self;
        private string BindingName => $"PlayerStats:Toggle::{self.info.rtEntityId}";

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            if (!self.IsOwner || this.self != null) return;
            this.self = self;
            InputPoller.AddBinding(new ActionBind("Open Stat Screen", Toggle, policy: ActionBind.Policy.Down),
                BindingName, InputLayer);
            InputPoller.AddBinding(new ActionBind("Next Category", _ => ChangeCategory(1), policy: ActionBind.Policy.Down),
                BindingName + ":Next", InputLayer);
            InputPoller.AddBinding(new ActionBind("Previous Category", _ => ChangeCategory(-1), policy: ActionBind.Policy.Down),
                BindingName + ":Previous", InputLayer);
        }

        private void Toggle(float _) {
            if (self == null || !self.IsOwner) return;
            if (screen != null) {
                bool opening = !screen.activeSelf;
                if (!opening) {
                    history.Clear();
                    graph.SetSamples(Array.Empty<EntityPopulationSample>(), HistoryCapacity);
                    nextSample = 0;
                }
                screen.SetActive(opening);
                return;
            }

            screen = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Stats/StatScreen"),
                GameUIManager.UIHandle.transform);
            ((RectTransform)screen.transform).anchoredPosition3D = Vector3.zero;
            statistics = screen.transform.Find("Statistics").GetComponent<TMP_Text>();
            RectTransform table = (RectTransform)screen.transform.Find("EntityPopulationTable");
            populationTitle = CreateLabel(table, "PopulationTitle", new Vector2(0.02f, 0.9f), new Vector2(0.98f, 0.99f), 18);
            CreateCategories();
            legend = CreateLabel(table, "Legend", new Vector2(0.72f, 0.1f), new Vector2(0.98f, 0.87f), 16);
            legend.enableAutoSizing = true;
            legend.fontSizeMin = 8;
            legend.fontSizeMax = 16;
            TMP_Text time = CreateLabel(table, "Time", new Vector2(0.03f, 0.01f), new Vector2(0.69f, 0.08f), 14);
            time.text = "Time (last 120 seconds) · gray indicates dead";

            var plot = new GameObject("PopulationGraph", typeof(RectTransform), typeof(CanvasRenderer), typeof(EntityPopulationGraph));
            RectTransform rect = (RectTransform)plot.transform;
            rect.SetParent(table, false);
            rect.anchorMin = new Vector2(0.03f, 0.12f);
            rect.anchorMax = new Vector2(0.69f, 0.87f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            plot.layer = table.gameObject.layer;
            graph = plot.GetComponent<EntityPopulationGraph>();
            graph.raycastTarget = false;
            RefreshPopulation();
            UpdatePosition();
        }

        private void CreateCategories() {
            var entities = Config.CURRENT.Generation.Entities;
            var categories = new[] {
                TagRegistry.Tags.Herbivore, TagRegistry.Tags.Carnivore, TagRegistry.Tags.SelectDiet,
                TagRegistry.Tags.TinyAnimal, TagRegistry.Tags.SmallAnimal, TagRegistry.Tags.MidSizedAnimal,
                TagRegistry.Tags.LargeAnimal, TagRegistry.Tags.GargantuanAnimal,
                TagRegistry.Tags.Terrestrial, TagRegistry.Tags.Aquatic, TagRegistry.Tags.Aerial
            };
            categoryTags.Add(TagRegistry.Tags.None);
            foreach (var category in categories) {
                bool used = false;
                for (int type = 0; type < entities.Reg.Count; type++)
                    used |= entities.GetMostSpecificTag(category, type, out _);
                if (used) categoryTags.Add(category);
            }
            currentCategory = 0;
            includedTypes = new bool[entities.Reg.Count];
            Array.Fill(includedTypes, true);
        }

        private void ChangeCategory(int direction) {
            if (self == null || !self.IsOwner || screen == null || !screen.activeInHierarchy) return;
            currentCategory = (currentCategory + direction + categoryTags.Count) % categoryTags.Count;
            var entities = Config.CURRENT.Generation.Entities;
            var tag = categoryTags[currentCategory];
            for (int type = 0; type < includedTypes.Length; type++)
                includedTypes[type] = tag == TagRegistry.Tags.None || entities.GetMostSpecificTag(tag, type, out _);
            history.Clear();
            nextSample = 0;
            RefreshPopulation();
        }

        private TMP_Text CreateLabel(Transform parent, string name, Vector2 min, Vector2 max, float fontSize) {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            obj.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            TMP_Text text = obj.GetComponent<TMP_Text>();
            text.font = statistics.font;
            text.fontSharedMaterial = statistics.fontSharedMaterial;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            return text;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Visual || !self.IsOwner
                || screen == null || !screen.activeInHierarchy) return;
            if (Time.unscaledTime >= nextSample) {
                nextSample = Time.unscaledTime + 1f;
                int typeCount = Config.CURRENT.Generation.Entities.Reg.Count;
                var sample = new EntityPopulationSample {
                    Counts = new int[typeCount],
                    Dead = new int[typeCount]
                };
                if (EntityManager.EntityReg != null) {
                    foreach (Entity entity in EntityManager.EntityReg) {
                        if (!entity.active || entity.info.entityType >= sample.Counts.Length
                            || !includedTypes[entity.info.entityType]) continue;
                        sample.Counts[entity.info.entityType]++;
                        if (entity.Is(out VitalityBehavior vitality) && vitality.IsDead)
                            sample.Dead[entity.info.entityType]++;
                    }
                }
                if (history.Count == HistoryCapacity) history.Dequeue();
                history.Enqueue(sample);
                RefreshPopulation();
            }
            UpdatePosition();
        }

        private void UpdatePosition() {
            Entity active = PlayerHandler.data;
            if (active == null || statistics == null) return;
            var position = active.position;
            statistics.SetText("Grid position ({0:2}, {1:2}, {2:2})", position.x, position.y, position.z);
        }

        private void RefreshPopulation() {
            if (graph == null) return;
            EntityPopulationSample[] samples = history.ToArray();
            graph.SetSamples(samples, HistoryCapacity);
            int maximum = 0;
            int total = 0;
            foreach (var sample in samples) {
                total = 0;
                foreach (int count in sample.Counts) total += count;
                maximum = Math.Max(maximum, total);
            }
            var category = categoryTags[currentCategory];
            string categoryName = category == TagRegistry.Tags.None ? "All" : Enum.GetName(typeof(TagRegistry.Tags), category);
            populationTitle.text = $"Entity population ({categoryName})  {total} total  (scale 0–{Math.Max(maximum, 1)})";
            var text = new StringBuilder();
            var entities = Config.CURRENT.Generation.Entities;
            for (int type = 0; type < entities.Reg.Count; type++) {
                bool present = false;
                foreach (var sample in samples) present |= sample.Counts[type] > 0;
                if (!present) continue;
                int count = samples.Length == 0 ? 0 : samples[samples.Length - 1].Counts[type];
                string color = ColorUtility.ToHtmlStringRGB(EntityPopulationGraph.EntityColor(type));
                text.Append("<color=#").Append(color).Append('>').Append(entities.RetrieveName(type))
                    .Append("  ").Append(count).Append("</color>\n");
            }
            legend.text = text.ToString();
        }

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (this.self == null) return;
            InputPoller.RemoveBinding(BindingName, InputLayer);
            InputPoller.RemoveBinding(BindingName + ":Next", InputLayer);
            InputPoller.RemoveBinding(BindingName + ":Previous", InputLayer);
            if (screen != null) GameObject.Destroy(screen);
            screen = null;
            statistics = populationTitle = legend = null;
            graph = null;
            categoryTags.Clear();
            includedTypes = null;
            history.Clear();
            nextSample = 0;
            this.self = null;
        }

        public override void Disable(BehaviorEntity.Animal self) => DisposeVisual(self);
    }

    public struct EntityPopulationSample {
        public int[] Counts;
        public int[] Dead;
    }

    // Created with the screen at runtime; the behavior owns its samples and lifetime.
    public class EntityPopulationGraph : Graphic {
        private EntityPopulationSample[] samples = Array.Empty<EntityPopulationSample>();
        private int capacity;

        public static Color EntityColor(int entityType) =>
            Color.HSVToRGB((entityType * 0.618033988749f) % 1f, 0.85f, 0.95f);

        public void SetSamples(EntityPopulationSample[] value, int historyCapacity) {
            samples = value;
            capacity = historyCapacity;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices) {
            vertices.Clear();
            if (samples.Length < 2) return;
            int types = samples[0].Counts.Length;
            // Keep the generated mesh within uGUI's 16-bit vertex limit.
            int count = Math.Min(samples.Length, 60000 / Math.Max(types * 4, 1));
            if (count < 2) return;
            int first = samples.Length - count;
            int maximum = 1;
            foreach (var sample in samples) {
                int total = 0;
                foreach (int population in sample.Counts) total += population;
                maximum = Math.Max(maximum, total);
            }
            Rect rect = GetPixelAdjustedRect();
            var cumulative = new int[count];
            for (int type = 0; type < types; type++) {
                bool present = false;
                for (int t = first; t < samples.Length; t++) present |= samples[t].Counts[type] > 0;
                if (!present) continue;
                int start = vertices.currentVertCount;
                Color color = EntityColor(type);
                float gray = color.grayscale;
                Color32 deadColor = new Color(gray, gray, gray, 1f);
                for (int t = 0; t < count; t++) {
                    var sample = samples[first + t];
                    float x = rect.xMin + rect.width * (capacity - count + t) / Math.Max(capacity - 1, 1);
                    float lower = rect.yMin + rect.height * cumulative[t] / maximum;
                    float split = rect.yMin + rect.height * (cumulative[t] + sample.Dead[type]) / maximum;
                    cumulative[t] += sample.Counts[type];
                    float upper = rect.yMin + rect.height * cumulative[t] / maximum;
                    vertices.AddVert(new Vector3(x, lower), deadColor, Vector2.zero);
                    vertices.AddVert(new Vector3(x, split), deadColor, Vector2.zero);
                    vertices.AddVert(new Vector3(x, split), color, Vector2.zero);
                    vertices.AddVert(new Vector3(x, upper), color, Vector2.zero);
                    if (t == 0) continue;
                    int previous = start + (t - 1) * 4;
                    // Dead and live regions meet at the split, while retaining distinct vertex colors.
                    vertices.AddTriangle(previous, previous + 1, previous + 5);
                    vertices.AddTriangle(previous, previous + 5, previous + 4);
                    vertices.AddTriangle(previous + 2, previous + 3, previous + 7);
                    vertices.AddTriangle(previous + 2, previous + 7, previous + 6);
                }
            }
        }
    }
}
