﻿/*
EGP 405-01 Final Project 12/7/17
Aaron Hamilton
James Smith
Thomas McGillicuddy
“We certify that this work is
entirely our own. The assessor of this project may reproduce this project
and provide copies to other academic staff, and/or communicate a copy of
this project to a plagiarism-checking service, which may retain a copy of the
project on its database.”
*/

using System.Collections;
using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Net;


[InitializeOnLoad]
[ExecuteInEditMode]
public class MultiuserPlugin
{
    //Importing DLL functions
    [DllImport("UnityMultiuserPlugin")]
    public static extern int StartServer(string targetIP, int portNum, int maxClients);
    [DllImport("UnityMultiuserPlugin")]
    public static extern int StartClient(string targetIP, int portNum, int maxClients);
    [DllImport("UnityMultiuserPlugin")]
    public static extern unsafe char* GetData();
    [DllImport("UnityMultiuserPlugin")]
    public static extern unsafe int SendData(int mID, string data, int length, string ownerIP);
    [DllImport("UnityMultiuserPlugin")]
    public static extern int Shutdown();
    [DllImport("UnityMultiuserPlugin")]
    public static extern unsafe char* GetLastPacketIP();

    //Unity Varibles
    public static bool mConnected, mIsPaused, mIsServer;  //If the system is running;
    public static float syncInterval = 0.5f;   //How often system should sync
    static DateTime lastSyncTime = DateTime.Now;
    public static mode toolMode;
    public static string objectId;
    public static int objCounter = 0;
    public static string serverIP;
    public static int clientID;
    public static bool newMessage;

    public static GameObject[] allGOs;

    struct ConnectedClientInfo  //For storing connected client information
    {
        public string IP;
        public string ID;
    }


    static List<ConnectedClientInfo> mConnectedClients = new List<ConnectedClientInfo>();

    public enum mode
    {
        EDIT,
        VIEW,
    }

    static MultiuserPlugin()
    {
        EditorApplication.update += Update;
        mConnected = false;
    }

    //Update Loop
    static void Update()
    {
        if (!Application.isPlaying && !mIsPaused)   // Only run the systems when the game is not in play mode and the user hasn't paused the sync system
        {
            if (mConnected)
            {
                if (toolMode == mode.EDIT)
                    editMode();
                else if (toolMode == mode.VIEW)
                    viewMode();

                checkData();
            }
        }

    }

    static void editMode()
    {
        GameObject[] allGameobjects = GameObject.FindObjectsOfType<GameObject>();
        List<GameObject> selectedObjects = new List<GameObject>(Selection.gameObjects);

        if (selectedObjects.Count > 0)
        {
            List<GameObject> approvedObjects = new List<GameObject>();
           
            for (int i = 0; i < selectedObjects.Count; ++i)
            {
                MarkerFlag selectedObjFlags = selectedObjects[i].GetComponent<MarkerFlag>();
                if (selectedObjFlags == null)    //If an object doesn't have the marker flag script on it
                {
                    //it will be added
                    selectedObjFlags = selectedObjects[i].AddComponent<MarkerFlag>();
                    selectedObjFlags.id = objectId + objCounter.ToString();
                    objCounter++;
                }
                else
                {
                    if (!selectedObjFlags.isLocked)
                    {
                        selectedObjFlags.isModified = true;
                        selectedObjFlags.isHeld = true;
                        approvedObjects.Add(selectedObjects[i]);
                    }
                }
            }
            Selection.objects = approvedObjects.ToArray();
        }

        for (int i = 0; i < allGameobjects.Length; ++i)
        {
            MarkerFlag pMarkerFlag = allGameobjects[i].GetComponent<MarkerFlag>();
            if (pMarkerFlag.isHeld) //If the object is marked as is held
            {
                if (!selectedObjects.Contains(allGameobjects[i])) //If the object isn't still selected
                {
                    pMarkerFlag.isHeld = false;
                    UnlockObject(pMarkerFlag);
                }
            }
        }

        //If the system is running AND the sync interval is 0 or if the current time is greater than the last sync time + the sync interval
        if (mConnected && (syncInterval == 0 || DateTime.Now.Minute * 60 + DateTime.Now.Second >=
            (lastSyncTime.Second + syncInterval + lastSyncTime.Minute * 60)))
        {
            Sync(Selection.gameObjects);
            lastSyncTime = DateTime.Now;
        }
    }

