using System.Collections.Generic;
using UnityEngine;

public class DeckManager : MonoBehaviour
{
    [Header("配置")]
    public GameObject cardPrefab;
    
    // [修改] 以前是一个 handArea，现在拆成左右两个
    public Transform leftDock;  
    public Transform rightDock; 
    
    // 这里模拟“带入的卡”，以后可以在 Menu 场景选好传过来
    private List<CardData> currentLoadout = new List<CardData>();

    [Header("状态")]
    public CardUI currentSelectedUI; // 当前选中的是哪张卡 UI
    private List<CardUI> spawnedCards = new List<CardUI>(); // 记录生成出来的卡

    private Game game;

    private void Start()
    {
        game = FindObjectOfType<Game>();
    }

    public void InitDeck(List<CardData> chosenCards)
    {
        currentLoadout = new List<CardData>(chosenCards);
        InitializeDeck(); // 收到卡牌数据，开始发牌
    }

    void InitializeDeck()
    {
        // 1. 清空两边的旧卡牌
        if (leftDock) foreach (Transform child in leftDock) Destroy(child.gameObject);
        if (rightDock) foreach (Transform child in rightDock) Destroy(child.gameObject);
        
        spawnedCards.Clear();

        // 2. 计算分配阈值
        int totalCards = currentLoadout.Count;
        // 逻辑：均分，若奇数则左边多一个
        // 例子：5张 -> (5+1)/2 = 3 (左边放前3张，右边放后2张)
        // 例子：4张 -> (4+1)/2 = 2 (左边放前2张，右边放后2张)
        int leftSideCount = (totalCards + 1) / 2;

        // 3. 循环生成并分配
        for (int i = 0; i < totalCards; i++)
        {
            var data = currentLoadout[i];
            
            // 决定父物体：如果索引小于阈值，放左边；否则放右边
            Transform targetParent = (i < leftSideCount) ? leftDock : rightDock;

            // 生成卡牌
            if (targetParent != null)
            {
                GameObject go = Instantiate(cardPrefab, targetParent);
                CardUI ui = go.GetComponent<CardUI>();
                
                // 初始化
                ui.Setup(data, this);
                spawnedCards.Add(ui);
            }
        }
    }

    // --- 选中逻辑 (保持不变) ---
    public void OnCardSelected(CardUI ui)
    {
        if (currentSelectedUI == ui)
        {
            DeselectCard();
            return;
        }

        if (currentSelectedUI != null) currentSelectedUI.SetSelected(false);
        
        currentSelectedUI = ui;
        currentSelectedUI.SetSelected(true);
    }

    public void DeselectCard()
    {
        if (currentSelectedUI != null) currentSelectedUI.SetSelected(false);
        currentSelectedUI = null;
    }

    // --- 核心事件响应 (保持不变) ---

    public void OnSkillCastSuccess(SkillType skillType, int cooldownTurns)
    {
        CardUI castCard = spawnedCards.Find(card => card != null && card.data != null && card.data.skillType == skillType);
        if (castCard != null)
        {
            castCard.StartCooldown(cooldownTurns);
            DeselectCard();
            RefreshAllCardsState();
        }
    }

    public void OnNewTurnStarted()
    {
        foreach (var card in spawnedCards)
        {
            if(card != null) card.ReduceCooldown();
        }
        RefreshAllCardsState();
    }

    public void RefreshAllCardsState()
    {
        if (game == null) return;

        var localPlayer = game.GetLocalPlayerIdentity();
        int myEnergy = 0;

        if (localPlayer == Cell.Owner.PlayerA)
        {
            myEnergy = game.energyP1.Value; 
        }
        else if (localPlayer == Cell.Owner.PlayerB)
        {
            myEnergy = game.energyP2.Value;
        }

        foreach (var card in spawnedCards)
        {
            if (card != null) card.RefreshState(myEnergy);
        }
    }
}
