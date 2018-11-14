using Base;
using Shard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
//using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VectorMath;

namespace UnityShardViewer
{
    public class Sector : MonoBehaviour
    {
		//private Shard.SDS sds = new Shard.SDS(12);
		private Thread connectionThread;

		private ShardID privateID;
		public ShardID publicID = new ShardID(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);

		public SDS SDS { get; private set; }
		private bool sdsChanged = false;
	

		private static ConcurrentDictionary<string, TaskCompletionSource<CSLogicProvider>> providerMap = new ConcurrentDictionary<string, TaskCompletionSource<CSLogicProvider>>();

		private Task<CSLogicProvider> ResolveProvider(string assemblyName)
		{
			TaskCompletionSource<CSLogicProvider> src;
			if (providerMap.TryGetValue(assemblyName, out src))
				return src.Task;
			providerMap.TryAdd(assemblyName, new TaskCompletionSource<CSLogicProvider>());	//maybe failed, maybe not
			return ResolveProvider(assemblyName);	//regardless, let's try again
		}

		void OnApplicationQuit()
		{
			Debug.Log("Application ending after " + Time.time + " seconds");
			stop = true;
			if (client != null)
				client.Close();
		}


		private float secondsPerTLG=1;

		private bool stop = false;
		private void ThreadMain()
		{
			if (lastHost.IsEmpty)
				return;
			Debug.Log("Sector: Thread active");

			try
			{
				while (!stop)
				{
					try
					{
						Debug.Log("Attempting to connect to " + lastHost);
						client = new TcpClient(lastHost.Host, lastHost.Port);
						var stream = new LZ4.LZ4Stream(client.GetStream(), LZ4.LZ4StreamMode.Decompress);
						var f = new BinaryFormatter();
						CSLogicProvider.AsyncFactory = ResolveProvider;

						Debug.Log("Sector: Connected to " + lastHost);

						while (client.Connected)
						{
							Debug.Log("Sector: Deserializing next object");
							var obj = f.UnsafeDeserialize(stream, null);
							Debug.Log("Sector: Deserialized object " + obj);

							if (obj is ShardID)
							{
								privateID = (ShardID)obj;
								Debug.Log("Sector: ID updated to " + privateID);
								OnNewNeighbor(privateID, lastHost);
							}
							else if (obj is ObserverTimingInfo)
							{
								secondsPerTLG = (float)((ObserverTimingInfo)obj).msPerTLG / 1000f;
							}
							else if (obj is CSLogicProvider)
							{
								var prov = (CSLogicProvider)obj;
								TaskCompletionSource<CSLogicProvider> entry;
								while (!providerMap.TryGetValue(prov.AssemblyName, out entry))
									providerMap.TryAdd(prov.AssemblyName, new TaskCompletionSource<CSLogicProvider>());
								if (!entry.Task.IsCompleted)
									entry.SetResult(prov);
								Debug.Log("Sector: Added new provider " + prov.AssemblyName);
							}
							else if (obj is FullShardAddress)
							{
								var h = (FullShardAddress)obj;
								newNeighbors.Add(h);
							}
							else if (obj is SDS)
							{
								SDS sds = (SDS)obj;
								//Debug.Log("Sector: Got new SDS. Deserializing entities...");

								foreach (var e in sds.FinalEntities)
								{
									var logic = e.MyLogic as DynamicCSLogic;
									if (logic != null)
										try
										{
											logic.FinishLoading(e.ID, TimeSpan.FromSeconds(1));
										}
										catch (ExecutionException ex)
										{
											Debug.LogException(ex);
										}
								}
								//Debug.Log("Sector: SDS processed. Signalling change");
								SDS = sds;
								sdsChanged = true;
							}
						}
					}
					catch (SocketException ex)
					{
						Debug.LogException(ex);
					}
					catch (IOException ex)
					{
						Debug.LogException(ex);
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						Debug.Log("Weird type: " + ex.GetType());
					}
					try
					{
						client.Close();
					}
					catch { };
					Debug.Log("Waiting, then retrying");
					Thread.Sleep(2000);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError("Encountered terminal exception");
				Debug.LogException(ex);
			}
		}


		private Address lastHost;

		private TcpClient client;

		private void StartConnection()
		{
			stop = false;
			connectionThread = new Thread(new ThreadStart(ThreadMain));
			connectionThread.Start();
		}

		private void AbortConnection()
		{
			try
			{
				if (client != null)
				{
					client.Close();
					client.Dispose();
				}
			}
			catch { }
			//client = null;
		}

		public Address Host
		{
			get
			{
				return lastHost;
			}
			set
			{
				if (lastHost == value)
					return;
				if (!lastHost.IsEmpty)
					AbortConnection();
				lastHost = value;
				if (started)
					StartConnection();
			}
		}

		private bool started = false;

		// Use this for initialization
		public void Start()
		{
			Debug.Log("Starting observation connector to " + Host);
			started = true;
			if (lastHost != null)
				StartConnection();
			if (cubePrototype != null)
			{
				cube = Instantiate(cubePrototype, transform);
				cube.name = "cube";
				cube.transform.name = "cube";
				//cube.transform.position = Convert(MyID.XYZ) * Scale;
			}

		}
		public static Vector3 Convert(Int3 v)
		{
			return new Vector3(v.X, v.Y, v.Z);
		}

		public static Vector3 Convert(Vec3 v)
		{
			return new Vector3(v.X, v.Y, v.Z);
		}
		public static Vector4 Convert(Vec3 v, float w)
		{
			return new Vector4(v.X, v.Y, v.Z,w);
		}

		public static Matrix4x4 Convert(Matrix3 o)
		{
			return new Matrix4x4(Convert(o.x, 0), Convert(o.y, 0), Convert(o.z, 0), new Vector4(0, 0, 0, 1));
		}


		private GameObject entityPrototype;
		public GameObject EntityPrototype
		{
			get
			{
				return entityPrototype;
			}
			set
			{
				if (value == entityPrototype)
					return;
				entityPrototype = value;
				availableEntityObjects.Clear();
				foreach (Transform child in transform)
					Destroy(child);
				if (SDS != null && started)
					sdsChanged = true;

			}
		}

		GameObject cubePrototype,cube;
		/// <summary>
		/// Modifies the geometry that represents the local simulation space.
		/// Should be roughly 1x1x1 (*Scale) to acurrately represent the sector
		/// </summary>
		public GameObject CubePrototype
		{
			get
			{
				return cubePrototype;
			}

			set
			{
				if (value == cubePrototype)
					return;
				cubePrototype = value;
				if (cube)
					Destroy(cube);
				if (started)
				{
					cube = Instantiate(value, transform);
					cube.name = "cube";
					cube.transform.position = Convert(publicID.XYZ) * Scale;
				}
			}
		}

		public Action<ShardID> onNewID;

		public Action<ShardID, Address> OnNewNeighbor { get; internal set; }

		public const float Scale = 100;

		private Dictionary<string, GameObject> availableEntityObjects = new Dictionary<string, GameObject>();

		private ConcurrentBag<FullShardAddress> newNeighbors = new ConcurrentBag<FullShardAddress>();


		private System.Random random = new System.Random();

		public Color myColor;

		public Sector()
		{
			myColor = new Color(random.NextFloat(0.5f, 1f), random.NextFloat(0.5f, 1f), random.NextFloat(0.5f, 1f));
		}

		private int updateNo = 0;
		// Update is called once per frame
		public void Update()
		{
			{
				FullShardAddress t;
				while (newNeighbors.TryTake(out t))
				{
					Debug.Log(name + ": received neighbor update: " + t);
					OnNewNeighbor(t.ShardID, t.ObserverAddress);
				}
			}
			


			if (sdsChanged)
			{
				sdsChanged = false;

				{
					var id = privateID;
					if (id != publicID)
					{
						Debug.Log("ID change detected: " + id);
						publicID = id;
						name = publicID.ToString();
						transform.name = publicID.ToString();
						onNewID?.Invoke(id);
						if (cube != null)
							cube.transform.position = Convert(publicID.XYZ) * Scale;
					}
				}


				//Debug.Log("Sector: processing change");
				updateNo++;

				SDS source = SDS;
				//Debug.Log("Sector: got "+transform.childCount+" children");
				LazyList<GameObject> toDestroy = new LazyList<GameObject>();
				foreach (Transform child in transform)
				{
					var obj = child.gameObject;
					if (obj.name == "cube")
					{
						//Debug.Log("Sector: got cube");
						continue;
					}
					if (obj.hideFlags == HideFlags.HideAndDontSave)
						continue;
					obj.hideFlags = HideFlags.HideAndDontSave;
					obj.GetComponent<Renderer>().enabled = false;

					if (!availableEntityObjects.ContainsKey(obj.name))
						availableEntityObjects.Add(obj.name, obj);
					else
					{
						toDestroy.Add(obj);
					}
				}
				foreach (var obj in toDestroy)
				{
					if (availableEntityObjects.ContainsValue(obj))
						Debug.LogError("Object "+obj.name+" still in use");
					else
						Destroy(obj);
				}


				//Debug.Log("Sector: recovered " + availableEntityObjects.Count + " objects");
				int reused = 0;
				foreach (var e in source.FinalEntities)
				{
					GameObject obj;
					var next = Convert(e.ID.Position) * Scale;
					Vector3 prev = next;
					string key = e.ID.Guid.ToString();
					if (!availableEntityObjects.ContainsKey(key))
					{
						obj = entityPrototype != null ? Instantiate(entityPrototype, transform) : new GameObject();
						obj.GetComponent<Renderer>().material.color = myColor;
						obj.transform.parent = transform;
						obj.name = key;
					}
					else
					{
						obj = availableEntityObjects[key];
						availableEntityObjects.Remove(key);
						if (obj == null)
							Debug.LogError("Object " + key + " is null. Bad shit will happen");
						obj.hideFlags = HideFlags.None;
						obj.GetComponent<Renderer>().enabled = true;
						prev = obj.transform.position;
						reused++;
					}
					var c = obj.GetComponent<EntityComponent>();
					if (c == null)
						c = obj.AddComponent<EntityComponent>();
					c.SetState(next - Convert( e.Velocity ) * Scale, next, secondsPerTLG);
					obj.transform.position = next;
				}
				//Debug.Log("Sector: got " + transform.childCount + " children, reusing "+reused);
			}
		}

	}
}