    static void viewMode()
    {
        Selection.activeObject = new UnityEngine.Object(); // prevents selection in heirarchy window ( can still select but can't do anything with selection, it only highlights in heirarchy window)
        Selection.objects = new UnityEngine.Object[0]; // always sets selection to a new gameobject that doesn't exist
    }

    public static void startupServer(int portNum, int maxClients)
    {
        Serializer.init();
        EditorUtility.DisplayProgressBar("Setting up", "", 0);
        objectId = "Server ";
        //Runs through entire scene and setups marker flags
        objCounter = 0;
        allGOs = GameObject.FindObjectsOfType<GameObject>();   //Get all gameobjs
        for (int i = 0; i < allGOs.Length; ++i)
        {
            MarkerFlag objectFlag = allGOs[i].GetComponent<MarkerFlag>();
            if (objectFlag == null)
                objectFlag = allGOs[i].AddComponent<MarkerFlag>();

            objectFlag.id = objectId + objCounter;

            Serializer.addToMap(objectFlag);

            objCounter++;
            EditorUtility.DisplayProgressBar("Setting up", allGOs[i].name, (float)i / (allGOs.Length - 1));
        }

        // get server ip address
        string hostName = System.Net.Dns.GetHostName();
        IPHostEntry ipEntry = System.Net.Dns.GetHostEntry(hostName);
        IPAddress[] addr = ipEntry.AddressList;

        //int index = 0;
        //foreach (IPAddress i in addr)
        //    index++;
        //serverIP = addr[3].ToString();
        //Debug.Log(serverIP);

        //Calls plugin function to start server
        StartServer("", portNum, maxClients);

        mIsServer = true;
        mConnected = true;

        EditorUtility.ClearProgressBar();

        ServerUtil.saveToNewScene();
        if (MultiuserEditor.limitAutosave)
            ServerUtil.checkTooManyScenes();
    }

    public static void UnlockObject(MarkerFlag target)
    {
        if (mConnected)
        {
            string targetID = Serializer.serialize(target.gameObject);
            if (!mIsServer)
            {
                SendData((int)Serializer.Message.GO_UPDATE, targetID, targetID.Length, "");
            }
            else
            {
                for (int j = 0; j < mConnectedClients.Count; ++j)
                {
                    if (mConnectedClients[j].IP != "")
                    {
                        SendData((int)Serializer.Message.GO_UPDATE, targetID, targetID.Length, mConnectedClients[j].IP);
                    }
                }
            }
        }
    }
    public static void startupClient(string targetIP, int portNum)
    {
        Serializer.init();

        serverIP = targetIP;

        //Clears any gameobjects from the current scene //TODO: (might change to just open new scene)
        objCounter = 0;
        GameObject[] allGameobjects = GameObject.FindObjectsOfType<GameObject>();   //Get all gameobjs

        for (int i = 0; i < allGameobjects.Length; ++i)
            MonoBehaviour.DestroyImmediate(allGameobjects[i]);

        StartClient(targetIP, portNum, 0);
        clientID++;

        mIsServer = false;
        mConnected = true;
    }

