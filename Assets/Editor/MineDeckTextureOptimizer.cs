using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MineDeckTextureOptimizer
{
    private const int CompressionQuality = 65;

    private static readonly string[] TextureRoots =
    {
        "Assets/Art/Icons",
        "Assets/Art/Sprites",
        "Assets/Sprites"
    };

    [MenuItem("Tools/Mine-Deck/Optimize Textures")]
    public static void OptimizeFromMenu()
    {
        int changed = OptimizeTextures();
        EditorUtility.DisplayDialog(
            "Mine-Deck Texture Optimizer",
            $"纹理压缩设置已更新：{changed} 个资源。",
            "确定");
    }

    public static void OptimizeBatch()
    {
        int changed = OptimizeTextures();
        Debug.Log($"[MineDeckTextureOptimizer] Texture optimization completed: {changed} assets changed.");
    }

    private static int OptimizeTextures()
    {
        string[] texturePaths = AssetDatabase.FindAssets("t:Texture2D", TextureRoots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsSupportedTexture)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var changedPaths = new List<string>();
        foreach (string path in texturePaths)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                continue;

            int maxTextureSize = GetMaxTextureSize(path);
            ConfigureImporter(importer, maxTextureSize);
            if (AssetDatabase.WriteImportSettingsIfDirty(path))
                changedPaths.Add(path);
        }

        foreach (string path in changedPaths)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        AssetDatabase.SaveAssets();
        return changedPaths.Count;
    }

    private static bool IsSupportedTexture(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMaxTextureSize(string path)
    {
        string normalized = path.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);

        if (normalized.StartsWith("Assets/Art/Icons/class/", StringComparison.Ordinal))
            return 256;

        if (normalized.StartsWith("Assets/Art/Icons/", StringComparison.Ordinal))
            return 512;

        if (normalized.StartsWith("Assets/Art/Sprites/", StringComparison.Ordinal) &&
            (fileName.Equals("background.png", StringComparison.OrdinalIgnoreCase) ||
             fileName.Equals("cardbackground.png", StringComparison.OrdinalIgnoreCase)))
        {
            return 2048;
        }

        return 512;
    }

    private static void ConfigureImporter(TextureImporter importer, int maxTextureSize)
    {
        importer.maxTextureSize = maxTextureSize;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.compressionQuality = CompressionQuality;
        importer.crunchedCompression = true;
        importer.isReadable = false;

        TextureImporterPlatformSettings defaultSettings =
            importer.GetDefaultPlatformTextureSettings();
        ConfigurePlatform(defaultSettings, maxTextureSize, false);
        importer.SetPlatformTextureSettings(defaultSettings);

        foreach (string platform in new[] { "Standalone", "WebGL" })
        {
            TextureImporterPlatformSettings settings =
                importer.GetPlatformTextureSettings(platform);
            ConfigurePlatform(settings, maxTextureSize, true);
            importer.SetPlatformTextureSettings(settings);
        }
    }

    private static void ConfigurePlatform(
        TextureImporterPlatformSettings settings,
        int maxTextureSize,
        bool overridden)
    {
        settings.overridden = overridden;
        settings.maxTextureSize = maxTextureSize;
        settings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        settings.format = TextureImporterFormat.Automatic;
        settings.textureCompression = TextureImporterCompression.Compressed;
        settings.compressionQuality = CompressionQuality;
        settings.crunchedCompression = true;
    }
}
