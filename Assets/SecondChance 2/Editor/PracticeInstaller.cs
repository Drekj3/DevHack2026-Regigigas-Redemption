using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SecondChance.Editor
{
    public static class PracticeInstaller
    {
        [MenuItem("Second Chance/Create Practice Scene")]
        public static void Create()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Second Chance - Practice Range");root.AddComponent<PracticeRange>();
            if(!AssetDatabase.IsValidFolder("Assets/SecondChance/Scenes"))AssetDatabase.CreateFolder("Assets/SecondChance","Scenes");
            var path=AssetDatabase.GenerateUniqueAssetPath("Assets/SecondChance/Scenes/PiazzaPractice.unity");
            EditorSceneManager.SaveScene(scene,path);Selection.activeGameObject=root;
            Debug.Log("Practice scene saved. Press Play to build the courtyard and open scenario setup.");
        }
        [MenuItem("Second Chance/Self-check Adaptive Read")]
        public static void SelfCheck()
        {
            string fail=PlayerRead.SelfCheck();
            if(fail==null)Debug.Log("Adaptive read self-check passed.");else Debug.LogError("Adaptive read self-check failed: "+fail);
        }
    }
}
