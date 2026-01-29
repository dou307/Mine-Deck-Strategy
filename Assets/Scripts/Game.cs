using System.Collections;
using UnityEngine;
using TMPro;

[DefaultExecutionOrder(-1)]
public class Game : MonoBehaviour
{

    [Header("回合制设置")]
    public Cell.Owner currentTurn = Cell.Owner.PlayerA; // 记录当前是谁的回合
    public Color activeTextColor = Color.white;         // 当前回合玩家名字颜色
    public Color inactiveTextColor = Color.gray;        // 等待中玩家名字颜色


    [Header("地图设置")]
    public int width = 32;
    public int height = 16;
    public int mineCount = 100;

     [Header("血量系统")]
    public int maxHealth = 100;
    public int damagePerMine = 5;
    
    // 玩家 A 血量
    public int healthA = 100;
    public UnityEngine.UI.Slider hpSliderA;
    public TMPro.TextMeshProUGUI hpTextA;

    // 玩家 B 血量
    public int healthB = 100;
    public UnityEngine.UI.Slider hpSliderB;
    public TMPro.TextMeshProUGUI hpTextB;

    [Header("能量系统 - 玩家 A (红)")]
    public int energyA = 0;
    public UnityEngine.UI.Slider energySliderA;
    public TMPro.TextMeshProUGUI energyTextA;

    [Header("能量系统 - 玩家 B (蓝)")]
    public int energyB = 0;
    public UnityEngine.UI.Slider energySliderB;
    public TMPro.TextMeshProUGUI energyTextB;

    [Header("能量通用设置")]
    public int maxEnergy = 100;
    public int energyPerCell = 5;

    public int buildTowerCost = 5; 

    public int buildTowerPenaltyDamage = 3;

    [Header("竞技设置")]

    public int maxTowersPerPlayer = 10; 
    
    // --- 新增：内部计数器 ---
    private int towerCountA = 0;
    private int towerCountB = 0;

    public Vector2Int p2CursorPos = new Vector2Int(31, 15); // P2 初始位置（比如右上角）
    public Transform p2CursorVisual; // 在编辑器里拖入一个高亮方框，显示P2在哪
    public float occupationLockTime = 3.0f; // 领地占领锁定时间

    public float moveRepeatRate = 0.15f; // 长按时每隔多少秒移动一格
    private float _nextMoveTime = 0f;    // 计时器

    private Board board;
    private CellGrid grid;
    private bool gameover;
    private bool generated;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        board = GetComponentInChildren<Board>();
    }

    private void Start() => NewGame();

    private void NewGame()
{
    StopAllCoroutines();

    // 2. 根据棋盘宽高将摄像机居中
    Camera.main.transform.position = new Vector3(width / 2f, height / 2f, -10f);

    // 3. 重置游戏基本状态
    gameover = false;
    generated = false; // 标记地雷尚未生成，等待第一下点击

    // 4. 创建新的逻辑网格并让渲染层重绘
    grid = new CellGrid(width, height);
    board.Draw(grid);

    towerCountA = 0;
    towerCountB = 0;

    // 5. 【核心修改】重置双方玩家的能量值
    energyA = 0;
    energyB = 0;

    // 6. 强制刷新双方的 UI（显示为 0/100）
    // 传入 0 是因为 AddEnergy 内部封装了刷新 UI 的逻辑
    AddEnergy(Cell.Owner.PlayerA, 0);
    AddEnergy(Cell.Owner.PlayerB, 0);

    healthA = maxHealth;
    healthB = maxHealth;
    UpdateHealthUI(Cell.Owner.PlayerA);
    UpdateHealthUI(Cell.Owner.PlayerB);

     p2CursorPos = new Vector2Int(width - 1, height - 1); 

    if (p2CursorVisual != null) {
        Vector3 worldPos = board.tilemap.CellToWorld((Vector3Int)p2CursorPos);
        p2CursorVisual.position = worldPos + new Vector3(0.5f, 0.5f, 0);
    }

    // --- 新增代码：重置回合 ---
    currentTurn = Cell.Owner.PlayerA;
    UpdateTurnUI(); // 刷新一下文字颜色
    
    Debug.Log("新对局已开始：能量已清空，双方血量已重置，基地准备就绪。");
}

