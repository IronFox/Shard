using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Base;

namespace Shard
{
	public class MessageHistory
	{
		private class IncomingMessages
		{
			private readonly ConcurrentBag<ClientMessage> bag = new ConcurrentBag<ClientMessage>();
			public readonly int Generation;
			private bool collected = false;

			public bool WasCollected => collected;

			public IncomingMessages(int generation)
			{
				Generation = generation;
			}

			public IncomingMessages(int generation, MessagePack pack)
			{
				Generation = generation;
				bag = new ConcurrentBag<ClientMessage>(pack.EnumerateMessages());
			}

			public MessagePack Export(bool complete)
			{
				Dictionary<Guid, List<ClientMessage>> temp = new Dictionary<Guid, List<ClientMessage>>();
				// The key equals the targeted entity Guid or Guid.Empty if the message should be broadcast to all entities.
				foreach (var m in bag)
					temp.GetOrCreate(m.ID.To).Add(m);
				Dictionary<Guid, ClientMessage[]> dict = new Dictionary<Guid, ClientMessage[]>();
				foreach (var p in temp)
					dict.Add(p.Key, p.Value.ToArray());

				collected = complete;
				return new MessagePack(dict, complete);
			}

			internal void Add(ClientMessage msg)
			{
				if (collected)
					throw new IntegrityViolation("Adding message to collected generation");
				bag.Add(msg);
			}
		}

		private IncomingMessages incomingMessages = new IncomingMessages(0);
		private readonly ConcurrentDictionary<int, IncomingMessages> generations = new ConcurrentDictionary<int, IncomingMessages>();

		public int CurrentGeneration => incomingMessages.Generation;

		public MessageHistory()
		{
		}

		public MessageHistory(int startGeneration, IEnumerable<MessagePack> generations)
		{
			int g = startGeneration;
			foreach (var m in generations)
			{
				this.generations.ForceAdd(g, new IncomingMessages(g, m));
				g++;
			}
			incomingMessages = new IncomingMessages(g);
		}

		public void EndGeneration(int endedGeneration)
		{
			var current = incomingMessages;
			if (current.Generation > endedGeneration)
				return;
			lock (this)
			{
				Log.Message("Inserting message generation " + current.Generation + "->" + (endedGeneration + 1));
				//if (!generations.IsEmpty && !generations.ContainsKey(current.Generation - 1))
				//throw new IntegrityViolation("Trying to insert generation " + current.Generation + ", but generation " + (current.Generation - 1) + " does not locally exist");
				
				if (generations.TryAdd(current.Generation, current))
				{
					DB.PutNow(new SerialCCS(new SDS.ID(Simulation.ID.XYZ, current.Generation), current.Export(true)), true);
				}
				for (int i = current.Generation + 1; i <= endedGeneration; i++)
				{
					Log.Message("Skipping empty message generation "+i);
					var m = new IncomingMessages(i, MessagePack.CompleteBlank);
					generations.TryAdd(i, m);
					DB.PutNow(new SerialCCS(new SDS.ID(Simulation.ID.XYZ, i), m.Export(true)), true);
				}
				incomingMessages = new IncomingMessages(endedGeneration + 1);
			}
		}
		public ExtMessagePack GetMessages(int generation)
		{
			var current = incomingMessages;
			if (generation > current.Generation)
				return new ExtMessagePack(new MessagePack());
			if (generation == current.Generation)
				return new ExtMessagePack(current.Export(false));
			if (generations.TryGetValue(generation, out current))
				return new ExtMessagePack(current.Export(true));
			Debug.Fail("Trying to retrieve previously trimmed messages at g"+generation+", current is g"+current.Generation);
			return new ExtMessagePack(true);
		}

		public void TrimGenerations(int generationOrOlder)
		{
			lock (this)
			{
				LazyList<int> eraseGenerations;
				foreach (var p in generations)
					if (p.Key <= generationOrOlder)
						eraseGenerations.Add(p.Key);
				foreach (var g in eraseGenerations)
				{
					IncomingMessages m;
					generations.ForceRemove(g, out m);
					//if (!m.WasCollected)
					//throw new IntegrityViolation("Dumping uncollected messages");
				}
				if (incomingMessages.Generation <= generationOrOlder)
					incomingMessages = new IncomingMessages(generationOrOlder + 1);
			}
		}


		public void Add(ClientMessage msg)
		{
			incomingMessages.Add(msg);
		}

		public void Insert(int generation, MessagePack pack)
		{
			if (!pack.Completed)
				throw new IntegrityViolation("Trying to insert incomplete message pack");

			Log.Message("Inserting message generation " + generation);

			generations.GetOrAdd(generation, g=> new IncomingMessages(g,pack));
		}
	}
}