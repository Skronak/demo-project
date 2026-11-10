#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Génère un jeu d'exemple : matériaux, prefabs (assemblés à partir de primitives),
/// PlaceableData, matériau du fantôme, puis les branche automatiquement dans le BuildSystem de la scène.
///
/// IMPORTANT : ce fichier doit se trouver dans un dossier nommé "Editor" (ex. Assets/Editor/).
/// Menu : Building > Generate Sample Content
/// </summary>
public static class BuildingSampleGenerator
{
    const string Root = "Assets/Building";
    const string MatDir = Root + "/Materials";
    const string PrefabDir = Root + "/Prefabs";
    const string DataDir = Root + "/Data";

    [MenuItem("Building/Generate Sample Content")]
    static void Generate()
    {
        EnsureFolder("Assets", "Building");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Prefabs");
        EnsureFolder(Root, "Data");

        // --- Matériaux ---
        Material stone = Lit("Stone", new Color(0.62f, 0.62f, 0.65f));
        Material darkStone = Lit("DarkStone", new Color(0.4f, 0.4f, 0.43f));
        Material wood = Lit("Wood", new Color(0.55f, 0.35f, 0.2f));
        Material plaster = Lit("Plaster", new Color(0.9f, 0.82f, 0.65f));
        Material roof = Lit("Roof", new Color(0.75f, 0.25f, 0.2f));
        Material leaf = Lit("Leaf", new Color(0.25f, 0.6f, 0.3f));
        Material water = Lit("Water", new Color(0.3f, 0.55f, 0.85f));

        Material ghost = LoadOrCreateGhostMaterial();

        // --- Prefabs + PlaceableData ---
        var datas = new List<PlaceableData>();

        datas.Add(Make("house", "Maison", new Vector2Int(2, 2), 2f, plaster.color, root =>
        {
            Part(root, PrimitiveType.Cube, "Walls", new Vector3(0, 0.6f, 0), new Vector3(1.8f, 1.2f, 1.8f), plaster);
            Part(root, PrimitiveType.Cube, "Roof", new Vector3(0, 1.2f, 0), new Vector3(1.3f, 1.3f, 2.0f), roof, new Vector3(0, 0, 45));
            Part(root, PrimitiveType.Cube, "Door", new Vector3(0, 0.4f, 0.91f), new Vector3(0.4f, 0.8f, 0.05f), wood);
        }));

        datas.Add(Make("tree", "Arbre", Vector2Int.one, 1.8f, leaf.color, root =>
        {
            Part(root, PrimitiveType.Cylinder, "Trunk", new Vector3(0, 0.4f, 0), new Vector3(0.2f, 0.4f, 0.2f), wood);
            Part(root, PrimitiveType.Sphere, "LeavesLow", new Vector3(0, 1.1f, 0), new Vector3(0.8f, 0.8f, 0.8f), leaf);
            Part(root, PrimitiveType.Sphere, "LeavesTop", new Vector3(0, 1.6f, 0), new Vector3(0.5f, 0.5f, 0.5f), leaf);
        }));

        datas.Add(Make("wall", "Mur", Vector2Int.one, 1f, stone.color, root =>
        {
            Part(root, PrimitiveType.Cube, "Block", new Vector3(0, 0.5f, 0), new Vector3(0.95f, 1f, 0.95f), stone);
        }));

        datas.Add(Make("long_wall", "Long mur", new Vector2Int(3, 1), 1f, stone.color, root =>
        {
            Part(root, PrimitiveType.Cube, "Block", new Vector3(0, 0.5f, 0), new Vector3(2.95f, 1f, 0.5f), stone);
        }));

        datas.Add(Make("tower", "Tour", Vector2Int.one, 2.6f, darkStone.color, root =>
        {
            Part(root, PrimitiveType.Cylinder, "Shaft", new Vector3(0, 1f, 0), new Vector3(0.7f, 1f, 0.7f), darkStone);
            Part(root, PrimitiveType.Cylinder, "Platform", new Vector3(0, 2.1f, 0), new Vector3(0.9f, 0.1f, 0.9f), stone);
            Part(root, PrimitiveType.Sphere, "Cap", new Vector3(0, 2.4f, 0), new Vector3(0.5f, 0.5f, 0.5f), roof);
        }));

        datas.Add(Make("crate", "Caisse", Vector2Int.one, 0.7f, wood.color, root =>
        {
            Part(root, PrimitiveType.Cube, "Box", new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.7f, 0.7f), wood);
        }));

