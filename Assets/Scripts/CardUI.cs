using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CardUI : MonoBehaviour
{
    [Header("UI 组件")]
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text costText;
    public TMP_Text descriptionText;
    public Button button;
    public GameObject selectionHighlight; // 选中高亮框 (黄色)
    
    [Header("冷却组件")]
    public GameObject cdOverlay;      // 黑色半透明遮罩
    public TMP_Text cdText;           // 显示剩余回合数
    public Image notEnoughEnergyMask; // (可选) 能量不足时的变灰遮罩

    [Header("分类角标 (Class Badge)")]
    public GameObject badgeRoot;    // ClassBadge 根物体 (用于控制显示/隐藏)
    public Image badgeBg;           // BadgeBg (背景框)
    public Image badgeIcon;         // BadgeIcon (图标)
    [Header("分类图标资源 (Sprite)")]
    public Sprite iconRecovery;     // 恢复 (十字)
    public Sprite iconDefense;      // 防御 (盾牌)
    public Sprite iconIntel;        // 侦查 (眼睛)
    public Sprite iconDisruption;   // 妨碍 (闪电)
    public Sprite iconDeployment;   // 部署 (齿轮)
    private readonly Color colRecovery = new Color(0f, 1f, 0.4f);   // 霓虹绿
    private readonly Color colDefense = new Color(0f, 0.8f, 1f);    // 力场蓝
    private readonly Color colIntel = new Color(0f, 0.5f, 1f);      // 深邃蓝
    private readonly Color colDisruption = new Color(0.8f, 0f, 1f); // 故障紫
    private readonly Color colDeployment = new Color(1f, 0.6f, 0f); // 警示橙
    [Header("备战隐藏")]
    public GameObject cdOverlayGroup; // 【新增】把 CD遮罩、CD文字、费用文字 的父物体都拖进去
    public GameObject costGroup;// 【新增】费用的背景和文字

    [Header("运行时状态")]
    public CardData data;
    public int currentCD; // 当前剩余冷却回合 (0 代表可用)
    private DeckManager deckManager;

    private enum CardCategory { None, Recovery, Defense, Intel, Disruption, Deployment }

    public void Setup(CardData cardData, DeckManager manager)
    {
        data = cardData;
        deckManager = manager;
        currentCD = 0; // 初始无冷却

        // 基础 UI 赋值
        if (iconImage) iconImage.sprite = data.icon;
        if (nameText) nameText.text = data.cardName;
        if (costText) costText.text = data.energyCost.ToString();
        if (descriptionText) descriptionText.text = data.description;
        
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnCardClicked);
        UpdateClassBadge(data.skillType);
        RefreshState(100); // 初始刷新一次状态
    }
    private void UpdateClassBadge(SkillType skill)
    {
        if (badgeRoot == null || badgeBg == null || badgeIcon == null) return;

        CardCategory cat = GetCategoryFromSkill(skill);

        if (cat == CardCategory.None)
        {
            badgeRoot.SetActive(false);
            return;
        }

        badgeRoot.SetActive(true);
        
        // 【关键修改】让图标始终显示原图颜色 (设为白色即不染色)
        badgeIcon.color = Color.white; 

        // 根据分类，只改变背景框(BadgeBg)的霓虹光颜色
        // 这样你的“绿色十字”会浮在“绿色光晕”的背景上，效果更好
        switch (cat)
        {
            case CardCategory.Recovery:
                badgeBg.color = colRecovery;     // 背景变绿
                badgeIcon.sprite = iconRecovery; // 显示你画好的彩色图标
                break;
            case CardCategory.Defense:
                badgeBg.color = colDefense;      // 背景变蓝
                badgeIcon.sprite = iconDefense;
                break;
            case CardCategory.Intel:
                badgeBg.color = colIntel;
                badgeIcon.sprite = iconIntel;
                break;
            case CardCategory.Disruption:
                badgeBg.color = colDisruption;
                badgeIcon.sprite = iconDisruption;
                break;
            case CardCategory.Deployment:
                badgeBg.color = colDeployment;
                badgeIcon.sprite = iconDeployment;
                break;
        }
    }
    private CardCategory GetCategoryFromSkill(SkillType skill)
    {
        switch (skill)
        {
            // [绿色] 恢复
            case SkillType.HealSmall:
            case SkillType.HealLarge:
                return CardCategory.Recovery;

            // [青色] 防御
            case SkillType.Ghost:
                return CardCategory.Defense;

            // [蓝色] 侦查
            case SkillType.RadarScan:
            case SkillType.Unchord: // 逻辑开图也算侦查
                return CardCategory.Intel;

            // [紫色] 妨碍
            case SkillType.CutWire:
            case SkillType.DataRollback:
                return CardCategory.Disruption;

            // [橙色] 部署
            case SkillType.PlaceTrap:
            case SkillType.BuildTower:
                return CardCategory.Deployment;
            
            // 如果有其他技能，默认None
            default:
                return CardCategory.None;
        }
    }

    // --- 核心状态刷新逻辑 ---
    // 这个函数每一回合、或者能量变化时都要调用
    public void RefreshState(int currentPlayerEnergy)
    {
        bool onCooldown = currentCD > 0;
        bool enoughEnergy = currentPlayerEnergy >= data.energyCost;

        // 1. 处理冷却显示
        if (cdOverlay) cdOverlay.SetActive(onCooldown);
        if (cdText) cdText.text = currentCD.ToString();

        // 2. 处理按钮交互 (冷却中 或 没蓝 都不能点)
        if (onCooldown)
        {
            button.interactable = false;
            if (notEnoughEnergyMask) notEnoughEnergyMask.gameObject.SetActive(false);
        }
        else if (!enoughEnergy)
        {
            button.interactable = false;
            // 如果你有单独的缺蓝遮罩，可以在这里显示
             if (notEnoughEnergyMask) notEnoughEnergyMask.gameObject.SetActive(true);
        }
        else
        {
            // 可用状态
            button.interactable = true;
            if (notEnoughEnergyMask) notEnoughEnergyMask.gameObject.SetActive(false);
        }
    }

    // --- 逻辑操作 ---

    private void OnCardClicked()
    {
        // 只有在按钮可交互时才能点进来，所以不用再判断 CD 和能量
        deckManager.OnCardSelected(this);
    }

    public void SetSelected(bool isSelected)
    {
        if (selectionHighlight) selectionHighlight.SetActive(isSelected);
    }

    // 开始冷却 (技能释放成功后调用)
    public void StartCooldown()
    {
        currentCD = data.cooldownTurns;
        SetSelected(false); // 取消高亮
        // RefreshState 会在 Game.cs 的能量扣除后统一刷新，或者手动刷新
    }

    // 回合开始时调用：减少冷却
    public void ReduceCooldown()
    {
        if (currentCD > 0)
        {
            currentCD--;
        }
    }

    // 【新增】专门用于备战界面的初始化方法
    public void SetupForPrep(CardData cardData)
    {
        // 1. 基础赋值
        if (iconImage) iconImage.sprite = cardData.icon;
        if (nameText) nameText.text = cardData.cardName;
        if (descriptionText) descriptionText.text = cardData.description;
        UpdateClassBadge(cardData.skillType);

        // 2. 暴力隐藏战斗 UI
        if (cdOverlayGroup) cdOverlayGroup.SetActive(false);
        if (costGroup) 
        {
            costGroup.SetActive(true); // 确保它是开着的
        }
        
        // 别忘了更新具体的数字！
        if (costText) 
        {
            costText.text = cardData.energyCost.ToString();
            // 如果你的 costText 单独有个父物体被关了，记得确保它的父物体也是开的
            // costText.gameObject.SetActive(true); 
        }

        // 3. 确保按钮能点
        if (button) button.interactable = true;
    }
}