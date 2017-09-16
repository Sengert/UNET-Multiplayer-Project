/* Initialization of connections and the framework for the messaging pipeline
 * were created through a tutorial by: N3K EN on his youtube channel, link to the
 * video https://www.youtube.com/watch?v=qGkkaNkq8co&t=2s
 *
*/

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;


public class Player
{
	public string playerName;
	public GameObject avatar;
	public Animator animator;
	public int connectionId;
	public float currentHP;
}


public class Client : MonoBehaviour {


	private const int MAX_CONNECTION = 10;

	private int port = 5701;

	private int hostId;

	private int reliableChannel;
	private int unreliableChannel;

	private int ourClientId;
	private int connectionId;

	private float connectionTime;
	private bool isConnected = false;
	private bool isStarted = false;
	private byte error;

	private string playerName;

	//Spawnables-----------------------------------------
	public GameObject playerPrefab;
	public GameObject projectilePrefab;
	//---------------------------------------------------
	public Dictionary<int, Player> players = new Dictionary<int, Player>();

	public void Connect()
	{
		//UI disabling is currently in spawnplayer()

		//Does the player have a name?
		string pName = GameObject.Find ("NameInput").GetComponent<InputField>().text;
		if (pName == "") 
		{
			Debug.Log ("You must enter a name..");
			return;
		}

		playerName = pName;


		NetworkTransport.Init ();
		ConnectionConfig cc = new ConnectionConfig ();

		reliableChannel = cc.AddChannel (QosType.Reliable);
		unreliableChannel = cc.AddChannel (QosType.Unreliable);

		HostTopology topo = new HostTopology (cc, MAX_CONNECTION);

		hostId = NetworkTransport.AddHost (topo, 0);
		connectionId = NetworkTransport.Connect (hostId, "127.0.0.1", port, 0, out error);//Use ip: 127.0.0.1 for LOCALHOST for testing on this machine
																													   //Use ip: 2602:306:36d3:a450:8105:2819:bfc7:9dca for LAN testing
																													   //Use ip: 2620:9b::190f:1076 for hamachi

		connectionTime = Time.time;
		isConnected = true;
	}

	//Tell the server that we want to spawn a projectile
	//TODO: add a parameter to pass in defining what projectile we want to spawn
	public void SpawnProjectile()
	{
		string msg = "MYPROJECTILESPAWNED|" + players[ourClientId].connectionId.ToString();

		Send(msg, reliableChannel);
	}

	private void Update()
	{
		if (!isConnected)
			return;

		int recHostId;
		int connectionId;
		int channelId;
		byte[] recBuffer = new byte[1024];
		int bufferSize = 1024;
		int dataSize;
		byte error;
		NetworkEventType recData = NetworkTransport.Receive (out recHostId, out connectionId, out channelId, recBuffer, bufferSize, out dataSize, out error);
		switch (recData) {
		case NetworkEventType.DataEvent:
			string msg = Encoding.Unicode.GetString (recBuffer, 0, dataSize);

			//Debug.Log ("Receiving : " + msg);

			string[] splitData = msg.Split ('|');
			//Filter out asking for coordinate updates, cluttering up debug log
			if(splitData[0] != "ASKPOSITION" && splitData[0] != "ASKROTATION")
				Debug.Log ("Receiving from server: " + msg);

			switch(splitData[0])
			{
				case "ASKNAME":
				OnAskName(splitData);
					break;
				case "CNN":
					SpawnPlayer (splitData [1], int.Parse (splitData [2]));
					break;
				case "DC":
					PlayerDisconnected (int.Parse(splitData[1]));
					break;
				case "ASKPOSITION":
					OnAskPosition (splitData);
					break;
				case "ASKROTATION":
					OnAskRotation(splitData);
					break;
				case "ASKANIMATION":
					OnAskAnimation(splitData);
					break;
				case "ASKCURRENTHEALTH":
					OnAskCurrentHealth (splitData);
					break;
				case "PROJECTILESPAWNED":
					OnProjectileWasSpawned (splitData);
					break;
				default:
					Debug.Log("Invalid message : " + msg);
					break;
			}

			break;

		}

	}

	private void OnAskName(string[] data)
	{
		// Set this client's ID
		ourClientId = int.Parse (data [1]);

		// Send our name to the server
		Send("NAMEIS|" + playerName, reliableChannel);

		// Create all the other players
		for (int i = 2; i < data.Length - 1; i++) 
		{
			string[] d = data [i].Split ('%');
			SpawnPlayer (d [0], int.Parse (d [1]));
		}
	}
	private void OnAskPosition(string[] data)
	{
		if (!isStarted)
			return;

		// Update everyone else
		for (int i = 1; i < data.Length; i++) 
		{
			string[] d = data [i].Split ('%');

			// Prevent the server form updating us
			if (ourClientId != int.Parse (d [0]))
			{
				Vector3 position = Vector3.zero;
				position.x = float.Parse (d[1]);
				position.y = float.Parse (d[2]);
				position.z = float.Parse (d[3]);
				players [int.Parse (d [0])].avatar.transform.position = position;
			}
		}

		// Send our own position
		Vector3 myPosition = players[ourClientId].avatar.transform.position;
		string m = "MYPOSITION|" + myPosition.x.ToString() + '|' + myPosition.y.ToString() + '|' + myPosition.z.ToString();
		Send(m, unreliableChannel);

	}

