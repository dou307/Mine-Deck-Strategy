#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public sealed class MineDeckE2ERunner : MonoBehaviour
{
    private const string HostArgument = "--mine-deck-e2e-host";
    private const string ClientArgument = "--mine-deck-e2e-client";
    private const string LocalTransportArgument = "--mine-deck-e2e-local";
    private const int StepTimeoutMilliseconds = 45000;

    private string role;
    private string sharedDirectory;
    private bool useLocalTransport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int modeIndex = Array.FindIndex(arguments, value =>
            value == HostArgument || value == ClientArgument);
        if (modeIndex < 0 || modeIndex + 1 >= arguments.Length) return;

        GameObject runnerObject = new GameObject(nameof(MineDeckE2ERunner));
        DontDestroyOnLoad(runnerObject);
        MineDeckE2ERunner runner = runnerObject.AddComponent<MineDeckE2ERunner>();
        runner.role = arguments[modeIndex] == HostArgument ? "host" : "client";
        runner.sharedDirectory = arguments[modeIndex + 1];
        runner.useLocalTransport = Array.IndexOf(arguments, LocalTransportArgument) >= 0;
    }

    private async void Start()
    {
        try
        {
            Directory.CreateDirectory(sharedDirectory);
            Log("BOOT");
            await WaitUntilAsync(() => RelayManager.Instance != null, "RelayManager");
            if (!useLocalTransport)
                await RelayManager.Instance.EnsureServicesReadyAsync();

            if (role == "host")
                await StartHostAsync();
            else
                await StartClientAsync();

            HideConnectionPanel();
            Game game = await WaitForGameAsync();
            await WaitForPeerAsync();
            Log($"CONNECTED local={NetworkManager.Singleton.LocalClientId}");

            int[] loadout =
            {
                (int)SkillType.RadarScan,
                (int)SkillType.PlaceTrap,
                (int)SkillType.BuildTower,
                (int)SkillType.HealSmall
            };
            game.PlayerReadyServerRpc(loadout);
            await WaitUntilAsync(() => game.gameHUD != null && game.gameHUD.activeSelf, "match start");
            Log($"MATCH_STARTED identity={game.GetLocalPlayerIdentity()}");

            await RunTurnsAndSkillAsync(game);
            File.WriteAllText(GetResultPath("done"), DateTime.UtcNow.ToString("O"));
            Log("PASS");
        }
        catch (Exception exception)
        {
            File.WriteAllText(GetResultPath("failed"), exception.ToString());
            Debug.LogError($"[E2E:{role}] FAIL {exception}");
        }
    }

    private async Task StartHostAsync()
    {
        if (useLocalTransport)
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            if (!NetworkManager.Singleton.StartHost())
                throw new InvalidOperationException("Local Netcode host start failed.");

            File.WriteAllText(Path.Combine(sharedDirectory, "join-code.txt"), "LOCAL");
            Log("LOCAL_HOST_STARTED port=7777");
            return;
        }

        string joinCode = await RelayManager.Instance.CreateRelay();
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new InvalidOperationException("Relay host creation returned no join code.");

        Game game = FindObjectOfType<Game>(true);
        if (game != null) game.currentJoinCode = joinCode;
        File.WriteAllText(Path.Combine(sharedDirectory, "join-code.txt"), joinCode);
        Log($"RELAY_CREATED code={joinCode}");
    }

    private async Task StartClientAsync()
    {
        string joinCodePath = Path.Combine(sharedDirectory, "join-code.txt");
        await WaitUntilAsync(() => File.Exists(joinCodePath), "join code file");
        string joinCode = File.ReadAllText(joinCodePath).Trim();

        if (useLocalTransport)
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 7777);
            if (!NetworkManager.Singleton.StartClient())
                throw new InvalidOperationException("Local Netcode client start failed.");

            Log("LOCAL_JOIN_REQUESTED port=7777");
            return;
        }

        if (!await RelayManager.Instance.JoinRelay(joinCode))
            throw new InvalidOperationException("Relay client start failed.");

        Log($"RELAY_JOIN_REQUESTED code={joinCode}");
    }

    private async Task<Game> WaitForGameAsync()
    {
        Game game = null;
        await WaitUntilAsync(() =>
        {
            game = FindObjectOfType<Game>(true);
            return game != null &&
                   NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsConnectedClient;
        }, "spawned Game");
        return game;
    }

    private static void HideConnectionPanel()
    {
        ConnectionUI connectionUI = FindObjectOfType<ConnectionUI>(true);
        if (connectionUI != null && connectionUI.panel != null)
            connectionUI.panel.SetActive(false);
    }

    private async Task WaitForPeerAsync()
    {
        if (role == "host")
        {
            await WaitUntilAsync(
                () => NetworkManager.Singleton.ConnectedClientsIds.Count == 2,
                "second player");
        }
        else
        {
            await Task.Delay(1500);
        }
    }

    private async Task RunTurnsAndSkillAsync(Game game)
    {
        Cell.Owner identity = game.GetLocalPlayerIdentity();
        int actionIndex = 0;
        int observedTurn = game.totalTurnCount.Value;
        bool skillCast = false;
        DateTime deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            string peerRole = role == "host" ? "client" : "host";
            if (skillCast && File.Exists(Path.Combine(sharedDirectory, $"{peerRole}.skill")))
                return;

            if (game.currentTurn.Value != identity)
            {
                await Task.Delay(100);
                continue;
            }

            int energyBefore = game.E2EGetEnergy(identity);
            int turnBefore = game.totalTurnCount.Value;

            if (!skillCast && energyBefore >= 40 && turnBefore >= 3)
            {
                Vector2Int target = identity == Cell.Owner.PlayerA
                    ? new Vector2Int(4, 4)
                    : new Vector2Int(game.width - 5, game.height - 5);
                game.E2ERequestSkill(SkillType.RadarScan, target.x, target.y);
                await WaitUntilAsync(
                    () => game.totalTurnCount.Value > turnBefore,
                    $"{identity} radar turn");

                int energyAfter = game.E2EGetEnergy(identity);
                if (energyAfter > energyBefore - 40)
                    throw new InvalidOperationException(
                        $"Radar cost was not enforced. Before={energyBefore}, after={energyAfter}.");

                Log($"SKILL_OK type=RadarScan energy={energyBefore}->{energyAfter}");
                File.WriteAllText(GetResultPath("skill"), DateTime.UtcNow.ToString("O"));
                skillCast = true;
                continue;
            }

            Vector2Int cell = GetRevealTarget(identity, actionIndex++, game.width, game.height);
            game.E2ERequestReveal(cell.x, cell.y);
            await Task.Delay(350);

            if (game.totalTurnCount.Value > turnBefore)
            {
                observedTurn = game.totalTurnCount.Value;
                Log($"REVEAL_OK cell={cell.x},{cell.y} turn={observedTurn} energy={game.E2EGetEnergy(identity)}");
            }
        }

        throw new TimeoutException(
            $"{identity} did not accumulate enough energy and cast RadarScan. Last turn={observedTurn}.");
    }

    private static Vector2Int GetRevealTarget(Cell.Owner identity, int index, int width, int height)
    {
        int halfWidth = width / 2;
        int localX = 1 + (index % Math.Max(1, halfWidth - 2));
        int y = 1 + ((index / Math.Max(1, halfWidth - 2)) % Math.Max(1, height - 2));
        int x = identity == Cell.Owner.PlayerA ? localX : width - 1 - localX;
        return new Vector2Int(x, y);
    }

    private async Task WaitUntilAsync(Func<bool> predicate, string step)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(StepTimeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for {step}.");
    }

    private string GetResultPath(string extension)
    {
        return Path.Combine(sharedDirectory, $"{role}.{extension}");
    }

    private void Log(string message)
    {
        Debug.Log($"[E2E:{role}] {message}");
    }
}
#endif
