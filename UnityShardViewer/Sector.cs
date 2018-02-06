using Shard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

		public ShardID MyID { get; private set; }
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

		private System.Diagnostics.Stopwatch sdsDelta = new System.Diagnostics.Stopwatch();

		private SpinLock deltaLock = new SpinLock();
		private double deltaSum = 0;
		private int deltaNum = 0;


		private bool stop = false;
		private void ThreadMain()
		{
			if (lastHost == null)
				return;
			Debug.Log("Sector: Thread active");

			try
			{
				while (!stop)
				{
					try
					{
						client = new TcpClient(lastHost, 16234);
						var stream = new LZ4.LZ4Stream(client.GetStream(), LZ4.LZ4StreamMode.Decompress);
						var f = new BinaryFormatter();
						CSLogicProvider.AsyncFactory = ResolveProvider;

						Debug.Log("Sector: Connected to " + lastHost);

						while (client.Connected)
						{
							//Debug.Log("Sector: Deserializing next object");
							var obj = f.UnsafeDeserialize(stream, null);
							//Debug.Log("Sector: Deserialized object " + obj);

							if (obj is ShardID)
							{
								MyID = (ShardID)obj;
								Debug.Log("Sector: ID updated to " + MyID);
							}
							else if (obj is CSLogicProvider)
							{
								var prov = (CSLogicProvider)obj;
								TaskCompletionSource<CSLogicProvider> entry;
								while (!providerMap.TryGetValue(prov.AssemblyName, out entry))
									providerMap.TryAdd(prov.AssemblyName, new TaskCompletionSource<CSLogicProvider>());
								entry.SetResult(prov);
								Debug.Log("Sector: Added new provider " + prov.AssemblyName);
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
								if (sdsDelta.IsRunning)
								{
									double delta = sdsDelta.Elapsed.TotalSeconds;
									deltaLock.DoLocked(() =>
									{
										deltaSum += delta;
										deltaNum++;
									});
									sdsDelta.Restart();
								}
							}
						}
					}
					catch (SocketException ex)
					{
						Debug.LogException(ex);
						Debug.Log("Waiting, then retrying");
					}
					catch (Exception ex)
					{
						Debug.LogException(ex);
						Debug.Log("Weird type: "+ex.GetType()+". Waiting, then retrying");
					}
					try
					{
						client.Close();
					}
					catch { };
					Thread.Sleep(2000);
				}
			}
			catch (Exception ex)
			{
				Debug.LogError("Encountered terminal exception");
				Debug.LogException(ex);
			}
		}


		private string lastHost = null;

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

		public string Host
		{
			get
			{
				return lastHost;
			}
			set
			{
				if (lastHost == value)
					return;
				if (lastHost != null)
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
				cube.transform.position = Convert(MyID.XYZ) * Scale;
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
					cube.transform.position = Convert(MyID.XYZ) * Scale;
				}
			}
		}

		public const float Scale = 100;

		private Dictionary<string, GameObject> availableEntityObjects = new Dictionary<string, GameObject>();
		private Dictionary<string, Queue<GameObject>> availableGeometryObjects = new Dictionary<string, Queue<GameObject>>();

		private int updateNo = 0;
		// Update is called once per frame
		public void Update()
		{
			if (sdsChanged)
			{
				sdsChanged = false;

				float timeDelta = 0;
				deltaLock.DoLocked(() =>
				{
					timeDelta = deltaNum > 0 ? (float)(deltaSum / deltaNum) : 1f;
				});

				//Debug.Log("Sector: processing change");
				updateNo++;
				if (cube != null)
					cube.transform.position = Convert(MyID.XYZ);

				SDS source = SDS;
				//Debug.Log("Sector: got "+transform.childCount+" children");
				foreach (Transform child in transform)
				{
					if (child.name == "cube")
					{
						//Debug.Log("Sector: got cube");
						continue;
					}
					child.gameObject.hideFlags = HideFlags.HideAndDontSave;
					foreach (Transform sub in child)
					{
						sub.gameObject.hideFlags = HideFlags.HideAndDontSave;
						availableGeometryObjects.GetOrCreate(sub.name).Enqueue(sub.gameObject);
						//availableEntityObjects.Enqueue(sub.gameObject);
					}

					if (!availableEntityObjects.ContainsKey(child.name))
						availableEntityObjects.Add(child.name, child.gameObject);
					else
					{
						Destroy(child.gameObject);
					}
				}
				//Debug.Log("Sector: recovered " + availableEntityObjects.Count + " objects");
				if (updateNo > 1 && availableEntityObjects.Count == 0)
				{
					throw new Exception("should have had some entity objects at update no "+ updateNo);
				}
				int reused = 0;
				foreach (var e in source.FinalEntities)
				{
					GameObject obj;
					string key = e.ID.Guid.ToString();
					if (!availableEntityObjects.ContainsKey(key))
					{
						obj = entityPrototype != null ? Instantiate(entityPrototype, transform) : new GameObject();
						obj.transform.parent = transform;
						if (updateNo > 1)
							throw new Exception("should have had enough entity objects at update no " + updateNo+" (reused "+reused+"/"+source.FinalEntities.Length+")");
					}
					else
					{
						obj = availableEntityObjects[key];
						availableEntityObjects.Remove(key);
						obj.hideFlags = HideFlags.None;
						reused++;
					}
					obj.name = e.ID.Guid.ToString();
					var c = obj.GetComponent<EntityComponent>();
					if (c == null)
						c = obj.AddComponent<EntityComponent>();
					var next = Convert(e.ID.Position) * Scale;
					c.SetState(obj.transform.position, next, timeDelta);
					obj.transform.position = next;
					foreach (var app in e.Appearances)
					{
						GeometricAppearance g = app as GeometricAppearance;
						if (g != null)
						{
							GameObject inst = null;
							if (availableGeometryObjects.ContainsKey(g.geometryName))
							{
								var queue = availableGeometryObjects[g.geometryName];
								if (queue.Count > 0)
									inst = queue.Dequeue();
							}
							if (inst == null)
							{
								inst = (GameObject)Instantiate(Resources.Load(g.geometryName), obj.transform);
								inst.name = g.geometryName;
							}
							else
							{
								inst.transform.parent = obj.transform;
								inst.transform.localPosition = Vector3.zero;
								inst.hideFlags = HideFlags.None;
							}
							Matrix4x4 matrix = Convert(g.orientation);
							inst.transform.rotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
						}
					}
				}
				Debug.Log("Sector: got " + transform.childCount + " children, reusing "+reused);
			}
		}

	}
}
