using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PrepManager : MonoBehaviour
{
    [Header("数据源")]
    public List<CardData> allAvailableCards; // 把那5张卡全拖进来
    public int maxCards = 4; 

    [Header("状态容器")]
    public GameObject waitingPanelRoot; // 整个 WaitingPanel
    public GameObject lobbyState;       // 旧的等待界面
    public GameObject prepState;        // 新的选卡界面

    [Header("备战 UI")]
    public Transform poolContainer;     // CardPool
    public Transform selectedContainer; // SelectedSlots
    public Button readyButton;          
    public TMP_Text readyButtonText;    // 按钮上的字 (TMP)

    [Header("按钮美化")]
    public Image readyButtonBg;         // ⚠️记得拖入按钮自己的 Image 组件
    public Color colorStandby = new Color(1f, 0.8f, 0f); // 暗黄色
    public Color colorReady = new Color(0f, 1f, 0f);     // 亮绿色/荧光绿
    public Color colorDisabled = Color.gray;             // 灰色

    [Header("Prefab")]
    public GameObject selectionCardPrefab; // 拖入你改好的 SelectionCardPrefab

    private List<CardData> mySelection = new List<CardData>();
    private bool isReady = false;
    private Game game;

    private void Start()
    {
        game = FindObjectOfType<Game>();
        if(readyButton) readyButton.onClick.AddListener(OnReadyClicked);
        
        // 默认状态：Host 刚进来时显示 Lobby，Client 还没进
        if (lobbyState) lobbyState.SetActive(true);
        if (prepState) prepState.SetActive(false);

        // 初始化按钮视觉状态
        UpdateButtonVisual(false);
    }

    // --- 阶段 1: 进入备战 (Lobby -> Prep) ---
    public void EnterPrepPhase()
    {
        Debug.Log("【PrepManager】进入备战阶段！");
        if (waitingPanelRoot) waitingPanelRoot.SetActive(true); // 确保大底板开着
        if (lobbyState) lobbyState.SetActive(false);            // 关掉房间号
        if (prepState) prepState.SetActive(true);               // 打开选卡界面

        // 生成卡池
        RefreshPoolUI();
        RefreshSelectedUI();
        UpdateButtonVisual(); // 刷新一下按钮状态
    }

    // --- 阶段 2: 战斗开始 (Prep -> Game) ---
    public void OnMatchStart()
    {
        // 1. 关掉整个 WaitingPanel
        if (waitingPanelRoot) waitingPanelRoot.SetActive(false);

        // 2. 把选好的卡传给 DeckManager，开始游戏
        FindObjectOfType<DeckManager>().InitDeck(mySelection);
    }

    // --- 选卡逻辑 ---

    void RefreshPoolUI()
    {
        foreach (Transform child in poolContainer) Destroy(child.gameObject);

        // 检查当前是否选满了
        bool isFull = mySelection.Count >= maxCards;

        foreach (var card in allAvailableCards)
        {
            if (mySelection.Contains(card)) continue; // 已选的就不显示在池子里

            GameObject go = Instantiate(selectionCardPrefab, poolContainer);
            CardUI ui = go.GetComponent<CardUI>();
            ui.SetupForPrep(card);            

            // 如果选满了，把池子里的卡变暗且不可点
            var btn = go.GetComponent<Button>();
            var canvasGroup = go.GetComponent<CanvasGroup>();
            
            if (isFull) 
            {
                btn.interactable = false;
                if(canvasGroup) canvasGroup.alpha = 0.5f; 
            }
            else
            {
                btn.interactable = true;
                if(canvasGroup) canvasGroup.alpha = 1f;
            }

            // 点击事件：加入已选
            go.GetComponent<Button>().onClick.AddListener(() => {
                if (!isReady && mySelection.Count < maxCards) {
                    mySelection.Add(card);
                    RefreshPoolUI();
                    RefreshSelectedUI();
                }
            });
        }
        UpdateButtonVisual();
    }

    void RefreshSelectedUI()
    {
        foreach (Transform child in selectedContainer) Destroy(child.gameObject);

        foreach (var card in mySelection)
        {
            GameObject go = Instantiate(selectionCardPrefab, selectedContainer);
            CardUI ui = go.GetComponent<CardUI>();
            ui.SetupForPrep(card);
            
            // 点击事件：移除已选
            go.GetComponent<Button>().onClick.AddListener(() => {
                if (!isReady) {
                    mySelection.Remove(card);
                    RefreshPoolUI();
                    RefreshSelectedUI();
                }
            });
        }
        UpdateButtonVisual();
    }

    // --- 核心：按钮视觉状态管理 ---
    void UpdateButtonVisual(bool forceReady = false)
    {
        // 如果没赋值，防止报错
        if (readyButton == null || readyButtonBg == null || readyButtonText == null) return;

        if (forceReady)
        {
            // 状态：已锁定/已准备
            readyButtonBg.color = colorReady; 
            readyButtonText.text = "DEPLOYING..."; // 正在部署...
            readyButtonText.color = Color.black;   // 亮底黑字
            return;
        }

        // 状态：检查卡牌数量是否够了 (maxCards = 4)
        // 这里我改成了 >= 1 即可开始，你可以改回 >= maxCards
        bool canDeploy = mySelection.Count >= 1; 

        readyButton.interactable = canDeploy;
        
        if (canDeploy)
        {
            // 状态：就绪，可以点
            readyButtonBg.color = colorStandby;
            readyButtonText.text = "DEPLOY";      // 部署
            readyButtonText.color = Color.black;
        }
        else
        {
            // 状态：卡没选够，不可用
            readyButtonBg.color = colorDisabled;
            readyButtonText.text = "WAITING INPUT"; // 等待输入
            readyButtonText.color = new Color(1,1,1, 0.5f); // 半透明白字
        }
    }

    // 只有这一个 OnReadyClicked，删掉了多余的
    void OnReadyClicked()
    {
        if (mySelection.Count == 0) return;
        
        isReady = true;
        readyButton.interactable = false; // 禁用点击，防止重复点
        
        // --- 视觉切换：变绿！ ---
        UpdateButtonVisual(true);
        
        // 告诉服务器
        if (game != null) 
        {
            int[] selectedSkillIds = mySelection.ConvertAll(card => (int)card.skillType).ToArray();
            game.PlayerReadyServerRpc(selectedSkillIds);
        }
        else 
        {
            Debug.LogError("找不到 Game 组件，无法发送准备指令！");
        }
    }
}
