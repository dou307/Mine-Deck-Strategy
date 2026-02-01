using System.Collections;
using UnityEngine;
using TMPro;
using Unity.Netcode; // 1. 引入命名空间

// 2. 改为继承 NetworkBehaviour
[DefaultExecutionOrder(-1)]
public class Game : NetworkBehaviour
{
    [Header("回合制设置")]
    // 3. 使用 NetworkVariable 同步回合所有权
    // NetworkVariable 只能被服务器修改，客户端会自动监听变化
    public NetworkVariable<Cell.Owner> netCurrentTurn = new NetworkVariable<Cell.Owner>(Cell.Owner.PlayerA);
    public NetworkVariable<int> netTurnCount = new NetworkVariable<int>(1);
    
    public Color activeTextColor = Color.white;
    public Color inactiveTextColor = Color.gray;

    [Header("地图设置")]
    public int width = 32;
    public int height = 16;
    public int mineCount = 100;

    public float defaultCameraSize = 10f;

    [Header("血量系统")]
    public int maxHealth = 100;
    public int damagePerMine = 5;
    public int damageTowerDirect = 10;
    public int damageTowerAOE = 3;

    // 4. 使用 NetworkVariable 同步血量
    public NetworkVariable<int> netHealthA = new NetworkVariable<int>(100);
    public NetworkVariable<int> netHealthB = new NetworkVariable<int>(100);

    public UnityEngine.UI.Slider hpSliderA;
    public TMPro.TextMeshProUGUI hpTextA;
    public UnityEngine.UI.Slider hpSliderB;
    public TMPro.TextMeshProUGUI hpTextB;

    [Header("能量系统")]
    // 5. 使用 NetworkVariable 同步能量
    public NetworkVariable<int> netEnergyA = new NetworkVariable<int>(0);
    public NetworkVariable<int> netEnergyB = new NetworkVariable<int>(0);

    public UnityEngine.UI.Slider energySliderA;
    public TMPro.TextMeshProUGUI energyTextA;
    public UnityEngine.UI.Slider energySliderB;
    public TMPro.TextMeshProUGUI energyTextB;

    [Header("能量通用设置")]
    public int maxEnergy = 100;
    public int energyPerCell = 5;
    public int buildTowerCost = 5;
    public int buildTowerPenaltyDamage = 3;

    [Header("竞技设置")]
    public int maxTowersPerPlayer = 10;
    private int towerCountA = 0;
    private int towerCountB = 0;

    public float occupationLockTime = 3.0f;

