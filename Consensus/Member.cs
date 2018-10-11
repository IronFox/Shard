using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Consensus
{
	public class Identity : IEquatable<Identity>
	{
		public readonly Address Address;


		public override string ToString()
		{
			return Address.ToString();
		}


		public Identity(Address address)
		{
			Address = address;
		}
		internal void LogError(object error)
		{
			Console.Error.WriteLine(Address + ": " + error);
		}
		internal void LogEvent(object ev)
		{
			Console.WriteLine(Address + ": " + ev);
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as Identity);
		}

		public bool Equals(Identity other)
		{
			return other != null &&
				   Address.Equals(other.Address);
		}

		public override int GetHashCode()
		{
			return -1984154133 + EqualityComparer<Address>.Default.GetHashCode(Address);
		}
	};

    public class Member : Identity, IDisposable
    {
		private readonly ConcurrentDictionary<Address, Connection> connections = new ConcurrentDictionary<Address, Connection>();

		internal long GetAppendMessageTimeout()
		{
			return GetNanoTime() + 300 * 1000000;
		}

		private TcpListener listener;
		private Thread listenThread,consensusThread;
		private volatile bool disposed = false;
		private Configuration config;

		public enum State
		{
			Follower,
			Leader,
			Candidate
		}



		private class PrivateEntry
		{
			private readonly LogEntry entry;
			

			public PrivateEntry(LogEntry source)
			{
				entry = source;
			}

			public void Execute(Member parent)
			{
				if (WasExecuted)
					throw new InvalidOperationException("Cannot re-execute local log entry");
				WasExecuted = true;
				entry.Execute(parent);
			}

			public bool WasExecuted { get; private set; } = false;
			public LogEntry Entry => entry;
		}


		private Identity leader = null;
		private Identity votedFor = null;
		private State state = State.Follower;
		private int currentTerm = 0;
		private int commitIndex = 0, lastApplied = 0;
		private long nextActionAt;
		private readonly List<PrivateEntry> log = new List<PrivateEntry>();
		private int numVotes = 0;
		private readonly Random localRandom = new Random();
		private ConcurrentQueue<LogEntry> dispatchQueue = new ConcurrentQueue<LogEntry>();


		private const long HEART_BEAT_TIMEOUT_NS = 50 * 1000000;

		private static long GetNanoTime()
		{
			return (long)((double)Stopwatch.GetTimestamp()*1000 / TimeSpan.TicksPerMillisecond);
		}

		private long GetElectionTimeoutNS()
		{
			return localRandom.Next(150 * 1000000) + 150 * 1000000;
		}

		private long GetElectionTimeout()
		{
			return GetNanoTime() + GetElectionTimeoutNS();
		}


		public LogEntry[] LogSubSet(int idxFrom)
		{
			lock (log)
			{
				idxFrom--;
				if (idxFrom >= log.Count)
					return null;
				int length = log.Count - idxFrom;
				LogEntry[] rs = new LogEntry[length];
				for (int i = 0; i < length; i++)
					rs[i] = log[i + idxFrom].Entry;
				return rs;
			}
		}

		public int GetLogTerm(int idx)
		{
			lock (log)
			{
				if (idx <= 0)
					return 0;
				if (idx > log.Count)
					return -1;
				return log[idx - 1].Entry.Term;
			}
		}

		private void CommitTo(int newCommitIndex)
		{
			if (commitIndex < newCommitIndex)
			{
				LogEvent("Committing " + commitIndex + ".." + newCommitIndex + ", history length " + log.Count);
				for (int i = commitIndex; i < newCommitIndex; i++)
				{
					PrivateEntry e = log[i];
					LogEvent("Executing " + e.Entry);
					e.Execute(this);
				}
				commitIndex = newCommitIndex;
			}
		}

		private void TruncateTo(int newTopIndex)
		{
			lock (log)
			{
				while (log.Count > newTopIndex)
				{
					if (log[log.Count - 1].WasExecuted)
						throw new InvalidOperationException("Removing entry " + (log.Count - 1) + " which has already been executed");
					log.RemoveAt(log.Count - 1);
				}
			}
		}


		public Member(Address myAddress, Configuration config):base(myAddress)
		{
			this.config = config;
			IPAddress filter = IPAddress.Any;
			listener = new TcpListener(filter,myAddress.Port);
			listenThread = new Thread(new ThreadStart(ThreadListen));
			listenThread.Start();
			consensusThread = new Thread(new ThreadStart(ThreadConsensus));
			consensusThread.Start();
		}

		private void ThreadListen()
		{
			while (!disposed)
			{
				try
				{
					var client = listener.AcceptTcpClient();
					var addr = new Address((IPEndPoint)client.Client.RemoteEndPoint);
					Disconnect(addr);
					var conn = new Connection(this, addr, client);
					if (!connections.TryAdd(addr, conn))
						conn.Dispose();
				}
				catch (Exception)
				{

				}
			}
		}



		private ConcurrentQueue<Tuple<Package,Connection>> inboundPackages = new ConcurrentQueue<Tuple<Package, Connection>>();

		public int CurrentTerm => currentTerm;

		public int CommitIndex => commitIndex;

		public int LogSize => log.Count;

		public State CurrentState => state;

		private void Broadcast(IDispatchable p)
		{
			foreach (var c in connections)
				try
				{
					c.Value.Dispatch(p);
				}
				catch (Exception ex)
				{
					LogError(ex);
					Connection c2;
					if (connections.TryRemove(c.Key, out c2))
						c2.Dispose();
				}
		}

		private void Broadcast(Package p)
		{
			Broadcast(new Wrapped(p));
		}

		private void ThreadConsensus()
		{
			nextActionAt = GetNanoTime() + localRandom.Next(100 * 1000000);//max 100ms


			while (!disposed)
			{
				Tuple<Package,Connection> pack;
				while (inboundPackages.TryDequeue(out pack))
				{
					if (pack.Item1.Term < currentTerm)
					{
						pack.Item1.OnBadTermIgnore(this,pack.Item2);
						continue;
					}
					pack.Item1.OnProcess(this, pack.Item2);
				}

				if (state == State.Leader)
					CheckTimeouts();

				if (GetNanoTime() < nextActionAt)
				{
					Thread.Sleep(TimeSpan.FromMilliseconds(10));
					continue;
				}
				switch (state)
				{
					case State.Leader:
						Broadcast(new AppendEntries(this));
						break;
					case State.Candidate:
					case State.Follower:
						{
							//elect new leader
							ClearRemoteInfo();
							numVotes = 1;
							currentTerm++;
							votedFor = this;
							state = State.Candidate;
							leader = null;
							nextActionAt = GetElectionTimeout();

							LogEvent("Timeout reached. Starting new election");

							Broadcast(new Wrapped(new RequestVote(this)));
						}
						break;
				}
			}

			while (!disposed)
			{

			}
		}

		private void ClearRemoteInfo()
		{
			foreach (var c in connections.Values)
				c.ResetConsensusState();
		}

		private void Disconnect(Address addr)
		{
			Connection conn;
			if (connections.TryRemove(addr, out conn))
				conn.Dispose();
		}

		private Connection ConnectTo(Address addr)
		{
			lock (this)
			{
				Connection rs;
				if (connections.TryGetValue(addr, out rs))
					return rs;
				rs = new Connection(this,addr);
				while (!connections.TryAdd(addr, rs))
				{
					Disconnect(addr);
				}
				return rs;
			}
		}

		public bool ConnectedTo(Address addr)
		{
			return connections.ContainsKey(addr);
		}

		private void Join(Address address)
		{
			if (ConnectedTo(address))
				return;
			ConnectTo(address);
		}

		public void Join(Configuration cfg)
		{
			var table = new HashSet<Address>();
			foreach (var addr in cfg.Addresses)
			{
				if (addr != Address)
				{
					ConnectTo(addr);
					table.Add(addr);
				}
			}
			foreach (var pair in connections)
			{
				if (!table.Contains(pair.Key))
					Disconnect(pair.Key);
			}
		}


		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			listener.Stop();
		}

		internal void SignalAppendEntries(AppendEntries p,Connection sender)
		{
			if (leader == null || p.Term > currentTerm)
			{
				state = State.Follower;
				leader = sender;
				votedFor = null;

				LogEntry e;
				while (dispatchQueue.TryDequeue(out e))
					sender.Dispatch(new Wrapped(new CommitEntry(p.Term, e)));
			}

			if (GetLogTerm(p.PrevLogIndex) == -1)    //don't have (yet)
			{
				LogEvent("Rejecting append entries because don't have prev log index " + p.PrevLogIndex + ", term " + p.Term);
				sender.Dispatch(new Wrapped(new AppendEntriesConfirmation(this, false));
			}
			else
			{
				if (p.Entries != null)
					for (int i = 0; i < p.Entries.Length; i++)
					{
						int at = i + 1 + p.PrevLogIndex;
						LogEntry e = p.Entries[i];
						if (log.Count >= at)
						{
							//already have an element
							int myTerm = GetLogTerm(at);
							if (myTerm == e.Term)
							{
								//same, ignore
								LogEvent("Skipping " + at);
								continue;
							}
							//not same: truncate
							if (at <= commitIndex)
								throw new InvalidOperationException(state + ": overwrite consistency failure. my term " + myTerm + " != " + e.Term
										+ ". Replacing " + log[at - 1] + " with " + e);
							TruncateTo(at - 1);
						}
						if (log.Count != at - 1)
							throw new InvalidOperationException(log.Count + " != " + (at - 1));
						log.Add(new PrivateEntry(e));
					}
				CommitTo(Math.Min(p.LeaderCommit, log.Count));
				sender.Dispatch(new Wrapped(new AppendEntriesConfirmation(this, true)));
			}
			nextActionAt = GetElectionTimeout();
		}

		internal void Receive(Tuple<Package, Connection> tuple)
		{
			inboundPackages.Enqueue(tuple);
		}

		internal void ReCheckCommitment()
		{
			throw new NotImplementedException();
		}

		internal void ProcessVoteRequest(Connection sender, int term, bool upToDate)
		{
			if ((votedFor == null || votedFor == sender || term > currentTerm) && upToDate)
			{
				state = State.Follower;
				LogEvent("Recognized vote request for term " + term + " from " + sender);
				nextActionAt = GetElectionTimeout();
				votedFor = sender;
				currentTerm = term;
				sender.Dispatch(new Wrapped( new VoteConfirm(currentTerm)));
			}
			else
				LogEvent("Rejected vote request for term " + term + " (at term " + currentTerm + ", upToDate=" + upToDate + ") from " + sender);

		}

		internal void ProcessVoteConfirmation(Connection sender, int term)
		{
			if (state == State.Candidate && term == currentTerm)
			{
				nextActionAt = GetElectionTimeout();
				numVotes++;
				if (numVotes >= config.Addresses.Length / 2 + 1)
				{
					LogEvent("Elected leader of term " + currentTerm);
					state = State.Leader;
					ClearRemoteInfo();
					Broadcast(new AppendEntries(this));
					nextActionAt = GetNanoTime() + HEART_BEAT_TIMEOUT_NS;

					if (commitIndex != LogSize)
					{
						Broadcast(new AppendEntries(this, commitIndex + 1));
						foreach (var c in connections.Values)
							c.AppendTimeout = GetAppendMessageTimeout();
					}
				}
			}

		}
	}
}
