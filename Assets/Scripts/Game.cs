using System.Collections;
using UnityEngine;
using TMPro;

[DefaultExecutionOrder(-1)]
public class Game : MonoBehaviour
{
    [Header("地图设置")]
    public int width = 16;
    public int height = 16;
    public int mineCount = 32;

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
    [Header("竞技设置")]

    public Vector2Int p2CursorPos = new Vector2Int(15, 15); // P2 初始位置（比如右上角）
    public Transform p2CursorVisual; // 在编辑器里拖入一个高亮方框，显示P2在哪
    public float occupationLockTime = 3.0f; // 领地占领锁定时间

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
    // 1. 停止上局所有的洪水填充（Flood Fill）协程，防止新旧混淆
    StopAllCoroutines();

    // 2. 根据棋盘宽高将摄像机居中
    Camera.main.transform.position = new Vector3(width / 2f, height / 2f, -10f);

    // 3. 重置游戏基本状态
    gameover = false;
    generated = false; // 标记地雷尚未生成，等待第一下点击

    // 4. 创建新的逻辑网格并让渲染层重绘
    grid = new CellGrid(width, height);
    board.Draw(grid);

    // 5. 【核心修改】重置双方玩家的能量值
    energyA = 0;
    energyB = 0;

    // 6. 强制刷新双方的 UI（显示为 0/100）
    // 传入 0 是因为 AddEnergy 内部封装了刷新 UI 的逻辑
    AddEnergy(Cell.Owner.PlayerA, 0);
    AddEnergy(Cell.Owner.PlayerB, 0);
    
    Debug.Log("新对局已开始：能量已清空，双方基地准备就绪。");
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

// --- 附带必须的支撑函数 ---

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

    // 玩家 1：完全基于鼠标，传入鼠标当前的格子
    private void HandleP1MouseInput(Cell.Owner player)
    {
        Cell targetCell = GetMouseCell(); // 获取鼠标指向的格子
        if (targetCell == null) return;

        if (Input.GetMouseButtonDown(0)) {
            Reveal(player, targetCell);
        } else if (Input.GetMouseButtonDown(1)) {
            Flag(player, targetCell);
        } else if (Input.GetMouseButton(2)) {
            Chord(targetCell); 
        } else if (Input.GetMouseButtonUp(2)) {
            Unchord(player, targetCell);
        }
    }

    // 玩家 2：完全基于键盘，传入 WASD 选中的格子
    private void HandleP2KeyboardInput(Cell.Owner player)
    {
        // 1. 处理 WASD 移动光标坐标
        HandleP2CursorMovement();

        // 2. 获取光标坐标对应的格子
        Cell targetCell = grid.GetCell(p2CursorPos.x, p2CursorPos.y);
        if (targetCell == null) return;

        // 3. 处理按键动作：空格点击，F插旗
        if (Input.GetKeyDown(KeyCode.Space)) {
            Reveal(player, targetCell);
        } else if (Input.GetKeyDown(KeyCode.F)) {
            Flag(player, targetCell);
        } 
        // 如果 P2 也需要 Chord 功能，可以绑定到其他按键，例如 E
        else if (Input.GetKeyDown(KeyCode.E)) {
            Chord(targetCell);
        } else if (Input.GetKeyUp(KeyCode.E)) {
            Unchord(player, targetCell);
        }

        // 4. 更新 P2 光标的视觉位置 (把 P2 的光标物体移动到对应的世界坐标)
        if (p2CursorVisual != null) {
            Vector3 worldPos = board.tilemap.CellToWorld((Vector3Int)p2CursorPos);
            p2CursorVisual.position = worldPos + new Vector3(0.5f, 0.5f, 0); // +0.5偏移使光标居中
        }
    }

    // 专门处理 P2 光标移动的逻辑
    private void HandleP2CursorMovement()
    {
        Vector2Int move = Vector2Int.zero;
        if (Input.GetKeyDown(KeyCode.W)) move.y += 1;
        if (Input.GetKeyDown(KeyCode.S)) move.y -= 1;
        if (Input.GetKeyDown(KeyCode.A)) move.x -= 1;
        if (Input.GetKeyDown(KeyCode.D)) move.x += 1;

        if (move != Vector2Int.zero) {
            p2CursorPos.x = Mathf.Clamp(p2CursorPos.x + move.x, 0, width - 1);
            p2CursorPos.y = Mathf.Clamp(p2CursorPos.y + move.y, 0, height - 1);
        }
    }

