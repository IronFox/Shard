using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VectorMath;
using static Shard.Tests.ComputationTests;
using Base;

namespace Shard.Tests
{
	[TestClass()]
	public class LinkTests
	{
		private static readonly string alphabet = "abcdefghijklmnopqrtsuvwxyz ABCDEFGHIJKLMNOPQRTSUVWXYZ_0123456789";
		private static string RandomString(Random random, int minLength = 3, int maxLength = 8)
		{
			int len = random.Next(minLength, maxLength + 1);

			char[] characters = new char[len];
			for (int i = 0; i < len; i++)
				characters[i] = alphabet[random.Next(alphabet.Length)];

			return new string(characters);
		}

		private class DataSet
		{
			private HashSet<string> data = new HashSet<string>();

			public int Size { get { return data.Count; } }

			public DataSet(Random random, int minSetSize=1, int maxSetSizeInclusive=32)
			{
				int count = random.Next(minSetSize, maxSetSizeInclusive+1);
				for (int i = 0; i < count; i++)
				{
					string key = RandomString(random);
					while (!data.Add(key))
						key = RandomString(random);
				}
			}

			public void SetFor(Link sender)
			{
				Assert.IsTrue(sender.OutboundItemCount == 0);
				foreach (var t in data)
					sender.Set(t, t);   //reuse key as data
			}

			public DataSetReceiverState NewState()
			{
				return new DataSetReceiverState(data);
			}

		}

		private class DataSetReceiverState
		{
			private readonly ManualResetEvent onAllSet = new ManualResetEvent(false),
												anyDataSet = new ManualResetEvent(false);
			private object lastData;

			private ConcurrentDictionary<string, bool> received = new ConcurrentDictionary<string, bool>();
			private int receivedCount = 0;

			public int Received { get { return (received.Count - receivedCount); } }

			public DataSetReceiverState(HashSet<string> data)
			{
				foreach (var d in data)
					Assert.IsTrue( received.TryAdd(d,false) );
			}

			public void OnReceive(object obj)
			{
				Assert.IsTrue(obj is string);
				if (obj is string)
				{
					string key = (string)obj;
					Assert.IsTrue(received.TryUpdate(key, true, false));
					receivedCount++;
					if (receivedCount == received.Count)
						onAllSet.Set();
				}
				lastData = obj;
				anyDataSet.Set();

			}

			public object AwaitAnySet(int timeoutMs = 1000)
			{
				if (anyDataSet.WaitOne(timeoutMs))
					return lastData;
				Assert.Fail("Data not received in set timeout " + timeoutMs);
				return null;
			}


			public bool AwaitAllSet(int timeoutMs = 1000)
			{
				return onAllSet.WaitOne(timeoutMs);
			}

		}


		private class Receiver : IDisposable
		{
			public readonly Listener Listener;
			public readonly Link Passive;
			private readonly DataSetReceiverState state;

			public Receiver(DataSetReceiverState state, int port)
			{
				this.state = state;
				Passive = new Link(new Address("localhost",port), false, 0, false);
				Passive.OnData = OnData;
				Listener = new Listener(h => Passive,port);
			}




			private void OnData(Link lnk, object obj)
			{
				Assert.IsTrue(lnk == Passive);
				state.OnReceive(obj);

			}

			public void Dispose()
			{
				Passive.Dispose();
				Listener.Dispose();
			}
		}


		private void OnActiveData(Link lnk, object obj)
		{

		}

		private void AwaitConnection(Link lnk)
		{
			lnk.AwaitConnection();
			Assert.IsTrue(lnk.ConnectionIsActive);
		}



		struct Message
		{
			public readonly Guid From;
			public readonly Guid To;
			public readonly Guid MessageID;
			public readonly int Channel;
			public readonly byte[] Data;

			public Message(Guid from, Guid to, int channel, byte[] data, int targetGen)
			{
				From = from;
				To = to;
				Channel = channel;
				Data = data;
				MessageID = Guid.NewGuid();
			}

		}


