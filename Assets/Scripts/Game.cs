using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

[DefaultExecutionOrder(-1)]
public class Game : NetworkBehaviour
{
    [Header("地图与摄像机设置")]
    public int width = 32;
    public int height = 16;
    public int mineCount = 80;

    [Header("回合与规则设置")]
    // 记录当前是谁的回合 (对应之前的 netCurrentTurn)
    public NetworkVariable<Cell.Owner> currentTurn = new NetworkVariable<Cell.Owner>(Cell.Owner.PlayerA);
    // 记录总回合数 (对应之前的 currentTurn)
    public NetworkVariable<int> totalTurnCount = new NetworkVariable<int>(1);
    
    public int fogRounds = 3;          // 迷雾回合数
    public int cellCooldownRounds = 3; // 抢地后冷却回合
    public int cellMaxDurability = 3;  // 【新增】格子耐久度

    [Header("数值配置 - 限制")]
    public int maxHealth = 100;
    public int maxEnergy = 100;
    public int maxTowersPerPlayer = 10;
    
    // 内部计数器 (用于限制塔数量)
    private int towerCountA = 0;
    private int towerCountB = 0;

    [Header("数值配置 - 伤害")]
    public int damageMine = 5;         // 踩雷伤害
    public int damageTrap = 10;        // 【新增】陷阱伤害
    public int damageTowerDirect = 10; // 点塔伤害
    public int damageTowerAOE = 5;     // 塔AOE伤害
    public int damageBuildFail = 3;    // 建塔失败反噬
    public int damageRoadConnected = 20; // 通路连通伤害

    [Header("数值配置 - 消耗与收益")]
    public int costBuildTower = 5;     // 建塔消耗
    public int gainNormal = 5;         // 点数字收益
    public int gainCapture = 7;        // 抢地收益

    [Header("陷阱配置")]
    public int maxTrapsPerPlayer = 5; 
    public int costPlaceTrap = 15; // 搬移到配置区
    private int trapCountP1 = 0;
    private int trapCountP2 = 0;

    [Header("网络变量 - 玩家属性")]
    // 对应原来的 healthA / healthB
    public NetworkVariable<int> hpP1 = new NetworkVariable<int>(100);
    public NetworkVariable<int> hpP2 = new NetworkVariable<int>(100);
    
    // 对应原来的 energyA / energyB
    public NetworkVariable<int> energyP1 = new NetworkVariable<int>(0); 
    public NetworkVariable<int> energyP2 = new NetworkVariable<int>(0);
    public NetworkVariable<int> immuneTurnsP1 = new NetworkVariable<int>(0);
    public NetworkVariable<int> immuneTurnsP2 = new NetworkVariable<int>(0);
    [Header("手感微调")]
    [Tooltip("点击位置的Y轴修正值。如果点上面偏了就调大，点下面偏了就调小")]
    public float clickOffsetY = 0.25f; // 默认值先改小一点

    [Header("备战管理")]
    public PrepManager prepManager; // 【记得拖拽引用】
    private bool p1Ready = false;
    private bool p2Ready = false;
    private bool matchStarted = false;
    private ulong? playerBClientId;
    private readonly Dictionary<Cell.Owner, HashSet<SkillType>> playerLoadouts = new Dictionary<Cell.Owner, HashSet<SkillType>>();
    private readonly Dictionary<Cell.Owner, Dictionary<SkillType, int>> playerCooldowns = new Dictionary<Cell.Owner, Dictionary<SkillType, int>>();

    [Header("UI 引用 - 玩家A (P1)")]
    public UnityEngine.UI.Slider hpSliderA;
    public TMPro.TextMeshProUGUI hpTextA;
    public UnityEngine.UI.Slider energySliderA;
    public TMPro.TextMeshProUGUI energyTextA;

    [Header("UI 引用 - 玩家B (P2)")]
    public UnityEngine.UI.Slider hpSliderB;
    public TMPro.TextMeshProUGUI hpTextB;
    public UnityEngine.UI.Slider energySliderB;
    public TMPro.TextMeshProUGUI energyTextB;

    [Header("UI 颜色设置")]
    public Color activeTextColor = Color.white;
    public Color inactiveTextColor = Color.gray;

    [Header("UI 控制")]
    public GameObject gameHUD;      // 游戏界面

    public GameObject gridParent; // 【新增】用来控制地图/格子的父物体
    public GameObject waitingPanel; // 【新增】用来在游戏开始时关闭它

    public GameObject gameOverPanel; 
    public DeckManager deckManager;
    public TMPro.TMP_Text winnerText;
    public string currentJoinCode;

    // 内部引用
    private Board board;
    public CellGrid grid;
    private bool gameover;
    private SkillManager skillManager;
    private bool mapGenerated = false;
    private float lastScreenWidth;
    private float lastScreenHeight;
    private List<Vector2Int> jammedCells = new List<Vector2Int>();

    private void Awake()
    {
        Application.targetFrameRate = 60;
        board = GetComponentInChildren<Board>();
    }

