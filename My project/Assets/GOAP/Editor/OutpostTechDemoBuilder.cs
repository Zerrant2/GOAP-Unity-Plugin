using System.Collections.Generic;
using System.Linq;
using Practice.GOAP.TechDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Practice.GOAP.Editor
{
    public static class OutpostTechDemoBuilder
    {
        public const string DomainPath = "Assets/GOAP/TechDemo/Generated/GOAP Outpost Domain.asset";
        public const string ScenePath = "Assets/GOAP/TechDemo/Scenes/GOAP Outpost.unity";
        private const string GeneratedFolder = "Assets/GOAP/TechDemo/Generated";
        private const string MaterialFolder = GeneratedFolder + "/Materials";
        private const string SessionKey = "Practice.GOAP.OutpostDemoBuildV5";

        [InitializeOnLoadMethod]
        private static void ScheduleInitialBuild()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () =>
            {
                var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(DomainPath);
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    (domain == null ||
                     AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null ||
                     NeedsDecisionUpgrade(domain)))
                {
                    BuildOrRefreshInternal(false);
                }
            };
        }

        [MenuItem("Tools/GOAP/Tech Demo/Build or Refresh Outpost")]
        public static void BuildOrRefresh()
        {
            BuildOrRefreshInternal(true);
        }

        private static void BuildOrRefreshInternal(bool interactive)
        {
            EnsureFolder("Assets/GOAP/TechDemo");
            EnsureFolder(GeneratedFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder("Assets/GOAP/TechDemo/Scenes");

            var domain = AssetDatabase.LoadAssetAtPath<GoapDomain>(DomainPath);
            if (domain == null)
            {
                domain = ScriptableObject.CreateInstance<GoapDomain>();
                domain.name = "GOAP Outpost Domain";
                AssetDatabase.CreateAsset(domain, DomainPath);
            }

            var content = EnsureDomainContent(domain);
            var profiles = EnsureProfiles(domain, content);
            AssetDatabase.SaveAssets();
            if (!EnsureScene(profiles, interactive))
            {
                Debug.LogWarning(
                    "GOAP Outpost assets were built, but scene generation is waiting for the current Untitled scene to be saved.");
                return;
            }

            EnsureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = domain;
            Debug.Log($"GOAP Outpost is ready. Open '{ScenePath}' and enter Play Mode.", domain);
        }

        [MenuItem("Tools/GOAP/Tech Demo/Open Outpost Scene %#&g")]
        public static void OpenScene()
        {
            BuildOrRefresh();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        private static OutpostContent EnsureDomainContent(GoapDomain domain)
        {
            var hunger = FloatFact(domain, "Hunger", 12f, "Agent hunger from 0 to 100.");
            var energy = FloatFact(domain, "Energy", 100f, "Agent energy from 0 to 100.");
            var health = FloatFact(domain, "Health", 100f, "Agent health from 0 to 100.");
            var foodStockpile = IntegerFact(domain, "Food Stockpile", 8, "Shared food in the outpost.");
            var woodStockpile = IntegerFact(domain, "Wood Stockpile", 4, "Shared wood in the outpost.");
            var carryFood = IntegerFact(domain, "Carry Food", 0, "Food carried by this agent.");
            var carryWood = IntegerFact(domain, "Carry Wood", 0, "Wood carried by this agent.");
            var hasWeapon = BoolFact(domain, "Has Weapon", false, "The agent has visited the armory.");
            var treeAvailable = BoolFact(domain, "Tree Available", true, "A reservable tree is available.");
            var foodSourceAvailable = BoolFact(domain, "Food Source Available", true, "A reservable bush is available.");
            var bedAvailable = BoolFact(domain, "Bed Available", true, "A reservable bed is available.");
            var needWood = BoolFact(domain, "Need Wood", true, "The stockpile is below its wood target.");
            var needFood = BoolFact(domain, "Need Food", true, "The stockpile is below its food target.");
            var woodDelivered = BoolFact(domain, "Wood Delivered", false, "This agent completed one wood delivery.");
            var foodDelivered = BoolFact(domain, "Food Delivered", false, "This agent completed one food delivery.");
            var campDamaged = BoolFact(domain, "Camp Damaged", false, "The central building needs repairs.");
            var campRepaired = BoolFact(domain, "Camp Repaired", false, "This agent completed one repair.");
            var enemyVisible = BoolFact(domain, "Enemy Visible", false, "At least one monster is alive.");
            var enemyDefeated = BoolFact(domain, "Enemy Defeated", false, "This guard defeated one monster.");
            var safe = BoolFact(domain, "Safe", true, "No monster is close to this agent.");
            var patrolDone = BoolFact(domain, "Patrol Done", false, "This guard completed one patrol leg.");

            var eat = Action(
                domain, "Eat at Stockpile", 1f, OutpostIds.Eat,
                new[]
                {
                    new GoapCondition(hunger, 65f, GoapComparison.GreaterOrEqual),
                    new GoapCondition(foodStockpile, 1, GoapComparison.GreaterOrEqual)
                },
                new[]
                {
                    new GoapCondition(hunger, 8f),
                    new GoapCondition(foodStockpile, 1, effectOperation: GoapEffectOperation.Subtract)
                },
                "Walk to the shared stockpile and consume one food.");
            var sleep = Action(
                domain, "Sleep in Bed", 1.2f, OutpostIds.Sleep,
                new[]
                {
                    new GoapCondition(energy, 25f, GoapComparison.LessOrEqual),
                    new GoapCondition(bedAvailable, true)
                },
                new[] { new GoapCondition(energy, 100f) },
                "Reserve a bed, move to it, and restore energy.");
            var flee = Action(
                domain, "Flee to Safety", 0.8f, OutpostIds.Flee,
                new[] { new GoapCondition(enemyVisible, true), new GoapCondition(safe, false) },
                new[] { new GoapCondition(safe, true) },
                "Leave a threatened area before returning to work.");
            var harvestWood = Action(
                domain, "Harvest Tree", 1.1f, OutpostIds.HarvestWood,
                new[]
                {
                    new GoapCondition(treeAvailable, true),
                    new GoapCondition(carryWood, 1, GoapComparison.Less)
                },
                new[] { new GoapCondition(carryWood, 1, effectOperation: GoapEffectOperation.Add) },
                "Reserve a tree and gather one unit of wood.");
            var deliverWood = Action(
                domain, "Deliver Wood", 0.7f, OutpostIds.DeliverWood,
                new[] { new GoapCondition(carryWood, 1, GoapComparison.GreaterOrEqual) },
                new[]
                {
                    new GoapCondition(carryWood, 0),
                    new GoapCondition(woodStockpile, 1, effectOperation: GoapEffectOperation.Add),
                    new GoapCondition(woodDelivered, true)
                },
                "Carry gathered wood back to the shared stockpile.");
            var harvestFood = Action(
                domain, "Gather Berries", 1f, OutpostIds.HarvestFood,
                new[]
                {
                    new GoapCondition(foodSourceAvailable, true),
                    new GoapCondition(carryFood, 1, GoapComparison.Less)
                },
                new[] { new GoapCondition(carryFood, 1, effectOperation: GoapEffectOperation.Add) },
                "Reserve a berry bush and gather one unit of food.");
            var deliverFood = Action(
                domain, "Deliver Food", 0.7f, OutpostIds.DeliverFood,
                new[] { new GoapCondition(carryFood, 1, GoapComparison.GreaterOrEqual) },
                new[]
                {
                    new GoapCondition(carryFood, 0),
                    new GoapCondition(foodStockpile, 1, effectOperation: GoapEffectOperation.Add),
                    new GoapCondition(foodDelivered, true)
                },
                "Carry gathered food back to the shared stockpile.");
            var takeWeapon = Action(
                domain, "Take Weapon", 0.8f, OutpostIds.TakeWeapon,
                new[] { new GoapCondition(hasWeapon, false) },
                new[] { new GoapCondition(hasWeapon, true) },
                "Visit the armory before entering combat.");
            var attack = Action(
                domain, "Attack Monster", 1.4f, OutpostIds.Attack,
                new[] { new GoapCondition(enemyVisible, true), new GoapCondition(hasWeapon, true) },
                new[] { new GoapCondition(enemyDefeated, true) },
                "Reserve the nearest monster, approach it, and attack.");
            var repair = Action(
                domain, "Repair Camp", 1.3f, OutpostIds.Repair,
                new[]
                {
                    new GoapCondition(campDamaged, true),
                    new GoapCondition(woodStockpile, 1, GoapComparison.GreaterOrEqual)
                },
                new[]
                {
                    new GoapCondition(campRepaired, true),
                    new GoapCondition(woodStockpile, 1, effectOperation: GoapEffectOperation.Subtract)
                },
                "Spend one wood to restore the camp.");
            var patrol = Action(
                domain, "Patrol Perimeter", 1f, OutpostIds.Patrol,
                new[] { new GoapCondition(enemyVisible, false) },
                new[] { new GoapCondition(patrolDone, true) },
                "Move between changing points around the outpost.");

            ConfigureContextAction(sleep, OutpostIds.BedCategory, 0.08f, GoapActionInterruptionPolicy.FinishCurrentAction);
            ConfigureContextAction(harvestWood, OutpostIds.TreeCategory, 0.1f, GoapActionInterruptionPolicy.FinishCurrentAction);
            ConfigureContextAction(harvestFood, OutpostIds.FoodCategory, 0.1f, GoapActionInterruptionPolicy.FinishCurrentAction);
            ConfigureContextAction(takeWeapon, OutpostIds.ArmoryCategory, 0.06f, GoapActionInterruptionPolicy.FinishCurrentAction);
            ConfigureContextAction(attack, OutpostIds.EnemyCategory, 0.08f, GoapActionInterruptionPolicy.FinishCurrentAction);
            deliverWood.ConfigureInterruption(GoapActionInterruptionPolicy.FinishCurrentAction);
            deliverFood.ConfigureInterruption(GoapActionInterruptionPolicy.FinishCurrentAction);
            repair.ConfigureInterruption(GoapActionInterruptionPolicy.FinishCurrentAction);

            var escapeGoal = Goal(
                domain, "Escape Danger", 100,
                new[] { new GoapCondition(enemyVisible, true), new GoapCondition(safe, false) },
                new[] { new GoapCondition(safe, true) },
                "Immediate self-preservation for civilian roles.");
            var defendGoal = Goal(
                domain, "Defend Outpost", 95,
                new[] { new GoapCondition(enemyVisible, true) },
                new[] { new GoapCondition(enemyDefeated, true) },
                "Defeat one current threat, then reassess the wave.");
            var hungerGoal = Goal(
                domain, "Satisfy Hunger", 85,
                new[]
                {
                    new GoapCondition(hunger, 65f, GoapComparison.GreaterOrEqual),
                    new GoapCondition(foodStockpile, 1, GoapComparison.GreaterOrEqual)
                },
                new[] { new GoapCondition(hunger, 20f, GoapComparison.LessOrEqual) },
                "Interrupt work when food is available and hunger is high.");
            var energyGoal = Goal(
                domain, "Recover Energy", 70,
                new[]
                {
                    new GoapCondition(energy, 25f, GoapComparison.LessOrEqual),
                    new GoapCondition(bedAvailable, true)
                },
                new[] { new GoapCondition(energy, 80f, GoapComparison.GreaterOrEqual) },
                "Reserve a bed and recover before continuing work.");
            var repairGoal = Goal(
                domain, "Repair Camp", 60,
                new[] { new GoapCondition(campDamaged, true) },
                new[] { new GoapCondition(campRepaired, true) },
                "Keep the central building operational.");
            var foodGoal = Goal(
                domain, "Collect Food", 45,
                new[] { new GoapCondition(needFood, true) },
                new[] { new GoapCondition(foodDelivered, true) },
                "Maintain the food target using gather and delivery actions.");
            var woodGoal = Goal(
                domain, "Collect Wood", 40,
                new[] { new GoapCondition(needWood, true) },
                new[] { new GoapCondition(woodDelivered, true) },
                "Maintain the wood target using gather and delivery actions.");
            var patrolGoal = Goal(
                domain, "Patrol", 10,
                new[] { new GoapCondition(enemyVisible, false) },
                new[] { new GoapCondition(patrolDone, true) },
                "Low-priority guard activity between attacks.");

            escapeGoal.ConfigureSelection(
                0.5f,
                new[] { new GoapGoalScoreModifier(health, 100f, 20f, 0f, 35f) });
            defendGoal.ConfigureSelection(
                0.75f,
                new[] { new GoapGoalScoreModifier(enemyVisible, 0f, 1f, 0f, 15f) });
            hungerGoal.ConfigureSelection(
                3f,
                new[] { new GoapGoalScoreModifier(hunger, 65f, 100f, 0f, 35f) });
            energyGoal.ConfigureSelection(
                3f,
                new[] { new GoapGoalScoreModifier(energy, 25f, 0f, 0f, 35f) });
            repairGoal.ConfigureSelection(2f);
            foodGoal.ConfigureSelection(1f);
            woodGoal.ConfigureSelection(1f);
            patrolGoal.ConfigureSelection(1.5f);

            var facts = domain.Facts.Where(item => item != null).ToArray();
            for (var index = 0; index < facts.Length; index++)
            {
                domain.SetNodePosition(facts[index].Id, GoapNodeKind.Fact, new Vector2(40f, 40f + index * 115f));
            }

            var actions = domain.Actions.Where(item => item != null).ToArray();
            for (var index = 0; index < actions.Length; index++)
            {
                domain.SetNodePosition(actions[index].Id, GoapNodeKind.Action, new Vector2(440f, 40f + index * 190f));
            }

            var goals = domain.Goals.Where(item => item != null).ToArray();
            for (var index = 0; index < goals.Length; index++)
            {
                domain.SetNodePosition(goals[index].Id, GoapNodeKind.Goal, new Vector2(900f, 40f + index * 230f));
            }

            EditorUtility.SetDirty(domain);
            return new OutpostContent(
                eat, sleep, flee, harvestWood, deliverWood, harvestFood, deliverFood,
                takeWeapon, attack, repair, patrol,
                escapeGoal, defendGoal, hungerGoal, energyGoal, repairGoal, foodGoal, woodGoal, patrolGoal);
        }

        private static OutpostProfiles EnsureProfiles(GoapDomain domain, OutpostContent content)
        {
            var commonCivilianActions = new[] { content.Eat, content.Sleep, content.Flee };
            var commonCivilianGoals = new[] { content.EscapeGoal, content.HungerGoal, content.EnergyGoal };
            var lumberjack = Profile(
                "Lumberjack",
                domain,
                commonCivilianActions.Concat(new[] { content.HarvestWood, content.DeliverWood }),
                commonCivilianGoals.Concat(new[] { content.WoodGoal }));
            var forager = Profile(
                "Forager",
                domain,
                commonCivilianActions.Concat(new[] { content.HarvestFood, content.DeliverFood }),
                commonCivilianGoals.Concat(new[] { content.FoodGoal }));
            var guard = Profile(
                "Guard",
                domain,
                new[] { content.Eat, content.Sleep, content.TakeWeapon, content.Attack, content.Patrol },
                new[] { content.DefendGoal, content.HungerGoal, content.EnergyGoal, content.PatrolGoal });
            var builder = Profile(
                "Builder",
                domain,
                commonCivilianActions.Concat(new[] { content.HarvestWood, content.DeliverWood, content.Repair }),
                commonCivilianGoals.Concat(new[] { content.RepairGoal }));
            return new OutpostProfiles(lumberjack, forager, guard, builder);
        }

        private static GoapAgentProfile Profile(
            string name,
            GoapDomain domain,
            IEnumerable<GoapActionDefinition> actions,
            IEnumerable<GoapGoalDefinition> goals)
        {
            var path = $"{GeneratedFolder}/{name} Profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<GoapAgentProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<GoapAgentProfile>();
                profile.name = $"{name} Profile";
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.Configure(domain, actions, goals, 0.18f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static bool EnsureScene(OutpostProfiles profiles, bool interactive)
        {
            var sceneAssetExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
            if (!sceneAssetExists && HasDirtyUntitledScene() &&
                (!interactive || !EditorSceneManager.EnsureUntitledSceneHasBeenSaved(
                    "Save the current Untitled scene before GOAP Outpost creates its generated scene.")))
            {
                return false;
            }

            var previous = SceneManager.GetActiveScene();
            var existingScene = SceneManager.GetSceneByPath(ScenePath);
            var wasLoaded = existingScene.IsValid() && existingScene.isLoaded;
            var scene = wasLoaded
                ? existingScene
                : sceneAssetExists
                    ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            SceneManager.SetActiveScene(scene);
            foreach (var rootObject in scene.GetRootGameObjects())
            {
                Object.DestroyImmediate(rootObject);
            }

            var root = new GameObject("GOAP Outpost Tech Demo");
            SceneManager.MoveGameObjectToScene(root, scene);
            var controller = root.AddComponent<OutpostGameController>();
            CreateLightingAndCamera(root.transform);
            CreateEnvironment(root.transform);

            var stockpileObject = new GameObject("Shared Stockpile");
            stockpileObject.transform.SetParent(root.transform);
            stockpileObject.transform.position = new Vector3(-2.2f, 0f, 0.3f);
            var stockpile = stockpileObject.AddComponent<OutpostStockpile>();
            stockpile.Configure(4, 8);
            CreateVisual("Wood Crate", PrimitiveType.Cube, stockpileObject.transform.position + new Vector3(-0.5f, 0.45f, 0f), new Vector3(0.8f, 0.8f, 0.8f), Material("Wood Crate", new Color(0.48f, 0.28f, 0.12f)), stockpileObject.transform);
            CreateVisual("Food Crate", PrimitiveType.Cube, stockpileObject.transform.position + new Vector3(0.5f, 0.45f, 0f), new Vector3(0.8f, 0.8f, 0.8f), Material("Food Crate", new Color(0.78f, 0.24f, 0.16f)), stockpileObject.transform);

            var campObject = new GameObject("Central Camp");
            campObject.transform.SetParent(root.transform);
            campObject.transform.position = new Vector3(0f, 0f, 2.5f);
            var camp = campObject.AddComponent<OutpostCamp>();
            camp.Configure(250f);
            CreateVisual("Camp Hall", PrimitiveType.Cube, campObject.transform.position + new Vector3(0f, 1.2f, 0f), new Vector3(4.2f, 2.4f, 3.2f), Material("Camp Hall", new Color(0.34f, 0.38f, 0.42f)), campObject.transform);
            CreateVisual("Camp Roof", PrimitiveType.Cube, campObject.transform.position + new Vector3(0f, 2.6f, 0f), new Vector3(4.7f, 0.35f, 3.7f), Material("Camp Roof", new Color(0.16f, 0.19f, 0.22f)), campObject.transform);

            for (var index = 0; index < 4; index++)
            {
                var bed = CreateVisual(
                    $"Bed {index + 1}", PrimitiveType.Cube,
                    new Vector3(-3f + index * 2f, 0.28f, 5.6f),
                    new Vector3(1.3f, 0.45f, 2.1f),
                    Material("Beds", new Color(0.24f, 0.55f, 0.78f)), root.transform);
                bed.AddComponent<GoapSmartObject>().Configure(OutpostIds.BedCategory, false);
            }

            var armoryObject = CreateVisual(
                "Armory", PrimitiveType.Cube, new Vector3(4.5f, 0.8f, 2.4f),
                new Vector3(1.8f, 1.6f, 1.8f), Material("Armory", new Color(0.82f, 0.58f, 0.18f)), root.transform);
            var armory = armoryObject.AddComponent<GoapSmartObject>();
            armory.Configure(OutpostIds.ArmoryCategory, false, 4);

            CreateResources(root.transform);

            var safePoint = Marker("Civilian Safe Point", new Vector3(0f, 0f, -8.5f), root.transform);
            var agentSpawn = Marker("Agent Spawn", new Vector3(-1f, 0f, -2f), root.transform);
            var monsterSpawn = Marker("Monster Spawn", new Vector3(0f, 0f, 12.5f), root.transform);

            var roleProfiles = new[]
            {
                new OutpostRoleProfile(OutpostRole.Lumberjack, profiles.Lumberjack, Material("Role Lumberjack", new Color(0.18f, 0.68f, 0.36f))),
                new OutpostRoleProfile(OutpostRole.Forager, profiles.Forager, Material("Role Forager", new Color(0.86f, 0.46f, 0.18f))),
                new OutpostRoleProfile(OutpostRole.Guard, profiles.Guard, Material("Role Guard", new Color(0.18f, 0.48f, 0.84f))),
                new OutpostRoleProfile(OutpostRole.Builder, profiles.Builder, Material("Role Builder", new Color(0.76f, 0.66f, 0.2f)))
            };
            var agents = new List<OutpostAgent>
            {
                CreateAgent(controller, OutpostRole.Lumberjack, roleProfiles, 0, new Vector3(-3f, 1f, -2.5f), root.transform),
                CreateAgent(controller, OutpostRole.Forager, roleProfiles, 1, new Vector3(-1f, 1f, -3.2f), root.transform),
                CreateAgent(controller, OutpostRole.Guard, roleProfiles, 2, new Vector3(1f, 1f, -2.5f), root.transform),
                CreateAgent(controller, OutpostRole.Builder, roleProfiles, 3, new Vector3(3f, 1f, -3.2f), root.transform)
            };
            var monsterMaterial = Material("Monster", new Color(0.72f, 0.12f, 0.16f));
            controller.Configure(stockpile, camp, safePoint, agentSpawn, monsterSpawn, armory, roleProfiles, agents, monsterMaterial);
            root.AddComponent<OutpostHud>().Configure(controller);

            EditorUtility.SetDirty(controller);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new System.InvalidOperationException($"Could not save generated scene to '{ScenePath}'.");
            }

            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            if (previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
            }

            return true;
        }

        private static bool HasDirtyUntitledScene()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (string.IsNullOrWhiteSpace(scene.path) && scene.isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CreateEnvironment(Transform parent)
        {
            CreateVisual("Ground", PrimitiveType.Plane, Vector3.zero, new Vector3(3.4f, 1f, 2.6f), Material("Ground", new Color(0.16f, 0.3f, 0.2f)), parent);
            CreateVisual("Main Path", PrimitiveType.Cube, new Vector3(0f, 0.03f, 0f), new Vector3(3.2f, 0.06f, 23f), Material("Path", new Color(0.38f, 0.4f, 0.36f)), parent);
            CreateVisual("Cross Path", PrimitiveType.Cube, new Vector3(0f, 0.04f, 1.5f), new Vector3(17f, 0.07f, 2.4f), Material("Path", new Color(0.38f, 0.4f, 0.36f)), parent);

            for (var index = -8; index <= 8; index += 2)
            {
                CreateVisual($"Fence North {index}", PrimitiveType.Cube, new Vector3(index, 0.45f, 8.2f), new Vector3(1.65f, 0.9f, 0.18f), Material("Fence", new Color(0.34f, 0.22f, 0.12f)), parent);
            }
        }

        private static void CreateResources(Transform parent)
        {
            var treeMaterial = Material("Tree Trunk", new Color(0.35f, 0.2f, 0.09f));
            var leafMaterial = Material("Tree Leaves", new Color(0.12f, 0.5f, 0.24f));
            var bushMaterial = Material("Berry Bush", new Color(0.28f, 0.58f, 0.18f));
            var berryMaterial = Material("Berries", new Color(0.68f, 0.12f, 0.28f));
            var treePositions = new[]
            {
                new Vector3(-13f, 0f, -8f), new Vector3(-11f, 0f, -3f), new Vector3(-13f, 0f, 3f),
                new Vector3(-10f, 0f, 8f), new Vector3(10f, 0f, -8f), new Vector3(13f, 0f, -4f),
                new Vector3(12f, 0f, 3f), new Vector3(10f, 0f, 8f)
            };
            for (var index = 0; index < treePositions.Length; index++)
            {
                var root = new GameObject($"Tree {index + 1}");
                root.transform.SetParent(parent);
                root.transform.position = treePositions[index];
                var smartObject = root.AddComponent<GoapSmartObject>();
                var node = root.AddComponent<OutpostResourceNode>();
                node.Configure(OutpostResourceKind.Wood, 3, 14f);
                CreateVisual("Trunk", PrimitiveType.Cylinder, root.transform.position + new Vector3(0f, 1f, 0f), new Vector3(0.55f, 1f, 0.55f), treeMaterial, root.transform);
                CreateVisual("Crown", PrimitiveType.Sphere, root.transform.position + new Vector3(0f, 2.5f, 0f), new Vector3(1.7f, 1.5f, 1.7f), leafMaterial, root.transform);
                EditorUtility.SetDirty(smartObject);
            }

            var bushPositions = new[]
            {
                new Vector3(-7.5f, 0f, -8.5f), new Vector3(-5f, 0f, -10f), new Vector3(-7f, 0f, 7.5f),
                new Vector3(6.5f, 0f, -9f), new Vector3(8.5f, 0f, 6.5f), new Vector3(5.5f, 0f, 9f)
            };
            for (var index = 0; index < bushPositions.Length; index++)
            {
                var root = new GameObject($"Berry Bush {index + 1}");
                root.transform.SetParent(parent);
                root.transform.position = bushPositions[index];
                root.AddComponent<GoapSmartObject>();
                var node = root.AddComponent<OutpostResourceNode>();
                node.Configure(OutpostResourceKind.Food, 4, 10f);
                CreateVisual("Bush", PrimitiveType.Sphere, root.transform.position + new Vector3(0f, 0.55f, 0f), new Vector3(1.4f, 0.9f, 1.4f), bushMaterial, root.transform);
                CreateVisual("Berries", PrimitiveType.Sphere, root.transform.position + new Vector3(0.35f, 0.8f, -0.2f), new Vector3(0.4f, 0.4f, 0.4f), berryMaterial, root.transform);
            }
        }

        private static OutpostAgent CreateAgent(
            OutpostGameController controller,
            OutpostRole role,
            IReadOnlyList<OutpostRoleProfile> profiles,
            int index,
            Vector3 position,
            Transform parent)
        {
            var roleProfile = profiles.First(item => item.Role == role);
            var gameObject = CreateVisual(
                $"Agent {index + 1} - {OutpostGameController.FormatRole(role)}",
                PrimitiveType.Capsule,
                position,
                new Vector3(0.72f, 0.9f, 0.72f),
                roleProfile.Material,
                parent);
            gameObject.AddComponent<GoapAgent>();
            var actor = gameObject.AddComponent<OutpostAgent>();
            gameObject.AddComponent<OutpostSensor>();
            gameObject.AddComponent<OutpostActionBehaviour>();
            actor.ConfigureAuthored(controller, role, roleProfile.Profile, roleProfile.Material, index);
            gameObject.AddComponent<OutpostAgentLabel>().Configure(actor);
            return actor;
        }

        private static void CreateLightingAndCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 22f, -20f), Quaternion.Euler(47f, 0f, 0f));
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 13.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.09f);

            var lightObject = new GameObject("Sun");
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.78f);
            light.intensity = 1.25f;
        }

        private static Transform Marker(string name, Vector3 position, Transform parent)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(parent);
            marker.transform.position = position;
            return marker.transform;
        }

        private static GameObject CreateVisual(
            string name,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Material material,
            Transform parent)
        {
            var gameObject = GameObject.CreatePrimitive(primitive);
            gameObject.name = name;
            gameObject.transform.SetParent(parent);
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private static Material Material(string name, Color color)
        {
            var safeName = new string(name.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
            var path = $"{MaterialFolder}/{safeName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = safeName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GoapFact BoolFact(GoapDomain domain, string name, bool value, string description)
        {
            var fact = domain.FindFact(name);
            if (fact == null)
            {
                fact = ScriptableObject.CreateInstance<GoapFact>();
                AssetDatabase.AddObjectToAsset(fact, domain);
                domain.AddFact(fact);
            }

            fact.Configure(name, value, description);
            EditorUtility.SetDirty(fact);
            return fact;
        }

        private static GoapFact IntegerFact(GoapDomain domain, string name, int value, string description)
        {
            var fact = domain.FindFact(name);
            if (fact == null)
            {
                fact = ScriptableObject.CreateInstance<GoapFact>();
                AssetDatabase.AddObjectToAsset(fact, domain);
                domain.AddFact(fact);
            }

            fact.ConfigureInteger(name, value, description);
            EditorUtility.SetDirty(fact);
            return fact;
        }

        private static GoapFact FloatFact(GoapDomain domain, string name, float value, string description)
        {
            var fact = domain.FindFact(name);
            if (fact == null)
            {
                fact = ScriptableObject.CreateInstance<GoapFact>();
                AssetDatabase.AddObjectToAsset(fact, domain);
                domain.AddFact(fact);
            }

            fact.ConfigureFloat(name, value, description);
            EditorUtility.SetDirty(fact);
            return fact;
        }

        private static GoapActionDefinition Action(
            GoapDomain domain,
            string name,
            float cost,
            string executorId,
            IEnumerable<GoapCondition> preconditions,
            IEnumerable<GoapCondition> effects,
            string description)
        {
            var action = domain.FindAction(executorId);
            if (action == null)
            {
                action = ScriptableObject.CreateInstance<GoapActionDefinition>();
                AssetDatabase.AddObjectToAsset(action, domain);
                domain.AddAction(action);
            }

            action.Configure(name, cost, executorId, preconditions, effects, description);
            EditorUtility.SetDirty(action);
            return action;
        }

        private static void ConfigureContextAction(
            GoapActionDefinition action,
            string category,
            float distanceCostPerUnit,
            GoapActionInterruptionPolicy interruptionPolicy)
        {
            action.ConfigureTargeting(
                GoapActionTargetMode.SmartObjectCategory,
                category,
                false,
                distanceCostPerUnit);
            action.ConfigureInterruption(interruptionPolicy);
            EditorUtility.SetDirty(action);
        }

        private static bool NeedsDecisionUpgrade(GoapDomain domain)
        {
            var harvest = domain != null ? domain.FindAction(OutpostIds.HarvestWood) : null;
            var hunger = domain?.Goals.FirstOrDefault(goal =>
                goal != null && goal.DisplayName == "Satisfy Hunger");
            return harvest == null ||
                   harvest.TargetMode != GoapActionTargetMode.SmartObjectCategory ||
                   harvest.DistanceCostPerUnit <= 0f ||
                   hunger == null ||
                   hunger.ScoreModifiers.Count == 0;
        }

        private static GoapGoalDefinition Goal(
            GoapDomain domain,
            string name,
            int priority,
            IEnumerable<GoapCondition> activation,
            IEnumerable<GoapCondition> desired,
            string description)
        {
            var goal = domain.Goals.FirstOrDefault(item => item != null && item.DisplayName == name);
            if (goal == null)
            {
                goal = ScriptableObject.CreateInstance<GoapGoalDefinition>();
                AssetDatabase.AddObjectToAsset(goal, domain);
                domain.AddGoal(goal);
            }

            goal.Configure(name, priority, activation, desired, description);
            EditorUtility.SetDirty(goal);
            return goal;
        }

        private static void EnsureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(scene => scene.path != ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = path.Substring(0, path.LastIndexOf('/'));
            var name = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private readonly struct OutpostContent
        {
            public readonly GoapActionDefinition Eat;
            public readonly GoapActionDefinition Sleep;
            public readonly GoapActionDefinition Flee;
            public readonly GoapActionDefinition HarvestWood;
            public readonly GoapActionDefinition DeliverWood;
            public readonly GoapActionDefinition HarvestFood;
            public readonly GoapActionDefinition DeliverFood;
            public readonly GoapActionDefinition TakeWeapon;
            public readonly GoapActionDefinition Attack;
            public readonly GoapActionDefinition Repair;
            public readonly GoapActionDefinition Patrol;
            public readonly GoapGoalDefinition EscapeGoal;
            public readonly GoapGoalDefinition DefendGoal;
            public readonly GoapGoalDefinition HungerGoal;
            public readonly GoapGoalDefinition EnergyGoal;
            public readonly GoapGoalDefinition RepairGoal;
            public readonly GoapGoalDefinition FoodGoal;
            public readonly GoapGoalDefinition WoodGoal;
            public readonly GoapGoalDefinition PatrolGoal;

            public OutpostContent(
                GoapActionDefinition eat,
                GoapActionDefinition sleep,
                GoapActionDefinition flee,
                GoapActionDefinition harvestWood,
                GoapActionDefinition deliverWood,
                GoapActionDefinition harvestFood,
                GoapActionDefinition deliverFood,
                GoapActionDefinition takeWeapon,
                GoapActionDefinition attack,
                GoapActionDefinition repair,
                GoapActionDefinition patrol,
                GoapGoalDefinition escapeGoal,
                GoapGoalDefinition defendGoal,
                GoapGoalDefinition hungerGoal,
                GoapGoalDefinition energyGoal,
                GoapGoalDefinition repairGoal,
                GoapGoalDefinition foodGoal,
                GoapGoalDefinition woodGoal,
                GoapGoalDefinition patrolGoal)
            {
                Eat = eat;
                Sleep = sleep;
                Flee = flee;
                HarvestWood = harvestWood;
                DeliverWood = deliverWood;
                HarvestFood = harvestFood;
                DeliverFood = deliverFood;
                TakeWeapon = takeWeapon;
                Attack = attack;
                Repair = repair;
                Patrol = patrol;
                EscapeGoal = escapeGoal;
                DefendGoal = defendGoal;
                HungerGoal = hungerGoal;
                EnergyGoal = energyGoal;
                RepairGoal = repairGoal;
                FoodGoal = foodGoal;
                WoodGoal = woodGoal;
                PatrolGoal = patrolGoal;
            }
        }

        private readonly struct OutpostProfiles
        {
            public readonly GoapAgentProfile Lumberjack;
            public readonly GoapAgentProfile Forager;
            public readonly GoapAgentProfile Guard;
            public readonly GoapAgentProfile Builder;

            public OutpostProfiles(
                GoapAgentProfile lumberjack,
                GoapAgentProfile forager,
                GoapAgentProfile guard,
                GoapAgentProfile builder)
            {
                Lumberjack = lumberjack;
                Forager = forager;
                Guard = guard;
                Builder = builder;
            }
        }
    }
}
