using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

			public Receiver(DataSetReceiverState state)
			{
				this.state = state;
				Passive = new Link(new Host("localhost"), false, 0, false);
				Passive.OnData = OnData;
				Listener = new Listener(h => Passive);
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

		[TestMethod()]
		public void LinkTest()
		{
			Random random = new Random();
			Host.DefaultPort = random.Next(1024, 32768);

			Link active = new Link(new Host("localhost"), true, 0, false);
			active.VerboseWriter = true;
			active.OnData = OnActiveData;

			for (int i = 0; i < 3; i++)
			{
				DataSet set = new DataSet(random);
				set.SetFor(active);
				for (int j = 0; j < 3; j++)
				{
					var state = set.NewState();
					Receiver recv = new Receiver(state);
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

		[TestMethod()]
		public void DisposeTest()
		{
			//Assert.Fail();
		}
	}
}