    #endregion

    #region 核心动作 (Actions)

    // 翻开/占领逻辑
private void Reveal(Cell.Owner player, Cell cell) 
{
    // 1. 基础合法性检查：如果格子不存在，直接退出
    if (cell == null) return;

    // 2. 【核心博弈：锁定逻辑】
    // 如果格子被别人占领了，且还在 3 秒保护期内，操作无效
    if (cell.lockTimer > 0 && cell.owner != player && cell.owner != Cell.Owner.None) {
        Debug.Log("格子被锁定中，无法操作！");
        return; 
    }

    // 3. 【地图生成逻辑】
    // 如果是整局游戏第一次点击，生成地雷（确保第一下不是雷）
    if (!generated) {
        grid.GenerateMines(cell, mineCount);
        grid.GenerateNumbers();
        generated = true;
    }

    // 4. 【分流：入侵 vs 正常点开】
    if (cell.owner != Cell.Owner.None && cell.owner != player) {
        // 如果格子的主人不是我，说明我在“入侵”对方领土
        CaptureCell(player, cell); 
    } else {
        // 如果格子是中立的或者是我的，正常执行点开逻辑
        ExecuteReveal(player, cell); 
    }

    // 5. 每次动作完重绘地图
    board.Draw(grid);
}

private void ExecuteReveal(Cell.Owner player, Cell cell)
{
    // 如果已经翻开、插旗或是雷，则停止
    if (cell.revealed || cell.flagged) return;

    // 如果是雷
    if (cell.type == Cell.Type.Mine)
    {
        //ExecuteExplode(player, cell);
        return;
    }

    // 如果是空格 (0)
    if (cell.type == Cell.Type.Empty)
    {
        // 只有这里需要启动协程进行连锁翻开
        StartCoroutine(Flood(cell, player));
    }
    else // 如果是数字格 (1-8)
    {
        // 数字格只翻开自己，不触发扩散
        cell.revealed = true;
        cell.owner = player;
        cell.lockTimer = occupationLockTime;
        AddEnergy(player, energyPerCell);
    }

    CheckWinCondition();
    board.Draw(grid);
}

private void Flag(Cell.Owner player, Cell cell)
{
    // 1. 基础合法性检查：如果格子不存在，直接退出
    if (cell == null) return;

    // 2. 只有没翻开的格子才能插旗
    if (cell.revealed) return;

    // 3. 锁定检查：如果格子被别人占领且在保护期，无法插旗
    if (cell.lockTimer > 0 && cell.owner != player && cell.owner != Cell.Owner.None) {
        return;
    }

    // 4. 只能在自己领地或中立地区插旗
    if (cell.owner == Cell.Owner.None || cell.owner == player) {
        cell.flagged = !cell.flagged;
        cell.owner = player; // 插旗也算占领该格
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
                cell.chorded = !cell.revealed && !cell.flagged;
            }
        }
    }

    board.Draw(grid);
}

    // 释放并尝试翻开周围
// 修改后：传入操作者和目标中心格
private void Unchord(Cell.Owner player, Cell center)
{
    // 1. 基础检查
    if (center == null || !center.revealed || center.type != Cell.Type.Number) {
        ClearAllChordedFlags(); // 辅助函数：清空所有格子的 chorded 状态
        return;
    }

    // 2. 逻辑判定：周围旗帜数是否等于数字格的数值
    if (grid.CountAdjacentFlags(center) >= center.number)
    {
        // 3. 尝试翻开周围 8 格
        for (int adjacentX = -1; adjacentX <= 1; adjacentX++) {
            for (int adjacentY = -1; adjacentY <= 1; adjacentY++) {
                if (adjacentX == 0 && adjacentY == 0) continue;

                if (grid.TryGetCell(center.position.x + adjacentX, center.position.y + adjacentY, out Cell cell)) {
                    // 核心调用：调用带参数的 Reveal
                    // 这样通过 Chord 翻开的领地也会正确归属于当前玩家，并检查雷
                    Reveal(player, cell);
                }
            }
        }
    }

    ClearAllChordedFlags();
    board.Draw(grid);
}

// 辅助函数，避免代码重复
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

    private void Explode(Cell cell)
    {
        // 目前暂定踩雷结束，后续可改为扣除基地血量
        gameover = true;
        cell.exploded = true;
        cell.revealed = true;

        // 显示所有雷
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                if (grid[x, y].type == Cell.Type.Mine) grid[x, y].revealed = true;
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

    #endregion
}