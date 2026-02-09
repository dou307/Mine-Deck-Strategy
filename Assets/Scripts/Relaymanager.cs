using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    // 单例模式，方便调用
    public static RelayManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private async void Start()
    {
        // 1. 初始化 Unity Services
        await UnityServices.InitializeAsync();

        // 2. 匿名登录 (Relay 必须先登录)
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    /// <summary>
    /// 创建主机 (Host)
    /// </summary>
    /// <returns>返回 Join Code 供分享</returns>
    public async Task<string> CreateRelay()
    {
        try
        {
            // 创建分配：最大 4 人 (3 个客户端 + 1 个主机)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3);

            // 获取 Join Code
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // --- 核心关键点 ---
            // WebGL 必须使用 "wss" (WebSockets Secure)
            // 编辑器/桌面端可以使用 "dtls" (UDP)
            // 这里的条件编译会自动处理
            string connectionType = Application.platform == RuntimePlatform.WebGLPlayer ? "wss" : "dtls";

            RelayServerData relayServerData = new RelayServerData(allocation, connectionType);

            // 配置 Transport
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 启动 Host (服务器+客户端)
            NetworkManager.Singleton.StartHost();

            return joinCode;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Relay 创建失败: {e}");
            return null;
        }
    }

    /// <summary>
    /// 加入游戏 (Client)
    /// </summary>
    /// <param name="joinCode">主机发来的代码</param>
    public async Task<bool> JoinRelay(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // 同样根据平台选择连接类型
            string connectionType = Application.platform == RuntimePlatform.WebGLPlayer ? "wss" : "dtls";

            RelayServerData relayServerData = new RelayServerData(joinAllocation, connectionType);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            // 启动客户端
            return NetworkManager.Singleton.StartClient();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Relay 加入失败: {e}");
            return false;
        }
    }
}