    public struct CellSyncData : INetworkSerializable
    {
        public int x, y;
        public int type;
        public int number;
        public int unlockTurn;
        public int jamTurns;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref x);
            serializer.SerializeValue(ref y);
            serializer.SerializeValue(ref type);
            serializer.SerializeValue(ref number);
            serializer.SerializeValue(ref jamTurns);
            serializer.SerializeValue(ref unlockTurn);
        }
    }

    public override void OnNetworkSpawn()
    {
        skillManager = GetComponent<SkillManager>();
        if (gameHUD != null) gameHUD.SetActive(false);
        if (gridParent != null) gridParent.SetActive(false);
        if (waitingPanel != null && IsServer) waitingPanel.SetActive(true); // Host 确保看到等待界面
        // 只有服务器负责初始化真正的游戏逻辑
        if (IsServer)
        {
            NewGame();
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        else
        {
            // 客户端也要初始化一个空的 Grid 用来接收显示数据
            grid = new CellGrid(width, height);
            // 客户端第一次连进来，请求服务器发一份完整数据（可选，这里先省略，依靠后续同步）
        }

        // 7. 绑定变量变化回调 (当数值变化时，自动刷新 UI)
        currentTurn.OnValueChanged += (oldVal, newVal) => 
        {
            UpdateTurnUI();
            
            // 【新增修复】当回合发生变化，且新回合是自己的时候，刷新 CD
            // 这个回调在主机和客户端都会触发，所以 P2 也能收到了！
            if (GetLocalPlayerIdentity() == newVal)
            {
                if (deckManager != null) 
                {
                    deckManager.OnNewTurnStarted();
                }
            }
            
            // 原有的重绘逻辑保持在下面...
            if (grid != null) {
                board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value);
            }
        };
        
        energyP1.OnValueChanged += (oldVal, newVal) => UpdateEnergyUI(Cell.Owner.PlayerA, newVal);
        energyP2.OnValueChanged += (oldVal, newVal) => UpdateEnergyUI(Cell.Owner.PlayerB, newVal);
        
        hpP1.OnValueChanged += (oldVal, newVal) => UpdateHealthUI(Cell.Owner.PlayerA, newVal);
        hpP2.OnValueChanged += (oldVal, newVal) => UpdateHealthUI(Cell.Owner.PlayerB, newVal);
        // 在 OnNetworkSpawn 里添加：
        immuneTurnsP1.OnValueChanged += (old, val) => UpdateHealthUI(Cell.Owner.PlayerA, hpP1.Value);
        immuneTurnsP2.OnValueChanged += (old, val) => UpdateHealthUI(Cell.Owner.PlayerB, hpP2.Value);

        // 强制刷新一次 UI
        UpdateTurnUI();

        currentTurn.OnValueChanged += (oldVal, newVal) => {
            if (grid != null) {
                board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value);
            }
        };

        // SetupCamera();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId) return;

        if (playerBClientId.HasValue && playerBClientId.Value != clientId)
        {
            Debug.LogWarning($"拒绝额外玩家连接：{clientId}");
            NetworkManager.DisconnectClient(clientId);
            return;
        }

        playerBClientId = clientId;

        // Host + 唯一一名 Client 到齐
        if (NetworkManager.Singleton.ConnectedClientsIds.Count == 2)
        {
            Debug.Log("玩家凑齐，广播进入备战！");
            EnterPrepClientRpc();

            // 【新增】如果是 Host，且存了 JoinCode，去更新 Supabase
            if (IsServer && !string.IsNullOrEmpty(currentJoinCode))
            {
                // 找到 LobbyManager 更新状态为 FULL
                var lobby = FindObjectOfType<LobbyManager>();
                if (lobby != null)
                {
                    lobby.UpdateRoomStatus(currentJoinCode, "FULL", 2);
                }
            }
        }
    }

    [ClientRpc]
    private void EnterPrepClientRpc()
    {
        // 如果之前 waitingPanel 被关了（虽然应该没关），确保它开着
        if (waitingPanel != null) waitingPanel.SetActive(true);

        if (prepManager != null)
        {
            prepManager.EnterPrepPhase();
        }
    }

    [ClientRpc]
    private void OpponentDisconnectedClientRpc()
    {
        gameover = false;
        if (gameHUD != null) gameHUD.SetActive(false);
        if (gridParent != null) gridParent.SetActive(false);
        if (waitingPanel != null) waitingPanel.SetActive(true);
        if (prepManager != null) prepManager.ReturnToLobbyPhase();
    }

    // 3. 玩家点击准备，同时把本局卡组交给服务器备案
    [ServerRpc(RequireOwnership = false)]
    public void PlayerReadyServerRpc(int[] selectedSkillIds, ServerRpcParams rpc = default)
    {
        ulong id = rpc.Receive.SenderClientId;
        if (!TryResolveNetworkPlayer(id, out Cell.Owner player)) return;

        if (matchStarted || (player == Cell.Owner.PlayerA ? p1Ready : p2Ready))
        {
            Debug.LogWarning($"玩家 {id} 重复提交准备请求，已忽略。");
            return;
        }

        if (!TryValidateLoadout(selectedSkillIds, out HashSet<SkillType> loadout))
        {
            Debug.LogWarning($"玩家 {id} 提交了无效卡组，已拒绝准备。");
            return;
        }

        playerLoadouts[player] = loadout;
        playerCooldowns[player] = new Dictionary<SkillType, int>();

        if (player == Cell.Owner.PlayerA) p1Ready = true;
        else p2Ready = true;

        Debug.Log($"玩家 {id} 已准备。P1:{p1Ready}, P2:{p2Ready}");

        // 两个人都准备好了，才真正开始游戏
        if (p1Ready && p2Ready)
        {
            matchStarted = true;
            StartMatchClientRpc();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!playerBClientId.HasValue || playerBClientId.Value != clientId) return;

        Debug.LogWarning("玩家 2 已断开，本局已停止并返回等待状态。");
        playerBClientId = null;
        NewGame();
        OpponentDisconnectedClientRpc();

        if (!string.IsNullOrEmpty(currentJoinCode))
        {
            LobbyManager lobby = FindObjectOfType<LobbyManager>();
            if (lobby != null) lobby.UpdateRoomStatus(currentJoinCode, "WAITING", 1);
        }
    }

    // 【新增】记得在销毁时取消监听，防止报错
    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    // --- 【新增】供 UI 按钮调用的重开方法 ---
    public void ServerRematch()
    {
        if (!IsServer) return;

        Debug.Log("房主发起重赛，重置游戏数据...");

        // 1. 重置所有数据
        NewGame();

        // 2. 广播让所有客户端（包括Host自己）重新进入备战界面
        // 注意：这里不需要重新连接网络，只是切换 UI 状态
        EnterPrepClientRpc();
    }

    // --- 【修改】更彻底的重置逻辑 ---
    private void NewGame()
    {
        if (!IsServer) return; 

        StopAllCoroutines();
        gameover = false;
        
        // 1. 重置回合与地图
        totalTurnCount.Value = 1; 
        currentTurn.Value = Cell.Owner.PlayerA; // 重置为 P1 先手
        grid = new CellGrid(width, height);     // 服务器生成新空网格
        mapGenerated = false;                   // 标记地图未生成（下次点击生成雷）
        jammedCells.Clear();                    // 清空被阻断的格子记录

        // 2. 重置计数器
        towerCountA = 0;
        towerCountB = 0;
        trapCountP1 = 0; // 【新增】重置陷阱数
        trapCountP2 = 0;

        // 3. 重置备战状态 (非常重要！否则会直接跳过选卡)
        p1Ready = false; 
        p2Ready = false;
        matchStarted = false;
        playerLoadouts.Clear();
        playerCooldowns.Clear();

        // 4. 重置 NetworkVariables (数值)
        energyP1.Value = 0;
        energyP2.Value = 0;
        hpP1.Value = maxHealth;
        hpP2.Value = maxHealth;
        
        // 【新增】重置无敌状态
        immuneTurnsP1.Value = 0;
        immuneTurnsP2.Value = 0;

        Debug.Log("服务器：数据已重置，等待玩家重新备战...");
        
        // 5. 通知客户端重置棋盘画面 (变回全白)
        ResetBoardClientRpc();
    }

    private void Update()
    {
        // 如果没有连接网络，不运行
        if (!IsSpawned) return; 

        // 游戏结束时的逻辑
        if (gameover)
        {
            // 这里删除了原来的 Input.GetKeyDown(KeyCode.R)
            // 改为完全由 UI 按钮调用 ServerRematch()，防止误触
            return;
        }

        // 屏幕适配逻辑 (保持不变)
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            // SetupCamera(); // 如果你需要动态调整相机
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
        }

        // 处理输入
        HandleInput(GetLocalPlayerIdentity());
    }

    private void HandleInput(Cell.Owner me)
    {
        // 1. 直接获取鼠标在世界空间的位置 (Z轴归零)
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0;

        // 2. 【核心修正】动态计算偏移量
        // 获取你的格子设置的高度 (你设置的是 0.6)
        float cellHeight = gridParent.GetComponent<Grid>().cellSize.y;
        
        // 因为你的 Tilemap Anchor Y 设为了 0 (底部)，
        // 当你点击菱形视觉中心时，由于透视关系，逻辑坐标其实偏高了。
        // 我们要把鼠标坐标向下修正 "半个格子高度"，让它去寻找“脚底”对应的锚点。
        worldPos.y -= clickOffsetY; 

        // 3. 转为格子坐标
        Vector3Int pos = board.tilemap.WorldToCell(worldPos);
        // 越界检查
        if (!grid.InBounds(pos.x, pos.y)) return;
        
        // 迷雾锁定检查 (前3回合分屏)
        if (totalTurnCount.Value <= fogRounds * 2)
        {
            if (me == Cell.Owner.PlayerA && pos.x >= width / 2) return;
            if (me == Cell.Owner.PlayerB && pos.x < width / 2) return;
        }
        // --- 鼠标左键：翻开 / 释放技能 ---
        if (Input.GetMouseButtonDown(0)) 
        {
            Debug.Log("点击了鼠标左键！");
            // [修改点] 这里改成 deckManager.currentSelectedUI
            if (deckManager != null && deckManager.currentSelectedUI != null)
            {
                // [修改点] 通过 UI 获取内部的数据 (data)
                CardData card = deckManager.currentSelectedUI.data;
                
                // 发送技能请求
                RequestSkillServerRpc(card.skillType, pos.x, pos.y);
                
                return; // 拦截掉普通的 REVEAL
            }

            // B. 如果没有卡牌，执行普通的翻开
            RequestActionServerRpc(pos.x, pos.y, "REVEAL");
        }

        // --- 鼠标右键：插旗 (新增) ---
        else if (Input.GetMouseButtonDown(1))
        {
            // 插旗不消耗回合，所以它不走 EndTurn 流程，独立使用 RequestFlagServerRpc
            RequestFlagServerRpc(pos.x, pos.y);
        }
        if (Input.GetMouseButton(2))
        {
            if (grid.TryGetCell(pos.x, pos.y, out Cell c)) 
            {
                ChordLocal(c); // 调用您已有的高亮逻辑
            }
        }
        // --- 辅助视觉：中键抬起清除高亮 ---
        if (Input.GetMouseButtonUp(2))
        {
            ClearAllChordedFlagsLocal();
        }
        // --- T键：建塔 ---
        // else if (Input.GetKeyDown(KeyCode.T)) 
        // {
        //     RequestActionServerRpc(pos.x, pos.y, "TOWER");
        // }
    }

    // 纯客户端视觉：高亮周围
    private void ChordLocal(Cell center)
    {
        // 先重置一遍，防止残留
        ClearAllChordedFlagsLocal();

        if (!center.revealed || center.type != Cell.Type.Number) return;

        Cell.Owner me = GetLocalPlayerIdentity();
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                int nx = center.position.x + x;
                int ny = center.position.y + y;
                if (grid.TryGetCell(nx, ny, out Cell c)) {
                    if (!c.revealed && !c.IsFlaggedBy(me) && !c.hasTower) {
                        c.chorded = true; // 临时标记
                    }
                }
            }
        }
        board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value); // 客户端重绘
    }

    private void ClearAllChordedFlagsLocal()
    {
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                grid[x, y].chorded = false;
            }
        }
        board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value);
    }

    // -----------------------------------------------------------------------
    // RPC 区域：网络通信的核心
    // -----------------------------------------------------------------------

    // [ServerRpc]：客户端调用，服务器执行

    [ClientRpc]
    private void StartMatchClientRpc()
    {
        // [修改] 通知 PrepManager 游戏开始了 (它会负责关闭 WaitingPanel 并通知 DeckManager 发牌)
        if (prepManager != null) 
        {
            prepManager.OnMatchStart();
        }

        // 打开游戏 HUD
        if (gameHUD != null) gameHUD.SetActive(true);
        if (gridParent != null) gridParent.SetActive(true);
        
        Debug.Log("游戏正式开始！");
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestActionServerRpc(int x, int y, string action, ServerRpcParams rpc = default)
    {
        // 获取发送者的 ID
        ulong senderId = rpc.Receive.SenderClientId;

        if (!matchStarted || !TryResolveNetworkPlayer(senderId, out Cell.Owner sender)) return;

        // 校验：如果不是当前回合的玩家在操作，直接拦截
        if (sender != currentTurn.Value) 
        {
            Debug.LogWarning($"非回合内玩家尝试操作：发送者 {sender}, 当前回合 {currentTurn.Value}");
            return;
        }

        if (!grid.TryGetCell(x, y, out Cell cell)) return;

        // 2. 【新增】如果是废墟，禁止操作
        if (cell.isBedrock) return; 

        // 3. 【新增】如果是冷却中，禁止操作
        if (totalTurnCount.Value < cell.unlockTurn) return; 

        bool success = false;
        // 根据发来的指令类型执行对应逻辑
        switch (action)
        {
            case "REVEAL": success = ProcessReveal(sender, cell); break;
            // case "TOWER": success = ProcessBuildTower(sender, cell); break;
        }

        // 如果操作成功，才结束回合
        if (success) EndTurn();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestFlagServerRpc(int x, int y, ServerRpcParams rpcParams = default)
    {
        // 1. 确定操作者身份
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!matchStarted || !TryResolveNetworkPlayer(clientId, out Cell.Owner sender)) return;

        if (grid.TryGetCell(x, y, out Cell cell))
        {
            bool isVisuallyRevealed = (cell.owner == sender && cell.revealed) || (cell.hasTower && (cell.towerOwner == sender || cell.isTowerRevealed));
            // 2. 规则检查：已翻开、有防御塔、或已炸开的地雷不能插旗
            if (isVisuallyRevealed || cell.isBedrock || cell.exploded) return;

            // 3. 切换状态：只修改属于该玩家的私有标记变量
            bool newState;
            if (sender == Cell.Owner.PlayerA)
            {
                cell.flaggedP1 = !cell.flaggedP1;
                newState = cell.flaggedP1;
            }
            else
            {
                cell.flaggedP2 = !cell.flaggedP2;
                newState = cell.flaggedP2;
            }

            // 4. 定向同步：利用 ClientRpcParams 只发送给当前操作的客户端
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            };

            // 通知该客户端更新本地的旗子显示状态
            UpdateSingleFlagClientRpc(x, y, newState, clientRpcParams);
        }
    }

    [ClientRpc]
    private void UpdateSingleFlagClientRpc(int x, int y, bool state, ClientRpcParams rpcParams = default)
    {
        if (grid.TryGetCell(x, y, out Cell c))
        {
            // 客户端获取自己的本地身份
            Cell.Owner me = GetLocalPlayerIdentity();

            // 更新本地对应的标记位
            if (me == Cell.Owner.PlayerA) c.flaggedP1 = state;
            else if (me == Cell.Owner.PlayerB) c.flaggedP2 = state;

            // 立即重绘棋盘，Board.Draw 会根据 me 的身份读取对应的标记
            board.Draw(grid, me, totalTurnCount.Value);
        }
    }


    [ServerRpc(RequireOwnership = false)]
    private void RequestSkillServerRpc(SkillType skillType, int x, int y, ServerRpcParams rpcParams = default)
    {
        // 1. 身份验证
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!TryResolveNetworkPlayer(clientId, out Cell.Owner sender)) return;

        // 2. 服务端规则检查：比赛状态、回合、卡组、冷却、费用都不能相信客户端
        if (!matchStarted || sender != currentTurn.Value) return;
        if (!TryGetCardData(skillType, out CardData cardData)) return;
        if (!playerLoadouts.TryGetValue(sender, out HashSet<SkillType> loadout) || !loadout.Contains(skillType))
        {
            Debug.LogWarning($"玩家 {sender} 尝试使用未携带的技能 {skillType}。");
            return;
        }
        if (GetServerCooldown(sender, skillType) > 0)
        {
            Debug.LogWarning($"玩家 {sender} 尝试使用冷却中的技能 {skillType}。");
            return;
        }
        if (cardData.needTarget && !grid.InBounds(x, y)) return;
        if (!HasEnoughEnergy(sender, cardData.energyCost)) return;

        // 3. 调用 SkillManager 执行逻辑
        bool success = skillManager.ExecuteSkill(skillType, sender, x, y);

        // 4. 效果成功后，由服务器统一扣费并登记冷却
        if (success)
        {
            ModifyEnergy(sender, -cardData.energyCost);
            SetServerCooldown(sender, skillType, cardData.cooldownTurns);
            ConfirmSkillCastClientRpc(sender, skillType, cardData.cooldownTurns);
            EndTurn();
        }
    }

    [ClientRpc]
    private void ConfirmSkillCastClientRpc(Cell.Owner owner, SkillType skillType, int cooldownTurns)
    {
        // 只有释放技能的那个玩家需要更新 UI
        if (GetLocalPlayerIdentity() == owner)
        {
            if (deckManager != null) 
            {
                // 1. 告诉 DeckManager 技能放出去了 (进入冷却 CD)
                deckManager.OnSkillCastSuccess(skillType, cooldownTurns);
                
                // 2. 顺便刷新一下所有卡的状态 (因为能量刚才肯定变了)
                deckManager.RefreshAllCardsState();
            }
        }
    }

    // 发送数据时的辅助包装函数
    public void SyncCell(Cell c)
    {
        SyncCellClientRpc(
            c.position.x, 
            c.position.y,
            (int)c.type, 
            c.number, 
            (int)c.owner, 
            c.revealed,
            c.hasTower, 
            (int)c.towerOwner,
            c.exploded, 
            c.unlockTurn,
            c.hasTrap,           
            (int)c.trapOwner,    
            c.isBedrock,         
            c.currentDurability,
            c.isTowerRevealed,
            c.jamTurns
        );
    }

    // 客户端接收数据的 RPC
    [ClientRpc]
    private void SyncCellClientRpc(int x, int y, int type, int num, int owner, bool rev, 
                                bool hasT, int tOwner, bool expl, int unlock,
                                bool trap, int trapOwner, bool bedrock, int dura, bool isTowerRev, int jamTurns)
    {
        if (grid.TryGetCell(x, y, out Cell c))
        {
            // 更新公共数据
            c.type = (Cell.Type)type;
            c.number = num;
            c.owner = (Cell.Owner)owner;
            c.revealed = rev;
            c.hasTower = hasT;
            c.towerOwner = (Cell.Owner)tOwner;
            c.exploded = expl;
            c.unlockTurn = unlock;
            c.hasTrap = trap;
            c.trapOwner = (Cell.Owner)trapOwner;
            c.isBedrock = bedrock;
            c.currentDurability = dura;
            c.isTowerRevealed = isTowerRev;
            c.jamTurns = jamTurns;

            // 【关键】由于 SyncCell 是公共同步，它不应该修改本地的 flaggedP1/P2。
            // 渲染时 Draw 会自动根据 localPlayer 身份读取本地保存的私有旗子。
            board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value);
        }
    }

    [ClientRpc]
    private void ResetBoardClientRpc()
    {
        grid = new CellGrid(width, height);
        board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value);
    }

    // -----------------------------------------------------------------------
    // 逻辑区域 (大部分只在服务器运行)
    // -----------------------------------------------------------------------

    private void SwitchTurn()
    {
        // 只有服务器能修改 NetworkVariable
        if (IsServer)
        {
            currentTurn.Value++; // 【新增】回合数 +1

            if (currentTurn.Value == Cell.Owner.PlayerA)
                currentTurn.Value = Cell.Owner.PlayerB;
            else
                currentTurn.Value = Cell.Owner.PlayerA;
        }
    }