private void Update()
{
    if (gameover) {
        if (Input.GetKeyDown(KeyCode.R)) NewGame();
        return;
    }

    UpdateCellTimers();

    // 只需要调用这两个重构好的函数
    HandleP1MouseInput(Cell.Owner.PlayerA);
    HandleP2KeyboardInput(Cell.Owner.PlayerB);

    if (Input.GetKeyDown(KeyCode.N)) NewGame();
}

private void SwitchTurn()
{
    // 1. 切换枚举
    if (currentTurn == Cell.Owner.PlayerA)
    {
        currentTurn = Cell.Owner.PlayerB;
    }
    else
    {
        currentTurn = Cell.Owner.PlayerA;
    }

    // 2. 更新 UI 显示 (让当前玩家的名字变亮，另一方变暗)
    UpdateTurnUI();
}

private void UpdateTurnUI()
{
    if (energyTextA && energyTextB)
    {
        if (currentTurn == Cell.Owner.PlayerA)
        {
            energyTextA.color = activeTextColor;
            energyTextB.color = inactiveTextColor;
        }
        else
        {
            energyTextA.color = inactiveTextColor;
            energyTextB.color = activeTextColor;
        }
    }
}
private void HandleP2Movement()
{
    Vector2Int move = Vector2Int.zero;
    if (Input.GetKeyDown(KeyCode.W)) move.y += 1;
    if (Input.GetKeyDown(KeyCode.S)) move.y -= 1;
    if (Input.GetKeyDown(KeyCode.A)) move.x -= 1;
    if (Input.GetKeyDown(KeyCode.D)) move.x += 1;

    if (move != Vector2Int.zero)
    {
        p2CursorPos.x = Mathf.Clamp(p2CursorPos.x + move.x, 0, width - 1);
        p2CursorPos.y = Mathf.Clamp(p2CursorPos.y + move.y, 0, height - 1);
    }
}

private Cell GetMouseCell()
{
    Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    Vector3Int cellPosition = board.tilemap.WorldToCell(worldPosition);
    grid.TryGetCell(cellPosition.x, cellPosition.y, out Cell cell);
    return cell;
}

