using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    public enum GoapContentPresetKind
    {
        BasicNeeds,
        ResourceGathering
    }

    public sealed class GoapContentPresetResult
    {
        public IReadOnlyList<GoapActionDefinition> Actions { get; }
        public IReadOnlyList<GoapGoalDefinition> Goals { get; }
        public IReadOnlyList<GoapFactValueReference> InitialFacts { get; }
        public IReadOnlyList<GoapProfileSensorDefinition> Sensors { get; }
        public bool RequiresInventory { get; }
        public int CreatedDefinitionCount { get; }

        public GoapContentPresetResult(
            IEnumerable<GoapActionDefinition> actions,
            IEnumerable<GoapGoalDefinition> goals,
            IEnumerable<GoapFactValueReference> initialFacts,
            IEnumerable<GoapProfileSensorDefinition> sensors,
            bool requiresInventory,
            int createdDefinitionCount)
        {
            Actions = actions?.Where(item => item != null).ToArray() ?? Array.Empty<GoapActionDefinition>();
            Goals = goals?.Where(item => item != null).ToArray() ?? Array.Empty<GoapGoalDefinition>();
            InitialFacts = initialFacts?.Where(item => item.IsValid).ToArray() ?? Array.Empty<GoapFactValueReference>();
            Sensors = sensors?.Where(item => item != null).ToArray() ?? Array.Empty<GoapProfileSensorDefinition>();
            RequiresInventory = requiresInventory;
            CreatedDefinitionCount = Mathf.Max(0, createdDefinitionCount);
        }
    }

    public static class GoapContentCreationService
    {
        public static event Action<GoapDomain> DomainChanged;

        public static GoapFact CreateFact(
            GoapDomain domain,
            string displayName,
            GoapFactType valueType,
            GoapValue defaultValue,
            IEnumerable<string> enumOptions = null)
        {
            RequireSavedDomain(domain);
            displayName = RequireText(displayName, "Fact name");
            var existing = domain.FindFact(displayName);
            if (existing != null)
            {
                EnsureFactType(existing, valueType);
                return existing;
            }

            Undo.RecordObject(domain, "Create GOAP Fact");
            var created = 0;
            var fact = CreateSubAsset<GoapFact>(domain, displayName, ref created);
            switch (valueType)
            {
                case GoapFactType.Integer:
                    fact.ConfigureInteger(displayName, defaultValue.Integer);
                    break;
                case GoapFactType.Float:
                    fact.ConfigureFloat(displayName, defaultValue.Float);
                    break;
                case GoapFactType.Enum:
                    fact.ConfigureEnum(displayName, enumOptions, defaultValue.Integer);
                    break;
                default:
                    fact.Configure(displayName, defaultValue.Boolean);
                    break;
            }

            domain.AddFact(fact);
            FinishDomainChange(domain);
            return fact;
        }

        public static GoapActionDefinition CreateAction(
            GoapDomain domain,
            string displayName,
            float cost,
            string executorId,
            IEnumerable<GoapCondition> preconditions,
            IEnumerable<GoapCondition> effects,
            IEnumerable<GoapActionStep> steps,
            string description = "")
        {
            RequireSavedDomain(domain);
            displayName = RequireText(displayName, "Action name");
            executorId = string.IsNullOrWhiteSpace(executorId)
                ? ToIdentifier(displayName)
                : RequireText(executorId, "Executor ID");
            if (domain.FindAction(executorId) != null)
            {
                throw new InvalidOperationException($"Action executor ID '{executorId}' already exists in this Domain.");
            }

            var checkedPreconditions = ValidateConditions(domain, preconditions, "Preconditions", false);
            var checkedEffects = ValidateConditions(domain, effects, "Effects", true);
            var checkedSteps = steps?.Where(step => step != null).ToArray() ?? Array.Empty<GoapActionStep>();
            if (checkedSteps.Length == 0)
            {
                throw new InvalidOperationException("An Action needs at least one execution step.");
            }

            Undo.RecordObject(domain, "Create GOAP Action");
            var created = 0;
            var action = CreateSubAsset<GoapActionDefinition>(domain, displayName, ref created);
            action.Configure(
                displayName,
                Mathf.Max(0.01f, cost),
                executorId,
                checkedPreconditions,
                checkedEffects,
                description);
            action.ConfigureExecutionSteps(checkedSteps);
            domain.AddAction(action);
            FinishDomainChange(domain);
            return action;
        }

        public static GoapGoalDefinition CreateGoal(
            GoapDomain domain,
            string displayName,
            int priority,
            IEnumerable<GoapCondition> activationConditions,
            IEnumerable<GoapCondition> desiredState,
            string description = "")
        {
            RequireSavedDomain(domain);
            displayName = RequireText(displayName, "Goal name");
            if (domain.Goals.Any(goal =>
                    goal != null && string.Equals(goal.DisplayName, displayName, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Goal '{displayName}' already exists in this Domain.");
            }

            var checkedActivation = ValidateConditions(domain, activationConditions, "Activation conditions", false);
            var checkedDesired = ValidateConditions(domain, desiredState, "Desired state", true);
            Undo.RecordObject(domain, "Create GOAP Goal");
            var created = 0;
            var goal = CreateSubAsset<GoapGoalDefinition>(domain, displayName, ref created);
            goal.Configure(
                displayName,
                priority,
                checkedActivation,
                checkedDesired,
                description);
            domain.AddGoal(goal);
            FinishDomainChange(domain);
            return goal;
        }

        public static string CreateIdentifier(string displayName)
        {
            return ToIdentifier(RequireText(displayName, "Display name"));
        }

        public static GoapContentPresetResult AddBasicNeedsPreset(GoapDomain domain)
        {
            RequireSavedDomain(domain);
            Undo.RecordObject(domain, "Add Basic Needs GOAP Preset");
            var created = 0;

            var hungry = GetOrCreateBooleanFact(
                domain,
                "Is Hungry",
                false,
                "The agent currently needs food.",
                ref created);
            var tired = GetOrCreateBooleanFact(
                domain,
                "Is Tired",
                false,
                "The agent currently needs rest.",
                ref created);
            var hasFood = GetOrCreateBooleanFact(
                domain,
                "Has Food",
                false,
                "The agent is carrying food.",
                ref created);
            var foodAvailable = GetOrCreateBooleanFact(
                domain,
                "Food Available",
                false,
                "A usable Food smart object is available.",
                ref created);
            var bedAvailable = GetOrCreateBooleanFact(
                domain,
                "Bed Available",
                false,
                "A usable Bed smart object is available.",
                ref created);

            var takeFood = GetOrCreateAction(
                domain,
                "take-food",
                "Take Food",
                1f,
                new[]
                {
                    new GoapCondition(foodAvailable, true),
                    new GoapCondition(hasFood, false)
                },
                new[]
                {
                    new GoapCondition(foodAvailable, false),
                    new GoapCondition(hasFood, true)
                },
                new[]
                {
                    GoapActionStep.Find("Food"),
                    GoapActionStep.Reserve(),
                    GoapActionStep.Move(),
                    new GoapActionStep(GoapActionStepKind.Interact),
                    GoapActionStep.Wait(0.4f),
                    new GoapActionStep(GoapActionStepKind.ConsumeTarget),
                    new GoapActionStep(GoapActionStepKind.ReleaseTarget)
                },
                "Find, reserve, approach, and collect an available food source.",
                ref created);
            var eat = GetOrCreateAction(
                domain,
                "eat",
                "Eat Food",
                1f,
                new[]
                {
                    new GoapCondition(hungry, true),
                    new GoapCondition(hasFood, true)
                },
                new[]
                {
                    new GoapCondition(hungry, false),
                    new GoapCondition(hasFood, false)
                },
                new[] { GoapActionStep.Wait(0.6f) },
                "Consume carried food and satisfy hunger.",
                ref created);
            var rest = GetOrCreateAction(
                domain,
                "rest",
                "Rest in Bed",
                1.25f,
                new[]
                {
                    new GoapCondition(tired, true),
                    new GoapCondition(bedAvailable, true)
                },
                new[] { new GoapCondition(tired, false) },
                new[]
                {
                    GoapActionStep.Find("Bed"),
                    GoapActionStep.Reserve(),
                    GoapActionStep.Move(),
                    new GoapActionStep(GoapActionStepKind.Interact),
                    GoapActionStep.Wait(1.2f),
                    new GoapActionStep(GoapActionStepKind.ReleaseTarget)
                },
                "Find an available bed, reserve it, and rest.",
                ref created);

            var satisfyHunger = GetOrCreateGoal(
                domain,
                "Satisfy Hunger",
                50,
                new[] { new GoapCondition(hungry, true) },
                new[] { new GoapCondition(hungry, false) },
                "Become not hungry using the cheapest available plan.",
                ref created);
            var recoverEnergy = GetOrCreateGoal(
                domain,
                "Recover Energy",
                40,
                new[] { new GoapCondition(tired, true) },
                new[] { new GoapCondition(tired, false) },
                "Use an available bed to become rested.",
                ref created);

            FinishDomainChange(domain);
            return new GoapContentPresetResult(
                new[] { takeFood, eat, rest },
                new[] { satisfyHunger, recoverEnergy },
                new[]
                {
                    new GoapFactValueReference(hungry, true),
                    new GoapFactValueReference(tired, true)
                },
                new[]
                {
                    SmartObjectSensor("Food Available", "Food", foodAvailable),
                    SmartObjectSensor("Bed Available", "Bed", bedAvailable)
                },
                false,
                created);
        }

        public static GoapContentPresetResult AddResourceGatheringPreset(
            GoapDomain domain,
            string resourceName,
            string smartObjectCategory,
            string itemId,
            int targetAmount = 1,
            int goalPriority = 30)
        {
            RequireSavedDomain(domain);
            resourceName = RequireText(resourceName, "Resource name");
            smartObjectCategory = RequireText(smartObjectCategory, "Smart Object category");
            itemId = RequireText(itemId, "Inventory item ID");
            targetAmount = Mathf.Max(1, targetAmount);
            var executorId = $"gather-{ToIdentifier(resourceName)}";
            var created = 0;

            Undo.RecordObject(domain, "Add Resource Gathering GOAP Preset");
            var available = GetOrCreateBooleanFact(
                domain,
                $"{resourceName} Available",
                false,
                $"An available {smartObjectCategory} smart object exists.",
                ref created);
            var count = GetOrCreateIntegerFact(
                domain,
                $"{resourceName} Count",
                0,
                $"Amount of {itemId} in the agent inventory.",
                ref created);
            var gather = GetOrCreateAction(
                domain,
                executorId,
                $"Gather {resourceName}",
                1f,
                new[]
                {
                    new GoapCondition(available, true),
                    new GoapCondition(count, targetAmount, GoapComparison.Less)
                },
                new[]
                {
                    new GoapCondition(available, false),
                    new GoapCondition(count, 1, GoapComparison.Equal, GoapEffectOperation.Add)
                },
                new[]
                {
                    GoapActionStep.Find(smartObjectCategory),
                    GoapActionStep.Reserve(),
                    GoapActionStep.Move(),
                    new GoapActionStep(GoapActionStepKind.Interact),
                    GoapActionStep.Wait(0.65f),
                    new GoapActionStep(GoapActionStepKind.ConsumeTarget),
                    GoapActionStep.Inventory(GoapActionStepKind.InventoryAdd, itemId),
                    new GoapActionStep(GoapActionStepKind.ReleaseTarget)
                },
                $"Reserve and collect {resourceName} into inventory.",
                ref created);
            var collect = GetOrCreateGoal(
                domain,
                $"Collect {resourceName}",
                goalPriority,
                null,
                new[] { new GoapCondition(count, targetAmount, GoapComparison.GreaterOrEqual) },
                $"Collect at least {targetAmount} {itemId}.",
                ref created);

            FinishDomainChange(domain);
            return new GoapContentPresetResult(
                new[] { gather },
                new[] { collect },
                null,
                new[]
                {
                    SmartObjectSensor($"{resourceName} Available", smartObjectCategory, available),
                    new GoapProfileSensorDefinition(
                        $"{resourceName} Inventory",
                        GoapProfileSensorKind.Inventory,
                        count,
                        itemId)
                },
                true,
                created);
        }

        public static GoapAgentProfile CreateProfile(
            GoapDomain domain,
            string profileName,
            GoapContentPresetResult preset = null,
            bool includeAllDomainContent = false)
        {
            RequireSavedDomain(domain);
            profileName = RequireText(profileName, "Profile name");
            var domainPath = AssetDatabase.GetAssetPath(domain);
            var folder = Path.GetDirectoryName(domainPath)?.Replace('\\', '/');
            var safeName = SanitizeFileName(profileName);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{safeName}.asset");
            var profile = ScriptableObject.CreateInstance<GoapAgentProfile>();
            profile.name = profileName;
            profile.Configure(
                domain,
                includeAllDomainContent || preset == null ? null : preset.Actions,
                includeAllDomainContent || preset == null ? null : preset.Goals,
                0.2f,
                false,
                preset?.InitialFacts,
                preset?.Sensors);
            AssetDatabase.CreateAsset(profile, path);
            Undo.RegisterCreatedObjectUndo(profile, "Create GOAP Agent Profile");
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static GoapAgentAuthoring SetupAgent(
            GameObject gameObject,
            GoapAgentProfile profile,
            bool addInventory = false,
            bool addStats = false)
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            if (profile == null || profile.Domain == null)
            {
                throw new ArgumentException("The agent profile and its domain are required.", nameof(profile));
            }

            var authoring = gameObject.GetComponent<GoapAgentAuthoring>() ??
                            Undo.AddComponent<GoapAgentAuthoring>(gameObject);
            if (addInventory && gameObject.GetComponent<GoapInventory>() == null)
            {
                Undo.AddComponent<GoapInventory>(gameObject);
            }

            if (addStats && gameObject.GetComponent<GoapStatSource>() == null)
            {
                Undo.AddComponent<GoapStatSource>(gameObject);
            }

            Undo.RecordObject(authoring, "Configure GOAP Agent");
            var serializedAuthoring = new SerializedObject(authoring);
            serializedAuthoring.FindProperty("_profile").objectReferenceValue = profile;
            serializedAuthoring.ApplyModifiedProperties();
            authoring.ApplyProfile();
            EditorUtility.SetDirty(authoring);
            MarkSceneDirty(gameObject);
            return authoring;
        }

        public static GameObject CreateAgent(
            string agentName,
            GoapAgentProfile profile,
            bool addInventory = false,
            bool addStats = false,
            bool createVisiblePlaceholder = true,
            Vector3? position = null)
        {
            agentName = RequireText(agentName, "Agent name");
            var gameObject = createVisiblePlaceholder
                ? GameObject.CreatePrimitive(PrimitiveType.Capsule)
                : new GameObject();
            gameObject.name = GameObjectUtility.GetUniqueNameForSibling(null, agentName);
            gameObject.transform.position = position ?? Vector3.zero;
            Undo.RegisterCreatedObjectUndo(gameObject, "Create GOAP Agent");
            SetupAgent(gameObject, profile, addInventory, addStats);
            Selection.activeGameObject = gameObject;
            return gameObject;
        }

        public static GoapSmartObject SetupSmartObject(
            GameObject gameObject,
            string category,
            bool consumeOnUse,
            int capacity = 1)
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            category = RequireText(category, "Smart Object category");
            var smartObject = gameObject.GetComponent<GoapSmartObject>() ??
                              Undo.AddComponent<GoapSmartObject>(gameObject);
            Undo.RecordObject(smartObject, "Configure GOAP Smart Object");
            smartObject.Configure(category, consumeOnUse, Mathf.Max(1, capacity));
            EditorUtility.SetDirty(smartObject);
            MarkSceneDirty(gameObject);
            return smartObject;
        }

        public static GameObject CreateSmartObject(
            string objectName,
            string category,
            bool consumeOnUse,
            int capacity = 1,
            PrimitiveType primitive = PrimitiveType.Cube,
            Vector3? position = null,
            Vector3? scale = null)
        {
            objectName = RequireText(objectName, "Object name");
            var gameObject = GameObject.CreatePrimitive(primitive);
            gameObject.name = GameObjectUtility.GetUniqueNameForSibling(null, objectName);
            gameObject.transform.position = position ?? Vector3.zero;
            gameObject.transform.localScale = scale ?? Vector3.one;
            Undo.RegisterCreatedObjectUndo(gameObject, "Create GOAP Smart Object");
            SetupSmartObject(gameObject, category, consumeOnUse, capacity);
            Selection.activeGameObject = gameObject;
            return gameObject;
        }

        private static GoapFact GetOrCreateBooleanFact(
            GoapDomain domain,
            string displayName,
            bool defaultValue,
            string description,
            ref int created)
        {
            var existing = domain.FindFact(displayName);
            if (existing != null)
            {
                EnsureFactType(existing, GoapFactType.Boolean);
                return existing;
            }

            var fact = CreateSubAsset<GoapFact>(domain, displayName, ref created);
            fact.Configure(displayName, defaultValue, description);
            domain.AddFact(fact);
            return fact;
        }

        private static GoapFact GetOrCreateIntegerFact(
            GoapDomain domain,
            string displayName,
            int defaultValue,
            string description,
            ref int created)
        {
            var existing = domain.FindFact(displayName);
            if (existing != null)
            {
                EnsureFactType(existing, GoapFactType.Integer);
                return existing;
            }

            var fact = CreateSubAsset<GoapFact>(domain, displayName, ref created);
            fact.ConfigureInteger(displayName, defaultValue, description);
            domain.AddFact(fact);
            return fact;
        }

        private static GoapActionDefinition GetOrCreateAction(
            GoapDomain domain,
            string executorId,
            string displayName,
            float cost,
            IEnumerable<GoapCondition> preconditions,
            IEnumerable<GoapCondition> effects,
            IEnumerable<GoapActionStep> steps,
            string description,
            ref int created)
        {
            var existing = domain.FindAction(executorId);
            if (existing != null)
            {
                return existing;
            }

            var action = CreateSubAsset<GoapActionDefinition>(domain, displayName, ref created);
            action.Configure(displayName, cost, executorId, preconditions, effects, description);
            action.ConfigureExecutionSteps(steps);
            domain.AddAction(action);
            return action;
        }

        private static GoapGoalDefinition GetOrCreateGoal(
            GoapDomain domain,
            string displayName,
            int priority,
            IEnumerable<GoapCondition> activation,
            IEnumerable<GoapCondition> desired,
            string description,
            ref int created)
        {
            var existing = domain.Goals.FirstOrDefault(goal =>
                goal != null && string.Equals(goal.DisplayName, displayName, StringComparison.Ordinal));
            if (existing != null)
            {
                return existing;
            }

            var goal = CreateSubAsset<GoapGoalDefinition>(domain, displayName, ref created);
            goal.Configure(displayName, priority, activation, desired, description);
            domain.AddGoal(goal);
            return goal;
        }

        private static T CreateSubAsset<T>(GoapDomain domain, string displayName, ref int created)
            where T : ScriptableObject
        {
            var definition = ScriptableObject.CreateInstance<T>();
            definition.name = displayName;
            Undo.RegisterCreatedObjectUndo(definition, "Create GOAP Definition");
            AssetDatabase.AddObjectToAsset(definition, domain);
            created++;
            return definition;
        }

        private static GoapProfileSensorDefinition SmartObjectSensor(
            string name,
            string category,
            GoapFact fact)
        {
            var sensor = new GoapProfileSensorDefinition(
                name,
                GoapProfileSensorKind.SmartObject,
                fact,
                category);
            sensor.ConfigureRange(0f);
            return sensor;
        }

        private static GoapCondition[] ValidateConditions(
            GoapDomain domain,
            IEnumerable<GoapCondition> conditions,
            string label,
            bool requireAtLeastOne)
        {
            var result = conditions?.ToArray() ?? Array.Empty<GoapCondition>();
            if (requireAtLeastOne && result.Length == 0)
            {
                throw new InvalidOperationException($"{label} must contain at least one Fact.");
            }

            if (result.Any(condition => !condition.IsValid))
            {
                throw new InvalidOperationException($"{label} contains an empty Fact reference.");
            }

            var foreignFact = result.FirstOrDefault(condition => !domain.Facts.Contains(condition.Fact));
            if (foreignFact.IsValid)
            {
                throw new InvalidOperationException(
                    $"Fact '{foreignFact.Fact.DisplayName}' from {label} does not belong to this Domain.");
            }

            var duplicate = result.GroupBy(condition => condition.Fact).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    $"Fact '{duplicate.Key.DisplayName}' appears more than once in {label}.");
            }

            return result;
        }

        private static void FinishDomainChange(GoapDomain domain)
        {
            EditorUtility.SetDirty(domain);
            foreach (var definition in domain.Facts.Cast<UnityEngine.Object>()
                         .Concat(domain.Actions)
                         .Concat(domain.Goals))
            {
                if (definition != null)
                {
                    EditorUtility.SetDirty(definition);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(domain));
            DomainChanged?.Invoke(domain);
        }

        private static void EnsureFactType(GoapFact fact, GoapFactType expected)
        {
            if (fact.ValueType != expected)
            {
                throw new InvalidOperationException(
                    $"Fact '{fact.DisplayName}' already exists as {fact.ValueType}, but the preset requires {expected}.");
            }
        }

        private static void RequireSavedDomain(GoapDomain domain)
        {
            if (domain == null)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(domain)))
            {
                throw new InvalidOperationException("Save the GOAP domain as an asset before adding content.");
            }
        }

        private static string RequireText(string value, string label)
        {
            value = value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{label} cannot be empty.");
            }

            return value;
        }

        private static string ToIdentifier(string value)
        {
            var characters = value.Trim().ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray();
            var identifier = string.Join(
                "-",
                new string(characters).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(identifier) ? "resource" : identifier;
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var result = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            return string.IsNullOrWhiteSpace(result) ? "GOAP Agent Profile" : result.Trim();
        }

        private static void MarkSceneDirty(GameObject gameObject)
        {
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
    }
}
