using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ApproachStudy.EditorTools
{
    public static class ApproachStudySetup
    {
        const string ConfigFolder = "Assets/ApproachStudy/Configs";
        const string DefaultConfigPath = ConfigFolder + "/ExampleStudy.asset";
        const string MaterialFolder = "Assets/ApproachStudy/Materials";

        [MenuItem("Tools/Approach Study/Add Experiment To Scene")]
        public static void AddToScene()
        {
            var controller = Object.FindFirstObjectByType<ExperimentController>();
            if (controller == null)
            {
                var go = new GameObject("Approach Study");
                Undo.RegisterCreatedObjectUndo(go, "Add Approach Study");
                controller = go.AddComponent<ExperimentController>();
                Debug.Log("[ApproachStudy] Added 'Approach Study' to the scene.");
            }
            else
            {
                Debug.Log("[ApproachStudy] The scene already has an Experiment Controller - updated it.");
            }

            if (controller.config == null) controller.config = CreateExampleConfig(selectIt: false);
            AssignMaterials(controller);

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeObject = controller.gameObject;
            Debug.Log("[ApproachStudy] Config: " + AssetDatabase.GetAssetPath(controller.config) + ". Remember to save the scene.");
        }

        [MenuItem("Tools/Approach Study/Create & Assign Materials")]
        public static void AssignMaterialsMenu()
        {
            var controller = Object.FindFirstObjectByType<ExperimentController>();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Approach Study", "No Experiment Controller in the open scene. Use 'Add Experiment To Scene' first.", "OK");
                return;
            }
            AssignMaterials(controller);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeObject = controller.gameObject;
            Debug.Log("[ApproachStudy] Materials assigned. Save the scene and rebuild.");
        }

        static void AssignMaterials(ExperimentController controller)
        {
            controller.characterMaterial = GetOrCreateMaterial("Character", lit: true, new Color(0.95f, 0.45f, 0.2f));
            controller.markerMaterial = GetOrCreateMaterial("SpotMarker", lit: true, new Color(0.2f, 0.85f, 0.4f));
            controller.panelMaterial = GetOrCreateMaterial("StatusPanel", lit: false, new Color(0.05f, 0.05f, 0.07f));
        }

        /// <summary>
        /// Saves a material that uses the project's render pipeline shader. Because the scene references it,
        /// the shader is included in the build (runtime-created default materials can end up pink on device).
        /// </summary>
        static Material GetOrCreateMaterial(string name, bool lit, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find(lit ? "Standard" : "Unlit/Color");   // built-in pipeline fallback
            if (shader == null)
            {
                Debug.LogError("[ApproachStudy] Could not find a URP or built-in shader for " + name);
                return null;
            }

            Directory.CreateDirectory(MaterialFolder);
            mat = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        [MenuItem("Tools/Approach Study/Create Example Config")]
        public static void CreateExampleConfigMenu() => CreateExampleConfig(selectIt: true);

        static ExperimentConfig CreateExampleConfig(bool selectIt)
        {
            var config = AssetDatabase.LoadAssetAtPath<ExperimentConfig>(DefaultConfigPath);
            if (config == null)
            {
                Directory.CreateDirectory(ConfigFolder);
                config = ScriptableObject.CreateInstance<ExperimentConfig>();
                config.FillWithExamples();
                AssetDatabase.CreateAsset(config, AssetDatabase.GenerateUniqueAssetPath(DefaultConfigPath));
                AssetDatabase.SaveAssets();
            }
            if (selectIt)
            {
                Selection.activeObject = config;
                EditorGUIUtility.PingObject(config);
            }
            return config;
        }

        [MenuItem("Tools/Approach Study/Open Data Folder (Editor runs)")]
        public static void OpenDataFolder()
        {
            string path = Path.Combine(Application.persistentDataPath, "ApproachStudy");
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }
    }
}
