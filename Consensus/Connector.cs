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
		public Func<Address> Address { get; set; }
		public readonly Identity Parent;

		public override string ToString()
		{
			if (Parent != null)
				return Parent + ":" + Address();
			return Address().ToString();
		}


		public Identity(Identity parent, Func<Address> address)
		{
			Parent = parent;
			Address = address;
		}
		internal void LogError(object error)
		{
			Console.Error.WriteLine(this + ": " + error);
		}
		internal void LogError(Exception ex)
		{
			LogEvent(ex.Message);
			LogError(ex.ToString());
		}
		internal void LogMinorEvent(object ev)
		{
			//ignore for now
		}
		internal void LogEvent(object ev)
		{
			//string str = ev.ToString();
			//if (str.StartsWith("Unable to write data"))
			//{
			//	bool brk = true;
			//}
			Console.WriteLine(this + ": " + ev);
		}

		public override bool Equals(object obj)
		{
			return Equals(obj as Identity);
		}

		public bool Equals(Identity other)
		{
			return other != null &&
				   Address().Equals(other.Address());
		}

		public override int GetHashCode()
		{
			return -1984154133 + EqualityComparer<Address>.Default.GetHashCode(Address());
		}
	};

	public class Connector : Identity, IDisposable
    {
		//private readonly ConcurrentDictionary<Address, Connection> connections = new ConcurrentDictionary<Address, Connection>();
		private Connection[] remoteMembers;

		/// <summary>
		/// Custom attachment
		/// </summary>
		public object Attachment { get; set; }

		internal long GetAppendMessageTimeout()
		{
			return GetNanoTime() + 300 * 1000000;
		}

		private TcpListener listener;
		private Thread listenThread,consensusThread;
		private volatile bool disposing = false;


		private Configuration config;
		private int myIndex;
		public int Index => myIndex;

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

			public void Execute(Connector owner)
			{
				if (WasExecuted)
					throw new InvalidOperationException("Cannot re-execute local log entry");
				WasExecuted = true;
				entry.Execute(owner);
			}

			public bool WasExecuted { get; private set; } = false;
			public LogEntry Entry => entry;
		}


		private Connection LeaderConnection
		{
			get
			{
				Identity ld = leader;
				if (ld == this)
					throw new InvalidOperationException("Leader is this");
				return (Connection)ld;
			}
		}

		private Identity leader = null;
		private Identity votedFor = null;
		private State state = State.Follower;
		private int currentTerm = 0;
		private int commitIndex = 0;//, lastApplied = 0;
		private long nextActionAt;
		private readonly List<PrivateEntry> log = new List<PrivateEntry>();
		private int numVotes = 0;
		private readonly Random localRandom = new Random();
		private ConcurrentQueue<ICommitable> dispatchQueue = new ConcurrentQueue<ICommitable>();


		private const long HEART_BEAT_TIMEOUT_NS = 50 * 1000000;

		internal static long GetNanoTime()
		{
			return (long)((double)Stopwatch.GetTimestamp() / TimeSpan.TicksPerMillisecond * 1000000);
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
				LogMinorEvent("Committing " + commitIndex + ".." + newCommitIndex + ", history length " + log.Count);
				for (int i = commitIndex; i < newCommitIndex; i++)
				{
					PrivateEntry e = log[i];
					LogEvent("Executing " + e.Entry);
					e.Execute(this);
				}
				commitIndex = newCommitIndex;
				if (IsLeader)
					Broadcast(new AppendEntries(this));
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


		public Connector(Configuration config, int myIndex):base(null,config.Addresses[myIndex])
		{
			IPAddress filter = IPAddress.Any;
			int port = config.Addresses[myIndex]().Port;
			LogMinorEvent("Starting to listen on port "+ port);
			listener = new TcpListener(filter,port);
			listener.Start();
			listenThread = new Thread(new ThreadStart(ThreadListen));
			listenThread.Start();
			consensusThread = new Thread(new ThreadStart(ThreadConsensus));
			consensusThread.Start();
			Join(config,myIndex);
		}

		private TcpClient lastAcceptedClient;
		private void ThreadListen()
		{
			byte[] id = new byte[4];
			while (!disposing)
			{
				try
				{
					var client = lastAcceptedClient = listener.AcceptTcpClient();

					var stream = client.GetStream();
					stream.Read(id, 0, 4);
					int remoteID = BitConverter.ToInt32(id, 0);
					if (remoteID < 0 || remoteID >= myIndex)
						throw new ArgumentOutOfRangeException("Invalid remote member ID " + remoteID);

					var conn = new Connection(this, config.Addresses[remoteID](), client);
					DoSerialized(() =>
					{
						if (remoteMembers[remoteID] != null)
							remoteMembers[remoteID].Dispose();
						remoteMembers[remoteID] = conn;
						LogMinorEvent("Added incoming connection " + conn +" as idx="+remoteID+"/"+config.Addresses[remoteID]());
					});


				}
				catch (Exception ex)
				{
					if (disposing)
						return;
					LogError(ex);
				}
			}
			bool brk = true;
		}




		public int CurrentTerm => currentTerm;

		public int CommitIndex => commitIndex;

		public int LogSize => log.Count;

		public State CurrentState => state;

		private void Broadcast(IDispatchable p)
		{
			ForeachConnection(c => c.Dispatch(p));
		}


		//private SpinLock serialLock = new SpinLock();

		private Mutex serialLock = new Mutex();
		private Thread lastLockedBy;

		internal void DoSerialized(Action action, bool ignoreDisposed = false)
		{
			if (disposing && !ignoreDisposed)
				return;
			if (!serialLock.WaitOne(10000))
				throw new InvalidOperationException("Unable to acquire lock in 1000ms");
			try
			{
				lastLockedBy = Thread.CurrentThread;
				action();
				lastLockedBy = null;
				serialLock.ReleaseMutex();
			}
			catch
			{
				lastLockedBy = null;
				serialLock.ReleaseMutex();
				throw;
			}
			
			//bool taken = false;
			//while (!taken)
			//	serialLock.Enter(ref taken);
			//try
			//{
			//	action();
			//}
			//finally
			//{
			//	serialLock.Exit();
			//}
		}

		internal void ForeachConnection(Action<Connection> action)
		{
			for (int i = 0; i < remoteMembers.Length; i++)
			{
				Connection c = remoteMembers[i];
				if (c == null)
					continue;
				if (IsLeader && !c.IsAlive && !(c is ActiveConnection))
				{
					LogEvent("Disconnecting unresponsive " + c);
					c.Dispose();
					remoteMembers[i] = null;
					continue;
				}
				try
				{
					action(c);
				}
				catch (Exception ex)
				{
					LogError(ex);
					c.Dispose();
					remoteMembers[i] = null;
				}
			}
		}

		private void ThreadConsensus()
		{
			nextActionAt = GetNanoTime() + localRandom.Next(100 * 1000000);//max 100ms
			LogMinorEvent("Started consensus engine: Next action at " + nextActionAt);

			while (!disposing)
			{
				try
				{
					if (state == State.Leader)
						CheckTimeouts();

					if (GetNanoTime() < nextActionAt)
					{
						//LogEvent(GetNanoTime()+"/"+nextActionAt);
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
							DoSerialized(() =>
							{
								//elect new leader
								ClearRemoteInfo();
								numVotes = 1;
								currentTerm++;
								votedFor = this;
								state = State.Candidate;
								leader = null;
								nextActionAt = GetElectionTimeout();

								LogMinorEvent("Timeout reached. Starting new election");

								Broadcast(new RequestVote(this));
							});
							break;
					}
				}
				catch
				{ }
			}

	
		}

		private void CheckTimeouts()
		{
			DoSerialized(() =>
			{
				long now = GetNanoTime();

				ForeachConnection(c =>
				{
					var info = c.ConsensusState;
					if (info.AppendTimeout != -1 && info.AppendTimeout < now)
					{
						LogEvent("Timeout reached. Re-sending " + info);
						c.Dispatch(new AppendEntries(this, info.MatchIndex + 1));
						info.AppendTimeout = GetAppendMessageTimeout();
					}
				});
			});
		}

		private void ClearRemoteInfo()
		{
			ForeachConnection(c => c.ResetConsensusState());
		}


		private ActiveConnection ConnectTo(int idx)
		{
			if (idx <= myIndex)
				return null;	//passive
			ActiveConnection rs = null;
			DoSerialized(() =>
			{
				if (remoteMembers[idx] != null)
					rs = (ActiveConnection)remoteMembers[idx];
				else
					remoteMembers[idx] = rs = new ActiveConnection(this, config.Addresses[idx], idx);
			}
			);
			return rs;
		}

		public bool ConnectedTo(int idx)
		{
			bool rs = false;
			DoSerialized(() => rs = remoteMembers[idx] != null && remoteMembers[idx].IsConnected);
			return rs;
		}

		internal void Join(Configuration cfg, int myIndex)
		{
			DoSerialized(() =>
			{
				if (remoteMembers != null)
					foreach (var m in remoteMembers)
						if (m != null)
							m.Dispose();
				remoteMembers = null;

				if (myIndex < 0 || myIndex >= cfg.Size)
					throw new ArgumentOutOfRangeException("myIndex");

				remoteMembers = new Connection[cfg.Size];
				config = cfg;
				this.myIndex = myIndex;
				for (int i = myIndex + 1; i < cfg.Size; i++)
				{
					remoteMembers[i] = new ActiveConnection(this, cfg.Addresses[i],myIndex);
				}
			});
		}


		public void Dispose()
		{
			if (disposing)
				return;
			disposing = true;
			DoSerialized(() =>
			{
				foreach (var c in remoteMembers)
					if (c != null)
						c.Dispose();
			},true);
			try
			{
				if (lastAcceptedClient != null)
					lastAcceptedClient.Dispose();
			}
			catch { }
			listener.Stop();
			listener.Server.Dispose();
			listenThread.Join();
			consensusThread.Join();
		}

		public bool IsLeader => state == State.Leader;

		public int ActiveConnectionCount
		{
			get
			{
				try
				{
					if (disposing)
						return 0;
					int rs = 0;
					for (int i = 0; i < remoteMembers.Length; i++)
					{
						var c = remoteMembers[i];
						if (c != null && c.IsConnected)
							rs++;
					}
					return rs;
				}
				catch (Exception ex)
				{
					LogError(ex);
					return 0;
				}
			}
		}

		public bool IsFullyConnected => ActiveConnectionCount == config.Addresses.Length - 1;

		public bool IsDisposed => disposing;

		/// <summary>
		/// Registers an object to be committed to the consensus.
		/// If a leader is currently known, the object is sent to the leader for logging.
		/// The leader executes the commit protocol on the new entry.
		/// If no leader is known, the message is queued for later delivery.
		/// </summary>
		/// <param name="entry">Object to commit. Null-objects are ignored. Type must be declared Serializable</param>
		public void Commit(ICommitable entry)
		{
			if (entry == null)
				return;
			if (state == State.Leader)
			{
				LogMinorEvent("Issuing " + entry);
				PrivateEntry p = new PrivateEntry(new LogEntry(currentTerm, entry));
				lock(log)
					log.Add(p);
				Broadcast(new AppendEntries(this, p.Entry));
				ForeachConnection(c => c.ConsensusState.AppendTimeout = GetAppendMessageTimeout());
			}
			else
			{
				if (leader != null)
				{
					LogMinorEvent("Dispatching " + entry + " to " + leader);
					LeaderConnection.Dispatch(new CommitEntry(currentTerm, entry));
				}
				else
				{
					LogEvent("Received message out of consensus. Logging " + entry);
					dispatchQueue.Enqueue(entry);
				}
			}
		}

		internal void SignalAppendEntries(AppendEntries p, Connection sender)
		{
			//LogEvent("Inbound append entries " + p);
			if (leader == null || p.Term > currentTerm)
			{
				LogEvent("Recognized new leader " + sender);
				state = State.Follower;
				leader = sender;
				votedFor = null;

				ICommitable e;
				while (dispatchQueue.TryDequeue(out e))
					sender.Dispatch(new CommitEntry(p.Term, e));
			}

			if (GetLogTerm(p.PrevLogIndex) == -1)    //don't have (yet)
			{
				LogEvent("Rejecting append entries because don't have prev log index " + p.PrevLogIndex + ", term " + p.Term);
				sender.Dispatch(new AppendEntriesConfirmation(this, false));
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
				sender.Dispatch(new AppendEntriesConfirmation(this, true));
			}
			nextActionAt = GetElectionTimeout();
		}


		internal void ReCheckCommitment()
		{
			for (int j = log.Count; j > commitIndex; j--)
			{
				int threshold = j;
				int cnt = 1;    //self
				ForeachConnection(c => {if (c.ConsensusState.MatchIndex >= threshold) cnt++; });
				if (cnt > config.Addresses.Length / 2)
				{
					CommitTo(j);
					return;
				}
			}
		}

		internal void ProcessVoteRequest(Connection sender, int term, bool upToDate)
		{
			if ((votedFor == null || votedFor == sender || term > currentTerm) && upToDate)
			{
				state = State.Follower;
				LogMinorEvent("Recognized vote request for term " + term + " from " + sender);
				nextActionAt = GetElectionTimeout();
				votedFor = sender;
				currentTerm = term;
				try
				{
					sender.Dispatch(new VoteConfirm(currentTerm));
				}
				catch
				{
					bool brk = true;
				}
			}
			else
				LogMinorEvent("Rejected vote request for term " + term + " (at term " + currentTerm + ", upToDate=" + upToDate + ") from " + sender);

		}

		internal void ProcessVoteConfirmation(Connection sender, int term)
		{
			LogMinorEvent("Processing vote confirmation for term " + term);
			if (state == State.Candidate && term == currentTerm)
			{
				nextActionAt = GetElectionTimeout();
				numVotes++;
				LogMinorEvent("Num Votes now " + numVotes);
				if (numVotes >= config.Addresses.Length / 2 + 1)
				{
					LogEvent("Elected leader of term " + currentTerm);
					state = State.Leader;
					leader = this;
					ClearRemoteInfo();
					Broadcast(new AppendEntries(this));
					nextActionAt = GetNanoTime() + HEART_BEAT_TIMEOUT_NS;

					if (commitIndex != LogSize)
					{
						Broadcast(new AppendEntries(this, commitIndex + 1));
						ForeachConnection(c => c.ConsensusState.AppendTimeout = GetAppendMessageTimeout());
					}
				}
			}
			else
				LogMinorEvent("Confirmation rejected: " + state + ", t" + term);

		}
	}
}
