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

public class ServerClient
{
	public int connectionId;
	public int animationState;
	public float currentHealth;
	public string playerName;
	public Vector3 position;
	public Vector3 rotation;
}

public class Server : MonoBehaviour 
{

	private const int MAX_CONNECTION = 10;

	private int port = 5701;

	private int hostId;
	//private int webHostId;

	private int reliableChannel;
	private int unreliableChannel;

	private bool isStarted = false;
	private byte error;

	private List<ServerClient> clients = new List<ServerClient>();

	//For Position send/receive rate
	private float lastMovementUpdate;
	private float movementUpdateRate = 0.5f;


	private void Start()
	{
		NetworkTransport.Init ();
		ConnectionConfig cc = new ConnectionConfig ();

		reliableChannel = cc.AddChannel (QosType.Reliable);
		unreliableChannel = cc.AddChannel (QosType.Unreliable);

		HostTopology topo = new HostTopology (cc, MAX_CONNECTION);

		hostId = NetworkTransport.AddHost (topo, port, null);
		//webHostId = NetworkTransport.AddWebsocketHost (topo, port, null);


		isStarted = true;
		Debug.Log ("Server started on port : " + port);
	}

	private void Update()
	{
		if (!isStarted)
			return;

		int recHostId;
		int connectionId;
		int channelId;
		byte[] recBuffer = new byte[1024];
		int bufferSize = 1024;
		int dataSize;
		byte error;
		NetworkEventType recData = NetworkTransport.Receive (out recHostId, out connectionId, out channelId, recBuffer, bufferSize, out dataSize, out error);
		switch (recData) 
		{
			case NetworkEventType.ConnectEvent:		//2
				Debug.Log ("Player " + connectionId + " has connected.");
				OnConnection (connectionId);
				break;
			case NetworkEventType.DataEvent:		//3
				string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
				Debug.Log("Receiving from client " + connectionId + " : " + msg);
				string[] splitData = msg.Split ('|');

				switch(splitData[0])
				{
				case "NAMEIS":
					OnNameIs(connectionId, splitData[1]);
					break;
				case "MYPOSITION":
					OnMyPosition (connectionId, float.Parse (splitData [1]), float.Parse (splitData [2]), float.Parse(splitData[3]));
					break;
				case "MYANIMATION":
					OnMyAnimation (connectionId, int.Parse(splitData[1]));
					break;
				case "MYROTATION":
					OnMyRotation (connectionId, float.Parse (splitData [1]), float.Parse (splitData [2]), float.Parse (splitData [3]));
					break;
				case "MYHEALTH":
					OnMyHealth (connectionId, float.Parse (splitData [1]));
					break;
				case "MYPROJECTILESPAWNED":
					OnSpawnProjectile (int.Parse(splitData[1]));
					break;
				default:
					Debug.Log("Invalid message : " + msg);
					break;
				}

				break;

			case NetworkEventType.DisconnectEvent:	//4
				Debug.Log ("Player " + connectionId + " has disconnected.");
				OncDisconnection (connectionId);
				break;
		}

		//-----------------------------------------------------------------------------------------
		// Ask player for their position, rotation, and animation state
		//TODO Need to split these and put everything except position on lower intervals
		//-----------------------------------------------------------------------------------------
		if (Time.time - lastMovementUpdate > movementUpdateRate) 
		{
			lastMovementUpdate = Time.time;

			//POSITION
			string m = "ASKPOSITION|";
			foreach (ServerClient sc in clients) 
				m += sc.connectionId.ToString() + '%' + sc.position.x.ToString () + '%' + sc.position.y.ToString () + '%' + sc.position.z.ToString () + '|';
			m = m.Trim ('|');

			Send (m, unreliableChannel, clients);

			//ROTATION
			string rot = "ASKROTATION|";
			foreach (ServerClient sc in clients)
				rot += sc.connectionId.ToString () + '%' + sc.rotation.x.ToString () + '%' + sc.rotation.y.ToString () + '%' + sc.rotation.z.ToString () + '|';
			rot = rot.Trim ('|');

			Send (rot, unreliableChannel, clients);

			//ANIMATION
			string anim = "ASKANIMATION|";
			foreach (ServerClient sc in clients)
				anim += sc.connectionId.ToString() + '%' + sc.animationState.ToString() + '|';
			anim = anim.Trim ('|');

			Send (anim, unreliableChannel, clients);

			//HEALTH
			string hp = "ASKCURRENTHEALTH|";
			foreach(ServerClient sc in clients)
				hp += sc.connectionId.ToString() + '%' + sc.currentHealth.ToString() + '|';
			hp = hp.Trim('|');

			Send(hp, unreliableChannel, clients);
		}
	
	}

	private void OnConnection(int cnnId)
	{
		// Add him to a list
		ServerClient c = new ServerClient();
		c.connectionId = cnnId;
		c.playerName = "TEMP";
		clients.Add (c);

		//When the player joins the server, tell him his ID
		//Request his name and send the name of all the other players
		string msg = "ASKNAME|" + cnnId + "|";
		foreach (ServerClient sc in clients) 
		{
			msg += sc.playerName + '%' + sc.connectionId + '|';
		}

		msg = msg.Trim ('|');

		// ASKNAME|3|DAVE%1|MICHAEL%2|TEMP%3
		Send(msg, reliableChannel, cnnId);
	}
	private void OncDisconnection(int cnnId)
	{
		// Remove this player from our client list
		clients.Remove(clients.Find(x => x.connectionId == cnnId));

		// Tell everyone that somebody else has disconnected
		Send("DC|" + cnnId, reliableChannel, clients);
	}

	//Send to all clients telling them who spawned a projectile
	//Later add to the string the name of projectile spawned
	//So the clients can load that specific projectile from assets
	private void OnSpawnProjectile(int cnnId)
	{
		string msg = "PROJECTILESPAWNED|";

			msg += cnnId.ToString ();
		msg = msg.Trim ('|');

		Send (msg, reliableChannel, clients);
	}

	private void OnNameIs(int cnnId, string playerName)
	{
		// Link the name to the connection ID
		clients.Find(x=>x.connectionId==cnnId).playerName = playerName;

		// Tell everybody that a new player has connected
		Send("CNN|" + playerName + '|' + cnnId, reliableChannel, clients);
	}
	private void OnMyPosition(int cnnId, float x, float y, float z)
	{
		//Update the connected client's position on the server
		clients.Find(c=> c.connectionId == cnnId).position = new Vector3(x ,y, z);
	}
	private void OnMyRotation(int cnnId, float x, float y, float z)
	{
		clients.Find (c => c.connectionId == cnnId).rotation = new Vector3 (x, y, z);
	}
	private void OnMyAnimation(int cnnId, int x)
	{
		//Update the connected client's animation state on the server
		clients.Find (c=> c.connectionId == cnnId).animationState = x;
	}
	private void OnMyHealth(int cnnId, float x)
	{
		//Update the connected client's current health value
		clients.Find(c=> c.connectionId == cnnId).currentHealth = x;
	}
	private void Send(string message, int channelID, int cnnId)
	{
		List<ServerClient> c = new List<ServerClient> ();
		c.Add (clients.Find (x => x.connectionId == cnnId));
		Send (message, channelID, c);
	}
	private void Send(string message, int channelId, List<ServerClient> c)
	{
		Debug.Log ("Sending to client: " + message);
		byte[] msg = Encoding.Unicode.GetBytes (message);
		foreach (ServerClient sc in c) 
		{
			NetworkTransport.Send (hostId, sc.connectionId, channelId, msg, message.Length * sizeof(char), out error);
		}
	}


}
