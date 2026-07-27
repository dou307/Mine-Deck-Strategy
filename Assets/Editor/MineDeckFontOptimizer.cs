using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

public static class MineDeckFontOptimizer
{
    private const string MainScenePath = "Assets/Scenes/Network_Playground.unity";
    private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/FZLTDHK.TTF";
    private const string OptimizedFontPath = "Assets/Fonts/MineDeck UI SDF.asset";
    private const string TmpSettingsPath =
        "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    private static readonly string[] LegacyFontPaths =
    {
        "Assets/TextMesh Pro/Fonts/FZLTDHK SDF.asset",
        "Assets/Fonts/FZHTK SDF.asset"
    };

    [MenuItem("Tools/Mine-Deck/Optimize UI Font")]
    public static void OptimizeFromMenu()
    {
        OptimizationResult result = Optimize();
        EditorUtility.DisplayDialog(
            "Mine-Deck Font Optimizer",
            $"字体优化完成：{result.CharacterCount} 个预热字符，" +
            $"{result.UpdatedTextComponents} 个文本引用已更新。",
            "确定");
    }

    public static void OptimizeBatch()
    {
        OptimizationResult result = Optimize();
        Debug.Log(
            $"[MineDeckFontOptimizer] Font optimization completed: " +
            $"characters={result.CharacterCount}, atlasCount={result.AtlasCount}, " +
            $"updatedTexts={result.UpdatedTextComponents}.");
    }

    private static OptimizationResult Optimize()
    {
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
            throw new InvalidOperationException($"Source font not found: {SourceFontPath}");

        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        string characterSet = BuildCharacterSet(scene);
        TMP_FontAsset optimizedFont = GetOrCreateOptimizedFont(sourceFont, characterSet);
        HashSet<TMP_FontAsset> legacyFonts = LegacyFontPaths
            .Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>)
            .Where(font => font != null)
            .ToHashSet();

        int updatedTexts = ReplaceSceneFonts(scene, optimizedFont, legacyFonts);
        updatedTexts += ReplaceDependencyPrefabFonts(optimizedFont, legacyFonts);
        UpdateTmpDefaultFont(optimizedFont);

        AssetDatabase.SaveAssets();
        return new OptimizationResult(
            characterSet.Length,
            optimizedFont.atlasTextures.Length,
            updatedTexts);
    }

    private static TMP_FontAsset GetOrCreateOptimizedFont(
        Font sourceFont,
        string characterSet)
    {
        TMP_FontAsset existing =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OptimizedFontPath);
        if (existing != null)
        {
            EnsureRequiredCharacters(existing, characterSet);
            return existing;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            64,
            6,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic,
            true);
        if (fontAsset == null)
            throw new InvalidOperationException("TMP failed to create the optimized font asset.");

        fontAsset.name = "MineDeck UI SDF";
        fontAsset.atlasTextures[0].name = "MineDeck UI SDF Atlas";
        fontAsset.material.name = "MineDeck UI SDF Material";

        AssetDatabase.CreateAsset(fontAsset, OptimizedFontPath);
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        EnsureRequiredCharacters(fontAsset, characterSet);

        foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
        {
            if (!AssetDatabase.Contains(atlasTexture))
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            EditorUtility.SetDirty(atlasTexture);
        }

        EditorUtility.SetDirty(fontAsset.material);
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    private static void EnsureRequiredCharacters(
        TMP_FontAsset fontAsset,
        string characterSet)
    {
        if (fontAsset.HasCharacters(characterSet))
            return;

        if (fontAsset.TryAddCharacters(characterSet, out string missingCharacters))
            return;

        throw new InvalidOperationException(
            $"Optimized font atlas could not fit or render required characters: " +
            $"{missingCharacters}");
    }

    private static string BuildCharacterSet(Scene scene)
    {
        var characters = new HashSet<char>();
        AddCharacters(
            characters,
            string.Concat(Enumerable.Range(32, 95).Select(value => (char)value)));
        AddCharacters(characters, "，。！？：；、“”‘’（）【】《》…—·\u200B");

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                AddCharacters(characters, text.text);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:CardData"))
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (card == null) continue;
            AddCharacters(characters, card.cardName);
            AddCharacters(characters, card.description);
        }

        foreach (string scriptPath in Directory.GetFiles(
                     "Assets/Scripts",
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(scriptPath);
            foreach (Match match in Regex.Matches(
                         source,
                         "@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\""))
            {
                string literal = match.Value;
                string value = literal.StartsWith("@\"", StringComparison.Ordinal)
                    ? literal.Substring(2, literal.Length - 3).Replace("\"\"", "\"")
                    : Regex.Unescape(literal.Substring(1, literal.Length - 2));
                AddCharacters(characters, value);
            }
        }

        return new string(characters.OrderBy(character => character).ToArray());
    }

    private static void AddCharacters(ISet<char> characters, string value)
    {
        if (string.IsNullOrEmpty(value)) return;

        foreach (char character in value)
        {
            if (!char.IsControl(character) && !char.IsSurrogate(character))
                characters.Add(character);
        }
    }

    private static int ReplaceSceneFonts(
        Scene scene,
        TMP_FontAsset optimizedFont,
        ISet<TMP_FontAsset> legacyFonts)
    {
        int updated = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!legacyFonts.Contains(text.font)) continue;
                text.font = optimizedFont;
                EditorUtility.SetDirty(text);
                updated++;
            }
        }

        if (updated > 0)
            EditorSceneManager.SaveScene(scene);
        return updated;
    }

    private static int ReplaceDependencyPrefabFonts(
        TMP_FontAsset optimizedFont,
        ISet<TMP_FontAsset> legacyFonts)
    {
        int updated = 0;
        string[] prefabPaths = AssetDatabase.GetDependencies(MainScenePath, true)
            .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();

        foreach (string prefabPath in prefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool changed = false;
            try
            {
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!legacyFonts.Contains(text.font)) continue;
                    text.font = optimizedFont;
                    changed = true;
                    updated++;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return updated;
    }

    private static void UpdateTmpDefaultFont(TMP_FontAsset optimizedFont)
    {
        TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
            throw new InvalidOperationException($"TMP settings not found: {TmpSettingsPath}");

        var serializedSettings = new SerializedObject(settings);
        SerializedProperty defaultFont =
            serializedSettings.FindProperty("m_defaultFontAsset");
        if (defaultFont == null)
            throw new InvalidOperationException("TMP default font property was not found.");

        defaultFont.objectReferenceValue = optimizedFont;
        serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    private readonly struct OptimizationResult
    {
        public OptimizationResult(
            int characterCount,
            int atlasCount,
            int updatedTextComponents)
        {
            CharacterCount = characterCount;
            AtlasCount = atlasCount;
            UpdatedTextComponents = updatedTextComponents;
        }

        public int CharacterCount { get; }
        public int AtlasCount { get; }
        public int UpdatedTextComponents { get; }
    }
}
