﻿using System.Collections;
using System.Collections.Generic;
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Runtime.InteropServices;


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
    public static extern unsafe int SendData(string data, int length, string ownerIP);
    [DllImport("UnityMultiuserPlugin")]
    public static extern int Shutdown();
    [DllImport("UnityMultiuserPlugin")]
    public static extern unsafe char* GetLastPacketIP();
    [DllImport("UnityMultiuserPlugin")]
    public static extern unsafe int SendMessageData(string data, int length, string ownerIP);

    //Unity Varibles
    public static bool mConnected, mIsPaused, mIsServer;  //If the system is running;
    public static float syncInterval = 0.5f;   //How often system should sync
    static DateTime lastSyncTime = DateTime.Now;
    public static mode toolMode;
    public static string objectId;
    public static int objCounter = 0;
    public static int clientID = 0;

    struct ConnectedClientInfo  //For storing connected client information
    {
        public string IP;
        public string userName;
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
            //Debug.Log(mConnected);
            if (mConnected)
            {
                if (toolMode == mode.EDIT)
                {
                    editMode();
                }
                else if (toolMode == mode.VIEW)
                {
                    viewMode();
                }
                else if (mIsServer)
                {
                }
                checkData();
            }
        }

    }

    static void editMode()
    {
        if (Selection.gameObjects.Length > 0)
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            for (int i = 0; i < selectedObjects.Length; ++i)
            {
                MarkerFlag selectedObjFlags = selectedObjects[i].GetComponent<MarkerFlag>();
                if (selectedObjFlags == null)    //If an object doesn't have the marker flag script on it
                {                                                           //it will be added
                    selectedObjFlags = selectedObjects[i].AddComponent<MarkerFlag>();
                }
                selectedObjFlags.isModified = true;
                selectedObjFlags.isLocked = true;
            }
        }

        //If the system is running AND the sync interval is 0 or if the current time is greater than the last sync time + the sync interval
        if (mConnected && (syncInterval == 0 || DateTime.Now.Minute * 60 + DateTime.Now.Second >=
            (lastSyncTime.Second + syncInterval + lastSyncTime.Minute * 60)))
        {
            Sync();
            lastSyncTime = DateTime.Now;
        }
        else
        {
            Debug.Log((DateTime.Now.Minute * 60 + DateTime.Now.Second) - (lastSyncTime.Second + syncInterval + lastSyncTime.Minute * 60));
        }
    }

    static void viewMode()
    {
        Selection.activeObject = null;
        // TODO:: force deselection
    }

    public static void startupServer(int portNum, int maxClients)
    {
        objectId = "Server ";
        //Runs through entire scene and setups marker flags
        objCounter = 0;
        GameObject[] allGameobjects = GameObject.FindObjectsOfType<GameObject>();   //Get all gameobjs
        for (int i = 0; i < allGameobjects.Length; ++i)
        {
            MarkerFlag objectFlag = allGameobjects[i].GetComponent<MarkerFlag>();
            if (objectFlag == null)
            {
                objectFlag = allGameobjects[i].AddComponent<MarkerFlag>();
            }

            objectFlag.id = objectId + objCounter;

            StructScript.addToMap(objectFlag);

            objCounter++;
        }

        //Calls plugin function to start server
        StartServer("", portNum, maxClients);

        mIsServer = true;
        mConnected = true;

        ServerUtil.saveToNewScene();
        if (Multiuser_Editor_Window.limitAutosave)
            ServerUtil.checkTooManyScenes();
    }

    public static void startupClient(string targetIP, int portNum)
    {
        //Clears any gameobjects from the current scene //TODO: (might change to just open new scene)
        objCounter = 0;
        GameObject[] allGameobjects = GameObject.FindObjectsOfType<GameObject>();   //Get all gameobjs
        for (int i = 0; i < allGameobjects.Length; ++i)
        {
            GameObject.DestroyImmediate(allGameobjects[i]);
            //TODO: Destroy the objects
        }

        //TODO: Start client with given port num, targetIP and password
        StartClient(targetIP, portNum, 0);

        mIsServer = false;
        mConnected = true;
    }

    public static void Sync()   //Sends out the data of the "modified" objects
    {
        GameObject[] allGameobjects = GameObject.FindObjectsOfType<GameObject>();
        //Debug.Log("Syncing");

        for (int i = 0; i < allGameobjects.Length; ++i) //Checks All objects in scene and 
        {
            MarkerFlag objectFlag = allGameobjects[i].GetComponent<MarkerFlag>();   //TODO: Potentially expensive might change

            if (objectFlag == null)    //If an object doesn't have the marker flag script on it
            {                          //it will be added. This happens when a new object has been made
                objectFlag = allGameobjects[i].AddComponent<MarkerFlag>();
                objectFlag.name = objectId + objCounter; //Make a uniquie name for the client so that other objects can't get confused by it
                objCounter++;
            }

            if (objectFlag.isModified)    //If this object's marker flag has been modified
            {
                string temp = StructScript.serialize(allGameobjects[i]);

                if (!mIsServer)
                {
                    //   Debug.Log("Test Sending to server"); 
                    SendData(temp, temp.Length, "");
                }
                else
                {
                    //  Debug.Log("Test Broadcasting");

                    for (int j = 0; j < mConnectedClients.Count; ++j)
                    {
                        Debug.Log(mConnectedClients[j].IP);
                        if (mConnectedClients[j].IP != "")
                        {
                            SendData(temp, temp.Length, mConnectedClients[j].IP);
                        }
                    }
                }
                objectFlag.isModified = false;
            }

            if (!Selection.Contains(allGameobjects[i]))
            {
                objectFlag.isLocked = false;
            }

        }
    }
    unsafe struct MyStringStruct
    {
        public int id;
        public fixed char pseudoString[512];
    }
    static unsafe void checkData()  //Checks the plugin network loop for a packet
    {
        //char* data = null;
        char* data = GetData();
        // Debug.Log(data[0]);
        if (data == null)
        {
            //Debug.Log(temp);
            return;
        }
        StructScript.deserializeMessage(data);
    }

    public static void testSerialize(GameObject testObj)
    {
        //  Debug.Log("Testing selected obj(s)");
        string temp = StructScript.serialize(testObj);
        //Debug.Log(temp);
        if (Selection.gameObjects.Length > 0)
        {
            GameObject[] testObjs = Selection.gameObjects;
            for (int i = 0; i < testObjs.Length; ++i)
            {
                if (!mIsServer)
                {
                    //   Debug.Log("Test Sending to server"); 
                    SendData(temp, temp.Length, "");
                }
                else
                {
                    //  Debug.Log("Test Broadcasting");

                    for (int j = 0; j < mConnectedClients.Count; ++j)
                    {
                        Debug.Log(mConnectedClients[j].IP);
                        if (mConnectedClients[j].IP != "")
                        {
                            SendData(temp, temp.Length, mConnectedClients[j].IP);
                        }
                    }
                }
            }
        }
    }

    public static unsafe void addClient()
    {
        char* ip = GetLastPacketIP();
        IntPtr careTwo = (IntPtr)ip;
        StraightCharPointer* dataTwo = (StraightCharPointer*)careTwo;
        //string start = Marshal.PtrToStringAnsi((IntPtr)dataTwo->mId);
        string newIP = Marshal.PtrToStringAnsi((IntPtr)dataTwo->mes);
        Debug.Log(newIP);
        ConnectedClientInfo newClient = new ConnectedClientInfo();
        newClient.ID = clientID.ToString();
        ++clientID;
        newClient.IP = newIP;
        mConnectedClients.Add(newClient);

        //Send a data buffer of all the objects currently in the scene to the newly connected client
        GameObject[] allGameobjects = GameObject.FindObjectsOfType<GameObject>();   //Get all gameobjs
        for (int i=0; i < allGameobjects.Length; ++i)
        {
            string temp = StructScript.serialize(allGameobjects[i]);
            SendData(temp, temp.Length, newIP);
        }

    }

    public static unsafe void deleteClient()
    {
        char* ip = GetLastPacketIP();
        IntPtr careTwo = (IntPtr)ip;
        StraightCharPointer* dataTwo = (StraightCharPointer*)careTwo;
        string newIP = Marshal.PtrToStringAnsi((IntPtr)dataTwo->mes);
        Debug.Log(newIP);
        ConnectedClientInfo delClient = new ConnectedClientInfo();
        delClient.IP = newIP;
        mConnectedClients.Remove(delClient);
    }
    public static void SendMessageOverNetwork(string msg)
    {
        if (mIsServer) // if it is the server
        {
            for (int i = 0; i < mConnectedClients.Count; ++i) // go through each of the connected clients
            {
                string targetIP = mConnectedClients[i].IP; // target ip is ip of client at i in mConnectedClients
                SendMessageData(msg, msg.Length, targetIP); // send message to target ip
            }
        }
        else // if client
            SendMessageData(msg, msg.Length, ""); // send message to the server
    }

    public static bool newMessage = false;

    public static void handleChatMessage(string msg)
    {
        Multiuser_Editor_Window.messageStack.Add(msg);
        // add received chat message to the stack
        if (mIsServer)
            SendMessageOverNetwork(msg);

        newMessage = true;
    }

    public static void Disconnect()
    {
        Shutdown();
        mConnected = false;
    }
}