		private static void SendMessage(NetworkStream stream, Message msg)
		{
			/*
			Guid from = reader.NextGuid();
			Guid to = reader.NextGuid();
			Guid id = reader.NextGuid();
			int msgChannel = reader.NextInt();
			int targetGen = reader.NextInt();
			byte[] data = reader.NextBytes();
			*/



			stream.Write(BitConverter.GetBytes((uint)InteractionLink.ChannelID.SendMessage), 0, 4);
			int dataLen = Helper.Length(msg.Data);
			stream.Write(BitConverter.GetBytes(dataLen + 56), 0, 4);
			stream.Write(msg.From.ToByteArray(), 0, 16);
			stream.Write(msg.To.ToByteArray(), 0, 16);
			stream.Write(msg.MessageID.ToByteArray(), 0, 16);
			stream.Write(BitConverter.GetBytes(msg.Channel), 0, 4);
			stream.Write(BitConverter.GetBytes((uint)dataLen), 0, 4);
			if (dataLen > 0)
				stream.Write(msg.Data, 0, dataLen);
		}

		private static byte[] ReadBytes(NetworkStream stream, int numBytes)
		{
			byte[] rs = new byte[numBytes];
			stream.Read(rs, numBytes);
			return rs;
		}


		private static InteractionLink.ChannelID ReadChannelID(NetworkStream stream)
		{
			return (InteractionLink.ChannelID)ReadInt(stream);
		}

		private static int ReadInt(NetworkStream stream)
		{
			return BitConverter.ToInt32(ReadBytes(stream, 4),0);
		}

		private static Guid ReadGuid(NetworkStream stream)
		{
			return new Guid(ReadBytes(stream, 16));
		}
		private static void ReadMessageDelivered(NetworkStream stream, Guid messageID)
		{
			InteractionLink.ChannelID id = ReadChannelID(stream);
			Assert.AreEqual(id, InteractionLink.ChannelID.MessageDelivered);
			int length = ReadInt(stream);
			Assert.AreEqual(length, 16);
			Guid msgID = ReadGuid(stream);
			Assert.AreEqual(msgID, messageID);
		}

		private static Message ReadMessage(NetworkStream stream, out int generation, int expectChannel)
		{
			/*
					stream.Write(BitConverter.GetBytes((uint)ChannelID.SendMessage), 0, 4);
					stream.Write(BitConverter.GetBytes(data.Length+36), 0, 4);
					stream.Write(senderEntity.ToByteArray(), 0, 16);
					stream.Write(receiverID.ToByteArray(), 0, 16);
					stream.Write(BitConverter.GetBytes((uint)data.Length), 0, 4);
					stream.Write(data, 0, data.Length);
				*/
			InteractionLink.ChannelID id = ReadChannelID(stream);
			Assert.AreEqual(id, InteractionLink.ChannelID.SendMessage);
			int length = ReadInt(stream);
			Guid sender = ReadGuid(stream);
			Guid receiver = ReadGuid(stream);
			generation = ReadInt(stream);
			int channel = ReadInt(stream);
			int byteLength = ReadInt(stream);
			byte[] payload = ReadBytes(stream, byteLength);
			Assert.AreEqual(length, byteLength + 4 + 4 + 16 + 16 + 4);
			Assert.AreEqual(expectChannel, channel);
			return new Message(sender, receiver, channel,payload,0);
		}

		private static void Register(NetworkStream stream, Guid me)
		{
			stream.Write(BitConverter.GetBytes((uint)InteractionLink.ChannelID.RegisterReceiver), 0, 4);
			stream.Write(BitConverter.GetBytes(16), 0, 4);
			stream.Write(me.ToByteArray(), 0, 16);
		}

		private static void Unregister(NetworkStream stream, Guid me)
		{
			stream.Write(BitConverter.GetBytes((uint)InteractionLink.ChannelID.UnregisterReceiver), 0, 4);
			stream.Write(BitConverter.GetBytes(16), 0, 4);
			stream.Write(me.ToByteArray(), 0, 16);
		}

		[Serializable]
		class SineLogic : EntityLogic
		{
			public SineLogic()
			{}

			protected override void Evolve(ref Actions newState, Entity currentState, int generation, EntityRandom randomSource, EntityRanges ranges, bool isInconsistent)
			{
				if (currentState.InboundMessages != null)
					foreach (var m in currentState.InboundMessages)
					{
						if (!m.Sender.IsEntity)
						{
							if (m.IsBroadcast)
							{
								newState.Send(m.Sender,0, currentState.ID.Guid.ToByteArray() );
								continue;
							}
							Assert.AreEqual(m.Payload.Length, 4);
							float f = BitConverter.ToSingle(m.Payload, 0);
							float f2 = (float)Math.Sin(f);
							Console.WriteLine("@"+generation+": "+ f + "->" + f2);
							newState.Send(m.Sender, 1, BitConverter.GetBytes(f2));
						}
					}
			}
		}