    private Board board;
    private CellGrid grid; // 服务器拥有权威 Grid，客户端拥有“影子”Grid
    private bool gameover;
    private bool generated;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        board = GetComponentInChildren<Board>();
    }

    // 6. 替代 Start()，这是网络对象的初始化入口
    public override void OnNetworkSpawn()
    {
        // 只有服务器负责初始化真正的游戏逻辑
        if (IsServer)
        {
            NewGame();
        }
        else
        {
            // 客户端也要初始化一个空的 Grid 用来接收显示数据
            grid = new CellGrid(width, height);
            // 客户端第一次连进来，请求服务器发一份完整数据（可选，这里先省略，依靠后续同步）
        }

        // 7. 绑定变量变化回调 (当数值变化时，自动刷新 UI)
        netCurrentTurn.OnValueChanged += (oldVal, newVal) => UpdateTurnUI();
        
        netEnergyA.OnValueChanged += (oldVal, newVal) => UpdateEnergyUI(Cell.Owner.PlayerA, newVal);
        netEnergyB.OnValueChanged += (oldVal, newVal) => UpdateEnergyUI(Cell.Owner.PlayerB, newVal);
        
        netHealthA.OnValueChanged += (oldVal, newVal) => UpdateHealthUI(Cell.Owner.PlayerA, newVal);
        netHealthB.OnValueChanged += (oldVal, newVal) => UpdateHealthUI(Cell.Owner.PlayerB, newVal);

        // 强制刷新一次 UI
        UpdateTurnUI();

        netTurnCount.OnValueChanged += (oldVal, newVal) => {
            if (grid != null) {
                board.Draw(grid, GetLocalPlayerIdentity(), newVal);
            }
        };

        SetupCamera();
    }

    private void NewGame()
    {
        if (!IsServer) return; // 只有服务器能重置游戏

        StopAllCoroutines();
        gameover = false;
        generated = false;
        netTurnCount.Value = 1;

        grid = new CellGrid(width, height);
        
        // 服务器初始化完，通知所有客户端“重画格子”
        // 这里简化处理：因为刚开始都是空的，所以客户端 new CellGrid 就行了
        
        towerCountA = 0;
        towerCountB = 0;

        // 修改 NetworkVariable
        netEnergyA.Value = 0;
        netEnergyB.Value = 0;
        netHealthA.Value = maxHealth;
        netHealthB.Value = maxHealth;
        netCurrentTurn.Value = Cell.Owner.PlayerA;

        Debug.Log("服务器：新对局已开始");
        
        // 通知客户端重置画面
        ResetBoardClientRpc();
    }

    private void Update()
    {
        // 如果没有连接网络，不运行
        if (!IsSpawned) return; 

        if (gameover)
        {
            // 只有服务器能按 R 重开
            if (IsServer && Input.GetKeyDown(KeyCode.R)) NewGame();
            return;
        }

        UpdateCellTimers(); // 计时器逻辑（服务器端跑就行，客户端 visuals 可以不跑）

        // 8. 核心修改：输入检测
        // 我们这里做一个简单约定：Host (主机) 永远是 PlayerA，Client (加入者) 永远是 PlayerB
        // 以后可以用 Player ID 系统做得更高级
        
        if (IsHost) 
        {
            HandleInput(Cell.Owner.PlayerA);
        }
        else if (IsClient) // 非 Host 的 Client
        {
            HandleInput(Cell.Owner.PlayerB);
        }
    }

    // 客户端运行：检测输入 -> 发送请求给服务器
    private void HandleInput(Cell.Owner myIdentity)
    {
        if (netCurrentTurn.Value != myIdentity) return;

        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int cellPos = board.tilemap.WorldToCell(worldPosition);

        // 越界检查
        if (cellPos.x < 0 || cellPos.x >= width || cellPos.y < 0 || cellPos.y >= height) return;

        // --- 1. 左键翻开 ---
        if (Input.GetMouseButtonDown(0)) 
        {
            RequestRevealServerRpc(cellPos.x, cellPos.y);
        }
        // --- 2. 右键插旗 ---
        else if (Input.GetMouseButtonDown(1)) 
        {
            RequestFlagServerRpc(cellPos.x, cellPos.y);
        }
        // --- 3. T键建塔 ---
        else if (Input.GetKeyDown(KeyCode.T)) 
        {
            RequestBuildTowerServerRpc(cellPos.x, cellPos.y);
        }
        // --- 4. 【新增】中键/双击预览 (只在本地显示，不发包) ---
        else if (Input.GetMouseButton(2)) 
        {
            // 获取本地格子的数据进行预览
            if(grid.TryGetCell(cellPos.x, cellPos.y, out Cell cell))
            {
                ChordLocal(cell); // 下面会写这个本地方法
            }
        }
        // --- 5. 【新增】中键/双击抬起 (发送请求) ---
        else if (Input.GetMouseButtonUp(2)) 
        {
            // 先清除本地预览效果
            ClearAllChordedFlagsLocal();
            // 发送请求给服务器
            RequestUnchordServerRpc(cellPos.x, cellPos.y);
        }
    }

    // 纯客户端视觉：高亮周围
    private void ChordLocal(Cell center)
    {
        // 先重置一遍，防止残留
        ClearAllChordedFlagsLocal();

        if (!center.revealed || center.type != Cell.Type.Number) return;

        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                int nx = center.position.x + x;
                int ny = center.position.y + y;
                if (grid.TryGetCell(nx, ny, out Cell c)) {
                    if (!c.revealed && !c.flagged && !c.hasTower) {
                        c.chorded = true; // 临时标记
                    }
                }
            }
        }
        board.Draw(grid, GetLocalPlayerIdentity(), netTurnCount.Value); // 客户端重绘
    }

    private void ClearAllChordedFlagsLocal()
    {
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                grid[x, y].chorded = false;
            }
        }
        board.Draw(grid, GetLocalPlayerIdentity(), netTurnCount.Value);
    }

    // -----------------------------------------------------------------------
    // RPC 区域：网络通信的核心
    // -----------------------------------------------------------------------

    // [ServerRpc]：客户端调用，服务器执行
    [ServerRpc(RequireOwnership = false)] // 允许任何客户端调用
    private void RequestRevealServerRpc(int x, int y, ServerRpcParams rpcParams = default)
    {
        // 1. 身份验证：是谁发的？
        bool isSenderHost = rpcParams.Receive.SenderClientId == NetworkManager.Singleton.LocalClientId;
        Cell.Owner sender = isSenderHost ? Cell.Owner.PlayerA : Cell.Owner.PlayerB;

        // 2. 规则验证
        if (sender != netCurrentTurn.Value) return; // 没轮到你

        // 3. 执行逻辑 (操作服务器的权威 Grid)
        if (grid.TryGetCell(x, y, out Cell cell))
        {
            if (Reveal(sender, cell))
            {
                SwitchTurn(); // 逻辑内部如果返回 true，说明耗费了回合
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestFlagServerRpc(int x, int y, ServerRpcParams rpcParams = default)
    {
        bool isSenderHost = rpcParams.Receive.SenderClientId == NetworkManager.Singleton.LocalClientId;
        Cell.Owner sender = isSenderHost ? Cell.Owner.PlayerA : Cell.Owner.PlayerB;

        if (grid.TryGetCell(x, y, out Cell cell))
        {
            Flag(sender, cell);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBuildTowerServerRpc(int x, int y, ServerRpcParams rpcParams = default)
    {
        bool isSenderHost = rpcParams.Receive.SenderClientId == NetworkManager.Singleton.LocalClientId;
        Cell.Owner sender = isSenderHost ? Cell.Owner.PlayerA : Cell.Owner.PlayerB;
        if (sender != netCurrentTurn.Value) return;

        if (grid.TryGetCell(x, y, out Cell cell))
        {
            if (BuildTower(sender, cell))
            {
                SwitchTurn();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestUnchordServerRpc(int x, int y, ServerRpcParams rpcParams = default)
    {
        bool isSenderHost = rpcParams.Receive.SenderClientId == NetworkManager.Singleton.LocalClientId;
        Cell.Owner sender = isSenderHost ? Cell.Owner.PlayerA : Cell.Owner.PlayerB;
        
        if (sender != netCurrentTurn.Value) return;

        if (grid.TryGetCell(x, y, out Cell cell))
        {
            // 调用核心逻辑 Unchord
            if (Unchord(sender, cell)) 
            {
                // 如果 Unchord 成功触发了翻开，SwitchTurn 会在 Reveal 里被调用吗？
                // 不会，Reveal 里只有翻开单个雷才切换回合。
                // 按照你之前的逻辑，Unchord 只要翻开了东西就算一回合。
                SwitchTurn();
            }
        }
    }

    // [ClientRpc]：服务器调用，所有客户端（包括 Host 自己）执行
    // 用来同步画面
    [ClientRpc]
    private void SyncCellClientRpc(int x, int y, int typeInt, int number, int ownerInt, bool revealed, bool flagged, bool hasTower, int towerOwnerInt, bool exploded)
    {
        // 客户端接收数据，更新本地的“影子”Grid
        if (grid.TryGetCell(x, y, out Cell cell))
        {
            cell.type = (Cell.Type)typeInt;
            cell.number = number;
            cell.owner = (Cell.Owner)ownerInt;
            cell.revealed = revealed;
            cell.flagged = flagged;
            cell.hasTower = hasTower;
            cell.towerOwner = (Cell.Owner)towerOwnerInt;
            cell.exploded = exploded;

            // 只有这个格子需要重绘，不需要重绘整个 Board (优化性能)
            // 但你的 Board.Draw 目前是重绘全部的，为了简单我们暂时还是重绘全部，或者你可以去 Board.cs 加一个 DrawSingleCell
            board.Draw(grid, GetLocalPlayerIdentity(), netTurnCount.Value);
        }
    }

    [ClientRpc]
    private void ResetBoardClientRpc()
    {
        grid = new CellGrid(width, height);
        board.Draw(grid, GetLocalPlayerIdentity(), netTurnCount.Value);
    }

    // -----------------------------------------------------------------------
    // 逻辑区域 (大部分只在服务器运行)
    // -----------------------------------------------------------------------

    private void SwitchTurn()
    {
        // 只有服务器能修改 NetworkVariable
        if (IsServer)
        {
            netTurnCount.Value++; // 【新增】回合数 +1

            if (netCurrentTurn.Value == Cell.Owner.PlayerA)
                netCurrentTurn.Value = Cell.Owner.PlayerB;
            else
                netCurrentTurn.Value = Cell.Owner.PlayerA;
        }
    }

    private bool Reveal(Cell.Owner player, Cell cell)
    {
        if (cell == null) return false;

        // 锁定检查
        if (cell.lockTimer > 0 && cell.owner != player && cell.owner != Cell.Owner.None) return false;

        bool isDirectEnemyTower = (cell.hasTower && cell.towerOwner != player);
        bool isCapturable = (cell.owner != Cell.Owner.None && cell.owner != player);
        bool isHidden = !cell.revealed;

        // 误触保护：已翻开且无利可图的格子，点了不算回合
        if (!isHidden && !isCapturable && !isDirectEnemyTower) return false;

        // --- 【新增】防御塔反伤计算 ---
        int defenseDamage = CheckAndApplyTowerDefense(player, cell);

        // 如果直接点了敌人的塔，扣了血就算回合结束，不需要继续翻开了
        if (isDirectEnemyTower)
        {
            return true; 
        }

        // 己方塔不能点
        if (cell.hasTower && cell.towerOwner == player) return false;

        // 地图生成 (First Click)
        if (!generated) {
            grid.GenerateMines(cell, mineCount);
            grid.GenerateNumbers();
            generated = true;
        }

        bool actionTaken = false;

        // 正常的翻开/入侵逻辑
        if (isCapturable) {
            CaptureCell(player, cell);
            actionTaken = true;
        } else {
            actionTaken = ExecuteReveal(player, cell);
        }

        // 如果受到了防御塔伤害，哪怕 ExecuteReveal 没翻开新东西（比如重复点），也强制算作有效回合
        if (defenseDamage > 0) actionTaken = true;

        // 只需要在状态改变时同步，如果只是扣血(defenseDamage)，血量变量会自动同步，这里不用 Sync
        // 但如果 CaptureCell 或 ExecuteReveal 改变了格子，它们内部需要 Sync
        
        return actionTaken;
    }

    // 执行具体的翻开逻辑 (服务器端)
    private bool ExecuteReveal(Cell.Owner player, Cell cell)
    {
        // 1. 基础检查：已经翻开或插旗的不能翻
        if (cell.revealed) return false;
        if (cell.flagged) return false;

        // 2. 情况 A：踩雷
        if (cell.type == Cell.Type.Mine)
        {
            cell.revealed = true;
            cell.exploded = true; // 标记爆炸
            cell.owner = player;  // 归属变更为踩雷者
            
            // 扣血
            TakeDamage(player, damagePerMine);
            
            // 同步数据给所有客户端
            SyncCellToClients(cell);
            
            return true; // 这是一个有效动作（消耗回合）
        }

        // 3. 情况 B：点到空地 -> 触发泛洪 (Flood)
        if (cell.type == Cell.Type.Empty)
        {
            // 开启协程进行扩散
            // 注意：Flood 协程内部每翻开一个格子，都要调用 SyncCellToClients
            StartCoroutine(Flood(cell, player));
            return true;
        }
        
        // 4. 情况 C：点到数字 -> 正常占领
        else
        {
            cell.revealed = true;
            cell.owner = player;
            cell.lockTimer = occupationLockTime; // 设置占领锁定时间
            
            // 加能量
            AddEnergy(player, energyPerCell);
            
            // 同步数据
            SyncCellToClients(cell);
            
            return true;
        }
    }

    private void CaptureCell(Cell.Owner player, Cell cell)
    {
        cell.owner = player;
        cell.lockTimer = occupationLockTime;
        // 记得同步！
        SyncCellToClients(cell); 
    }
    
    // 逻辑层：插旗
    private void Flag(Cell.Owner player, Cell cell)
    {
        if (cell.revealed) return;
        cell.flagged = !cell.flagged;
        SyncCellToClients(cell); // 同步
    }

    // 服务器端逻辑：建造防御塔
    private bool BuildTower(Cell.Owner player, Cell cell)
    {
        if (cell == null) return false;

        // 1. 【规则检查】被锁定的格子不能操作
        if (cell.lockTimer > 0 && cell.owner != player && cell.owner != Cell.Owner.None) return false;

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

        // 6. 【能量检查】
        int currentEnergy = (player == Cell.Owner.PlayerA) ? netEnergyA.Value : netEnergyB.Value;
        if (currentEnergy < buildTowerCost) return false;

        // --- 扣除消耗 ---
        AddEnergy(player, -buildTowerCost);

        // 7. 【判定结果】
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
            Debug.Log($"建塔失败！受到 {buildTowerPenaltyDamage} 点反噬伤害");
            TakeDamage(player, buildTowerPenaltyDamage);
            
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
            cell.flagged,
            cell.hasTower,
            (int)cell.towerOwner,
            cell.exploded
        );
    }

    // 服务端逻辑
    private bool Unchord(Cell.Owner player, Cell center)
    {
        if (center == null || !center.revealed || center.type != Cell.Type.Number) return false;

        // 检查周围旗子数量是否达标
        if (CountAdjacentFlagsAndTowers(center) >= center.number)
        {
            bool anyChange = false;
            // 遍历周围
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    if (x == 0 && y == 0) continue;
                    
                    if (grid.TryGetCell(center.position.x + x, center.position.y + y, out Cell neighbor)) {
                        // 递归调用 Reveal，只要有一个成功，就算动作有效
                        // 注意：这里调用的是服务端的 Reveal
                        if (Reveal(player, neighbor)) {
                            anyChange = true;
                        }
                    }
                }
            }
            return anyChange;
        }
        return false;
    }
    

    
    // 辅助函数：计算周围旗子和塔
    private int CountAdjacentFlagsAndTowers(Cell cell)
    {
        int count = 0;
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                if (x == 0 && y == 0) continue;
                if (grid.TryGetCell(cell.position.x + x, cell.position.y + y, out Cell neighbor)) {
                    if ((!neighbor.revealed && neighbor.flagged) || neighbor.hasTower) {
                        count++;
                    }
                }
            }
        }
        return count;
    }

    [ClientRpc]
    private void GameOverClientRpc(Cell.Owner winner)
    {
        gameover = true;
        Debug.Log($"游戏结束！获胜者是: {winner}");
        
        // 这里你可以做更复杂的 UI，比如弹出一个 Panel 显示胜利者
        // 比如：uiManager.ShowWinPanel(winner);
        
        // 简单起见，先把所有雷显示出来（给输家看个明白）
        // 注意：这是客户端本地显示，服务器不需要再发包了，因为游戏都结束了
        // 或者你可以请求服务器 RevealAllMines
    }

    // 泛洪算法 (需要改为服务器端协程)
    private IEnumerator Flood(Cell cell, Cell.Owner player)
    {
        if (gameover || cell.revealed || cell.flagged || cell.type == Cell.Type.Mine) yield break;

        cell.revealed = true;
        cell.owner = player;
        AddEnergy(player, energyPerCell);
        
        // 每次变动都同步
        SyncCellToClients(cell);
        
        yield return new WaitForSeconds(0.01f); // 服务器端可以稍微快点

        if (cell.type == Cell.Type.Empty)
        {
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    if (x == 0 && y == 0) continue;
                    if (grid.TryGetCell(cell.position.x + x, cell.position.y + y, out Cell neighbor)) {
                        if (!neighbor.revealed) yield return Flood(neighbor, player);
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // 数值管理
    // -----------------------------------------------------------------------

    public void AddEnergy(Cell.Owner player, int amount)
    {
        if (player == Cell.Owner.PlayerA)
            netEnergyA.Value = Mathf.Clamp(netEnergyA.Value + amount, 0, maxEnergy);
        else
            netEnergyB.Value = Mathf.Clamp(netEnergyB.Value + amount, 0, maxEnergy);
    }

    private void TakeDamage(Cell.Owner player, int amount)
    {
        if (player == Cell.Owner.PlayerA)
        {
            netHealthA.Value -= amount;
            if (netHealthA.Value <= 0) {
                netHealthA.Value = 0;
                GameOver(Cell.Owner.PlayerB); // P1 死了，P2 赢
            }
        }
        else
        {
            netHealthB.Value -= amount;
            if (netHealthB.Value <= 0) {
                netHealthB.Value = 0;
                GameOver(Cell.Owner.PlayerA); // P2 死了，P1 赢
            }
        }
    }

    private void GameOver(Cell.Owner winner)
    {
        gameover = true;
        // 通知所有客户端
        GameOverClientRpc(winner);
    }

    // ----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // UI 更新 (客户端回调)
    // -----------------------------------------------------------------------

    private void UpdateTurnUI()
    {
        // 根据 netCurrentTurn.Value 变色
        if (energyTextA && energyTextB) {
            bool isA = netCurrentTurn.Value == Cell.Owner.PlayerA;
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
        if (player == Cell.Owner.PlayerA) {
            if (hpSliderA) hpSliderA.value = (float)value / maxHealth;
            if (hpTextA) hpTextA.text = $"HP: {value}";
        } else {
            if (hpSliderB) hpSliderB.value = (float)value / maxHealth;
            if (hpTextB) hpTextB.text = $"HP: {value}";
        }
    }
    
    private void UpdateCellTimers() {
        // 服务器端简单的计时器更新，实际可以通过 NetworkTime 优化，暂时先跑通
        if (!IsServer) return;
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                if (grid[x, y].lockTimer > 0) grid[x, y].lockTimer -= Time.deltaTime;
            }
        }
    }

private void SetupCamera()
{
    // 1. 居中逻辑不变
    Vector3 centerPos = new Vector3(width / 2f - 0.5f, height / 2f - 0.5f, -10f);
    Camera.main.transform.position = centerPos;

    // 2. 【修改这里】不再自动计算 (height / 2f) + 2f，而是直接用你设置的变量
    float targetSize = defaultCameraSize; 
    
    // 3. 保留宽屏适配逻辑（防止手机竖屏时两边显示不全）
    // 如果屏幕特别窄，代码会自动在你的 defaultCameraSize 基础上再放大一点
    float screenRatio = (float)Screen.width / Screen.height;
    float targetRatio = (float)width / height;
    
    if (screenRatio < targetRatio)
    {
        // 以宽度为基准反推 Size
        targetSize = (width / 2f + 1f) / screenRatio;
    }

    Camera.main.orthographicSize = targetSize;
}

    // 检查并应用防御塔伤害 (AOE + 直接攻击)
    private int CheckAndApplyTowerDefense(Cell.Owner attacker, Cell targetCell)
    {
        int totalDamage = 0;
        Cell.Owner enemy = (attacker == Cell.Owner.PlayerA) ? Cell.Owner.PlayerB : Cell.Owner.PlayerA;

        // 1. 直接攻击敌方塔
        if (targetCell.hasTower && targetCell.towerOwner == enemy)
        {
            totalDamage += damageTowerDirect;
            Debug.Log("攻击敌方塔！受到反伤！");
        }

        // 2. 踩在敌方塔的 AOE 范围内 (3x3)
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue; // 中心点上面已经算过了（或者不是塔）

                int checkX = targetCell.position.x + x;
                int checkY = targetCell.position.y + y;

                if (grid.TryGetCell(checkX, checkY, out Cell neighbor))
                {
                    // 如果旁边有敌人的塔
                    if (neighbor.hasTower && neighbor.towerOwner == enemy)
                    {
                        totalDamage += damageTowerAOE;
                    }
                }
            }
        }

        // 3. 执行扣血
        if (totalDamage > 0)
        {
            TakeDamage(attacker, totalDamage);
        }

        return totalDamage;
    }

    // 获取当前客户端是 P1 还是 P2
    private Cell.Owner GetLocalPlayerIdentity()
    {
        if (IsHost) return Cell.Owner.PlayerA;
        if (IsClient) return Cell.Owner.PlayerB; // 注意：Host 也是 Client，但 Host 判定优先
        return Cell.Owner.None;
    }

}