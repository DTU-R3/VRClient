﻿using System;
using System.Collections.Generic;
using ROSBridgeLib;
using UnityEngine;

[RequireComponent(typeof(RobotLogger))]
public abstract class ROSController : MonoBehaviour
{
    public enum RobotLocomotionState
    {
        MOVING,
        STOPPED
    }

    public enum RobotLocomotionType
    {
        WAYPOINT,
        DIRECT
    }

    public delegate void RosStarted(ROSBridgeWebSocketConnection rosBridge);

    public event RosStarted OnRosStarted;

    public RobotLocomotionState CurrentRobotLocomotionState { get; protected set; }
    public RobotLocomotionType CurrenLocomotionType { get; protected set; }
    [HideInInspector] public RobotConfigFile RobotConfig;
    [HideInInspector] public string RobotName;

    [SerializeField] public List<RobotModule> _robotModules;

    protected bool _robotModelInitialised;
    protected ROSBridgeWebSocketConnection _rosBridge;
    protected List<WaypointController.Waypoint> Waypoints = new List<WaypointController.Waypoint>();
    protected RobotLogger _robotLogger;
    protected bool _shouldClose;

    protected virtual void Awake()
    {
        _robotLogger = GetComponent<RobotLogger>();
    }

    protected virtual void Update()
    {
        if (_shouldClose)
        {
            StopROS();
        }
    }

    protected virtual void OnApplicationQuit()
    {
        StopROS();
    }

    protected abstract void StartROS();

    /// <summary>
    /// Disconnects from rosbridge.
    /// </summary>
    protected virtual void StopROS()
    {
        _rosBridge.Disconnect();
    }

    protected virtual void LostConnection()
    {
        RobotMasterController.Instance.RobotLostConnection(this);
    }

    /// <summary>
    /// Disconnects from rosbridge and destroys robot gameobject.
    /// </summary>
    public void Destroy()
    {
        StopROS();
        Destroy(gameObject);
    }

    public abstract void MoveDirect(Vector2 movementCommand);

    public abstract void MovePath(List<WaypointController.Waypoint> waypoints);

    public abstract void PausePath();

    public abstract void ResumePath();

    public abstract void StopRobot();

    public abstract void OverridePositionAndOrientation(Vector3 position, Quaternion orientation);

    /// <summary>
    /// When robot is selected on the "Select Robot" dropdown menu. 
    /// Subscribes to relevant scripts and updates UI.
    /// </summary>
    public virtual void OnSelected()
    {
        if (Waypoints != null && RobotMasterController.SelectedRobot == this)
            WaypointController.Instance.CreateRoute(Waypoints);
    }

    /// <summary>
    /// When robot is deselected from the "Select Robot" dropdown menu.
    /// Ubsubscribes from relevant scripts and updates UI.
    /// </summary>
    public virtual void OnDeselected()
    {
    }

    /// <summary>
    /// Initialises robot and ROS Bridge connections and starts all attached modules.
    /// </summary>
    /// <param name="rosBridge">Ros bridge connection.</param>
    /// <param name="robotConfig">Config file that contains robot parameters.</param>
    public virtual void InitialiseRobot(ROSBridgeWebSocketConnection rosBridge, RobotConfigFile robotConfig, string robotName)
    {
        RobotName = robotName;
        _rosBridge = rosBridge;
        _rosBridge.OnDisconnect += clean =>
        {
            if (!clean) LostConnection();
        };
        RobotConfig = robotConfig;
        StartROS();

        foreach (RobotModule module in _robotModules)
        {
            module.Initialise(rosBridge);
        }

        if (OnRosStarted != null)
            OnRosStarted(rosBridge);

    }

    public virtual void ResetRobot() { }

    public abstract List<RobotLog> GetRobotLogs();

    public virtual void SubscribeToRobotLogsUpdate(RobotLogger.ReceivedRobotLog subscriber)
    {
        _robotLogger.OnReceivedRobotLog += subscriber;
    }

    public virtual void UnsubscribeToRobotLogsUpdate(RobotLogger.ReceivedRobotLog subscriber)
    {
        if (_robotLogger != null)
            _robotLogger.OnReceivedRobotLog -= subscriber;
    }

}