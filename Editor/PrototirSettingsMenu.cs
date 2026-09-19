using System.IO;
using UnityEditor;
using UnityEngine;

namespace Prototir.Editor
{
    /// <summary>Creates the settings asset a downloadable build reads its slug from.
    ///
    /// <para>It has to live in <c>Resources</c>, and it has to be named exactly
    /// <see cref="PrototirSettings.ResourceName"/>, or <see cref="PrototirSettings.Load"/> will not
    /// find it at runtime. Both are easy to get wrong by hand and impossible to notice until an
    /// exported build refuses to pair, which is why this is a menu item rather than a sentence in
    /// the documentation.</para></summary>
    public static class PrototirSettingsMenu
    {
        private const string Folder = "Assets/Resources";

        [MenuItem("Prototir/Create Settings", priority = 1)]
        public static void CreateSettings()
        {
            var asset = Selected() ?? Create();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>Greyed out once the asset exists, so the menu cannot be used to make a second
        /// one: two settings assets in Resources is a coin toss over which one a build reads.</summary>
        [MenuItem("Prototir/Create Settings", validate = true)]
        private static bool CanCreateSettings() => Selected() == null;

        private static PrototirSettings Selected() =>
            Resources.Load<PrototirSettings>(PrototirSettings.ResourceName);

        private static PrototirSettings Create()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            var asset = ScriptableObject.CreateInstance<PrototirSettings>();
            AssetDatabase.CreateAsset(
                asset, $"{Folder}/{PrototirSettings.ResourceName}.asset");
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Prototir: created Assets/Resources/PrototirSettings.asset. Fill in the slug from " +
                "your prototype's URL, prototir.com/p/<slug>, before exporting a download.");
            return asset;
        }
    }
}