        datas.Add(Make("fountain", "Fontaine", new Vector2Int(2, 2), 1.2f, water.color, root =>
        {
            Part(root, PrimitiveType.Cylinder, "Basin", new Vector3(0, 0.15f, 0), new Vector3(1.8f, 0.15f, 1.8f), stone);
            Part(root, PrimitiveType.Cylinder, "Water", new Vector3(0, 0.31f, 0), new Vector3(1.5f, 0.02f, 1.5f), water);
            Part(root, PrimitiveType.Cylinder, "Pillar", new Vector3(0, 0.6f, 0), new Vector3(0.2f, 0.35f, 0.2f), stone);
            Part(root, PrimitiveType.Sphere, "Top", new Vector3(0, 1.0f, 0), new Vector3(0.4f, 0.4f, 0.4f), water);
        }));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        BuildingIconGenerator.Run(); // génère les miniatures du menu de construction

        // --- Branchement automatique dans la scène ---
        var bs = UnityEngine.Object.FindFirstObjectByType<BuildSystem>();
        if (bs != null)
        {
            var so = new SerializedObject(bs);
            var catalog = so.FindProperty("catalog");
            catalog.ClearArray();
            for (int i = 0; i < datas.Count; i++)
            {
                catalog.InsertArrayElementAtIndex(i);
                catalog.GetArrayElementAtIndex(i).objectReferenceValue = datas[i];
            }
            so.FindProperty("ghostMaterial").objectReferenceValue = ghost;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(bs);
            EditorSceneManager.MarkSceneDirty(bs.gameObject.scene);
            Debug.Log($"Contenu d'exemple généré dans {Root} et branché au BuildSystem ({datas.Count} objets, touches 1 à {Mathf.Min(9, datas.Count)}).");
        }
        else
        {
            Debug.Log($"Contenu d'exemple généré dans {Root}. Aucun BuildSystem dans la scène : " +
                      "ajoutez les PlaceableData au catalogue et le matériau Ghost au champ « Ghost Material » à la main.");
        }
    }

    // ------------------------------------------------------------------

    static PlaceableData Make(string id, string displayName, Vector2Int size, float height, Color color, Action<GameObject> build)
    {
        // Prefab : pivot au centre de l'empreinte, au niveau du sol, pour des cases de 1 unité.
        var root = new GameObject(displayName);
        build(root);

        string prefabPath = $"{PrefabDir}/{displayName}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        string dataPath = $"{DataDir}/{displayName}.asset";
        var data = AssetDatabase.LoadAssetAtPath<PlaceableData>(dataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<PlaceableData>();
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.id = id;
        data.displayName = displayName;
        data.prefab = prefab;
        data.size = size;
        data.height = height;
        data.color = color;
        EditorUtility.SetDirty(data);
        return data;
    }

    static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); // pas de colliders : tout passe par la grille
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material Lit(string name, Color color)
    {
        string path = $"{MatDir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var m = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material LoadOrCreateGhostMaterial()
    {
        string path = $"{MatDir}/Ghost.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Building/GhostURP");
        if (shader == null)
        {
            Debug.LogError("Shader « Building/GhostURP » introuvable : ajoutez GhostURP.shader au projet puis relancez la génération.");
            return null;
        }

        var m = new Material(shader) { color = new Color(0.3f, 1f, 0.4f, 0.55f) };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
