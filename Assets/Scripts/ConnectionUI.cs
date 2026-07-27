using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;
using System.Collections.Generic; // 必须引用

public class ConnectionUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject panel;
    public GameObject waitingPanel;
    public TMP_Text codeText;
    public TMP_InputField joinInput;

    public TMP_InputField renameInput;
    
    [Header("Lobby UI")]
    public Transform roomListContent; // 列表容器 (ScrollView 的 Content)
    public GameObject roomItemPrefab; // 房间模版 (刚才做的 Button)
    public Button refreshButton;      // 刷新按钮 (你需要新建一个或者用代码自动刷新)
    private string currentJoinCode;

    [Header("Managers")]
    public LobbyManager lobbyManager; // 记得拖拽赋值

    private void Start()
    {
        InitializeServices();
        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(OnRefreshClicked);
        }
    }

    private async void InitializeServices()
    {
        try
        {
            await RelayManager.Instance.EnsureServicesReadyAsync();
            Debug.Log("Unity Services 初始化完成, ID: " + AuthenticationService.Instance.PlayerId);
            
            // 初始化完成后，自动刷新一次列表
            RefreshRoomList(); 
        }
        catch (System.Exception e)
        {
            Debug.LogError("初始化失败: " + e);
        }
    }

    // --- Host 流程 ---

    public async void OnHostClicked()
    {
        // 1. 创建房间
        string code = await RelayManager.Instance.CreateRelay();

        if (!string.IsNullOrEmpty(code))
        {
            currentJoinCode = code; // 本地 UI 存一份
            
            // --- 【核心步骤：传递给 Game.cs】 ---
            // FindObjectOfType 可以找到场景里挂载了 Game 脚本的物体
            Game gameLogic = FindObjectOfType<Game>();
            if (gameLogic != null)
            {
                gameLogic.currentJoinCode = code;
                Debug.Log($"已将 JoinCode {code} 传递给 Game 逻辑层");
            }
            else
            {
                Debug.LogError("严重错误：场景中找不到 Game 组件！无法同步房间号！");
            }
            // ----------------------------------

            // 2. 上传到 Supabase (保持你原有的逻辑)
            if (lobbyManager != null)
            {
                // 记得：这里要用带 status 参数的新版 PostRoom
                string roomName = "Player " + UnityEngine.Random.Range(100, 999) + "'s Room";
                lobbyManager.PostRoom(currentJoinCode, roomName); 
            }

            // 3. UI 切换
            if (panel != null) panel.SetActive(false);
            if (waitingPanel != null) waitingPanel.SetActive(true);
             if (codeText != null)
            {
                codeText.text = "房间号: " + code; // 或者直接 codeText.text = code;
                Debug.Log("UI 已更新房间号: " + code);
            }
            else
            {
                Debug.LogError("忘了在 Inspector 里拖拽 Code Text 组件！");
            }
        }
    }

    // --- Join 流程 (旧版手动输入) ---
    public void OnJoinClicked()
    {
        string code = joinInput.text;
        JoinRelayGame(code);
    }

    // --- Join 流程 ---
    private async void JoinRelayGame(string joinCode)
    {
        if (string.IsNullOrEmpty(joinCode)) return;

        Debug.Log("正在通过 RelayManager 加入房间: " + joinCode);

        // 调用 RelayManager 加入 (它内部会自动处理 WSS 协议和 StartClient)
        bool success = await RelayManager.Instance.JoinRelay(joinCode);

        if (success)
        {
            HideUI();
        }
        else
        {
            Debug.LogError("加入失败，请检查验证码或网络");
        }
    }
    // --- 房间列表逻辑 ---
    public void RefreshRoomList()
    {
        if (lobbyManager == null) return;
        
        // 绑定回调：当数据回来时，执行 UpdateUI
        lobbyManager.OnRoomListFetched = UpdateListUI;
        // 发起请求
        lobbyManager.FetchRooms();
    }

    private void UpdateListUI(List<RoomData> rooms)
    {
        // 1. 清空旧按钮
        foreach (Transform child in roomListContent)
        {
            Destroy(child.gameObject);
        }

        // 2. 生成新按钮
        foreach (var room in rooms)
        {
            GameObject newItem = Instantiate(roomItemPrefab, roomListContent);
            
            // --- 【核心修改】使用 RoomItem 脚本来设置状态 ---
            RoomItem itemScript = newItem.GetComponent<RoomItem>();
            
            if (itemScript != null)
            {
                // 把房间数据传进去，同时传入点击回调 JoinRelayGame
                itemScript.Setup(room, (code) => JoinRelayGame(code));
            }
            else
            {
                Debug.LogError("你的 Prefab 上没挂 RoomItem 脚本！");
            }
        }
    }

    // --- 【新增】候机室改名按钮点击事件 ---
    public void OnRenameClicked()
    {
        // 1. 检查输入如果不为空
        if (renameInput != null && !string.IsNullOrEmpty(renameInput.text))
        {
            string newName = renameInput.text;
            
            // 2. 调用 LobbyManager 发送更新请求
            if (lobbyManager != null && !string.IsNullOrEmpty(currentJoinCode))
            {
                lobbyManager.UpdateRoomName(currentJoinCode, newName);
                Debug.Log("正在请求修改房间名为: " + newName);
            }
        }
    }

    // --- 【新增】刷新按钮点击逻辑 ---
    private void OnRefreshClicked()
    {
        // 1. 禁用按钮 (防止连续点击)
        if (refreshButton != null) refreshButton.interactable = false;

        Debug.Log("正在刷新房间列表...");

        // 2. 调用刷新
        RefreshRoomList();

        // 3. 延迟 2 秒后恢复按钮 (协程)
        StartCoroutine(ResetRefreshButton());
    }

    private System.Collections.IEnumerator ResetRefreshButton()
    {
        yield return new WaitForSeconds(2.0f); // 冷却时间 2秒
        if (refreshButton != null) refreshButton.interactable = true;
    }

    private void HideUI()
    {
        if (panel != null) panel.SetActive(false);
    }
    public void OnBackToMenuClicked()
    {
        // 1. 断开网络连接 (这非常重要！)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 2. 重新加载当前场景 (最简单的重置方法)
        // 这样所有的变量、UI状态都会恢复到刚打开游戏的样子
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        );
    }
    }
