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
    }
}