#region 输入处理 (Input Handling)

    private void HandleP1MouseInput(Cell.Owner player)
    {
        if (currentTurn != player) return;// 如果不是 P1 的回合，直接退出，禁止操作

        Cell targetCell = GetMouseCell(); // 获取鼠标指向的格子
        if (targetCell == null) return;

        if (Input.GetMouseButtonDown(0)) {
        // 尝试翻开，如果成功翻开/占领，内部会切换回合
            if (Reveal(player, targetCell)) SwitchTurn(); 
        } else if (Input.GetMouseButtonDown(1)) {
            Flag(player, targetCell);
        } else if (Input.GetMouseButton(2)) {
            Chord(targetCell); // 预览不消耗回合
        } else if (Input.GetMouseButtonUp(2)) {
            if (Unchord(player, targetCell)) SwitchTurn();
        }
        else if (Input.GetKeyDown(KeyCode.T)) {
            if (BuildTower(player, targetCell)) SwitchTurn();
        }
    }

    // 玩家 2：完全基于键盘，传入 WASD 选中的格子
    private void HandleP2KeyboardInput(Cell.Owner player)
    {
        // 1. 允许 P2 随时移动光标 (保持原有移动逻辑)
        HandleP2CursorMovement();

        // 更新光标视觉位置
        if (p2CursorVisual != null) {
            Vector3 worldPos = board.tilemap.CellToWorld((Vector3Int)p2CursorPos);
            p2CursorVisual.position = worldPos + new Vector3(0.5f, 0.5f, 0);
        }

        // 【新增】动作拦截：如果不是 P2 的回合，不处理按键动作
        if (currentTurn != player) return;

        Cell targetCell = grid.GetCell(p2CursorPos.x, p2CursorPos.y);
        if (targetCell == null) return;

        if (Input.GetKeyDown(KeyCode.Space)) {
            if (Reveal(player, targetCell)) SwitchTurn();
        } else if (Input.GetKeyDown(KeyCode.F)) {
            Flag(player, targetCell);
        } 
        else if (Input.GetKeyDown(KeyCode.E)) {
            Chord(targetCell);
        } else if (Input.GetKeyUp(KeyCode.E)) {
            if (Unchord(player, targetCell)) SwitchTurn();
        }
        else if (Input.GetKeyDown(KeyCode.T)) {
            if (BuildTower(player, targetCell)) SwitchTurn();
        }
    }

    // 专门处理 P2 光标移动的逻辑
    private void HandleP2CursorMovement()
    {
        Vector2Int move = Vector2Int.zero;
        bool isInputActive = false;

        // 使用 GetKey 而不是 GetKeyDown 来检测长按
        // 同时也保留 GetKeyDown 的即时响应（可选，这里为了简单直接用计时器逻辑）
        
        if (Input.GetKey(KeyCode.W)) { move.y += 1; isInputActive = true; }
        else if (Input.GetKey(KeyCode.S)) { move.y -= 1; isInputActive = true; }
        
        // 使用 else if 防止斜向移动（如果想要斜向移动，去掉 else）
        if (Input.GetKey(KeyCode.A)) { move.x -= 1; isInputActive = true; }
        else if (Input.GetKey(KeyCode.D)) { move.x += 1; isInputActive = true; }

        // 只有当有输入 且 当前时间超过了下一次允许移动的时间
        if (isInputActive && Time.time >= _nextMoveTime)
        {
            if (move != Vector2Int.zero)
            {
                // 移动光标
                p2CursorPos.x = Mathf.Clamp(p2CursorPos.x + move.x, 0, width - 1);
                p2CursorPos.y = Mathf.Clamp(p2CursorPos.y + move.y, 0, height - 1);
                
                // 只有真正发生了移动才重置计时器
                // 这里还可以加一个小技巧：如果是刚按下(GetKeyDown)，延迟稍微长一点(0.3s)，
                // 之后的连续移动(GetKey)快一点(0.1s)，手感会更好。这里先用统一速度。
                _nextMoveTime = Time.time + moveRepeatRate;
            }
        }
        
        // 如果没有任何按键按下，重置计时器，保证下次按下能立刻响应
        if (!isInputActive)
        {
            _nextMoveTime = 0f;
        }
    }

    #endregion

    #region 核心动作 (Actions)

        // --- 新增：建塔逻辑 ---
 private bool BuildTower(Cell.Owner player, Cell cell)
    {
        if (cell == null) return false;

        // 1. 【前置检查】被锁定的格子不能操作（除非是自己的）
        if (cell.lockTimer > 0 && cell.owner != player && cell.owner != Cell.Owner.None) {
            Debug.Log("该区域被锁定，无法建塔！");
            return false;
        }

        // 2. 【前置检查】已经有塔的位置不能再建
        if (cell.hasTower) {
            Debug.Log("这里已经有一座防御塔了！");
            return false;
        }

        // 3. 【前置检查】已翻开的“安全格/数字格”不能建塔
        // (既然已经知道不是雷了，建塔没有意义，防止误触浪费能量)
        if (cell.revealed && cell.type != Cell.Type.Mine) {
            Debug.Log("目标是已知安全区，无法建塔。");
            return false;
        }

         // =========================================================
        //  新增检查 A：数量限制
        // =========================================================
        int currentCount = (player == Cell.Owner.PlayerA) ? towerCountA : towerCountB;
        if (currentCount >= maxTowersPerPlayer)
        {
            Debug.Log($"无法建造！防御塔数量已达上限 ({currentCount}/{maxTowersPerPlayer})");
            return false; // 不扣能量，不扣回合，直接拒绝
        }

        // =========================================================
        //  新增检查 B：地域限制 (左上到右下分界线)
        //  公式：x * H + y * W  vs  W * H
        // =========================================================
        long posValue = (long)cell.position.x * height + (long)cell.position.y * width;
        long threshold = (long)width * height;
        
        // P1 位于左下角 (0,0)，值应该 小于 阈值
        if (player == Cell.Owner.PlayerA)
        {
            if (posValue >= threshold) {
                Debug.Log("无法建造！该区域属于敌方半场 (越过分界线)。");
                return false;
            }
        }
        // P2 位于右上角 (W,H)，值应该 大于 阈值
        else if (player == Cell.Owner.PlayerB)
        {
            if (posValue <= threshold) {
                Debug.Log("无法建造！该区域属于敌方半场 (越过分界线)。");
                return false;
            }
        }

        // 4. 【能量检查】
        int currentEnergy = (player == Cell.Owner.PlayerA) ? energyA : energyB;
        if (currentEnergy < buildTowerCost) {
            Debug.Log($"能量不足 ({currentEnergy}/{buildTowerCost})，无法建塔！");
            return false;
        }

        // ============================================================
        //  从这里开始，操作被视为“已确认执行”
        //  无论结果是成功还是失败，都要扣能量、算回合
        // ============================================================

        // 5. 扣除能量
        AddEnergy(player, -buildTowerCost);

        // 6. 判定逻辑
        bool isMine = (cell.type == Cell.Type.Mine);
        
        // 允许建塔的两种情况：
        // A. 格子未翻开 (未知状态 或 插旗状态) -> 盲狙
        // B. 格子已炸开 (必须是雷) 且 归属于自己 -> 巩固防线
        
        // 注意：这里我们重新定义一下，只有未翻开的雷，或者己方炸开的雷算作“成功”
        // 如果是“未翻开的非雷”，则算失败。

        if (isMine)
        {
            
            bool canBuildOnExploded = cell.exploded && cell.owner == player;
            bool isHidden = !cell.revealed; // 包含插旗状态

            if (isHidden || canBuildOnExploded)
            {
                // --- 建塔成功 ---
                cell.hasTower = true;
                cell.towerOwner = player;
                cell.owner = player; // 强行夺取领地权
                
                // 塔本身就代表了视野，所以设为 revealed
                cell.revealed = true; 

                if (player == Cell.Owner.PlayerA) towerCountA++;
                else towerCountB++;
                
                // 塔覆盖在雷上，不再视为“爆炸”状态，而是“防御”状态
                // (虽然实际上它还是雷，但视觉上塔优先)
                
                Debug.Log($"玩家 {player} 建塔成功！当前塔数: {(player == Cell.Owner.PlayerA ? towerCountA : towerCountB)}/{maxTowersPerPlayer}");
            }
            else
            {
                Debug.Log("建塔失败：不能在敌方领地建塔。");
            }
        }
        else
        {
            // --- 建塔失败 (目标不是雷) ---
            // 既然能过前面的检查，说明这里是 isHidden (未翻开) 的非雷格子
            
            Debug.Log($">>> 建塔失误！目标下方空空如也！受到 {buildTowerPenaltyDamage} 点反噬伤害！ <<<");
            
            // 扣血
            TakeDamage(player, buildTowerPenaltyDamage);

            // 【关键博弈设计】
            // 既然建塔失败了，要不要翻开这个格子告诉大家这里是安全的？
            // 不翻开
            // 玩家亏了血、亏了能量、亏了回合，而且还不知道这个格子到底是数字几。
            // cell.revealed = false; // 保持原样
        }

        // 7. 刷新画面并结束回合
        board.Draw(grid);
        return true; 
    }

    // 翻开/占领逻辑
    private bool Reveal(Cell.Owner player, Cell cell) 
    {
        if (cell == null) return false;

        // 锁定检查
        if (cell.lockTimer > 0 && cell.owner != player && cell.owner != Cell.Owner.None) {
            Debug.Log("格子被锁定中！");
            return false; // 操作无效，不切换回合
        }

         // 有塔也不能翻开（或者有塔算作已保护？暂时逻辑：有塔无法被普通翻开覆盖）
        if (cell.hasTower) return false;

        // 地图生成
        if (!generated) {
            grid.GenerateMines(cell, mineCount);
            grid.GenerateNumbers();
            generated = true;
        }

        bool actionTaken = false;

        // 分流逻辑
        if (cell.owner != Cell.Owner.None && cell.owner != player) {
            CaptureCell(player, cell);
            actionTaken = true; // 发生了入侵，算一回合
        } else {
            // 如果 ExecuteReveal 真的做了什么，才算一回合
            actionTaken = ExecuteReveal(player, cell); 
        }

        board.Draw(grid);
        return actionTaken;
    }

    private bool ExecuteReveal(Cell.Owner player, Cell cell)
    {
        if (cell.revealed) return false;
        if (cell.flagged) return false;

        if (cell.type == Cell.Type.Mine)
        {
            // 1. 将地雷设为已翻开、已爆炸状态（为了让 Board 显示爆炸图块）
            cell.revealed = true;
            cell.exploded = true;
            
            cell.owner = player;

            // 2. 扣除血量
            TakeDamage(player, damagePerMine);

            // 3. 重绘地图显示这个雷
            board.Draw(grid);

            // 4. 返回 true，表示执行了动作，这会触发外部的 SwitchTurn()，换对方行动
            // (也就是说，踩雷不仅扣血，还会结束当前回合)
            return true; 
        }

        if (cell.type == Cell.Type.Empty)
        {
            StartCoroutine(Flood(cell, player));
        }
        else
        {
            cell.revealed = true;
            cell.owner = player;
            cell.lockTimer = occupationLockTime;
            AddEnergy(player, energyPerCell);
        }

        CheckWinCondition(); // 检查是否因为翻开了所有非雷格子而获胜
        return true;
    }

    private void Flag(Cell.Owner player, Cell cell)
    {
        if (cell == null) return;
        if (cell.revealed) return; // 翻开了不能插旗
        if (cell.hasTower) return; // 有塔了不需要插旗

        // 只有无主地或者自己的地可以插旗/取消旗
        if (cell.owner == Cell.Owner.None || cell.owner == player) {
            cell.flagged = !cell.flagged;
            // 插旗不改变 owner，保持 None 或 Player
            // 也不消耗回合
            board.Draw(grid);
        }
    }

    // 占领/入侵逻辑
    private void CaptureCell(Cell.Owner player, Cell cell)
    {
        cell.owner = player;
        cell.lockTimer = occupationLockTime;
        // 如果原本就是翻开的数字格，直接变色即可
        Debug.Log($"玩家 {player} 占领了格子 {cell.position}");
    }

    #endregion

    #region 高级逻辑 (Chord, Flood, Explode)

    // 预览周围未翻开格子
