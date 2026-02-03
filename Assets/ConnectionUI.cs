using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public class ConnectionUI : MonoBehaviour
{
    [Header("UI 组件")]
    public Button hostBtn;       
    public Button clientBtn;     
    public TMP_InputField joinCodeInput; 
    public TextMeshProUGUI joinCodeText; 
    
    // 【新增】用于显示 "Waiting for the Client..." 的文本组件
    public TextMeshProUGUI statusText;   
    
    public GameObject panel;     
    public LobbyManager lobbyManager;

    private async void Start()
    {
        await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        hostBtn.onClick.AddListener(CreateRelayGame);
        clientBtn.onClick.AddListener(JoinRelayGame);

        // 【关键】订阅 NetworkManager 的连接事件
        // 当有客户端连接成功时，Unity 会自动调用 OnClientConnected 这个方法
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    // --- 这是一个回调函数，当有人连上时自动触发 ---
    private void OnClientConnected(ulong clientId)
    {
        // NetworkManager.Singleton.IsHost: 判断当前是房主还是客户端
        
        if (NetworkManager.Singleton.IsHost)
        {
            // 如果我是房主
            // clientId = 0 是房主自己，clientId = 1 是第一个进来的客人
            // 只有当 clientId != 0 (说明是别人进来了)，我们才开始游戏
            if (clientId != 0) 
            {
                Debug.Log($"玩家 {clientId} 已加入，游戏开始！");
                HideUI(); // 房主隐藏 UI，进入游戏
            }
        }
        else 
        {
            // 如果我是客户端
            // 只要连上了，就说明进入房间成功了，直接隐藏 UI
            Debug.Log("连接主机成功，进入游戏！");
            HideUI(); 
        }
    }
    
    //记得在脚本销毁时取消订阅，防止报错
    private void OnDestroy() 
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    // --- Host 逻辑 ---
    private async void CreateRelayGame()
    {
        try
        {
            // 1. 禁用按钮，防止重复点击
            hostBtn.interactable = false;
            clientBtn.interactable = false;
            
            // 2. 更新状态文本
            if(statusText) statusText.text = "Creating Room...";

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            
            // 3. 界面更新：显示房间号 + 等待提示
            Debug.Log("房间号: " + joinCode);

            if (lobbyManager != null)
            {
                // 房间名可以先写死，以后做输入框
                string roomName = "Player " + UnityEngine.Random.Range(100, 999) + "'s Room";
                lobbyManager.PostRoom(joinCode, roomName);
            }
            if(joinCodeText) joinCodeText.text = "Code: " + joinCode;
            
            // 自动复制方便测试
            GUIUtility.systemCopyBuffer = joinCode; 
            
            // 【重点】更新下面的状态文字
            if(statusText) statusText.text = "Waiting for the Client...";

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();
            
            // 【注意】这里不再调用 HideUI()！
            // UI 会一直显示，直到 OnClientConnected 检测到有人进来
        }
        catch (System.Exception e)
        {
            Debug.LogError("创建房间失败: " + e.Message);
            // 失败了记得把按钮恢复
            hostBtn.interactable = true;
            clientBtn.interactable = true;
            if(statusText) statusText.text = "Error: " + e.Message;
        }
    }

    // --- Client 逻辑 ---
    private async void JoinRelayGame()
    {
        string code = joinCodeInput.text.Trim();
        if (string.IsNullOrEmpty(code)) return;

        try
        {
            hostBtn.interactable = false;
            clientBtn.interactable = false;
            if(statusText) statusText.text = "Connecting...";

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();
            
            // Client 也不需要立即 HideUI，
            // StartClient 成功后会触发 OnClientConnected，那里会处理 UI 隐藏
        }
        catch (System.Exception e)
        {
            Debug.LogError("加入房间失败: " + e.Message);
            hostBtn.interactable = true;
            clientBtn.interactable = true;
            if(statusText) statusText.text = "Join Failed!";
        }
    }

    private void HideUI()
    {
        if (panel != null) panel.SetActive(false);
    }
}