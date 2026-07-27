using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MineDeckProjectValidator
{
    private const string MainScenePath = "Assets/Scenes/Network_Playground.unity";
    private const string CardFolder = "Assets/Gamedata/Cards";

    [MenuItem("Tools/Mine-Deck/Validate Project")]
    public static void ValidateFromMenu()
    {
        IReadOnlyList<string> errors = CollectErrors();
        if (errors.Count == 0)
        {
            Debug.Log("Mine-Deck 项目校验通过。");
            EditorUtility.DisplayDialog("Mine-Deck", "项目校验通过。", "确定");
            return;
        }

        string message = string.Join("\n", errors);
        Debug.LogError(message);
        EditorUtility.DisplayDialog("Mine-Deck 校验失败", message, "确定");
    }

    public static void ValidateBatch()
    {
        IReadOnlyList<string> errors = CollectErrors();
        if (errors.Count > 0)
        {
            foreach (string error in errors) Debug.LogError($"[MineDeckValidator] {error}");
            throw new InvalidOperationException($"Mine-Deck 项目校验失败，共 {errors.Count} 项。");
        }

        Debug.Log("[MineDeckValidator] 项目校验通过。");
    }

    private static IReadOnlyList<string> CollectErrors()
    {
        List<string> errors = new List<string>();
        ValidateBuildSettings(errors);

        string[] cardGuids = AssetDatabase.FindAssets("t:CardData", new[] { CardFolder });
        List<CardData> cards = cardGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<CardData>)
            .Where(card => card != null)
            .ToList();
        ValidateCards(cards, errors);

        if (!System.IO.File.Exists(MainScenePath))
        {
            errors.Add($"缺少主场景：{MainScenePath}");
            return errors;
        }

        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        ValidateScene(scene, cards, errors);
        return errors;
    }

    private static void ValidateBuildSettings(ICollection<string> errors)
    {
        bool mainSceneEnabled = EditorBuildSettings.scenes.Any(
            scene => scene.enabled && scene.path == MainScenePath);
        if (!mainSceneEnabled) errors.Add("主场景未加入 Build Settings 或未启用。");
    }

    private static void ValidateCards(IReadOnlyCollection<CardData> cards, ICollection<string> errors)
    {
        if (cards.Count == 0)
        {
            errors.Add("没有找到任何 CardData 资产。");
            return;
        }

        HashSet<SkillType> skillTypes = new HashSet<SkillType>();
        foreach (CardData card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.cardName)) errors.Add($"{card.name} 缺少卡牌名称。");
            if (card.icon == null) errors.Add($"{card.name} 缺少图标。");
            if (card.skillType == SkillType.None) errors.Add($"{card.name} 未配置技能类型。");
            if (!skillTypes.Add(card.skillType)) errors.Add($"技能类型重复：{card.skillType}。");
            if (card.energyCost < 0) errors.Add($"{card.name} 的能量费用不能为负数。");
            if (card.cooldownTurns < 0) errors.Add($"{card.name} 的冷却回合不能为负数。");
        }
    }

    private static void ValidateScene(Scene scene, IReadOnlyCollection<CardData> cards, ICollection<string> errors)
    {
        Game game = RequireSingle<Game>(errors);
        PrepManager prep = RequireSingle<PrepManager>(errors);
        RequireSingle<DeckManager>(errors);
        RequireSingle<SkillManager>(errors);
        RequireSingle<RelayManager>(errors);
        RequireSingle<ConnectionUI>(errors);
        RequireSingle<LobbyManager>(errors);
        RequireSingle<NetworkManager>(errors);

        if (game != null)
        {
            if (game.prepManager == null) errors.Add("Game 未绑定 PrepManager。");
            if (game.deckManager == null) errors.Add("Game 未绑定 DeckManager。");
            if (game.gameHUD == null) errors.Add("Game 未绑定 gameHUD。");
            if (game.gridParent == null) errors.Add("Game 未绑定 gridParent。");
            if (game.width <= 0 || game.height <= 0) errors.Add("棋盘尺寸必须为正数。");
            if (game.mineCount <= 0 || game.mineCount >= game.width * game.height)
                errors.Add("地雷数量必须大于 0 且小于格子总数。");
        }

        if (prep != null)
        {
            List<CardData> configuredCards = prep.allAvailableCards ?? new List<CardData>();
            if (configuredCards.Any(card => card == null)) errors.Add("PrepManager 卡池包含空引用。");
            if (configuredCards.Count != configuredCards.Distinct().Count()) errors.Add("PrepManager 卡池包含重复卡牌。");
            if (!new HashSet<CardData>(configuredCards).SetEquals(cards))
                errors.Add("PrepManager 卡池与 CardData 资产目录不一致。");
            if (prep.maxCards < 1 || prep.maxCards > configuredCards.Count)
                errors.Add("PrepManager.maxCards 超出有效范围。");
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.gameObject.GetComponents<Component>().Any(component => component == null))
                    errors.Add($"场景对象存在丢失脚本：{GetHierarchyPath(transform)}");
            }
        }
    }

    private static T RequireSingle<T>(ICollection<string> errors) where T : UnityEngine.Object
    {
        T[] objects = UnityEngine.Object.FindObjectsOfType<T>(true);
        if (objects.Length != 1)
        {
            errors.Add($"场景中 {typeof(T).Name} 数量应为 1，当前为 {objects.Length}。");
            return null;
        }

        return objects[0];
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = $"{transform.name}/{path}";
        }

        return path;
    }
}
