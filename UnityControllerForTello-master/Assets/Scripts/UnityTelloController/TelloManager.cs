﻿using System.Collections.Generic;
using UnityEngine;
using TelloLib;
using System.Collections;

namespace UnityControllerForTello
{
    /// <summary>
    /// Displays information about the quadcopter from <see cref="TelloLib"/> library
    /// Takes inputs to send to the quadcopter
    /// </summary>
    public class TelloManager : MonoBehaviour
    {
        public Vector3 Offset = Vector3.zero;
        public string state;
        public int EXTToF;
        public bool drawFlightPath = true;
        public bool limitPathDistance = false;
        private TelloVideoTexture telloVideoTexture;
        public float yaw, pitch, roll;

        public float posX = 0, posY, posZ;
        public float quatW;
        public float quatX;
        public float quatY;
        public float quatZ;
        public Vector3 toEuler;
        public Quaternion onTrackStartRot;
        public Vector3 originPoint, originEuler;

        public bool flying = false, hovering = false;
        public bool tracking = false;

        private List<FlightPoint> flightPoints;
        private float prevPosX, prevPosY, prevPosZ;
        private Transform ground, telloGround, telloModel, flightPointsParent;
        private bool updateReceived = false;

        //Tello api
        public float posUncertainty;
        public bool batteryLow;
        public int batteryPercent;
        public int cameraState;
        public bool downVisualState;
        public int telloBatteryLeft;
        public int telloFlyTimeLeft;
        public int flymode;
        public int flyspeed;
        public int flyTime;
        public bool gravityState;
        public int height;
        public int imuCalibrationState;
        public bool imuState;
        public int lightStrength;
        public bool onGround = true;
        public bool powerState;
        public bool pressureState;
        public int temperatureHeight;
        public int wifiDisturb;
        public int wifiStrength;
        public bool windState;

        private bool startingProps = false;
        private int startUpCount = 0, startUpLimit = 300;


        private bool instatiatePoints = false;

        public SceneManager sceneManager;
        private InputController inputController;

        public int telloFrameCount = 0;
        public void CustomAwake(SceneManager sceneManager, InputController inputController)
        {
            this.sceneManager = sceneManager;
            this.inputController = inputController;
            try
            {
                telloModel = transform.Find("Tello Model");
                ground = transform.Find("Ground");
                telloGround = transform.Find("Tello Ground");
                flightPointsParent = GameObject.Find("Track Points").transform;
            }
            catch
            {
                Debug.Log("Missing a gameObject");
            }

            if (telloVideoTexture == null)
                telloVideoTexture = FindObjectOfType<TelloVideoTexture>();
        }

        /// <summary>
        /// Attempt to connect to the quadcopter via <see cref="Tello"/>
        /// Must be connected to quadcopter via wifi
        /// </summary>
        public void ConnectToTello()
        {
            Tello.onConnection += Tello_onConnection;
            Tello.onUpdate += Tello_onUpdate;
            Tello.onVideoData += Tello_onVideoData;
            Tello.startConnecting();
        }

        /// <summary>
        /// Send command to quadcopter to take off and elevate to quads predetermined height
        /// Cannot control the drone durring until <see cref="SceneManager.FlightStatus.Flying"/>
        /// </summary>
        public void AutoTakeOff()
        {
            tracking = true;
            drawFlightPath = true;
            Debug.Log("TakeOff!");
            var preFlightPanel = GameObject.Find("Pre Flight Panel");
            if (preFlightPanel)
                preFlightPanel.SetActive(false);
            Tello.takeOff();
            sceneManager.flightStatus = SceneManager.FlightStatus.Launching;
            StartCoroutine(ExecuteAfterTime(3));
        }
        IEnumerator ExecuteAfterTime(float time)
        {
            yield return new WaitForSeconds(time);
            Offset = new Vector3(posX /5, posY /2, posZ /5);
            instatiatePoints = true;
            tracking = true;
            drawFlightPath = true;
        }
        public void PrimeProps()
        {
            Debug.Log("Start Prop");
            sceneManager.flightStatus = SceneManager.FlightStatus.PrimingProps;
            Tello.SuspendControllerUpdate();
            int i = 0;
            do
            {
                i++;
                Tello.StartMotors();
            } while (i < 900);
            // OnFlyBegin();
            Tello.ResumeControllerUpdate();
            var preFlightPanel = GameObject.Find("Pre Flight Panel");
            if (preFlightPanel)
                preFlightPanel.SetActive(false);
            sceneManager.flightStatus = SceneManager.FlightStatus.Launching;
            Debug.Log("props started");
        }

        public void OnLand()
        {
            tracking = false;
            drawFlightPath = false;
            Debug.Log("Land");
            Tello.land();
            Offset = Vector3.zero;
            sceneManager.flightStatus = SceneManager.FlightStatus.PreLaunch;


        }

        public void CheckForUpdate()
        {
            if (updateReceived)
            {

                UpdateLocalState();
                sceneManager.RunFrame();
                SendTelloInputs();
                updateReceived = false;
                if (sceneManager.flightStatus == SceneManager.FlightStatus.Flying) {
                    CreateFlightPoint();
                }
            }
        }

        public void SendTelloInputs()
        {
            if (sceneManager.flightStatus != SceneManager.FlightStatus.PreLaunch)
            {
                Tello.controllerState.setAxis(sceneManager.yaw, sceneManager.elv, sceneManager.roll, sceneManager.pitch);
            }
        }

        Vector3 GetCurrentPos()
        {
            state = Tello.state.ToString();
            var telloPosX = posX - originPoint.x;
            var telloPosY = posY - originPoint.y;
            var telloPosZ = posZ - originPoint.z;

            return new Vector3(telloPosX, telloPosY, telloPosZ);     
        }