// 修改后：传入目标格子
private void Chord(Cell center)
{
    // 1. 先清除全图所有的 chorded 预览状态
    // (因为同一时间只能预览一个区域)
    for (int x = 0; x < width; x++) {
        for (int y = 0; y < height; y++) {
            grid[x, y].chorded = false;
        }
    }

    // 2. 合法性检查：只有点开的数字格才能触发 Chord 预览
    if (center == null || !center.revealed || center.type != Cell.Type.Number) {
        board.Draw(grid); // 即使无效也要重画以清除旧的预览
        return;
    }

    // 3. 将 center 周围 8 格标记为 chorded (用于 Tilemap 渲染高亮)
    for (int adjacentX = -1; adjacentX <= 1; adjacentX++) {
        for (int adjacentY = -1; adjacentY <= 1; adjacentY++) {
            if (grid.TryGetCell(center.position.x + adjacentX, center.position.y + adjacentY, out Cell cell)) {
                // 只有没翻开且没插旗的格子才显示“下沉/高亮”效果
                cell.chorded = !cell.revealed && !cell.flagged && !cell.hasTower;
            }
        }
    }

    board.Draw(grid);
}

    // 释放并尝试翻开周围
// 修改后：传入操作者和目标中心格
private bool Unchord(Cell.Owner player, Cell center)
{
    if (center == null || !center.revealed || center.type != Cell.Type.Number) {
        ClearAllChordedFlags();
        return false;
    }

    bool anyChange = false;

    if (CountAdjacentFlagsAndTowers(center) >= center.number)
    {
        for (int adjacentX = -1; adjacentX <= 1; adjacentX++) {
            for (int adjacentY = -1; adjacentY <= 1; adjacentY++) {
                if (adjacentX == 0 && adjacentY == 0) continue;

                if (grid.TryGetCell(center.position.x + adjacentX, center.position.y + adjacentY, out Cell cell)) {
                    // 递归调用 Reveal，只要有一个成功，就算动作有效
                    if (Reveal(player, cell)) {
                        anyChange = true;
                    }
                }
            }
        }
    }

    ClearAllChordedFlags();
    board.Draw(grid);
    return anyChange; // 如果周围有格子被翻开，则消耗回合
}