	private void OnAskCurrentHealth (string[] data)
	{
		if(!isStarted)
			return;

		//Update all other clients current health values
		for(int i = 1; i < data.Length; i++)
		{
			string[] d = data [i].Split ('%');

			// If check prevents the server from updating us
			if (ourClientId != int.Parse (d [0]))
			{
				players[int.Parse(d[0])].currentHP = float.Parse(d[1]);
				players [int.Parse (d [0])].avatar.GetComponent<PlayerStats> ().CurHP = players [int.Parse (d [0])].currentHP;
			}
		}

		// Send our own client's animation state
		float myCurrentHealth = players[ourClientId].avatar.GetComponent<PlayerStats>().CurHP;
		string m = "MYHEALTH|" + myCurrentHealth.ToString();
		Send(m, unreliableChannel);
	}

	private void OnAskRotation(string[] data)
	{

		if (!isStarted)
			return;

		//Update everyone else
		for (int i = 1; i < data.Length; i++) 
		{
			string[] d = data [i].Split ('%');

			// Prevent the server form updating us
			if (ourClientId != int.Parse (d [0]))
			{
				Vector3 rotation = Vector3.zero;
				float x = float.Parse (d[1]);
				float y = float.Parse (d[2]);
				float z = float.Parse (d[3]);
				rotation = new Vector3 (x, y, z);

				players [int.Parse (d [0])].avatar.transform.eulerAngles = rotation;
			}
		}

		// Send our own rotation
		Vector3 myRotation = players[ourClientId].avatar.transform.eulerAngles;
		string m = "MYROTATION|" + myRotation.x.ToString() + '|' + myRotation.y.ToString() + '|' + myRotation.z.ToString();
		Send(m, unreliableChannel);

	}
		
	private void OnAskAnimation(string[] data)
	{
		if(!isStarted)
		return;

		// Update all other clients current animation states
		for(int i = 1; i < data.Length; i++)
		{
			string[] d = data [i].Split ('%');

			// If check prevents the server from updating us
			if (ourClientId != int.Parse (d [0]))
			{
				players[int.Parse(d[0])].animator.SetInteger("AnimationState", int.Parse(d[1]));
			}
		}

		// Send our own client's animation state
		int myAnimationState = players[ourClientId].animator.GetInteger("AnimationState");
		string m = "MYANIMATION|" + myAnimationState.ToString();
		Send(m, unreliableChannel);

	}

	private void OnProjectileWasSpawned(string[] data)
	{
		if (!isStarted)
			return;
		int idThatSpawned = int.Parse (data [1]);

		if(players.ContainsKey(idThatSpawned))
		{
			Transform spawnPoint = players [idThatSpawned].avatar.transform.GetChild (2).transform;
			GameObject tempProj = Instantiate (Resources.Load("Projectile"), spawnPoint.transform.position, spawnPoint.transform.rotation) as GameObject;
			Destroy (tempProj, 3);
			Debug.Log ("Projectile spawned from player with ID: " + idThatSpawned);
		}
		else
		{
			Debug.Log("Player attempting to spawn projectile not found");
		}

	}

	private void SpawnPlayer(string playerName, int cnnId)
	{
		GameObject go = Instantiate (playerPrefab) as GameObject;
		go.AddComponent<PlayerStats> ();

		// Is this ours?
		if (cnnId == ourClientId) 
		{
			// Add mobility
			go.AddComponent<PlayerMotor>();

			//Set camera to our player
			Camera cam = Camera.main;
			//Transform anchor = GameObject.Find ("Camera Anchor").transform;
			Transform anchor = go.transform.GetChild (3).transform;
			cam.transform.parent = anchor;
			cam.transform.position = anchor.position;
			cam.transform.rotation = anchor.rotation;

			//Remove Canvas
			//TODO move these to Connect() function above ^^
			//GameObject.Find("Canvas").SetActive(false);
			GameObject.Find("Background").SetActive(false);
			GameObject.Find("NameInput").SetActive(false);
			GameObject.Find("ConnectButton").SetActive(false);

			isStarted = true;
		}

		Player p = new Player ();
		p.avatar = go;
		p.animator = p.avatar.GetComponentInChildren<Animator> ();
		p.playerName = playerName;
		p.connectionId = cnnId;
		p.avatar.GetComponentInChildren<TextMesh> ().text = playerName;
		players.Add (cnnId, p);
	}

	private void PlayerDisconnected(int cnnId)
	{
		Destroy (players [cnnId].avatar);
		players.Remove (cnnId);
	}

	private void Send(string message, int channelId)
	{
		string[] splitMsg = message.Split ('|');
		if (splitMsg [0] != "MYPOSITION" && splitMsg[0] != "MYROTATION") 
		{
			Debug.Log ("Sending to server: " + message);
		} 
		else if (splitMsg [0] == "MYPOSITION") 
		{
			Debug.Log ("Sending to server: " + "MYPOSITION|");//filters all the coordinate updates cluttering up debug log
		} 
		else if (splitMsg [0] == "MYROTATION") 
		{
			Debug.Log ("Sending to server: " + "MYROTATION|");//filters all the orientation updates cluttering up debug log
		}
			
		byte[] msg = Encoding.Unicode.GetBytes (message);
		NetworkTransport.Send (hostId, connectionId, channelId, msg, message.Length * sizeof(char), out error);
	}


}