        public void CheckForLaunchComplete()
        {
            if (flymode == 6)
            {
                originPoint = GetCurrentPos();
                Debug.Log("Y Offset " + originPoint + " tello frame count " + telloFrameCount);
                originEuler = new Vector3(pitch, yaw, roll);
                // onTrackStartRot = new Quaternion(quatW, quatX, quatY, quatZ);
                ground.position -= new Vector3(0, height * .1f, 0);
                flightPoints = new List<FlightPoint>();
                CreateFlightPoint();

                Debug.Log("tello height set to " + height * .1f);
                telloGround.position = transform.position - new Vector3(0, height * .1f, 0);
                elevationOffset = height * .1f;
                sceneManager.SetHomePoint(new Vector3(0, height * .1f, 0));
                sceneManager.flightStatus = SceneManager.FlightStatus.Flying;
            }
        }

        public bool SetTelloPosition()
        {
            validTrackingFrame = true;
            var currentPos = GetCurrentPos();
            Vector3 dif = currentPos - transform.position;
            //transform.position = currentPos - Offset;

            var xDif = dif.x;
            var yDif = dif.y;
            var zDif = dif.z;

            //valid tello frame
            if (Mathf.Abs(xDif) < 2000 & Mathf.Abs(yDif) < 2000 & Mathf.Abs(zDif) < 2000)
            {
                transform.position += new Vector3(0, elevationOffset, 0);
                prevDeltaPos = dif;
                Vector3 flightPointDif = flightPoints[flightPoints.Count - 1].transform.position - currentPos;
                if (flightPointDif.magnitude > .001f)
                {
                    CreateFlightPoint();
                }
                yaw = yaw * (180 / Mathf.PI);
                transform.eulerAngles = new Vector3(0, yaw, 0);
                pitch = pitch * (180 / Mathf.PI);
                roll = roll * (180 / Mathf.PI);
                telloModel.localEulerAngles = new Vector3(pitch - 90, 0, roll);
            }
            else
            {
                Debug.Log("Tracking lost " + Mathf.Abs(xDif) + "/" + Mathf.Abs(yDif) + "/" + Mathf.Abs(zDif));

                validTrackingFrame = false;
            }
            return validTrackingFrame;
        }

        bool validTrackingFrame;
        Vector3 prevDeltaPos;

        float elevationOffset;

        /// <summary>
        /// Create a point for tracking
        /// </summary>
        void CreateFlightPoint()
        {
            if (!instatiatePoints) return;
            var newPoint = Instantiate(GameObject.Find("FlightPoint")).GetComponent<FlightPoint>();
            newPoint.transform.position = transform.position;
            newPoint.transform.SetParent(flightPointsParent);
            newPoint.CustomStart();

            if (flightPoints.Count > 0 & drawFlightPath)
            {
                newPoint.SetPointOne(flightPoints[flightPoints.Count - 1].transform.position);
            }
            flightPoints.Add(newPoint);
        }
        private void Tello_onConnection(Tello.ConnectionState newState)
        {
            if (newState == Tello.ConnectionState.Connected)
            {
                Debug.Log("Connected to Tello, please wait for camera feed");
                Tello.setPicVidMode(1); // 0: picture, 1: video
                Tello.setVideoBitRate((int)TelloController.VideoBitRate.VideoBitRateAuto);
                Tello.requestIframe();
            }
        }
        //Dealing with telloLib
        private void Tello_onUpdate(int cmdID)
        {
            updateReceived = true;
        }
        //This just saves all the tello variables locally for viewing in the inspector
        public void UpdateLocalState()
        {
            var state = Tello.state;

            posX = Tello.state.posY;
            posY = -Tello.state.posZ;
            posZ = Tello.state.posX;

            transform.position = new Vector3((posX / 5) - Offset.x, (posY / 2) - Offset.y, (posZ / 5) - Offset.z);

            quatW = state.quatW;
            quatX = state.quatW;
            quatY = state.quatW;
            quatZ = state.quatW;

            var eulerInfo = state.toEuler();

            pitch = (float)eulerInfo[0];
            roll = (float)eulerInfo[1];
            yaw = (float)eulerInfo[2];

            toEuler = new Vector3(pitch, roll, yaw);

            posUncertainty = state.posUncertainty;
            batteryLow = state.batteryLow;
            batteryPercent = state.batteryPercentage;
            cameraState = state.cameraState;
            downVisualState = state.downVisualState;
            telloBatteryLeft = state.droneBatteryLeft;
            telloFlyTimeLeft = state.droneFlyTimeLeft;
            flymode = state.flyMode;
            flyspeed = state.flySpeed;
            flyTime = state.flyTime;
            gravityState = state.gravityState;
            height = state.height;
            imuCalibrationState = state.imuCalibrationState;
            imuState = state.imuState;
            lightStrength = state.lightStrength;
            onGround = state.onGround;
            powerState = state.powerState;
            pressureState = state.pressureState;
            temperatureHeight = state.temperatureHeight;
            wifiDisturb = state.wifiDisturb;
            wifiStrength = state.wifiStrength;
            windState = state.windState;
        }
        public void CustomOnApplicationQuit()
        {
            Tello.onConnection -= Tello_onConnection;
            Tello.onUpdate -= Tello_onUpdate;
            Tello.onVideoData -= Tello_onVideoData;
            Tello.stopConnecting();
        }
        private void Tello_onVideoData(byte[] data)
        {
            if (telloVideoTexture != null)
                telloVideoTexture.PutVideoData(data);
        }
    }
}