// 辅助计算周围旗子和塔
    private int CountAdjacentFlagsAndTowers(Cell cell)
    {
        int count = 0;
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                if (x == 0 && y == 0) continue;
                if (grid.TryGetCell(cell.position.x + x, cell.position.y + y, out Cell neighbor)) {
                    // 如果是没翻开且插旗了 OR 已经建塔了，都算作“雷”
                    if ((!neighbor.revealed && neighbor.flagged) || neighbor.hasTower) {
                        count++;
                    }
                }
            }
        }
        return count;
    }
private void ClearAllChordedFlags()
{
    for (int x = 0; x < width; x++) {
        for (int y = 0; y < height; y++) {
            grid[x, y].chorded = false;
        }
    }
}

private IEnumerator Flood(Cell cell, Cell.Owner player)
{
    // 递归出口条件：
    // 1. 游戏结束
    // 2. 格子已经是翻开状态（非常重要！防止回溯无限循环）
    // 3. 格子是旗子
    // 4. 格子是雷
    if (gameover || cell.revealed || cell.flagged || cell.type == Cell.Type.Mine) 
        yield break;

    // --- 第一步：翻开当前格子并处理归属 ---
    cell.revealed = true;
    cell.owner = player;
    cell.lockTimer = occupationLockTime;
    AddEnergy(player, energyPerCell);
    
    // 每一小步都重绘一下，可以看到水流扩散效果
    board.Draw(grid);
    // 如果想要扩散快一点，可以把等待时间缩短或去掉
    yield return new WaitForSeconds(0.01f); 

    // --- 第二步：判断是否继续扩散 ---
    // 只有当前格是“空格”时，才允许向周围 8 个方向扩散
    if (cell.type == Cell.Type.Empty)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue; // 跳过自己

                int checkX = cell.position.x + x;
                int checkY = cell.position.y + y;

                if (grid.TryGetCell(checkX, checkY, out Cell neighbor))
                {
                    // 递归调用：如果邻居没翻开，就流过去
                    if (!neighbor.revealed)
                    {
                        yield return Flood(neighbor, player);
                    }
                }
            }
        }
    }
    // 如果当前格是“数字格”，逻辑到此为止，不再进入上面的 if，也就停止了扩散。
}

    private void RevealAllMines()
    {
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                if (grid[x, y].type == Cell.Type.Mine) {
                    grid[x, y].revealed = true;
                }
            }
        }
        board.Draw(grid);
    }

    #endregion

    #region 辅助功能 (Utilities)

    private void UpdateCellTimers()
    {
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                if (grid[x, y].lockTimer > 0)
                    grid[x, y].lockTimer -= Time.deltaTime;
            }
        }
    }

    private void CheckWinCondition()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid[x, y].type != Cell.Type.Mine && !grid[x, y].revealed) return;

        gameover = true;
        Debug.Log("Win!");
    }

    private bool TryGetCellAtMousePosition(out Cell cell)
    {
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector3Int cellPosition = board.tilemap.WorldToCell(worldPosition);
        return grid.TryGetCell(cellPosition.x, cellPosition.y, out cell);
    }

    public void AddEnergy(Cell.Owner player, int amount)
    {
        if (player == Cell.Owner.PlayerA)
        {
            energyA = Mathf.Clamp(energyA + amount, 0, maxEnergy);
            if (energySliderA) energySliderA.value = energyA;
            if (energyTextA) energyTextA.text = $"P1 Energy: {energyA}/{maxEnergy}";
        }
        else if (player == Cell.Owner.PlayerB)
        {
            energyB = Mathf.Clamp(energyB + amount, 0, maxEnergy);
            if (energySliderB) energySliderB.value = energyB;
            if (energyTextB) energyTextB.text = $"P2 Energy: {energyB}/{maxEnergy}";
        }
    }

    private void UpdateHealthUI(Cell.Owner player)
{
    if (player == Cell.Owner.PlayerA)
    {
        if (hpSliderA) hpSliderA.value = (float)healthA / maxHealth; // 假设Slider是0-1
        if (hpTextA) hpTextA.text = $"HP: {healthA}";
    }
    else if (player == Cell.Owner.PlayerB)
    {
        if (hpSliderB) hpSliderB.value = (float)healthB / maxHealth;
        if (hpTextB) hpTextB.text = $"HP: {healthB}";
    }
}

// 核心扣血逻辑
    private void TakeDamage(Cell.Owner player, int amount)
    {
        if (gameover) return;

        if (player == Cell.Owner.PlayerA)
        {
            healthA -= amount;
            UpdateHealthUI(Cell.Owner.PlayerA);
            
            if (healthA <= 0)
            {
                healthA = 0;
                Debug.Log("P1 血量耗尽，P2 获胜！");
                GameOver(Cell.Owner.PlayerB); // 传入获胜者
            }
        }
        else if (player == Cell.Owner.PlayerB)
        {
            healthB -= amount;
            UpdateHealthUI(Cell.Owner.PlayerB);

            if (healthB <= 0)
            {
                healthB = 0;
                Debug.Log("P2 血量耗尽，P1 获胜！");
                GameOver(Cell.Owner.PlayerA); // 传入获胜者
            }
        }
    }

        private void GameOver(Cell.Owner winner)
    {
        gameover = true;
        RevealAllMines();
        // 这里可以添加显示胜利面板的逻辑
        // 比如：winText.text = $"{winner} Wins!";
        Debug.Log($"游戏结束，获胜者：{winner}");
    }

    #endregion
}