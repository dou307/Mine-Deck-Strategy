using UnityEngine;
using Unity.Netcode; // 引入 Netcode 命名空间

public class PlayerController : NetworkBehaviour // 注意：继承 NetworkBehaviour
{
    public float moveSpeed = 5f;

    // Start 已经改成 OnNetworkSpawn
    public override void OnNetworkSpawn()
    {
        // Debug.Log($"Player {OwnerClientId} spawned. IsLocalPlayer: {IsLocalPlayer}, IsOwner: {IsOwner}, IsServer: {IsServer}");

        // 只有当这个 NetworkPlayer 是当前客户端控制的本地玩家时，才允许它接收输入。
        // 否则它就是其他客户端的玩家，我们只需要同步它的位置。
        if (IsOwner) 
        {
            // 如果是Host（也就是服务器自己也是一个客户端），并且是Host的Player，
            // 可以给它一个不同的颜色以示区分，或者移动到不同位置避免重叠
            if (IsHost)
            {
                // For Host player
                GetComponent<SpriteRenderer>().color = Color.blue; // 蓝色代表Host
                transform.position = new Vector3(-2f, 0, 0);
            }
            else
            {
                // For Client player
                GetComponent<SpriteRenderer>().color = Color.green; // 绿色代表Client
                transform.position = new Vector3(2f, 0, 0);
            }
        } else {
             // 其他玩家（非本地玩家），显示为白色或者其他默认色
             // 这样在Host的窗口里，就能看到一个蓝色方块（Host自己）和一个绿色方块（Client）
             // 在Client的窗口里，就能看到一个绿色方块（Client自己）和一个蓝色方块（Host）
             GetComponent<SpriteRenderer>().color = Color.white; 
        }
    }

    // Update 方法用来处理输入和发送 RPC
    void Update()
    {
        // 只有当前客户端控制的 NetworkPlayer (IsOwner 为 True) 才能接收输入并移动
        if (!IsOwner) return;

        Vector3 moveDirection = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) moveDirection.y += 1;
        if (Input.GetKey(KeyCode.S)) moveDirection.y -= 1;
        if (Input.GetKey(KeyCode.A)) moveDirection.x -= 1;
        if (Input.GetKey(KeyCode.D)) moveDirection.x += 1;

        // 如果有移动输入
        if (moveDirection != Vector3.zero)
        {
            // 在客户端的 Update 中，调用 ServerRpc 将移动指令发送给服务器
            // 服务器会处理移动，并通过 NetworkTransform 同步回所有客户端
            // 注意：这里我们还没有用 NetworkTransform，先手动模拟一下
            
            // 为了简单演示，我们直接在客户端修改位置，但是真正的同步需要 RPC
            // transform.position += moveDirection.normalized * moveSpeed * Time.deltaTime;

            // 正确的做法是发送 RPC 给服务器，让服务器来权威移动
            RequestMoveServerRpc(moveDirection.normalized * moveSpeed * Time.deltaTime);
        }
    }

    // 这是一个 ServerRpc 方法，它会在服务器端被调用和执行
    // RequireOwnership = false 表示任何客户端都可以调用它（即使不是自己的 PlayerController）
    // 但这里我们已经在 Update 里用 IsOwner 过滤了，所以实际上只有 Owner 会调用
    [ServerRpc(RequireOwnership = true)] // RequireOwnership = true 确保只有 PlayerController 的所有者才能调用此 RPC
    void RequestMoveServerRpc(Vector3 movement)
    {
        // 这个方法在服务器上执行
        // 服务器是权威，所以由服务器来真正修改 NetworkPlayer 的位置
        transform.position += movement;
        
        // 重要：如果位置不是 NetworkVariable 自动同步的，需要手动 ClientRpc 广播
        // 但更好的做法是使用 NetworkTransform 组件，它会自动同步
        // 我们暂不写 ClientRpc，直接进入下一步，使用 NetworkTransform
    }
}