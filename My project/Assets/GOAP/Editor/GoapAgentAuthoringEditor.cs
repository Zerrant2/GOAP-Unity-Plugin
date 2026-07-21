using UnityEditor;
using UnityEngine;

namespace Practice.GOAP.Editor
{
    [CustomEditor(typeof(GoapAgentAuthoring))]
    public sealed class GoapAgentAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var authoring = (GoapAgentAuthoring)target;
            EditorGUILayout.Space(6f);

            if (authoring.Profile == null)
            {
                EditorGUILayout.HelpBox("Assign or create an Agent Profile.", MessageType.Info);
                if (GUILayout.Button("Open Setup Wizard"))
                {
                    GoapContentWizardWindow.Open(null, authoring.gameObject);
                }

                if (GUILayout.Button("Create Agent Profile"))
                {
                    CreateProfile(authoring);
                }
            }
            else
            {
                var domain = authoring.Profile.Domain;
                EditorGUILayout.HelpBox(
                    domain == null
                        ? "Profile has no domain."
                        : $"{authoring.Profile.Actions.Count} actions, {authoring.Profile.Goals.Count} goals",
                    domain == null ? MessageType.Error : MessageType.None);
                if (GUILayout.Button("Apply Profile"))
                {
                    authoring.ApplyProfile();
                    EditorUtility.SetDirty(authoring);
                }

                if (GUILayout.Button("Open Content Wizard"))
                {
                    GoapContentWizardWindow.Open(domain, authoring.gameObject);
                }
            }

            if (GUILayout.Button("Add Inventory and Stats"))
            {
                AddOptionalSources(authoring.gameObject);
            }
        }

        private static void CreateProfile(GoapAgentAuthoring authoring)
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create GOAP Agent Profile",
                $"{authoring.gameObject.name} Profile",
                "asset",
                "Choose where to save the profile.");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var profile = CreateInstance<GoapAgentProfile>();
            var agent = authoring.GetComponent<GoapAgent>();
            profile.Configure(agent != null ? agent.Domain : null);
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            var serializedAuthoring = new SerializedObject(authoring);
            serializedAuthoring.FindProperty("_profile").objectReferenceValue = profile;
            serializedAuthoring.ApplyModifiedProperties();
            Selection.activeObject = profile;
        }

        private static void AddOptionalSources(GameObject gameObject)
        {
            if (gameObject.GetComponent<GoapInventory>() == null)
            {
                Undo.AddComponent<GoapInventory>(gameObject);
            }

            if (gameObject.GetComponent<GoapStatSource>() == null)
            {
                Undo.AddComponent<GoapStatSource>(gameObject);
            }
        }
    }
}
