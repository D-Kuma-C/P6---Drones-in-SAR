using BetterTelloLib.Commander;
using BetterTelloLib.Commander.Events.EventArgs;
using BetterTelloLib.Commander.Factories;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityControllerForTello;
using UnityEngine;
using UnityEngine.UIElements;

public class BetterTelloManager : MonoBehaviour
{
    public BetterTello BetterTello = new();
    public TelloConnectionState ConnectionState = TelloConnectionState.Disconnected;
    public FlyingState FlyingState = FlyingState.Grounded;
    public float Height = 0;
    public Vector3 PositionVel = Vector3.zero;
    public Vector3 PositionAcc = Vector3.zero;
    public Vector3 PositionMissionPad = Vector3.zero;
    public float TempH = 0;
    public float TempL = 0;
    public float ExtTof = 0;
    public int Bat = 0;
    public int Tof;
    public Quaternion PYR = new();
    private float Pitch = 0;
    private float Roll = 0;
    private float Yaw = 0;
    private TelloVideoTexture telloVideoTexture;
    private SceneManager sceneManager;
    private InputController inputController;
    private FlightPathController flightPathController;
    public Transform Transform;

    private List<int> Timestamps = new();
    private List<Vector3> Vels = new();

    private bool waitingForOk = false;


    private void Start()
    {
        gameObject.SetActive(true);
        ConnectToTello();
        Transform = GetComponent<Transform>();
    }
    public void ConnectToTello()
    {
        flightPathController.drawFlightPath = false;
        ConnectionState = TelloConnectionState.Connecting;
        BetterTello.Events.OnStateRecieved += OnStateUpdate;
        BetterTello.Events.OnVideoDataRecieved += Tello_onVideoData;
        BetterTello.Connect();
        ConnectionState = TelloConnectionState.Connected;
        BetterTello.Factories.OnTaskRecieved += TaskRecieved;
        BetterTello.Commands.SetBitrate(0);
        Task.Factory.StartNew(async ()=> await Run());
    }

    private void TaskRecieved(object? sender, TaskRecievedEventArgs e)
    {
        Debug.Log($"{e.Received}");
        if (e.Received.Contains("ok"))
            waitingForOk = false;
    }

    public void CustomOnApplicationQuit()
    {
        BetterTello.Events.OnStateRecieved -= OnStateUpdate;
        BetterTello.Events.OnVideoDataRecieved -= Tello_onVideoData;
        BetterTello.Factories.OnTaskRecieved -= TaskRecieved;
        Timestamps.Clear();
        BetterTello.Dispose();
    }
    void Awake()
    {
        this.flightPathController = GetComponent<FlightPathController>();
        if (telloVideoTexture == null)
            telloVideoTexture = FindObjectOfType<TelloVideoTexture>();
    }

    public void EmergencyStop()
    {
        BetterTello.Commands.Stop();
        BetterTello.Commands.Land();
        //CustomOnApplicationQuit();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
            BetterTello.Commands.Land();
        UpdateTransform();
        if (BetterTello?.State != null)
            FlyingState = BetterTello.State.FlyingState;
    }
    private void FixedUpdate()
    {
        flightPathController.CreateFlightPoint();
    }
    public async Task<int> Takeoff()
    {
        var r = await RunCommand(BetterTello.Commands.Takeoff);
        flightPathController.drawFlightPath = true;
        return r;
    }
    public async Task<int> Land()
    {
        flightPathController.drawFlightPath = false;
        var r = await RunCommand(BetterTello.Commands.Land);
        return r;
    }
    public async Task<int> Up(int x) => await RunCommand(BetterTello.Commands.Up, x);
    public async Task<int> Cw(int x) => await RunCommand(BetterTello.Commands.Cw, x);
    public async Task<int> Forward(int x) => await RunCommand(BetterTello.Commands.Forward, x);
    public async Task<int> Back(int x) => await RunCommand(BetterTello.Commands.Back, x);
    public async Task Run()
    {
        await Takeoff();
        await Scan();
        await Land();
        //BetterTello.Commands.Emergency();
    }

    public async Task Scan()
    {
        for (int i = 0; i < 100; i++)
            await Cw(5);
    }
    public async Task<int> RunCommand(Func<int, int> Function, int x)
    {
        Debug.Log("Sending command: " + Function.Method.Name + $"({x})");
        var ret = Function(x);
        await WaitForOk();
        return ret;
    }
    public async Task<int> RunCommand(Func<int> Function)
    {
        Debug.Log("Sending command: " + Function.Method.Name + "()");
        var ret = Function();
        await WaitForOk();
        return ret;
    }

    public async Task WaitForOk()
    {
        waitingForOk = true;
        while (waitingForOk)
            await Task.Delay(5);
        await Task.Delay(500);
    }


    public void TakeOff()
    {
        Debug.Log("TakeOff!");
        var preFlightPanel = GameObject.Find("Pre Flight Panel");
        if (preFlightPanel)
            preFlightPanel.SetActive(false);
        BetterTello.Commands.Takeoff();
        flightPathController.TakeOff(this);
    }

    public void OnStateUpdate(object? sender, StateEventArgs e)
    {
        var state = e.State;
        var vel = new Vector3(state.Vgx, state.Vgy, state.Vgz);
        PositionAcc += new Vector3(state.Agx, state.Agy, state.Agz);
        PositionMissionPad = new Vector3(state.X, state.Y, state.Z);
        Pitch = state.Pitch;
        Roll = state.Roll;
        Yaw = state.Yaw;
        PYR = Quaternion.Euler(Pitch, Yaw, Roll);
        ExtTof = state.ExtTof;
        Bat = state.Bat;
        Tof = state.Tof;
        Height = state.H;
        TempH = state.Temph;
        TempL = state.Templ;
        Timestamps.Add(state.Time);
        Vels.Add(vel);
        if (Timestamps.Count > 1 && Vels.Count > 1)
        {
            List<int> localtime = new();
            int prevtime = Timestamps.First();
            foreach (var item in Timestamps.ToArray()[1..^0])
                localtime.Add(Math.Abs(prevtime - item));
            List<Vector3> localvel = Vels.ToArray()[1..^0].ToList();
            for (int i = 0; i < localvel.Count; i++)
            {
                localvel[i] = new Vector3(localvel[i].x * localtime[i], localvel[i].y * localtime[i], localvel[i].z * localtime[i]);
            }
            PositionVel = new Vector3()
            {
                x = localvel.Select(p => p.y).Sum() / 100,
                y = localvel.Select(p => p.z).Sum() / 100,
                z = localvel.Select(p => p.x).Sum() / 100,
            };
        }
    }

    public void UpdateTransform()
    {
        if (ConnectionState == TelloConnectionState.Connected)
        {
            Transform.position = PositionVel;
            Transform.rotation = PYR;
        }
    }



    private void Tello_onVideoData(object? sender, VideoDataRecievedEventArgs data)
    {
        if (telloVideoTexture != null)
            telloVideoTexture.PutVideoData(data.Data);
    }
}