		[TestMethod()]
		public void FullInteractionLinkTest()
		{
			Random random = new Random();
			var timing = new BaseDB.TimingContainer();
			BaseDB.FallbackTimingFetch = () => timing;

			SimulationRun run = new SimulationRun(
				new BaseDB.ConfigContainer() { extent = new ShardID(new Int3(1), 1), r = 1f / 8, m = 1f / 16 },
				ShardID.Zero,
				new Entity[]
				{
					new Entity(
						new EntityID(Guid.NewGuid(), Simulation.MySpace.Center),
						Vec3.Zero, 
						new SineLogic()),
				}
			);

			run.InstallConsensusCluster(3, random.Next(1024, 16000),true);

			bool keepRunning = true;
			//parallel evolution:
			var task = Task.Run(() =>
			{
				for (int i = 0; keepRunning; i++)
				{
					var rs = run.AdvanceTLG(true, true);
					Assert.AreEqual(rs.IntermediateSDS.entities.Count, 1);
					Assert.AreEqual(rs.SDS.FinalEntities.Length, 1);
				}
			});

			var defaultPort = random.Next(1024, 32768);
			using (Listener listener = new Listener(null,defaultPort))
			{
				InteractionLink myLink = null;
				AutoResetEvent onLink = new AutoResetEvent(false),
								onRegister = new AutoResetEvent(false),
								onUnregister = new AutoResetEvent(false),
								onMessage = new AutoResetEvent(false);
				Guid me = Guid.NewGuid();
				Guid lastTargetEntity = Guid.Empty;
				byte[] lastData = null;
				listener.OnNewInteractionLink = lnk =>
				{
					myLink = lnk;
					lnk.OnRegisterReceiver = receiver =>
					{
						Assert.AreEqual(receiver, me);
						onRegister.Set();
					};
					lnk.OnMessage = (from, to, message) =>
					{
						Assert.AreEqual(from, me);
						lastTargetEntity = to;
						lastData = message;
						onMessage.Set();
					};
					lnk.OnUnregisterReceiver = receiver =>
					{
						Assert.AreEqual(receiver, me);
						onUnregister.Set();
					};
					onLink.Set();
				};



				using (TcpClient client = new TcpClient("localhost", defaultPort))
				{
					var stream = client.GetStream();
					Assert.IsTrue(onLink.WaitOne());
					Register(stream, me);
					Assert.IsTrue(onRegister.WaitOne());

					Message findEntity = new Message(me, Guid.Empty, 0, null, 50);
					SendMessage(stream, findEntity); //broadcast, find entity


					int gen;
					ReadMessageDelivered(stream,findEntity.MessageID);
					Message broadCastResponse = ReadMessage(stream, out gen,0);
					Assert.AreEqual(broadCastResponse.Data.Length, 16);
					Assert.AreEqual(broadCastResponse.To, me);

					Guid entityID = broadCastResponse.From;


					for (int i = 0; i < 100; i++)
					{
						float f = random.NextFloat(0, 100);
						Console.WriteLine(i + ": " + f);
						Message job = new Message(me, entityID, 0, BitConverter.GetBytes(f), 50);
						SendMessage(stream, job);
						ReadMessageDelivered(stream,job.MessageID);

						Message response = ReadMessage(stream, out gen,1);
						Assert.AreEqual(response.To, me);
						Assert.AreEqual(response.From, entityID);
						Assert.IsNotNull(response.Data);
						Assert.AreEqual(response.Data.Length, 4);
						float rs = BitConverter.ToSingle(response.Data, 0);
						Assert.AreEqual((float)Math.Sin(f), rs, i+": "+f);
					}
				}
			}
			keepRunning = false;
			task.Wait();
		}

