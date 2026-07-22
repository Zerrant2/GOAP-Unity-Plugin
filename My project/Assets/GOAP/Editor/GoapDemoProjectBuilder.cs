using System;
using System.Collections.Generic;
using System.Linq;
using Practice.GOAP.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Practice.GOAP.Editor
{
    public static class GoapDemoProjectBuilder
    {
        public const string DomainPath = "Assets/GOAP/Demo/Generated/GOAP Demo Domain.asset";
        public const string LumberjackProfilePath = "Assets/GOAP/Demo/Generated/Lumberjack Profile.asset";
        public const string WorkerProfilePath = "Assets/GOAP/Demo/Generated/Worker Profile.asset";
        public const string ResidentProfilePath = "Assets/GOAP/Demo/Generated/Resident Profile.asset";
        public const string GuardProfilePath = "Assets/GOAP/Demo/Generated/Guard Profile.asset";
        public const string SurvivorProfilePath = "Assets/GOAP/Demo/Generated/Survivor Profile.asset";
        public const string ScenePath = "Assets/GOAP/Demo/Scenes/GOAP Demo.unity";
        public const string BenchmarkFolder = "Assets/GOAP/Demo/Benchmarks";
        private const string InitialBuildSessionKey = "Practice.GOAP.InitialDemoBuildV4";
        private const string GeneratedContentVersionKeyPrefix = "Practice.GOAP.GeneratedContentVersion";
        private const int GeneratedContentVersion = 4;

        private static string GeneratedContentVersionKey =>
            $"{GeneratedContentVersionKeyPrefix}.{Application.dataPath}";

        [InitializeOnLoadMethod]
        private static void ScheduleInitialBuild()
        {
            if (SessionState.GetBool(InitialBuildSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(InitialBuildSessionKey, true);
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    (EditorPrefs.GetInt(GeneratedContentVersionKey, 0) < GeneratedContentVersion ||
                     AssetDatabase.LoadAssetAtPath<GoapDomain>(DomainPath) == null ||
                     AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(LumberjackProfilePath) == null ||
                     AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(WorkerProfilePath) == null ||
                     AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(ResidentProfilePath) == null ||
                     AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(GuardProfilePath) == null ||
                     AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(SurvivorProfilePath) == null))
                {
                    BuildOrRefreshDemo();
                }
            };
        }

        [MenuItem("Tools/GOAP/Build Demo Project")]
        public static void BuildOrRefreshDemo()
        {
            EnsureFolder("Assets/GOAP/Demo");
            EnsureFolder("Assets/GOAP/Demo/Generated");
            EnsureFolder("Assets/GOAP/Demo/Scenes");

            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(DomainPath);
            if (domain == null)
            {
                domain = CreateDemoDomain();
            }

            var resourceDefinitions = EnsureResourceGatheringDefinitions(domain);
            EnsureUniversalExecutions(domain, resourceDefinitions);
            var profiles = EnsureAgentProfiles(domain, resourceDefinitions);
            EnsureDemoScene(domain, profiles);
            EnsureBenchmarkScenes(profiles.Lumberjack);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorPrefs.SetInt(GeneratedContentVersionKey, GeneratedContentVersion);
            Selection.activeObject = domain;
            Debug.Log($"GOAP demo is ready. Open '{ScenePath}' and enter Play Mode.", domain);
        }

        [MenuItem("Tools/GOAP/Open Demo Scene")]
        public static void OpenDemoScene()
        {
            BuildOrRefreshDemo();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        [MenuItem("Tools/GOAP/Build Benchmark Scenes")]
        public static void BuildBenchmarkScenes()
        {
            BuildOrRefreshDemo();
        }

        private static GoapDomain CreateDemoDomain()
        {
            var domain = ScriptableObject.CreateInstance<GoapDomain>();
            domain.name = "GOAP Demo Domain";
            AssetDatabase.CreateAsset(domain, DomainPath);

            var hungry = AddFact(domain, "Is Hungry", false, "The agent needs food.");
            var tired = AddFact(domain, "Is Tired", false, "The agent needs rest.");
            var hasFood = AddFact(domain, "Has Food", false, "The agent carries food.");
            var foodAvailable = AddFact(domain, "Food Available", false, "Usable food exists in the world.");
            var bedAvailable = AddFact(domain, "Bed Available", false, "A usable bed exists in the world.");
            var hasWeapon = AddFact(domain, "Has Weapon", false, "The agent carries a weapon.");
            var weaponAvailable = AddFact(domain, "Weapon Available", false, "A weapon can be collected.");
            var enemyVisible = AddFact(domain, "Enemy Visible", false, "A live enemy is visible.");

            var takeFood = AddAction(
                domain,
                "Take Food",
                1f,
                "take-food",
                Conditions((foodAvailable, true), (hasFood, false)),
                Conditions((foodAvailable, false), (hasFood, true)),
                "Move to the nearest food source and collect it.");
            var eat = AddAction(
                domain,
                "Eat Food",
                1f,
                "eat",
                Conditions((hungry, true), (hasFood, true)),
                Conditions((hungry, false), (hasFood, false)),
                "Consume carried food to satisfy hunger.");
            var rest = AddAction(
                domain,
                "Rest in Bed",
                1.25f,
                "rest",
                Conditions((tired, true), (bedAvailable, true)),
                Conditions((tired, false)),
                "Move to an available bed and recover energy.");
            var takeWeapon = AddAction(
                domain,
                "Take Weapon",
                1.5f,
                "take-weapon",
                Conditions((weaponAvailable, true), (hasWeapon, false)),
                Conditions((weaponAvailable, false), (hasWeapon, true)),
                "Collect a weapon before confronting an enemy.");
            var attack = AddAction(
                domain,
                "Attack Enemy",
                2f,
                "attack",
                Conditions((enemyVisible, true), (hasWeapon, true)),
                Conditions((enemyVisible, false)),
                "Approach and defeat the visible enemy.");

            var hungerGoal = AddGoal(
                domain,
                "Satisfy Hunger",
                50,
                Conditions((hungry, true)),
                Conditions((hungry, false)),
                "Become not hungry using the cheapest available plan.");
            var restGoal = AddGoal(
                domain,
                "Recover Energy",
                40,
                Conditions((tired, true)),
                Conditions((tired, false)),
                "Become rested.");
            var combatGoal = AddGoal(
                domain,
                "Defeat Enemy",
                100,
                Conditions((enemyVisible, true)),
                Conditions((enemyVisible, false)),
                "Remove the immediate threat. This goal has the highest priority.");

            domain.SetNodePosition(takeFood.Id, GoapNodeKind.Action, new Vector2(80f, 80f));
            domain.SetNodePosition(eat.Id, GoapNodeKind.Action, new Vector2(390f, 80f));
            domain.SetNodePosition(rest.Id, GoapNodeKind.Action, new Vector2(390f, 330f));
            domain.SetNodePosition(takeWeapon.Id, GoapNodeKind.Action, new Vector2(80f, 580f));
            domain.SetNodePosition(attack.Id, GoapNodeKind.Action, new Vector2(390f, 580f));
            domain.SetNodePosition(hungerGoal.Id, GoapNodeKind.Goal, new Vector2(760f, 80f));
            domain.SetNodePosition(restGoal.Id, GoapNodeKind.Goal, new Vector2(760f, 330f));
            domain.SetNodePosition(combatGoal.Id, GoapNodeKind.Goal, new Vector2(760f, 580f));

            EditorUtility.SetDirty(domain);
            AssetDatabase.SaveAssets();
            return domain;
        }

        private static GoapFact AddFact(GoapDomain domain, string displayName, bool defaultValue, string description)
        {
            var fact = ScriptableObject.CreateInstance<GoapFact>();
            fact.Configure(displayName, defaultValue, description);
            AssetDatabase.AddObjectToAsset(fact, domain);
            domain.AddFact(fact);
            return fact;
        }

        private static GoapFact AddIntegerFact(GoapDomain domain, string displayName, int defaultValue, string description)
        {
            var fact = ScriptableObject.CreateInstance<GoapFact>();
            fact.ConfigureInteger(displayName, defaultValue, description);
            AssetDatabase.AddObjectToAsset(fact, domain);
            domain.AddFact(fact);
            return fact;
        }

        private static GoapActionDefinition AddAction(
            GoapDomain domain,
            string displayName,
            float cost,
            string executorId,
            IReadOnlyList<GoapCondition> preconditions,
            IReadOnlyList<GoapCondition> effects,
            string description)
        {
            var action = ScriptableObject.CreateInstance<GoapActionDefinition>();
            action.Configure(displayName, cost, executorId, preconditions, effects, description);
            AssetDatabase.AddObjectToAsset(action, domain);
            domain.AddAction(action);
            return action;
        }

        private static GoapGoalDefinition AddGoal(
            GoapDomain domain,
            string displayName,
            int priority,
            IReadOnlyList<GoapCondition> activation,
            IReadOnlyList<GoapCondition> desired,
            string description)
        {
            var goal = ScriptableObject.CreateInstance<GoapGoalDefinition>();
            goal.Configure(displayName, priority, activation, desired, description);
            AssetDatabase.AddObjectToAsset(goal, domain);
            domain.AddGoal(goal);
            return goal;
        }

        private static GoapCondition[] Conditions(params (GoapFact fact, bool value)[] values)
        {
            return values.Select(value => new GoapCondition(value.fact, value.value)).ToArray();
        }

        private static ResourceDemoDefinitions EnsureResourceGatheringDefinitions(GoapDomain domain)
        {
            var woodAvailable = domain.FindFact("Wood Available") ??
                                AddFact(domain, "Wood Available", false, "A reservable tree is available.");
            var woodCount = domain.FindFact("Wood Count") ??
                            AddIntegerFact(domain, "Wood Count", 0, "Amount of wood in the agent inventory.");

            var gather = domain.FindAction("gather-wood");
            if (gather == null)
            {
                gather = AddAction(
                    domain,
                    "Gather Wood",
                    1f,
                    "gather-wood",
                    new[]
                    {
                        new GoapCondition(woodAvailable, true),
                        new GoapCondition(woodCount, 1, GoapComparison.Less)
                    },
                    new[]
                    {
                        new GoapCondition(woodAvailable, false),
                        new GoapCondition(woodCount, 1, GoapComparison.Equal, GoapEffectOperation.Add)
                    },
                    "Reserve the nearest tree, move to it, and collect one unit of wood.");
                gather.ConfigureBuiltInExecution(GoapBuiltInActionSettings.Interact(
                    "Wood",
                    0.65f,
                    true,
                    GoapInventoryOperation.Add,
                    "Wood",
                    1));
            }

            var collectGoal = domain.Goals.FirstOrDefault(goal =>
                goal != null && goal.DisplayName == "Collect Wood");
            if (collectGoal == null)
            {
                collectGoal = AddGoal(
                    domain,
                    "Collect Wood",
                    30,
                    null,
                    new[] { new GoapCondition(woodCount, 1, GoapComparison.GreaterOrEqual) },
                    "Collect at least one unit of wood.");
            }

            domain.SetNodePosition(woodAvailable.Id, GoapNodeKind.Fact, new Vector2(40f, 1060f));
            domain.SetNodePosition(woodCount.Id, GoapNodeKind.Fact, new Vector2(40f, 1190f));
            domain.SetNodePosition(gather.Id, GoapNodeKind.Action, new Vector2(390f, 1040f));
            domain.SetNodePosition(collectGoal.Id, GoapNodeKind.Goal, new Vector2(760f, 1040f));
            EditorUtility.SetDirty(domain);
            EditorUtility.SetDirty(gather);
            EditorUtility.SetDirty(collectGoal);
            return new ResourceDemoDefinitions(gather, collectGoal);
        }

        private static GoapAgentProfile EnsureLumberjackProfile(
            GoapDomain domain,
            ResourceDemoDefinitions definitions)
        {
            var profile = AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(LumberjackProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<GoapAgentProfile>();
                profile.name = "Lumberjack Profile";
                AssetDatabase.CreateAsset(profile, LumberjackProfilePath);
            }

            profile.Configure(
                domain,
                new[] { definitions.GatherAction },
                new[] { definitions.CollectGoal },
                0.15f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static DemoProfiles EnsureAgentProfiles(
            GoapDomain domain,
            ResourceDemoDefinitions resourceDefinitions)
        {
            var hungry = domain.FindFact("Is Hungry");
            var tired = domain.FindFact("Is Tired");
            var foodAvailable = domain.FindFact("Food Available");
            var bedAvailable = domain.FindFact("Bed Available");
            var weaponAvailable = domain.FindFact("Weapon Available");
            var enemyVisible = domain.FindFact("Enemy Visible");
            var woodAvailable = domain.FindFact("Wood Available");
            var woodCount = domain.FindFact("Wood Count");

            var hungerGoal = domain.Goals.First(goal => goal != null && goal.DisplayName == "Satisfy Hunger");
            var restGoal = domain.Goals.First(goal => goal != null && goal.DisplayName == "Recover Energy");
            var combatGoal = domain.Goals.First(goal => goal != null && goal.DisplayName == "Defeat Enemy");

            var foodSensor = SmartObjectSensor("Food Nearby", "Food", foodAvailable);
            var bedSensor = SmartObjectSensor("Bed Nearby", "Bed", bedAvailable);
            var weaponSensor = SmartObjectSensor("Weapon Nearby", "Weapon", weaponAvailable);
            var enemySensor = SmartObjectSensor("Enemy Nearby", "Enemy", enemyVisible);
            var woodSensor = SmartObjectSensor("Wood Nearby", "Wood", woodAvailable);
            var woodInventorySensor = new GoapProfileSensorDefinition(
                "Wood Inventory",
                GoapProfileSensorKind.Inventory,
                woodCount,
                "Wood");

            var worker = EnsureProfile(
                WorkerProfilePath,
                "Worker Profile",
                domain,
                new[] { domain.FindAction("take-food"), domain.FindAction("eat") },
                new[] { hungerGoal },
                new[] { new GoapFactValueReference(hungry, true) },
                new[] { foodSensor });
            var resident = EnsureProfile(
                ResidentProfilePath,
                "Resident Profile",
                domain,
                new[] { domain.FindAction("rest") },
                new[] { restGoal },
                new[] { new GoapFactValueReference(tired, true) },
                new[] { bedSensor });
            var guard = EnsureProfile(
                GuardProfilePath,
                "Guard Profile",
                domain,
                new[] { domain.FindAction("take-weapon"), domain.FindAction("attack") },
                new[] { combatGoal },
                null,
                new[] { weaponSensor, enemySensor });
            var survivor = EnsureProfile(
                SurvivorProfilePath,
                "Survivor Profile",
                domain,
                new[] { domain.FindAction("take-food"), domain.FindAction("eat"), domain.FindAction("rest") },
                new[] { hungerGoal, restGoal },
                new[]
                {
                    new GoapFactValueReference(hungry, true),
                    new GoapFactValueReference(tired, true)
                },
                new[] { foodSensor, bedSensor });
            var lumberjack = EnsureProfile(
                LumberjackProfilePath,
                "Lumberjack Profile",
                domain,
                new[] { resourceDefinitions.GatherAction },
                new[] { resourceDefinitions.CollectGoal },
                null,
                new[] { woodSensor, woodInventorySensor });

            return new DemoProfiles(worker, resident, guard, survivor, lumberjack);
        }

        private static GoapAgentProfile EnsureProfile(
            string path,
            string profileName,
            GoapDomain domain,
            IEnumerable<GoapActionDefinition> actions,
            IEnumerable<GoapGoalDefinition> goals,
            IEnumerable<GoapFactValueReference> initialFacts,
            IEnumerable<GoapProfileSensorDefinition> sensors)
        {
            var profile = AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<GoapAgentProfile>();
                profile.name = profileName;
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.Configure(domain, actions, goals, 0.15f, false, initialFacts, sensors);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static GoapProfileSensorDefinition SmartObjectSensor(
            string sensorName,
            string category,
            GoapFact fact)
        {
            var sensor = new GoapProfileSensorDefinition(
                sensorName,
                GoapProfileSensorKind.SmartObject,
                fact,
                category);
            sensor.ConfigureRange(0f);
            return sensor;
        }

        private static void EnsureUniversalExecutions(
            GoapDomain domain,
            ResourceDemoDefinitions resourceDefinitions)
        {
            var hungry = domain.FindFact("Is Hungry");
            var tired = domain.FindFact("Is Tired");
            var hasFood = domain.FindFact("Has Food");
            var hasWeapon = domain.FindFact("Has Weapon");

            ConfigureSequence(domain.FindAction("take-food"),
                GoapActionStep.Find("Food"),
                GoapActionStep.Reserve(),
                GoapActionStep.Move(),
                new GoapActionStep(GoapActionStepKind.Interact),
                GoapActionStep.Wait(0.45f),
                new GoapActionStep(GoapActionStepKind.ConsumeTarget),
                GoapActionStep.Fact(GoapActionStepKind.SetFact, new GoapFactValueReference(hasFood, true)),
                new GoapActionStep(GoapActionStepKind.ReleaseTarget));
            ConfigureSequence(domain.FindAction("eat"),
                GoapActionStep.Wait(0.4f),
                GoapActionStep.Fact(GoapActionStepKind.SetFact, new GoapFactValueReference(hasFood, false)),
                GoapActionStep.Fact(GoapActionStepKind.SetFact, new GoapFactValueReference(hungry, false)));
            ConfigureSequence(domain.FindAction("rest"),
                GoapActionStep.Find("Bed"),
                GoapActionStep.Reserve(),
                GoapActionStep.Move(),
                new GoapActionStep(GoapActionStepKind.Interact),
                GoapActionStep.Wait(1.1f),
                GoapActionStep.Fact(GoapActionStepKind.SetFact, new GoapFactValueReference(tired, false)),
                new GoapActionStep(GoapActionStepKind.ReleaseTarget));
            ConfigureSequence(domain.FindAction("take-weapon"),
                GoapActionStep.Find("Weapon"),
                GoapActionStep.Reserve(),
                GoapActionStep.Move(),
                GoapActionStep.Wait(0.5f),
                new GoapActionStep(GoapActionStepKind.ConsumeTarget),
                GoapActionStep.Fact(GoapActionStepKind.SetFact, new GoapFactValueReference(hasWeapon, true)),
                new GoapActionStep(GoapActionStepKind.ReleaseTarget));
            ConfigureSequence(domain.FindAction("attack"),
                GoapActionStep.Find("Enemy"),
                GoapActionStep.Reserve(),
                GoapActionStep.Move(),
                GoapActionStep.Wait(0.8f),
                new GoapActionStep(GoapActionStepKind.ConsumeTarget),
                new GoapActionStep(GoapActionStepKind.ReleaseTarget));
            ConfigureSequence(resourceDefinitions.GatherAction,
                GoapActionStep.Find("Wood"),
                GoapActionStep.Reserve(),
                GoapActionStep.Move(),
                GoapActionStep.Wait(0.65f),
                new GoapActionStep(GoapActionStepKind.ConsumeTarget),
                GoapActionStep.Inventory(GoapActionStepKind.InventoryAdd, "Wood"),
                new GoapActionStep(GoapActionStepKind.ReleaseTarget));
        }

        private static void ConfigureSequence(GoapActionDefinition action, params GoapActionStep[] steps)
        {
            if (action == null)
            {
                return;
            }

            action.ConfigureExecutionSteps(steps);
            EditorUtility.SetDirty(action);
        }

        private static void EnsureDemoScene(GoapDomain domain, DemoProfiles profiles)
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            var previousActiveScene = SceneManager.GetActiveScene();
            var createMode = NewSceneMode.Additive;
            if (sceneAsset == null && !PrepareForNewScene(out createMode))
            {
                Debug.LogWarning("Skipped demo scene generation: save the current untitled scene first.");
                return;
            }

            var demoScene = sceneAsset == null
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, createMode)
                : SceneManager.GetSceneByPath(ScenePath);
            var wasLoaded = demoScene.IsValid() && demoScene.isLoaded;
            if (sceneAsset != null && !wasLoaded)
            {
                demoScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(demoScene);
            foreach (var existingRoot in demoScene.GetRootGameObjects())
            {
                UnityEngine.Object.DestroyImmediate(existingRoot);
            }

            var root = new GameObject("GOAP Authored Demo");
            SceneManager.MoveGameObjectToScene(root, demoScene);
            CreateCameraAndLight(root.transform);
            CreateVisual(
                "Floor",
                PrimitiveType.Cube,
                new Vector3(0f, -0.15f, 1f),
                new Vector3(18f, 0.25f, 12f),
                new Color(0.16f, 0.19f, 0.21f),
                root.transform);

            var foodA = CreateSmartObject("Food A", "Food", true, new Vector3(-6f, 0.55f, 3.5f), Color.green, root.transform);
            var foodB = CreateSmartObject("Food B", "Food", true, new Vector3(-2.5f, 0.55f, 3.5f), new Color(0.35f, 0.85f, 0.42f), root.transform);
            var bed = CreateSmartObject("Bed", "Bed", false, new Vector3(0f, 0.55f, 3.8f), new Color(0.25f, 0.55f, 0.9f), root.transform, new Vector3(2.2f, 0.7f, 1.2f));
            var weapon = CreateSmartObject("Weapon", "Weapon", true, new Vector3(5.7f, 0.75f, 0f), new Color(0.95f, 0.7f, 0.18f), root.transform);
            var enemy = CreateSmartObject("Enemy", "Enemy", true, new Vector3(6f, 1f, 4.3f), new Color(0.88f, 0.2f, 0.22f), root.transform);
            var tree = CreateSmartObject("Tree", "Wood", true, new Vector3(7f, 1f, -3.8f), new Color(0.2f, 0.62f, 0.3f), root.transform, new Vector3(0.75f, 2f, 0.75f));

            var worker = CreateAuthoredAgent("Worker NPC", profiles.Worker, new Vector3(-6f, 1f, -2.5f), new Color(0.2f, 0.8f, 0.48f), root.transform);
            var resident = CreateAuthoredAgent("Resident NPC", profiles.Resident, new Vector3(0f, 1f, -2.5f), new Color(0.22f, 0.55f, 0.95f), root.transform);
            var guard = CreateAuthoredAgent("Guard NPC", profiles.Guard, new Vector3(4.5f, 1f, -2.5f), new Color(0.95f, 0.63f, 0.18f), root.transform);
            var survivor = CreateAuthoredAgent("Survivor NPC", profiles.Survivor, new Vector3(-2.5f, 1f, -2.5f), new Color(0.72f, 0.35f, 0.95f), root.transform);
            var lumberjack = CreateAuthoredAgent("Lumberjack NPC", profiles.Lumberjack, new Vector3(7f, 1f, 0.5f), new Color(0.24f, 0.75f, 0.62f), root.transform, true);

            var agents = new[] { worker, resident, guard, survivor, lumberjack };
            var bootstrap = root.AddComponent<GoapDemoBootstrap>();
            bootstrap.ConfigureAuthored(
                domain,
                agents,
                worker,
                resident,
                guard,
                survivor,
                new[] { foodA, foodB },
                bed,
                weapon,
                enemy,
                tree);

            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.SaveScene(demoScene, ScenePath);
            if ((sceneAsset == null || !wasLoaded) && createMode != NewSceneMode.Single)
            {
                EditorSceneManager.CloseScene(demoScene, true);
            }

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void CreateCameraAndLight(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(parent);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0f, 13f, -15f),
                Quaternion.Euler(34f, 0f, 0f));
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.07f, 0.09f);

            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
        }

        private static GoapSmartObject CreateSmartObject(
            string name,
            string category,
            bool consume,
            Vector3 position,
            Color color,
            Transform parent,
            Vector3? scale = null)
        {
            var gameObject = CreateVisual(
                name,
                category == "Enemy" ? PrimitiveType.Capsule : category == "Wood" ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                position,
                scale ?? Vector3.one,
                color,
                parent);
            var smartObject = gameObject.AddComponent<GoapSmartObject>();
            smartObject.Configure(category, consume);
            return smartObject;
        }

        private static GoapAgent CreateAuthoredAgent(
            string name,
            GoapAgentProfile profile,
            Vector3 position,
            Color color,
            Transform parent,
            bool inventory = false)
        {
            var gameObject = CreateVisual(name, PrimitiveType.Capsule, position, Vector3.one, color, parent);
            if (inventory)
            {
                gameObject.AddComponent<GoapInventory>();
            }

            var authoring = gameObject.AddComponent<GoapAgentAuthoring>();
            authoring.Configure(profile);
            var agent = gameObject.GetComponent<GoapAgent>();
            gameObject.AddComponent<DemoAgentLabel>().Configure(agent, name.Replace(" NPC", ""), Color.white);
            return agent;
        }

        private static GameObject CreateVisual(
            string name,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Color color,
            Transform parent)
        {
            var gameObject = GameObject.CreatePrimitive(primitive);
            gameObject.name = name;
            gameObject.transform.SetParent(parent);
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = EnsureMaterial(name, color);
            return gameObject;
        }

        private static Material EnsureMaterial(string name, Color color, bool enableInstancing = false)
        {
            EnsureFolder("Assets/GOAP/Demo/Generated/Materials");
            var safeName = new string(name.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
            var path = $"Assets/GOAP/Demo/Generated/Materials/{safeName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = safeName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            material.enableInstancing = enableInstancing;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureBenchmarkScenes(GoapAgentProfile profile)
        {
            EnsureFolder(BenchmarkFolder);
            foreach (var count in new[] { 10, 100, 500 })
            {
                EnsureBenchmarkScene(profile, count);
            }
        }

        private static void EnsureBenchmarkScene(GoapAgentProfile profile, int agentCount)
        {
            var path = $"{BenchmarkFolder}/GOAP Benchmark {agentCount}.unity";
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            var previousActiveScene = SceneManager.GetActiveScene();
            var createMode = NewSceneMode.Additive;
            if (sceneAsset == null && !PrepareForNewScene(out createMode))
            {
                Debug.LogWarning(
                    $"Skipped benchmark scene {agentCount}: save the current untitled scene first.");
                return;
            }

            var benchmarkScene = sceneAsset == null
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, createMode)
                : SceneManager.GetSceneByPath(path);
            var wasLoaded = benchmarkScene.IsValid() && benchmarkScene.isLoaded;
            if (sceneAsset != null && !wasLoaded)
            {
                benchmarkScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(benchmarkScene);
            foreach (var existingRoot in benchmarkScene.GetRootGameObjects())
            {
                UnityEngine.Object.DestroyImmediate(existingRoot);
            }

            var root = new GameObject($"GOAP Benchmark {agentCount}");
            SceneManager.MoveGameObjectToScene(root, benchmarkScene);
            var runner = root.AddComponent<GoapBenchmarkRunner>();
            runner.Configure(profile, agentCount);

            const float spacing = 1.25f;
            var columns = Mathf.CeilToInt(Mathf.Sqrt(agentCount));
            var rows = Mathf.CeilToInt((float)agentCount / columns);
            var width = Mathf.Max(5f, (columns - 1) * spacing + 3f);
            var depth = Mathf.Max(5f, (rows - 1) * spacing + 3f);
            var floor = CreateVisual(
                "Benchmark Floor",
                PrimitiveType.Plane,
                Vector3.zero,
                new Vector3(width / 10f, 1f, depth / 10f),
                new Color(0.13f, 0.15f, 0.17f),
                root.transform);
            var sharedResource = CreateVisual(
                "Shared Wood Resource",
                PrimitiveType.Cylinder,
                new Vector3(0f, 1f, 0f),
                new Vector3(0.7f, 1f, 0.7f),
                new Color(0.18f, 0.66f, 0.32f),
                root.transform);
            sharedResource.AddComponent<GoapSmartObject>().Configure("Wood", false, agentCount);

            runner.ConfigureVisualization(
                EnsureMaterial("Benchmark Idle", new Color(0.42f, 0.47f, 0.53f), true),
                EnsureMaterial("Benchmark Queued", new Color(0.96f, 0.64f, 0.12f), true),
                EnsureMaterial("Benchmark Active", new Color(0.12f, 0.7f, 0.95f), true),
                EnsureMaterial("Benchmark Completed", new Color(0.2f, 0.82f, 0.42f), true),
                floor.GetComponent<Renderer>(),
                sharedResource.GetComponent<Renderer>());
            CreateBenchmarkCameraAndLight(root.transform, width, depth);
            EditorSceneManager.SaveScene(benchmarkScene, path);

            if ((sceneAsset == null || !wasLoaded) && createMode != NewSceneMode.Single)
            {
                EditorSceneManager.CloseScene(benchmarkScene, true);
            }

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static void CreateBenchmarkCameraAndLight(Transform parent, float width, float depth)
        {
            var extent = Mathf.Max(width, depth);
            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(parent);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, Mathf.Max(12f, extent * 1.05f), -extent * 0.7f);
            cameraObject.transform.LookAt(Vector3.zero);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(depth * 0.62f, width * 0.38f) + 2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = Mathf.Max(100f, extent * 4f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.07f, 0.09f);

            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
        }

        private static bool PrepareForNewScene(out NewSceneMode createMode)
        {
            createMode = NewSceneMode.Additive;
            var untitledScenes = new List<Scene>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!string.IsNullOrWhiteSpace(scene.path))
                {
                    continue;
                }

                if (scene.isDirty)
                {
                    return false;
                }

                untitledScenes.Add(scene);
            }

            if (untitledScenes.Count == 0)
            {
                return true;
            }

            if (SceneManager.sceneCount == untitledScenes.Count)
            {
                createMode = NewSceneMode.Single;
                return true;
            }

            foreach (var scene in untitledScenes)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            return true;
        }

        private readonly struct ResourceDemoDefinitions
        {
            public GoapActionDefinition GatherAction { get; }
            public GoapGoalDefinition CollectGoal { get; }

            public ResourceDemoDefinitions(
                GoapActionDefinition gatherAction,
                GoapGoalDefinition collectGoal)
            {
                GatherAction = gatherAction;
                CollectGoal = collectGoal;
            }
        }

        private readonly struct DemoProfiles
        {
            public GoapAgentProfile Worker { get; }
            public GoapAgentProfile Resident { get; }
            public GoapAgentProfile Guard { get; }
            public GoapAgentProfile Survivor { get; }
            public GoapAgentProfile Lumberjack { get; }

            public DemoProfiles(
                GoapAgentProfile worker,
                GoapAgentProfile resident,
                GoapAgentProfile guard,
                GoapAgentProfile survivor,
                GoapAgentProfile lumberjack)
            {
                Worker = worker;
                Resident = resident;
                Guard = guard;
                Survivor = survivor;
                Lumberjack = lumberjack;
            }
        }

        private static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existingIndex = scenes.FindIndex(scene => scene.path == ScenePath);
            if (existingIndex >= 0)
            {
                if (!scenes[existingIndex].enabled)
                {
                    scenes[existingIndex] = new EditorBuildSettingsScene(ScenePath, true);
                }
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var separatorIndex = path.LastIndexOf('/');
            var parent = path[..separatorIndex];
            var folderName = path[(separatorIndex + 1)..];
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
