﻿using System.IO;
using ROSBridgeLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

//The control interface to the robot
//TODO: To be changed to ROS
public class RobotInterface : MonoBehaviour
{
    public enum CommandType
    {
        TurnSeatLeft,
        TurnSeatRight,
        TurnRobotLeft,
        TurnRobotRight,
        DriveRobotForwards,
        DriveRobotReverse,
        ParkingBrakeEngaged,
        ParkingBrakeDisengaged,
        StopRobot
    }

    private string _telerobotConfigPath;

    public static RobotInterface Instance { get; private set; }
    public bool Parked { get; private set; }

    public bool IsConnected { get; private set; }
    public AnimationCurve SpeedCurve;

    [SerializeField] private int _motorMaxSpeed = 255;
    [SerializeField] private int _motorMinSpeed = 60;
    [SerializeField] private float _commandTimer = 50;

    [SerializeField] private float MaximumVelocity = 0.4f;
    [SerializeField] private float BackwardsVelocity = 0.1f;

    [SerializeField] private float MaxVelocityHorizon = 0;
    [SerializeField] private float UpperDeadZoneLimit = -0.5f;
    [SerializeField] private float LowerDeadZoneLimit = -0.7f;
    [SerializeField] private float RightDeadZoneLimit = 0.2f;
    [SerializeField] private float LeftDeadZoneLimit = -0.2f;

    [SerializeField] private float LeftBackZoneLimit = -0.2f;
    [SerializeField] private float UpperBackZoneLimit = -0.8f;

    private float _timer = 0;
    private Vector2 InitRange;
    private Vector2 NewRange;
    private string _portName;
    private bool _left, _right, _forward, _reverse;
    private bool _isStopped;
    private ROSLocomotionDirect _rosLocomotionDirect;
    private ROSBridgeWebSocketConnection _rosBridge;
    private Telerobot_ThetaFile _telerobotConfigFile;

    void Awake()
    {
        Instance = this;
        _telerobotConfigPath = Application.streamingAssetsPath + "/Config/Telerobot_ThetaS.json";
    }

    void Start()
    {
        string robotFileJson = File.ReadAllText(_telerobotConfigPath);
        _telerobotConfigFile = JsonUtility.FromJson<Telerobot_ThetaFile>(robotFileJson);

        InitRange = new Vector2(UpperDeadZoneLimit , MaxVelocityHorizon);
        NewRange = new Vector2(0,MaximumVelocity);
       // Debug.log(_telerobotConfigFile);
    }

    void OnApplicationQuit()
    {
        if (_rosBridge != null)
            _rosBridge.Disconnect();
    }

    private string GetMotorSpeedString(float speed)
    {
        int intIntensity = (int) (SpeedCurve.Evaluate(Mathf.Abs(speed)) * (_motorMaxSpeed - _motorMinSpeed) + _motorMinSpeed);
        if (speed == 0)
            return "000";
        else
            return intIntensity.ToString("000");
    }

    private void SendCommandToRobot(Vector2 controlOutput)
    {
        //Debug.Log("Sending command to robot");
       // Debug.Log("ControlOutput is :" + controlOutput.x +"  " +  controlOutput.y);

       // Debug.Log("Sending command to robot");
        Vector2 movement = new Vector2(controlOutput.y, -controlOutput.x);

       
         Debug.Log("Intial Linear speed was :" + movement.x + "Initial Angular speed was : " + movement.y);
        //if you are not at the dead zone 
        if (!InsideDeadZone(movement.x, movement.y))
        {
            //normalize speed and send data
            movement = new Vector2(FilterLinearVelocity(movement.x), movement.y);
            //Debug.Log("Normalized Linear speed was :" + movement.x + "Normalized Initial Angular speed was : " +
            // movement.y);
            _rosLocomotionDirect.PublishData(movement.x, movement.y);
            _isStopped = false;
        }
        else
        {
            Debug.Log("Inside Dead Zone");
            Debug.Log("Linear speed was :" + 0 + "Angular speed was : " + 0);
            _rosLocomotionDirect.PublishData(0, 0);
            _isStopped = false;
        }

    }

    public void StopRobot()
    {
        if (!IsConnected || _isStopped) return;
        _rosLocomotionDirect.PublishData(0, 0);
        _isStopped = true;
    }

    public void SendCommand(Vector2 controlOutput)
    {
        
        if (!IsConnected)
        {
            Debug.Log("RobotInterace not connected");
            return;
        }
       
        if (_timer < _commandTimer / 1000f)
        {
            _timer += Time.deltaTime;
            return;
        }
        _timer = 0;

        SendCommandToRobot(controlOutput);
    }

    public void SetParkingBrake(bool isOn)
    {
        if (isOn)
        {
            GuiController.Instance.SetRobotControlVisibility(false);
            StreamController.Instance.EnableParkedMode();
        }
        else
        {
            StreamController.Instance.DisableParkedMode();
            GuiController.Instance.SetSeatControlVisibility(false);
            VRController.Instance.CenterSeat();
        }
        Parked = isOn;
    }

    public void DoneEnableDrivingMode()
    {
        GuiController.Instance.SetRobotControlVisibility(true);
        Viewport.Instance.SetEnabled(true);
    }

    public void DoneEnableParkMode()
    {
        GuiController.Instance.SetSeatControlVisibility(true);
    }

    public void Connect()
    {
        //adding the ws in the uri is essential : copied this from robotmastercontroller
        if (!_telerobotConfigFile.RosBridgeUri.StartsWith("ws://"))
            _telerobotConfigFile.RosBridgeUri = "ws://" + _telerobotConfigFile.RosBridgeUri;

        _rosBridge = new ROSBridgeWebSocketConnection(_telerobotConfigFile.RosBridgeUri, _telerobotConfigFile.RosBridgePort, "Telerobot_ThetaS");
        _rosLocomotionDirect = new ROSLocomotionDirect(ROSAgent.AgentJob.Publisher, _rosBridge, "/cmd_vel");
        _rosBridge.Connect(((s, b) => { Debug.Log(s + " - " + b); }));
        IsConnected = true;
    }

    public float NormalizeValues(float value)
    {

        return (((NewRange.y - NewRange.x) * (value - InitRange.x)) / (InitRange.y - InitRange.x) ) + NewRange.x;

    }

    public float FilterLinearVelocity(float InitValue)
    {
        //From this point and upwards, you move with maximum velocity
        if (InitValue >= MaxVelocityHorizon)
        {
            Debug.Log("Maximum Velocity");
            return MaximumVelocity;
        }
        //between that point and until the dead zone move with adjusted speed
        else if (InitValue < MaxVelocityHorizon && InitValue > UpperDeadZoneLimit)
        {
            float vel = NormalizeValues(InitValue);
            Debug.Log("Normalized Velocity : " + vel );
            return vel;
        }
        else if (InitValue < UpperBackZoneLimit && InitValue > -1)
        {
            Debug.Log("Normalized Velocity : " );
            return BackwardsVelocity;
        }


        return 0;
    }

    //returns true if we are in the bounding box of the dead zone
    private bool InsideDeadZone(float x, float y)
    {
        if (x > LeftDeadZoneLimit && x <= RightDeadZoneLimit
            && y > LowerDeadZoneLimit && y < UpperDeadZoneLimit)
        {
            return true;
        }
            

        return false;
    }

    //returns true if we are in the bounding box of the dead zone
    private bool InsideBackZone(float x, float y)
    {
        //leftbackzone is negative
        if (x > LeftBackZoneLimit && x <= -LeftBackZoneLimit
            && y <UpperBackZoneLimit)
            return true;

        return false;
    }
}