using UnityEngine;

[CreateAssetMenu(fileName = "NewCard", menuName = "Game/Card Data")]
public class CardData : ScriptableObject
{
    [Header("基础信息")]
    public string cardName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("技能配置")]
    public SkillType skillType; // 对应 SkillManager 里的枚举
    public int energyCost;      // 消耗能量

    [Header("冷却配置")]
    public int cooldownTurns;

    [Header("使用限制")]
    public bool needTarget = true; // 是否需要选择地图目标（比如雷达需要点地，全屏大招不需要）
}