		/// <summary>
		/// Checks whether registration and message delivery to the listener work
		/// </summary>
		[TestMethod()]
		public void InteractionLinkTest()
		{
			Random random = new Random();
			var defaultPort = random.Next(1024, 32768);
			using (Listener listener = new Listener(null,defaultPort))
			{
				InteractionLink myLink = null;
				AutoResetEvent onLink = new AutoResetEvent(false),
								onRegister = new AutoResetEvent(false),
								onUnregister = new AutoResetEvent(false),
								onMessage = new AutoResetEvent(false);
				Guid me = Guid.NewGuid();
				Guid lastTargetEntity = Guid.Empty;
				byte[] lastData = null;
				listener.OnNewInteractionLink = lnk =>
				{
					myLink = lnk;
					lnk.OnRegisterReceiver = receiver =>
					{
						Assert.AreEqual(receiver, me);
						onRegister.Set();
					};
					lnk.OnMessage = (from, to, message) =>
					{
						Assert.AreEqual(from, me);
						lastTargetEntity = to;
						lastData = message;
						onMessage.Set();
					};
					lnk.OnUnregisterReceiver = receiver =>
					{
						Assert.AreEqual(receiver, me);
						onUnregister.Set();
					};
					onLink.Set();
				};
				using (TcpClient client = new TcpClient("localhost", defaultPort))
				{
					Assert.IsTrue(onLink.WaitOne());

					var stream = client.GetStream();

					for (int k = 0; k < 3; k++)
					{
						me = Guid.NewGuid();
						Register(stream, me);
						Assert.IsTrue(onRegister.WaitOne());

						for (int i = 0; i < 10; i++)
						{
							Guid target = Guid.NewGuid();
							for (int j = 0; j < 10; j++)
							{
								byte[] data = random.NextBytes(0, 100);
								SendMessage(stream, new Message(me, target, 0,data,5));
								Assert.IsTrue(onMessage.WaitOne());
								Assert.AreEqual(lastTargetEntity, target);
								Assert.IsTrue(Helper.AreEqual(data, lastData));
							}
						}
						Unregister(stream, me);
						Assert.IsTrue(onUnregister.WaitOne());
					}
				}
			}
		}


		[TestMethod()]
		public void LinkTest()
		{
			Random random = new Random();
			var defaultPort = random.Next(1024, 32768);

			Link active = new Link(new Address("localhost", defaultPort), true, 0, false);
			active.AddressLocked = true;
			active.VerboseWriter = true;
			active.OnData = OnActiveData;

			for (int i = 0; i < 3; i++)
			{
				DataSet set = new DataSet(random);
				set.SetFor(active);
				for (int j = 0; j < 3; j++)
				{
					var state = set.NewState();
					Receiver recv = new Receiver(state,defaultPort);
					AwaitConnection(recv.Passive);
					AwaitConnection(active);
					if (!state.AwaitAllSet(10000))
					{
						Assert.AreEqual(active.SentSinceLastReconnect, set.Size, active.OutboundSentCount + "/"+ active.OutboundItemCount);
						Assert.Fail("Data not received. Remaining: " + state.Received + "/" + set.Size);
					}
					recv.Dispose();
				}
				active.ClearOutData();
			}

			//string testData = "test";
			//active.Set("yo", testData);
			//object received = recv.AwaitData(10000);
			//Assert.IsTrue(received is string);
			//Assert.AreEqual(received, testData);

			//Assert.Fail();
		}

		[TestMethod()]
		public void InboundRCSTest()
		{
			//Assert.Fail();
		}

		[TestMethod()]
		public void OutboundRCSTest()
		{
			//Assert.Fail();
		}

		[TestMethod()]
		public void SetPassiveClientTest()
		{
			//Assert.Fail();
		}

		[TestMethod()]
		public void SetTest()
		{
			//Assert.Fail();
		}

		[TestMethod()]
		public void FilterTest()
		{
			//Assert.Fail();
		}

		private class DelayedStream : Stream
		{
			private readonly byte[] data;
			private int location = 0;
			public DelayedStream(byte[] raw)
			{
				data = raw;
			}
			public override bool CanRead => true;

			public override bool CanSeek => false;

			public override bool CanWrite => false;

			public override long Length => throw new NotImplementedException();

			public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

			public override void Flush()
			{
				throw new NotImplementedException();
			}

			static Random random = new Random();
			public override int Read(byte[] buffer, int offset, int count)
			{
				if (count > data.Length - location)
					return -1;
				if (random.NextBool())
					return 0;
				buffer[offset] = data[location++];
				return 1;
			}

			public override long Seek(long offset, SeekOrigin origin)
			{
				throw new NotImplementedException();
			}

			public override void SetLength(long value)
			{
				throw new NotImplementedException();
			}

			public override void Write(byte[] buffer, int offset, int count)
			{
				throw new NotImplementedException();
			}
		}

		[TestMethod()]
		public void DisposeTest()
		{
			//Assert.Fail();
		}
	}
}
 