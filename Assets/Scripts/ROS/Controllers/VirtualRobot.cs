﻿using System;
using System.Collections;
using System.Collections.Generic;
using ROSBridgeLib.geometry_msgs;
using ROSBridgeLib.nav_msgs;
using ROSBridgeLib.std_msgs;
using UnityEngine;

public class VirtualRobot : ROSController
{
    [SerializeField] private float _publishInterval = 0.05f;

    public ArlobotROSController.RobotLocomotionState CurrentRobotLocomotionState { get; private set; }
    public ArlobotROSController.RobotLocomotionType CurrenLocomotionType { get; private set; }

    private SensorBusController _sensorBusController;
    private Dictionary<Type, ROSAgent> _rosAgents;
    private List<Type> _agentsWaitingToStart;
    private Rigidbody _rigidbody;

    //Subscribers
    private ROSLocomotionDirect _rosLocomotionDirect;
    private bool _hasLocomotionDirectDataToConsume;
    private TwistMsg _locomotionDirectDataToConsume;
    private ROSGenericSubscriber<TwistMsg> _rosJoystick;
    private bool _hasJoystickDataToConsume;
    private TwistMsg _joystickDataToConsume;

    //Publishers
    private Coroutine _transformUpdateCoroutine;
    private ROSLocomotionWaypointState _rosLocomotionWaypointState;
    private ROSLocomotionWaypoint _rosLocomotionWaypoint;
    private ROSGenericPublisher _rosLocomotionLinear;
    private ROSGenericPublisher _rosLocomotionAngular;
    private ROSLocomotionControlParams _rosLocomotionControlParams;
    private ROSGenericPublisher _rosOdometry;

    //Modules
    private WaypointNavigation _waypointNavigationModule;

    //Navigation
    private Vector3 _currentWaypoint;
    private int _waypointIndex;
    private float _waypointDistanceThreshhold = 0.1f;
    private List<GeoPointWGS84> _waypoints;

    void Awake() {
        _rosAgents = new Dictionary<Type, ROSAgent>();
        _agentsWaitingToStart = new List<Type>();
        _rigidbody = GetComponent<Rigidbody>();
        _waypointNavigationModule = GetComponent<WaypointNavigation>();
        OnRosStarted += _waypointNavigationModule.InitialiseRos;

        _waypointDistanceThreshhold = ConfigManager.ConfigFile.WaypointDistanceThreshold;
        CurrenLocomotionType = ArlobotROSController.RobotLocomotionType.DIRECT;
        CurrentRobotLocomotionState = ArlobotROSController.RobotLocomotionState.STOPPED;
    }

    void Start() {
        _sensorBusController = new SensorBusController(this);
    }

    void Update() {
        if (_agentsWaitingToStart.Count > 0) {
            foreach (Type type in _agentsWaitingToStart) {
                StartAgent(type);
            }
            _agentsWaitingToStart = new List<Type>();
        }

        //Navigation to waypoint
        if (CurrenLocomotionType != ArlobotROSController.RobotLocomotionType.DIRECT && CurrentRobotLocomotionState != ArlobotROSController.RobotLocomotionState.STOPPED) {
            //Waypoint reached
            if (Vector3.Distance(transform.position, _currentWaypoint) < _waypointDistanceThreshhold) {
                if (_waypointIndex < _waypoints.Count - 1)
                    MoveToNextWaypoint();
                else {
                    EndWaypointPath();
                }
            }
        }

        if (_hasJoystickDataToConsume) {
            _rigidbody.velocity = transform.forward * (float)_joystickDataToConsume._linear._x;
            _rigidbody.angularVelocity = new Vector3(0, (float)-_joystickDataToConsume._angular._z, 0);
            _hasJoystickDataToConsume = false;
        }
        if (_hasLocomotionDirectDataToConsume)
        {
            _rigidbody.velocity = transform.forward * (float)_locomotionDirectDataToConsume._linear._x;
            _rigidbody.angularVelocity = new Vector3(0, (float)-_locomotionDirectDataToConsume._angular._z, 0);
            _hasLocomotionDirectDataToConsume = false;
        }
    }

    private void ReceivedJoystickUpdate(ROSBridgeMsg data) {
        _joystickDataToConsume = (TwistMsg)data;
        _hasJoystickDataToConsume = true;
    }

    private void ReceivedLocomotionDirectUpdate(ROSBridgeMsg data)
    {
        _locomotionDirectDataToConsume = (TwistMsg) data;
        _hasLocomotionDirectDataToConsume = true;
    }

    private IEnumerator SendTransformUpdate(float interval)
    {
        while (true)
        {
            GeoPointWGS84 wgs = transform.position.ToUTM().ToWGS84();
            UnityEngine.Quaternion rot = transform.rotation;
            PoseMsg pose = new PoseMsg(new PointMsg(wgs.longitude, wgs.latitude, wgs.altitude), 
                new QuaternionMsg(rot.x, rot.y, rot.z, rot.w));
            PoseWithCovarianceMsg poseWithCovariance = new PoseWithCovarianceMsg(pose, null);

            OdometryMsg odometry = new OdometryMsg();
            odometry._pose = poseWithCovariance;
            _rosOdometry.PublishData(odometry);

            yield return new WaitForSeconds(interval);
        }
    }

    //TODO: Rework SIM
    private void TransmitSensorData()
    {
        /*
        foreach (SensorBus sensorBus in _sensorBusController.SensorBusses)
        {
            if (!_rosAgents.ContainsKey(sensorBus.ROSAgentType)) continue;
            _rosAgents[sensorBus.ROSAgentType].PublishData(sensorBus.GetSensorData());
        }
        */
    }