    public static void Sync(GameObject[] gOsToSend)   //Sends out the data of the "modified" objects
    {
        for (int i = 0; i < gOsToSend.Length; ++i) //Checks All objects in scene and 
        {
            MarkerFlag objectFlag = gOsToSend[i].GetComponent<MarkerFlag>();   //TODO: Potentially expensive might change

            if (objectFlag == null)    //If an object doesn't have the marker flag script on it
            {                          //it will be added. This happens when a new object has been made

                objectFlag = gOsToSend[i].AddComponent<MarkerFlag>();
                objectFlag.name = objectId + objCounter; //Make a uniquie name for the client so that other objects can't get confused by it
                objCounter++;
            }

            if (objectFlag.isModified)    //If this object's marker flag has been modified
            {
                string serializedObj = Serializer.serialize(gOsToSend[i]);

                if (!mIsServer)
                    SendData((int)Serializer.Message.GO_UPDATE, serializedObj, serializedObj.Length, serverIP);
                else
                    for (int j = 0; j < mConnectedClients.Count; ++j)
                        if (mConnectedClients[j].IP != "")
                            SendData((int)Serializer.Message.GO_UPDATE, serializedObj, serializedObj.Length, mConnectedClients[j].IP);

                objectFlag.isModified = false;
            }
        }
    }

    static unsafe void checkData()  //Checks the plugin network loop for a packet
    {
        char* data = GetData();
        if (data == null)
            return;
        else
            Serializer.deserializeMessage(data);
    }

    public static unsafe void Echo(Serializer.Message front ,string message) //Used to send the given message with the given message ID to all clients minus the client with the IP of the last packet
    {
        char* oGIP = GetLastPacketIP(); //Get the IP of the packet from the original sender to prevent ghosting
        IntPtr careIP = (IntPtr)oGIP;
        StraightCharPointer* IPdata = (StraightCharPointer*)careIP;
        string newIP = Marshal.PtrToStringAnsi((IntPtr)IPdata->mes);

        for (int i = 0; i < mConnectedClients.Count; ++i)
            if (mConnectedClients[i].IP != newIP)
                SendData((int)front, message, message.Length, mConnectedClients[i].IP);
    }

    public static void DeleteObject(MarkerFlag target)
    {
        if (mConnected)
        {
            string targetID = target.id + "|";
            if (!mIsServer)
                SendData((int)Serializer.Message.GO_DELETE, targetID, targetID.Length, "");
            else
                for (int j = 0; j < mConnectedClients.Count; ++j)
                    if (mConnectedClients[j].IP != "")
                        SendData((int)Serializer.Message.GO_DELETE, targetID, targetID.Length, mConnectedClients[j].IP);
        }
    }

    public static unsafe void addClient()
    {
        char* ip = GetLastPacketIP();
        IntPtr careTwo = (IntPtr)ip;
        StraightCharPointer* dataTwo = (StraightCharPointer*)careTwo;
        string newIP = Marshal.PtrToStringAnsi((IntPtr)dataTwo->mes);
        ConnectedClientInfo newClient = new ConnectedClientInfo();
        newClient.IP = newIP;
        newClient.ID = clientID.ToString();
        ++clientID;
        mConnectedClients.Add(newClient);

        string gOCount = allGOs.Length.ToString() + "|";
        SendData((int)Serializer.Message.LOADLEVEL, gOCount, gOCount.Length, newIP);
        for (int i = 0; i < allGOs.Length; ++i)
        {
            string serializedObj = Serializer.serialize(allGOs[i]);
            SendData((int)Serializer.Message.GO_UPDATE, serializedObj, serializedObj.Length, newIP);
        }

        SendData((int)Serializer.Message.LEVELLOADED, gOCount, gOCount.Length, newIP);
    }

    public static void SendMessageOverNetwork(string message)
    {
        switch (mIsServer)
        {
            case true:
                {
                    foreach (ConnectedClientInfo c in mConnectedClients)
                        SendData((int)Serializer.Message.CHAT_MESSAGE, message, message.Length, c.IP);
                    break;
                }

            case false:
                {
                    SendData((int)Serializer.Message.CHAT_MESSAGE, message, message.Length, serverIP);
                    break;
                }
        }
    }

    public static void handleChatMessage(string message)
    {
        MultiuserEditor.messageStack.Add(message);

        if (mIsServer)
            SendMessageOverNetwork(message);

        newMessage = true;
    }

    public static void Disconnect()
    {
        Shutdown();
        mConnected = false;
    }
}