public bool ProcessReveal(Cell.Owner player, Cell cell)
    {
        if (cell.owner == player) return false;

        // 如果地图还没生成，利用当前点击的格子作为“安全点”生成地图
        if (!mapGenerated)
        {
            Debug.Log($"初次点击 ({cell.position.x}, {cell.position.y})，开始生成地雷...");
            
            // 1. 生成地雷 (传入当前格子以避免在该位置生成雷)
            grid.GenerateMines(cell, mineCount);
            
            // 2. 计算周围数字
            grid.GenerateNumbers();
            
            // 3. 标记已生成
            mapGenerated = true;
            
            // 注意：此时 cell 里的数据(Type/Number)已经因为上面两步变了，
            // 接下来的逻辑会使用最新的数据进行判断。
        }

        // 1. 检查塔防 AOE (保留原逻辑)
        CheckAndApplyTowerDefense(player, cell);

        // 2. 【新增】检查人造陷阱 (如果这里有别人的陷阱)
        if (cell.hasTrap && cell.trapOwner != player)
        {
            cell.hasTrap = false;
            cell.trapOwner = Cell.Owner.None;
            
            // 扣血 10 点
            ModifyHP(player, damageTrap);             
            if (cell.trapOwner == Cell.Owner.PlayerA) trapCountP1--;
            else trapCountP2--;
            Debug.Log($"{player} 踩中了陷阱！受到伤害并触发了该格子。");
        }

        // 3. 抢地逻辑 (敌人已翻开的格子)
        if (cell.revealed && cell.owner != Cell.Owner.None)
        {
            if (cell.hasTower)
            {
                ModifyHP(player, damageTowerDirect); // 点塔扣血
                return true;
            }

            // 【新增】焦土耐久扣除
            cell.currentDurability--;
            
            if (cell.currentDurability <= 0)
            {
                // 变废墟
                cell.isBedrock = true;
                cell.owner = Cell.Owner.None;
                Debug.Log("土地崩坏！");
            }
            else
            {
                // 正常占领
                cell.owner = player;
                cell.unlockTurn = totalTurnCount.Value + cellCooldownRounds; // 设置冷却
                ModifyEnergy(player, gainCapture); // 加能量
            }
            SyncCell(cell);
            return true;
        }

        // 4. 开荒逻辑 (未知格子)
        if (!cell.revealed)
        {
            if (cell.type == Cell.Type.Mine)
            {
                // 踩雷
                cell.revealed = true;
                cell.exploded = true;
                cell.owner = player;
                ModifyHP(player, damageMine);
                SyncCell(cell);
                return true;
            }
            else if (cell.type == Cell.Type.Empty)
            {
                ExecuteFloodFillServer(cell, player);
            }
            else
            {
                // 普通数字格
                cell.revealed = true;
                cell.owner = player;
                cell.unlockTurn = totalTurnCount.Value + cellCooldownRounds;
                ModifyEnergy(player, gainNormal);
                SyncCell(cell);
            }
            return true;
        }
        return false;
    }

    private bool ProcessPlaceTrap(Cell.Owner player, Cell cell)
    {
        // 规则：必须是自己的地盘，且不能是雷，不能有塔，不能已经有陷阱
        if (cell.owner != player) return false;
        if (cell.type == Cell.Type.Mine) return false;
        if (cell.hasTower) return false;
        if (cell.hasTrap) return false;

        // 检查能量
        int currentE = (player == Cell.Owner.PlayerA) ? energyP1.Value : energyP2.Value;
        if (currentE < costPlaceTrap) return false;

        // 扣能量
        ModifyEnergy(player, -costPlaceTrap);

        // 设置陷阱
        cell.hasTrap = true;
        cell.trapOwner = player;
        
        Debug.Log("陷阱部署完毕。");
        SyncCell(cell);
        return true;
    }

    // 逻辑层：插旗
    private void Flag(Cell.Owner player, Cell cell)
    {
        // 已经翻开、有塔、已炸的不能插旗
        if (cell.revealed || cell.hasTower || cell.exploded) return;

        // 修改点：根据操作者身份切换对应的私有变量
        if (player == Cell.Owner.PlayerA) cell.flaggedP1 = !cell.flaggedP1;
        else if (player == Cell.Owner.PlayerB) cell.flaggedP2 = !cell.flaggedP2;

        // 注意：这里不要用 SyncCellToClients，
        // 应在 RequestFlagServerRpc 中使用定向的 ClientRpcParams 同步给个人
    }
    // 服务器端逻辑：建造防御塔
    public bool ProcessBuildTower(Cell.Owner player, Cell cell)
    {
        if (cell == null) return false;

        // 1. 【规则检查】被锁定的格子不能操作
        if (totalTurnCount.Value < cell.unlockTurn && cell.owner != player && cell.owner != Cell.Owner.None) 
        {
            Debug.Log("该格子处于战乱冷却期，无法操作！");
            return false;
        }
        // 2. 【规则检查】已有塔
        if (cell.hasTower) return false;

        // 3. 【规则检查】已知安全区不能建塔 (只能盲注或在己方雷上建)
        if (cell.revealed && cell.type != Cell.Type.Mine) return false;

        // 4. 【数量检查】
        int currentCount = (player == Cell.Owner.PlayerA) ? towerCountA : towerCountB;
        if (currentCount >= maxTowersPerPlayer)
        {
            Debug.Log($"塔数量已满 ({currentCount}/{maxTowersPerPlayer})");
            return false;
        }

        // 5. 【领地检查】(沿用你原版的对角线分割算法)
        long posValue = (long)cell.position.x * height + (long)cell.position.y * width;
        long threshold = (long)width * height;
        
        if (player == Cell.Owner.PlayerA && posValue >= threshold) {
            Debug.Log("不能在敌方半场建塔");
            return false;
        }
        else if (player == Cell.Owner.PlayerB && posValue <= threshold) {
            Debug.Log("不能在敌方半场建塔");
            return false;
        }

        // 6. 【判定结果】费用由 RequestSkillServerRpc 按 CardData 统一处理
        bool isMine = (cell.type == Cell.Type.Mine);
        bool canBuildOnExploded = cell.exploded && cell.owner == player;
        bool isHidden = !cell.revealed;

        if (isMine && (isHidden || canBuildOnExploded))
        {
            // --- 建造成功 ---
            cell.hasTower = true;
            cell.towerOwner = player;
            cell.owner = player;
            cell.revealed = true; 
            // 塔覆盖雷，视觉上不再是爆炸状态，而是塔
            cell.exploded = false; 

            if (player == Cell.Owner.PlayerA) towerCountA++;
            else towerCountB++;

            // 同步给所有人
            SyncCellToClients(cell);
            return true;
        }
        else
        {
            // --- 建造失败 (盲注错误) ---
            Debug.Log($"建塔失败！受到 {damageBuildFail} 点反噬伤害");
            ModifyHP(player, damageBuildFail);
            
            // 注意：建塔失败不翻开格子，只扣血。
            // 也不需要 SyncCellToClients，因为格子状态没变，只是血量变了（血量会自动同步）
            return true; // 依然算作消耗了一个回合
        }
    }

    // 辅助：打包数据发送同步
    private void SyncCellToClients(Cell cell)
    {
        SyncCellClientRpc(
            cell.position.x,
            cell.position.y,
            (int)cell.type,
            cell.number,
            (int)cell.owner,
            cell.revealed,
            cell.hasTower,
            (int)cell.towerOwner,
            cell.exploded,
            
            // --- 以下是新增参数，必须加上 ---
            cell.unlockTurn,       // 冷却解锁回合
            cell.hasTrap,          // 是否有陷阱
            (int)cell.trapOwner,   // 陷阱归属
            cell.isBedrock,        // 是否是废墟
            cell.currentDurability, // 当前耐久
            cell.isTowerRevealed,
            cell.jamTurns
        );
    }

    [ClientRpc]
    private void GameOverClientRpc(Cell.Owner winner)
    {
        // 1. 锁定游戏状态
        gameover = true;
        Debug.Log($"游戏结束！获胜者是: {winner}");
        
        // 2. 显示黑色背景板
        if (gameOverPanel != null) 
        {
            gameOverPanel.SetActive(true);
        }

        // 3. 设置胜者文字
        if (winnerText != null)
        {
            // 把枚举 PlayerA 转换成更好看的 "Player 1"
            string showName = (winner == Cell.Owner.PlayerA) ? "Player 1" : "Player 2";
            winnerText.text = "胜者: " + showName + "!";
        }
    }

    private void ExecuteFloodFillServer(Cell startCell, Cell.Owner player)
    {
        if (!IsServer) return;

        System.Collections.Generic.Queue<Cell> queue = new System.Collections.Generic.Queue<Cell>();
        System.Collections.Generic.List<CellSyncData> changedCells = new System.Collections.Generic.List<CellSyncData>();
        
        queue.Enqueue(startCell);

        while (queue.Count > 0)
        {
            Cell cell = queue.Dequeue();

            if (cell.revealed || cell.type == Cell.Type.Mine) continue;

            // 执行原有逻辑：翻开、分配所有权、增加能量
            cell.revealed = true;
            cell.owner = player;
            cell.unlockTurn = totalTurnCount.Value + cellCooldownRounds;
            // 只有开荒（非抢地）通过 FloodFill 触发时，应用正常的收益逻辑
            ModifyEnergy(player, gainNormal);

            // 记录变化坐标用于同步
            changedCells.Add(new CellSyncData { x = cell.position.x, y = cell.position.y,type = (int)cell.type,number = cell.number,unlockTurn = cell.unlockTurn });

            // 如果是空格，继续向四周扩散
            if (cell.type == Cell.Type.Empty)
            {
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        if (grid.TryGetCell(cell.position.x + x, cell.position.y + y, out Cell neighbor))
                        {
                            if (!neighbor.revealed && neighbor.type != Cell.Type.Mine)
                            {
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }
        }

        // 批量同步给所有客户端
        if (changedCells.Count > 0)
        {
            SyncFloodFillClientRpc(changedCells.ToArray(), player);
        }
    }

    [ClientRpc]
    private void SyncFloodFillClientRpc(CellSyncData[] cells, Cell.Owner player)
    {
        foreach (var data in cells)
        {
            if (grid.TryGetCell(data.x, data.y, out Cell c))
            {
                c.revealed = true;
                c.owner = player;
                c.type = (Cell.Type)data.type;
                c.number = data.number;
                c.unlockTurn = data.unlockTurn;
            }
        }
        // 一次性重绘，避免多次重绘造成的掉帧
        board.Draw(grid, GetLocalPlayerIdentity(), totalTurnCount.Value);
    }

    // -----------------------------------------------------------------------
    // 数值管理
    // -----------------------------------------------------------------------

    public void ModifyEnergy(Cell.Owner player, int amount)
    {
        if (player == Cell.Owner.PlayerA)
            energyP1.Value = Mathf.Clamp(energyP1.Value + amount, 0, maxEnergy);
        else
            energyP2.Value = Mathf.Clamp(energyP2.Value + amount, 0, maxEnergy);
    }

    public void ModifyHP(Cell.Owner player, int damage)
    {
        // 1. 如果是治疗 (damage < 0)，允许加血，不受无敌影响
        if (damage < 0)
        {
            // 保持原有的加血逻辑
            if (player == Cell.Owner.PlayerA) hpP1.Value = Mathf.Clamp(hpP1.Value - damage, 0, maxHealth);
            else hpP2.Value = Mathf.Clamp(hpP2.Value - damage, 0, maxHealth);
            return;
        }

        // 2. 【新增】如果是扣血 (damage > 0)，检查无敌状态
        int currentImmune = (player == Cell.Owner.PlayerA) ? immuneTurnsP1.Value : immuneTurnsP2.Value;
        if (currentImmune > 0)
        {
            Debug.Log($"{player} 处于无敌状态！免疫了 {damage} 点伤害！");
            return; // 直接跳出，不扣血
        }

        // 3. 正常扣血逻辑 (保持不变)
        if (player == Cell.Owner.PlayerA)
        {
            hpP1.Value = Mathf.Clamp(hpP1.Value - damage, 0, maxHealth);
            if (hpP1.Value <= 0) GameOver(Cell.Owner.PlayerB);
        }
        else
        {
            hpP2.Value = Mathf.Clamp(hpP2.Value - damage, 0, maxHealth);
            if (hpP2.Value <= 0) GameOver(Cell.Owner.PlayerA);
        }
    }

    private void GameOver(Cell.Owner winner)
    {
        if (gameover) return; // 状态锁：防止一回合内多次触发结算
        gameover = true;
        
        Debug.Log($"游戏结束！获胜者是: {winner}");
        GameOverClientRpc(winner);
    }

    // ----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // UI 更新 (客户端回调)
    // -----------------------------------------------------------------------

    private void UpdateTurnUI()
    {
        if (energyTextA && energyTextB) {
            bool isA = currentTurn.Value == Cell.Owner.PlayerA;
            energyTextA.color = isA ? activeTextColor : inactiveTextColor;
            energyTextB.color = !isA ? activeTextColor : inactiveTextColor;
        }
    }

    private void UpdateEnergyUI(Cell.Owner player, int value)
    {
        if (player == Cell.Owner.PlayerA) {
            if (energySliderA) energySliderA.value = value;
            if (energyTextA) energyTextA.text = $"P1 Energy: {value}/{maxEnergy}";
        } else {
            if (energySliderB) energySliderB.value = value;
            if (energyTextB) energyTextB.text = $"P2 Energy: {value}/{maxEnergy}";
        }
    }

    private void UpdateHealthUI(Cell.Owner player, int value)
    {
        // 1. 获取无敌回合数
        int immune = (player == Cell.Owner.PlayerA) ? immuneTurnsP1.Value : immuneTurnsP2.Value;
        
        // 2. 如果大于0，准备好黄色的[无敌]字样
        string statusSuffix = (immune > 0) ? " <color=yellow>[无敌]</color>" : "";

        if (player == Cell.Owner.PlayerA) {
            if (hpSliderA) hpSliderA.value = (float)value / maxHealth;
            
            // 3. 【关键修改】在字符串末尾加上 {statusSuffix}
            if (hpTextA) hpTextA.text = $"P1 Health: {value}/100{statusSuffix}";
        } else {
            if (hpSliderB) hpSliderB.value = (float)value / maxHealth;
            
            // 3. 【关键修改】同上
            if (hpTextB) hpTextB.text = $"P2 Health: {value}/100{statusSuffix}";
        }
    }

    // private void SetupCamera()
    // {
    //     // 1. 永远居中
    //     Vector3 centerPos = new Vector3(width / 2f, height / 2f, -10f);
    //     Camera.main.transform.position = centerPos;

    //     // 2. 基础大小 (基于高度)
    //     // OrthographicSize = 高度的一半。如果你的棋盘高度是 16，理论上 size = 8 刚好塞满。
    //     // 加一点边距 (比如 + 2)
    //     float targetSize = (height / 2f) + 1f; 

    //     // 3. 宽度适配 (防止两边被切掉)
    //     float screenRatio = (float)Screen.width / Screen.height; // 当前屏幕宽高比
    //     float boardRatio = (float)width / height;              // 棋盘宽高比 (32/16 = 2.0)

    //     // 如果屏幕比棋盘更“方”（比如手机竖屏，或者非宽屏），宽度就不够了
    //     // 这时要基于宽度来反推 Size
    //     if (screenRatio < boardRatio)
    //     {
    //         // 目标 Size = (宽度一半) / 屏幕比例
    //         targetSize = (width / 2f + 2f) / screenRatio;
    //     }

    //     Camera.main.orthographicSize = targetSize;
    // }

    // 检查并应用防御塔伤害 (AOE + 直接攻击)
    private int CheckAndApplyTowerDefense(Cell.Owner attacker, Cell targetCell)
    {
        int totalDamage = 0;
        Cell.Owner enemy = (attacker == Cell.Owner.PlayerA) ? Cell.Owner.PlayerB : Cell.Owner.PlayerA;

        // 1. 直接攻击敌方塔
        if (targetCell.hasTower && targetCell.towerOwner == enemy)
        {
            totalDamage += damageTowerDirect;
            if (!targetCell.isTowerRevealed) {
                targetCell.isTowerRevealed = true;
                SyncCell(targetCell); 
            }
            Debug.Log("攻击敌方塔！受到反伤！");
        }

        // 2. 踩在敌方塔的 AOE 范围内 (3x3)
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue; // 中心点上面已经算过了（或者不是塔）
                if (grid.TryGetCell(targetCell.position.x + x, targetCell.position.y + y, out Cell neighbor))
                {
                    // 如果旁边有敌人的塔
                    if (neighbor.hasTower && neighbor.towerOwner == enemy)
                    {
                        totalDamage += damageTowerAOE;
                        if (!neighbor.isTowerRevealed) {
                            neighbor.isTowerRevealed = true;
                            SyncCell(neighbor); 
                        }
                    }
                }
            }
        }

        // 3. 执行扣血
        if (totalDamage > 0)
        {
            ModifyHP(attacker, totalDamage);
        }

        return totalDamage;
    }

    // 【新增】供 SkillManager 调用
    public void RegisterJam(int x, int y, int turns)
    {
        if (grid.TryGetCell(x, y, out Cell cell))
        {
            cell.jamTurns = turns;
            
            // 记录到列表里 (防止重复添加)
            Vector2Int pos = new Vector2Int(x, y);
            if (!jammedCells.Contains(pos))
            {
                jammedCells.Add(pos);
            }

            // 立即同步一次
            SyncCell(cell);
        }
    }
    private void EndTurn()
    {
        // 1. 检查通路连通性 (若连通则造成伤害)
        if (CheckPathConnection(Cell.Owner.PlayerA))
        {
            Debug.Log("P1 通路连通！重创 P2！");
            ModifyHP(Cell.Owner.PlayerB, damageRoadConnected);
        }
        if (CheckPathConnection(Cell.Owner.PlayerB))
        {
            Debug.Log("P2 通路连通！重创 P1！");
            ModifyHP(Cell.Owner.PlayerA, damageRoadConnected);
        }

        if (IsServer)
        {
            for (int i = jammedCells.Count - 1; i >= 0; i--)
            {
                Vector2Int pos = jammedCells[i];
                Cell c = grid[pos.x, pos.y];

                if (c.jamTurns > 0)
                {
                    c.jamTurns--;
                    
                    // 如果减完归零了，说明效果结束，从名单里踢出去
                    if (c.jamTurns == 0)
                    {
                        jammedCells.RemoveAt(i);
                        Debug.Log($"({pos.x},{pos.y}) 的阻断效果结束了。");
                    }
                    
                    // 同步状态
                    SyncCell(c);
                }
                else
                {
                    // 防御性代码：如果已经是0了还在名单里，直接踢掉
                    jammedCells.RemoveAt(i);
                }
            }
            // 谁的回合结束了，谁的无敌时间就 -1
            if (currentTurn.Value == Cell.Owner.PlayerA)
            {
                if (immuneTurnsP1.Value > 0) immuneTurnsP1.Value--;
            }
            else
            {
                if (immuneTurnsP2.Value > 0) immuneTurnsP2.Value--;
            }
        }

        // 2. 切换回合
        totalTurnCount.Value++;
        
        if (currentTurn.Value == Cell.Owner.PlayerA)
            currentTurn.Value = Cell.Owner.PlayerB;
        else
            currentTurn.Value = Cell.Owner.PlayerA;

        ReduceServerCooldowns(currentTurn.Value);
    }

    private bool TryValidateLoadout(int[] selectedSkillIds, out HashSet<SkillType> loadout)
    {
        loadout = new HashSet<SkillType>();
        if (selectedSkillIds == null || prepManager == null) return false;
        if (selectedSkillIds.Length < 1 || selectedSkillIds.Length > prepManager.maxCards) return false;

        foreach (int skillId in selectedSkillIds)
        {
            SkillType skillType = (SkillType)skillId;
            if (skillType == SkillType.None || !TryGetCardData(skillType, out _)) return false;
            if (!loadout.Add(skillType)) return false;
        }

        return true;
    }

    private bool TryGetCardData(SkillType skillType, out CardData cardData)
    {
        cardData = null;
        if (prepManager == null || prepManager.allAvailableCards == null) return false;

        foreach (CardData candidate in prepManager.allAvailableCards)
        {
            if (candidate != null && candidate.skillType == skillType)
            {
                cardData = candidate;
                return true;
            }
        }

        return false;
    }

    private int GetServerCooldown(Cell.Owner player, SkillType skillType)
    {
        if (playerCooldowns.TryGetValue(player, out Dictionary<SkillType, int> cooldowns) &&
            cooldowns.TryGetValue(skillType, out int turns))
        {
            return turns;
        }

        return 0;
    }

    private void SetServerCooldown(Cell.Owner player, SkillType skillType, int turns)
    {
        if (!playerCooldowns.TryGetValue(player, out Dictionary<SkillType, int> cooldowns))
        {
            cooldowns = new Dictionary<SkillType, int>();
            playerCooldowns[player] = cooldowns;
        }

        cooldowns[skillType] = Mathf.Max(0, turns);
    }

    private void ReduceServerCooldowns(Cell.Owner player)
    {
        if (!playerCooldowns.TryGetValue(player, out Dictionary<SkillType, int> cooldowns)) return;

        foreach (SkillType skillType in new List<SkillType>(cooldowns.Keys))
        {
            cooldowns[skillType] = Mathf.Max(0, cooldowns[skillType] - 1);
        }
    }
    private bool CheckPathConnection(Cell.Owner player)
    {
        // 定义起点和终点
        Vector2Int startPos = (player == Cell.Owner.PlayerA) ? new Vector2Int(0, 0) : new Vector2Int(width - 1, height - 1);
        Vector2Int targetPos = (player == Cell.Owner.PlayerA) ? new Vector2Int(width - 1, height - 1) : new Vector2Int(0, 0);

        // 获取起点和终点的格子对象
        Cell startCell = grid[startPos.x, startPos.y];

        // --- 【修改 1】起点检查 ---
        // 如果起点不属于自己，或者起点正处于“瘫痪/阻断”状态，直接断开
        if (startCell.owner != player || startCell.jamTurns > 0) return false;

        // BFS 搜索初始化
        System.Collections.Generic.Queue<Vector2Int> queue = new System.Collections.Generic.Queue<Vector2Int>();
        System.Collections.Generic.HashSet<Vector2Int> visited = new System.Collections.Generic.HashSet<Vector2Int>();

        queue.Enqueue(startPos);
        visited.Add(startPos);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            // 检查上下左右
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            foreach (var dir in dirs)
            {
                Vector2Int next = current + dir;
                
                // 越界检查
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height) continue;
                
                // 访问检查
                if (visited.Contains(next)) continue;
                if (next == targetPos) return true;

                Cell neighbor = grid[next.x, next.y];

                // --- 【修改 3：核心逻辑】 ---
                // 必须满足三个条件才能传导信号：
                // 1. 是自己的领地
                // 2. 不是废墟/基岩
                // 3. 没有被阻断 (jamTurns 必须为 0)
                if (neighbor.owner == player && !neighbor.isBedrock && neighbor.jamTurns == 0)
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
        return false;
    }

    // 获取当前客户端是 P1 还是 P2
    public Cell.Owner GetLocalPlayerIdentity()
    {
        // 获取本地玩家的唯一 ID
        ulong myId = NetworkManager.Singleton.LocalClientId;
        
        return (myId == NetworkManager.ServerClientId) ? Cell.Owner.PlayerA : Cell.Owner.PlayerB;
    }

    private bool TryResolveNetworkPlayer(ulong clientId, out Cell.Owner player)
    {
        if (clientId == NetworkManager.ServerClientId)
        {
            player = Cell.Owner.PlayerA;
            return true;
        }

        if (playerBClientId.HasValue && clientId == playerBClientId.Value)
        {
            player = Cell.Owner.PlayerB;
            return true;
        }

        player = Cell.Owner.None;
        Debug.LogWarning($"忽略未登记客户端 {clientId} 的游戏请求。");
        return false;
    }

    // 检查能量是否足够
    public bool HasEnoughEnergy(Cell.Owner player, int amount) {
        int currentE = (player == Cell.Owner.PlayerA) ? energyP1.Value : energyP2.Value;
        return currentE >= amount;
    }

    // 获取陷阱数量
    public int GetTrapCount(Cell.Owner player) {
        return (player == Cell.Owner.PlayerA) ? trapCountP1 : trapCountP2;
    }

    // 增加陷阱计数
    public void IncrementTrapCount(Cell.Owner player) {
        if (player == Cell.Owner.PlayerA) trapCountP1++;
        else trapCountP2++;
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    public void E2ERequestReveal(int x, int y)
    {
        RequestActionServerRpc(x, y, "REVEAL");
    }

    public void E2ERequestSkill(SkillType skillType, int x, int y)
    {
        RequestSkillServerRpc(skillType, x, y);
    }

    public int E2EGetEnergy(Cell.Owner player)
    {
        return player == Cell.Owner.PlayerA ? energyP1.Value : energyP2.Value;
    }
#endif

}