    protected override void StopROS()
    {
        StopCoroutine(_transformUpdateCoroutine);
    }

    //TODO: Rework SIM
    public void StartAgent(Type agentType)
    {
        /*
        if (_rosBridge != null)
        {
            _agentsWaitingToStart.Add(agentType);
            return;
        }
        ROSAgent agent = (ROSAgent) Activator.CreateInstance(agentType);
        agent.StartAgent();
        _rosAgents.Add(agentType, agent);
        */
    }

    protected override void StartROS()
    {
        _rosLocomotionDirect = new ROSLocomotionDirect(ROSAgent.AgentJob.Subscriber, _rosBridge, "/cmd_vel");
        _rosLocomotionDirect.OnDataReceived += ReceivedLocomotionDirectUpdate;
        _rosJoystick = new ROSGenericSubscriber<TwistMsg>(_rosBridge, "/teleop_velocity_smoother/raw_cmd_vel", TwistMsg.GetMessageType(), (msg) => new TwistMsg(msg));
        _rosJoystick.OnDataReceived += ReceivedJoystickUpdate;

        _rosOdometry = new ROSGenericPublisher(_rosBridge, "/robot_gps_pose", OdometryMsg.GetMessageType());
        _transformUpdateCoroutine = StartCoroutine(SendTransformUpdate(_publishInterval));
        
        _rosLocomotionWaypointState = new ROSLocomotionWaypointState(ROSAgent.AgentJob.Publisher, _rosBridge, "/waypoint/state");
        _rosLocomotionWaypoint = new ROSLocomotionWaypoint(ROSAgent.AgentJob.Publisher, _rosBridge, "/waypoint");
        _rosLocomotionLinear = new ROSGenericPublisher(_rosBridge, "/waypoint/max_linear_speed", Float32Msg.GetMessageType());
        _rosLocomotionAngular = new ROSGenericPublisher(_rosBridge, "/waypoint/max_angular_speed", Float32Msg.GetMessageType());
        _rosLocomotionControlParams = new ROSLocomotionControlParams(ROSAgent.AgentJob.Publisher, _rosBridge, "/waypoint/control_parameters");

        _rosLocomotionLinear.PublishData(new Float32Msg(ConfigManager.ConfigFile.MaxLinearSpeed));
        _rosLocomotionAngular.PublishData(new Float32Msg(ConfigManager.ConfigFile.MaxAngularSpeed));
        _rosLocomotionControlParams.PublishData(ConfigManager.ConfigFile.ControlParameterRho, ConfigManager.ConfigFile.ControlParameterRoll,
            ConfigManager.ConfigFile.ControlParameterPitch, ConfigManager.ConfigFile.ControlParameterYaw);
}

    public override void MoveDirect(Vector2 command) {
        if (CurrenLocomotionType != ArlobotROSController.RobotLocomotionType.DIRECT)
            _rosLocomotionWaypointState.PublishData(ROSLocomotionWaypointState.RobotWaypointState.STOP);
        _rosLocomotionDirect.PublishData(command);
        CurrenLocomotionType = ArlobotROSController.RobotLocomotionType.DIRECT;
        CurrentRobotLocomotionState = ArlobotROSController.RobotLocomotionState.MOVING;
    }

    private void StartWaypointRoute() {
        _waypointIndex = 0;
        CurrenLocomotionType = ArlobotROSController.RobotLocomotionType.WAYPOINT;
        _currentWaypoint = _waypoints[_waypointIndex].ToUTM().ToUnity();
        Move(_currentWaypoint);
    }

    private void MoveToNextWaypoint() {
        _waypointIndex++;
        _currentWaypoint = _waypoints[_waypointIndex].ToUTM().ToUnity();
        Move(_currentWaypoint);
    }

    private void EndWaypointPath() {
        StopRobot();
        PlayerUIController.Instance.SetDriveMode(false);
    }

    private void Move(Vector3 position) {
        GeoPointWGS84 point = position.ToUTM().ToWGS84();
        _rosLocomotionWaypoint.PublishData(point);
        _currentWaypoint = position;
        CurrenLocomotionType = ArlobotROSController.RobotLocomotionType.WAYPOINT;
        _rosLocomotionWaypointState.PublishData(ROSLocomotionWaypointState.RobotWaypointState.RUNNING);
        CurrentRobotLocomotionState = ArlobotROSController.RobotLocomotionState.MOVING;
    }

    public override void MoveToPoint(GeoPointWGS84 point)
    {
        _waypoints.Clear();
        _waypoints.Add(point);
        _waypointIndex = 0;
    }

    public override void MovePath(List<GeoPointWGS84> waypoints) 
    {
        _waypoints = waypoints;
        StartWaypointRoute();
    }

    public override void PausePath()
    {
        _rosLocomotionWaypointState.PublishData(ROSLocomotionWaypointState.RobotWaypointState.PARK);
    }

    public override void ResumePath()
    {
        _rosLocomotionWaypointState.PublishData(ROSLocomotionWaypointState.RobotWaypointState.RUNNING);
    }

    public override void StopRobot()
    {
        CurrentRobotLocomotionState = ArlobotROSController.RobotLocomotionState.STOPPED;
        _rosLocomotionWaypointState.PublishData(ROSLocomotionWaypointState.RobotWaypointState.STOP);
        _rosLocomotionDirect.PublishData(Vector2.zero);
    }
}