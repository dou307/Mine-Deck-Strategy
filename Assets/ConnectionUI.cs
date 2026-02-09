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
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
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
        // 1. 调用 RelayManager 创建房间 (它内部会自动处理 WSS 协议和 StartHost)
        // 注意：这里假设你的 RelayManager.CreateRelay() 返回的是 Task<string> joinCode
        string code = await RelayManager.Instance.CreateRelay();

        if (!string.IsNullOrEmpty(code))
        {
            currentJoinCode = code;
            Debug.Log("房间创建成功, Code: " + currentJoinCode);
            if (codeText != null) codeText.text = "Code: " + currentJoinCode;

            // 2. 上传到 Supabase (保留你原有的逻辑)
            if (lobbyManager != null)
            {
                string roomName = "Player " + UnityEngine.Random.Range(100, 999) + "'s Room";
                lobbyManager.PostRoom(currentJoinCode, roomName);
            }

            // 3. UI 切换
            if (panel != null) panel.SetActive(false);
            if (waitingPanel != null) waitingPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("Host 失败: 无法从 RelayManager 获取 JoinCode");
        }
    }

    // --- Join 流程 (旧版手动输入) ---
    public async void OnJoinClicked()
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
            
            // 设置文字
            TMP_Text t = newItem.GetComponentInChildren<TMP_Text>();
            if (t) t.text = $"{room.room_name} (点击加入)";

            // 设置点击事件 (Lambda表达式闭包)
            Button b = newItem.GetComponent<Button>();
            if (b) 
            {
                b.onClick.AddListener(() => {
                    JoinRelayGame(room.join_code);
                });
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