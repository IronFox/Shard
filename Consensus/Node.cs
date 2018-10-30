using Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Consensus
{

	public abstract class Node : Identity, IDisposable
	{
		//private readonly ConcurrentDictionary<Address, Connection> connections = new ConcurrentDictionary<Address, Connection>();
		private Connection[] remoteMembers;

		private SharedDebugState debugState;
		public SharedDebugState DebugState
		{
			get { return debugState; }
			set
			{
				if (debugState != null)
					debugState.Unregister(this);
				debugState = value;
				debugState.Register(this);
			}
		}

		/// <summary>
		/// Custom attachment
		/// </summary>
		public object Attachment { get; set; }

		internal PreciseTime GetAppendMessageTimeout()
		{
			return PreciseTime.Now + PreciseTimeSpan.FromMilliseconds(300);
		}

		private TcpListener listener;
		private Thread listenThread, consensusThread;
		private volatile bool disposing = false;


		private Configuration config;
		protected Configuration Configuration => config;

		public Configuration.Member MemberID { get; private set; }
		public int MyLinearIndex { get; private set; }

		
		public enum State
		{
			Follower,
			Leader,
			Candidate
		}


		private readonly ConcurrentQueue<string> eventLog = new ConcurrentQueue<string>();
		internal void LogError(object error, Identity sender = null)
		{
			Console.Error.WriteLine((sender ?? this) + ": " + error);
			LogMinorEvent("ERROR: " + error, sender);
		}
		internal void LogError(Exception ex, Identity sender = null)
		{
			//LogEvent(ex.Message);
			LogError(ex.Message + " [" + ex.StackTrace[0].ToString() + "]", sender);
		}
		internal void LogMinorEvent(object ev, Identity sender = null)
		{
		//	return;
			string id;
			if (sender == null)
			{
				sender = this;
				id = state + " " + ToString();
			}
			else
				id = sender.ToString();
			eventLog.Enqueue("[" + Clock.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + id + ": " + ev);
			string dummy;
			while (eventLog.Count > 500)
				eventLog.TryDequeue(out dummy);

			//Console.WriteLine(id + ": " + ev);
		}
		internal void LogEvent(object ev, Identity sender = null)
		{
			//string str = ev.ToString();
			//if (str.StartsWith("Unable to write data"))
			//{
			//	bool brk = true;
			//}
			LogMinorEvent(ev, sender);
			Console.WriteLine((sender ?? this) + ": " + ev);
		}


		private class PrivateEntry
		{
			private readonly LogEntry entry;

			public PrivateEntry(LogEntry source)
			{
				entry = source;
			}

			public void Execute(Node owner)
			{
				if (WasExecuted)
					throw new InvalidOperationException("Cannot re-execute local log entry");
				WasExecuted = true;
				entry.Execute(owner);
			}

			public override string ToString()
			{
				return entry.ToString();
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
		//private int votedInTerm = -1;
		private State state = State.Follower;
		private int currentTerm = 0;
		private int commitCount = 0;//, lastApplied = 0;
		private PreciseTime nextActionAt;
		private readonly OffsetList<PrivateEntry> log = new OffsetList<PrivateEntry>();
		private int numVotes = 0;
		private readonly Random localRandom = new Random((int)PreciseTime.Now.Ticks);
		//private ConcurrentQueue<Tuple<CommitID, ICommitable>> dispatchQueue = new ConcurrentQueue<Tuple<CommitID, ICommitable>>();


		private static readonly PreciseTimeSpan HEART_BEAT_TIMEOUT_NS = PreciseTimeSpan.FromMilliseconds(50);


		private PreciseTimeSpan GetElectionTimeoutNS()
		{
			lock (localRandom)
				return PreciseTimeSpan.FromMilliseconds(localRandom.Next(350) + 150);
		}

		private PreciseTime GetElectionTimeout()
		{
			var delta = GetElectionTimeoutNS();
			LogMinorEvent("Election timeout at " + (DateTime.Now + delta.TimeSpan).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
			return PreciseTime.Now + delta;
		}
		private PreciseTime GetCandidateFailedElectionTimeout()
		{
			PreciseTimeSpan delta;
			lock (localRandom)
				delta = PreciseTimeSpan.FromMilliseconds(localRandom.Next(350) + 150);
			//			LogMinorEvent("Election timeout at " + (DateTime.Now + delta.TimeSpan).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
			return PreciseTime.Now + delta;
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
				if (idx <= log.Offset)
					return 0;   //skipped
				return log[idx - 1].Entry.Term;
			}
		}

		private DateTime lastCommit = DateTime.Now;
		private void CommitTo(int newCommitCount)
		{
			serialLock.AssertIsLockedByMe();
			if (commitCount < newCommitCount)
			{
				lastCommit = DateTime.Now;
				LogMinorEvent("Committing " + commitCount + ".." + newCommitCount + ", history length " + log.Count);
				for (int i = commitCount; i < newCommitCount; i++)
				{
					PrivateEntry e = log[i];
					if (e == null)
						LogMinorEvent("Skipping removed entry at #" + i);
					else
					{
						if (DebugState != null)
							DebugState.SignalExecution(i, e.Entry.Term, this);
						LogMinorEvent("Executing " + e.Entry);
						e.Execute(this);
						committed.TryRemove(e.Entry.CommitID);
					}
				}
				commitCount = newCommitCount;
				if (IsLeader)
				{
					Broadcast(new AppendEntries(this));
					nextActionAt = NextHeartbeat;
				}
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
					if (DebugState != null)
						DebugState.SignalLogRemoval(log.Count - 1, log[log.Count - 1].Entry.Term);
					log.RemoveAt(log.Count - 1);
				}
			}
		}

		private Address myAddress = new Address(0);
		public override Address PublicAddress => myAddress;

		public abstract Address GetAddress(int memberID);
		/// <summary>
		/// Invoked prior to disposing the local node as a result of mismatch between bound and public address
		/// </summary>
		public abstract void OnAddressMismatchDispose();
		/// <summary>
		/// Triggered prior to disposing the local node as a result of joining a new configuration that the local node is not a part of
		/// </summary>
		public abstract void OnOutOfConfig(Configuration newConfig);
		public virtual void OnDispose() { }
		public Address BoundAddress { get; private set; }


		public Node(Configuration.Member self) : base(null)
		{
			MemberID = self;
			serialLock = new DebugMutex("Consensus.Node[" + self + "]");
		}

		/// <summary>
		/// </summary>
		/// <param name="config">Consensus configuration to join</param>
		/// <param name="getAddress">Address resolution function: Fetches the IP address for a given member identifier</param>
		public int Start(Configuration config, Address bindingAddress, Action<Address> onAddressBound)
		{
			if (!config.ContainsIdentifier(MemberID))
				throw new ArgumentOutOfRangeException("Given consensus configuration " + config + " does not contain local node identifier " + MemberID);
			IPAddress filter = IPAddress.Any;
			listener = new TcpListener(filter, bindingAddress.Port);
			listener.Start();
			int port = bindingAddress.Port;
			if (port == 0)
				port = ((IPEndPoint)listener.LocalEndpoint).Port;
			BoundAddress = new Address(bindingAddress.Host, port);
			onAddressBound?.Invoke(BoundAddress);
			Join(config);	//join first, so members are initialized
			LogMinorEvent("Started to listening on port " + port);
			listenThread = new Thread(new ThreadStart(ThreadListen));
			listenThread.Start();
			consensusThread = new Thread(new ThreadStart(ThreadConsensus));
			consensusThread.Start();
			return port;
		}


		private ConfigurationChange cfgChange;
		private DateTime cfgChangeAt;
		/// <summary>
		/// Attempts to commit changing the configuration if a consensus exists at this point.
		/// Otherwise a join is performed.
		/// If a consensus is lost before the desired configuration was reached, it is instantly applied.
		/// 
		/// </summary>
		/// <param name="cfg"></param>
		public void ChangeConfiguration(Configuration cfg)
		{
			DoSerialized(() =>
			{
				if (IsInConsensus)
				{
					cfgChange = new ConfigurationChange(cfg);
					cfgChangeAt = DateTime.Now;
					Schedule(cfgChange);
				}
				else
				{
					cfgChange = null;
					Join(cfg);
				}
			});
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
					int linear;
					if (!config.ToIndex(remoteID, out linear))
						throw new ArgumentOutOfRangeException("Invalid remote member ID " + remoteID);
					if (linear >= MyLinearIndex)
						throw new ArgumentOutOfRangeException("Remote ID should be passive");

					var conn = new Connection(this, config.Members[linear], client);
					DoSerialized(() =>
					{
						if (remoteMembers[linear] != null)
							Dispose(remoteMembers[linear]);
						remoteMembers[linear] = conn;
						LogMinorEvent("Added incoming connection #"+linear+" " + conn +" as idx="+remoteID+"/"+GetAddress(remoteID));
					});


				}
				catch (Exception ex)
				{
					if (disposing)
						return;
					LogError(ex);
				}
			}
		}




		public int CurrentTerm => currentTerm;

		public int CommitIndex => commitCount;

		public int LogSize => log.Count;

		public State CurrentState => state;

		private void Broadcast(IDispatchable p)
		{
			LogMinorEvent("Broadcasting " + p);
			ForeachConnection(c => c.Dispatch(p));
		}



		private readonly DebugMutex serialLock;
		private readonly ConcurrentBag<Connection> garbage = new ConcurrentBag<Connection>(), old = new ConcurrentBag<Connection>();

		internal void DoSerialized(Action action, bool ignoreDisposed = false)
		{
			if (disposing && !ignoreDisposed)
				return;
			serialLock.DoLocked(action);

			if (!serialLock.IsLockedByMe)
			{
				if (!garbage.IsEmpty)
				{
					LogMinorEvent("Disposing garbage");
					Connection c;
					while (garbage.TryTake(out c))
					{
						c.Dispose();
						old.Add(c);
					}
				}
			}

		}

		internal void ForeachConnection(Action<Connection> action)
		{
			for (int i = 0; i < remoteMembers.Length; i++)
			{
				Connection c = remoteMembers[i];
				if (c == null)
					continue;
				if (IsLeader && !c.IsAlive && !c.IsActive)
				{
					LogEvent("Disconnecting unresponsive " + c);
					Dispose(c);
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
					Dispose(c);
					remoteMembers[i] = null;
				}
			}
		}

		private void ThreadConsensus()
		{
			lock (localRandom)
				nextActionAt = PreciseTime.Now + PreciseTimeSpan.FromMilliseconds(localRandom.NextDouble()*100);
			LogMinorEvent("Started consensus engine: Next action in " + (PreciseTime.Now - nextActionAt));

			while (!disposing)
			{
				myAddress = GetAddress(MemberID.Identifier);
				if (myAddress != Address.None && myAddress != BoundAddress)
				{
					//GetAddress(MemberID.Identifier);
					OnAddressMismatchDispose();
					Dispose();
					return;
				}

				try
				{
					if (state == State.Leader)
						CheckTimeouts(false);
					CheckCommittedTimeouts();

					var dl = nextActionAt;
					if (PreciseTime.Now < dl)
					{
						//LogEvent(GetNanoTime()+"/"+nextActionAt);
						lock (localRandom)
							Thread.Sleep(TimeSpan.FromMilliseconds(localRandom.Next(10)));
						continue;
					}
					dl = nextActionAt;
					if (PreciseTime.Now < dl)
						continue;
					switch (state)
					{
						case State.Leader:
							DoSerialized(() =>
							{
								//CheckTimeouts(true);
								Broadcast(new AppendEntries(this));
								if (IsLeader)
									nextActionAt = NextHeartbeat;
							});
							break;
						case State.Candidate:
						case State.Follower:
							if (MemberID.CanBeLeader)
								InitElection();
							else
								state = State.Follower;
							break;
					}
				}
				catch
				{ }
			}

	
		}

		private void InitElection()
		{
			DoSerialized(() =>
			{
				if (PreciseTime.Now < nextActionAt)
					return;
				//elect new leader
				ClearRemoteInfo();
				numVotes = 1;
				currentTerm++;
				votedFor = this;
				//votedInTerm = currentTerm;
				state = State.Candidate;
				leader = null;

				LogMinorEvent("Timeout " + PreciseTime.Now + "/" + nextActionAt + " reached. Starting new election at log term " + GetLogTerm(LogSize));

				nextActionAt = GetElectionTimeout();

				Broadcast(new RequestVote(this));
			});

		}

		private void Dispose(Connection c)
		{
			LogMinorEvent("Scheduling unresponsive connection for disposal: " + c);
			garbage.Add(c);
		}

		private int FindLogEntry(CommitID id)
		{
			//for (int i = log.Offset; i < log.Count; i++)
			for (int i = log.Count-1; i >= log.Offset; i--)	//better run backwards in case member has been rebooted and started with fresh commit ids
				if (log[i].Entry.CommitID == id)
					return i;
			return -1;
		}

		private void CheckCommittedTimeouts()
		{
			if (leader == null)
				return;
			DoSerialized(() =>
			{
				foreach (var c in committed)
				{
					if (DateTime.Now - c.Value.lastAttempt > TimeSpan.FromSeconds(1))
					{
						int at = FindLogEntry(c.Key);
						if (at != -1)
							c.Value.lastAttempt = DateTime.Now;
						else
						{
							LogMinorEvent("Trying to recommit " + c.Value.comm);
							c.Value.lastAttempt = DateTime.Now;
							Schedule(c.Key, c.Value.comm);
						}
					}
				}
			});
		}

		private void CheckTimeouts(bool forceSend)
		{
			DoSerialized(() =>
			{
				var now = PreciseTime.Now;

				ForeachConnection(c =>
				{
					var info = c.ConsensusState;
					if (forceSend || (info.AppendTimeout != PreciseTime.None && info.AppendTimeout < now))
					{
						LogEvent("Timeout reached. Re-sending " + info);
						c.Dispatch(new AppendEntries(this, info.MatchIndex + 1));
						info.AppendTimeout = GetAppendMessageTimeout();
					}
				});


				var age = DateTime.Now - lastCommit;
				if (CommitIndex < log.Count && age > TimeSpan.FromSeconds(2))
					Yield();


			});
		}

		private void ClearRemoteInfo()
		{
			ForeachConnection(c => c.ResetConsensusState());
		}




		internal void Join(Configuration cfg)
		{
			List<Connection> doDispose = new List<Connection>();

			DoSerialized(() =>
			{
				if (cfgChange != null && cfgChange.NewCFG == cfg)
				{
					LogMinorEvent("Recognized requested cfg");
					cfgChange = null;
				}
				if (cfg == config)
					return;
 
				LogEvent("Implementing consensus configuration " + cfg);
				int at;
				if (!cfg.ToIndex(MemberID, out at))
				{
					LogError("Local member ID "+MemberID+" is not part of new consensus configuration "+cfg+". Closing down node");
					OnOutOfConfig(cfg);
					Dispose();
					throw new ArgumentException("Local member ID "+MemberID+" is not part of new consensus configuration "+cfg);
				}
				MemberID = cfg.Members[at];
				MyLinearIndex = at;

				var newMembers = new Connection[cfg.Size];
				if (remoteMembers != null)
					foreach (var m in remoteMembers)
					{
						if (m == null)
							continue;
						int linear;
						if (cfg.ToIndex(m.RemoteIdentifier, out linear) && m.IsActive == linear > MyLinearIndex && m.RemoteIdentifier == cfg.Members[linear])
						{
							if (at == linear)
								throw new InvalidOperationException("Found self among remotes");
							newMembers[linear] = m;
						}
						else
							doDispose.Add(m);
					}
				config = cfg;
				for (int i = MyLinearIndex + 1; i < cfg.Size; i++)
				{
					if (newMembers[i] == null)
						newMembers[i] = new ActiveConnection(this, cfg.Members[i]);
				}
				remoteMembers = newMembers;

				if (IsLeader)
				{
					if (!MemberID.CanBeLeader)
						Yield();
				}
				else
				{
					var l = leader as Connection;
					int linear;
					if (l != null && (!cfg.ToIndex(l.RemoteIdentifier, out linear) || !cfg.Members[linear].CanBeLeader))
						Yield();
				}
			});

			foreach (var d in doDispose)
				d.Dispose();
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
			if (listenThread != Thread.CurrentThread)
				listenThread.Join();
			if (consensusThread != Thread.CurrentThread)
				consensusThread.Join();
			OnDispose();
		}

		public bool IsLeader => state == State.Leader;
		public bool KnowsRemoteLeader => state == State.Follower && leader != null;
		public bool IsInConsensus => leader != null;

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

		public bool IsFullyConnected => ActiveConnectionCount == config.Size - 1;

		public bool IsDisposed => disposing;

		internal static PreciseTime NextHeartbeat => PreciseTime.Now + PreciseTimeSpan.FromMilliseconds(100);

		public int CountStoredLogEntries => log.ActualEntryCount;

		public int LogOffset => log.Offset;

		public Configuration Config => config;

		private int commitSerial = 0;


		private class Committed
		{
			public ICommitable comm;
			public DateTime lastAttempt;
		}
		private ConcurrentDictionary<CommitID, Committed> committed = new ConcurrentDictionary<CommitID, Committed>();

		internal void Schedule(CommitID cID, ICommitable entry)
		{
			serialLock.AssertIsLockedByMe();
			if (state == State.Leader)
			{
				foreach (var l in log)
					if (l.Entry.CommitID == cID)
					{
						LogMinorEvent("Already in log: " + entry + ". Ignoring");
						return;
					}

				LogMinorEvent("Issuing " + entry);
				PrivateEntry p = new PrivateEntry(new LogEntry(cID, currentTerm, entry));
				if (DebugState != null)
					DebugState.SignalSignalAppendAttempt(log.Count, p.Entry.Term);

				if (CommitIndex == log.Count)
					lastCommit = DateTime.Now;
				log.Add(p);	//add local
				Broadcast(new AppendEntries(this, p.Entry));//transport remote

				ForeachConnection(c => c.ConsensusState.AppendTimeout = GetAppendMessageTimeout());
			}
			else
			{
				var cp = leader as Connection;
				if (cp != null)
				{
					LogMinorEvent("Dispatching " + entry + " to " + leader);
					cp.Dispatch(new CommitEntry(cID,currentTerm, entry));
				}
				else
				{
					LogEvent("Received message out of consensus. Logging " + entry);
					//dispatchQueue.Enqueue(Helper.Tuple(cID, entry));
				}
			}
		}

		/// <summary>
		/// Registers an object to be committed to the consensus.
		/// If a leader is currently known, the object is sent to the leader for logging.
		/// The leader executes the commit protocol on the new entry.
		/// If no leader is known, the message is queued for later delivery.
		/// </summary>
		/// <param name="entry">Object to commit. Null-objects are ignored. Type must be declared Serializable</param>
		public CommitID Schedule(ICommitable entry)
		{
			if (!Attribute.IsDefined(entry.GetType(), typeof(SerializableAttribute)))
				throw new SerializationException("Entry " + entry + " is not marked serializable");
			CommitID rs = CommitID.None;
			if (entry == null)
				return rs;
			DoSerialized(() =>
			{
				var cID = rs = new CommitID(MemberID.Identifier, commitSerial++);
				Schedule(cID, entry);
				committed.GetOrAdd(cID, new Committed() { comm = entry, lastAttempt = DateTime.Now });
			});
			return rs;
		}

		/// <summary>
		/// Schedules removal of all committed log entries older than the specified commit.
		/// Nothing happens if the specified commit is not found at the time of execution.
		/// </summary>
		/// <param name="threshold">Commit to compare with</param>
		/// <param name="includeThreshold"></param>
		public CommitID RemoveFossils(CommitID threshold, bool includeThreshold)
		{
			if (threshold == CommitID.None)
				return CommitID.None;
			return Schedule(new FossilShredder(threshold, includeThreshold));
		}
		
		/// <summary>
		/// Completely removes all currently logged/scheduled entries.
		/// The fossil remover itself cannot be removed
		/// </summary>
		/// <returns></returns>
		public CommitID RemoveFossils()
		{
			CommitID rs = CommitID.None;
			DoSerialized(() =>
			{
				var cID = rs = new CommitID(MemberID.Identifier, commitSerial++);
				var e = new FossilShredder(cID, false);
				Schedule(cID, e);
				committed.GetOrAdd(cID, new Committed() { comm = e, lastAttempt = DateTime.Now });
			});
			return rs;
		}

		[Serializable]
		private class FossilShredder : ICommitable
		{
			private CommitID threshold;
			private bool includeThreshold;

			public FossilShredder(CommitID threshold, bool includeThreshold)
			{
				this.threshold = threshold;
				this.includeThreshold = includeThreshold;
			}

			public void Commit(Node node, CommitID myID)
			{
				node.DoSerialized(() =>
				{
					int cnt = node.FindLogEntry(threshold);
					node.LogEvent("Attempting to clean log of length "+node.log.Count+". Threshold found at "+cnt);
					if (includeThreshold)
						cnt++;
					if (cnt > 0)
					{
						node.log.SetOffset(cnt);
					}
				});
			}
		}


		internal void SignalAppendEntries(AppendEntries p, Connection sender)
		{
			serialLock.AssertIsLockedByMe();
			LogEvent("Inbound append entries " + p);
			if ((leader == null && p.Term==currentTerm) || p.Term > currentTerm)
			{
				LogEvent("Recognized new leader " + sender+"@t"+p.Term+"/"+currentTerm);
				state = State.Follower;
				leader = sender;
				cfgChangeAt = DateTime.Now;
				votedFor = null;
				TruncateTo(Math.Max(p.LeaderCommit,commitCount));
				//votedInTerm = -1;

				//Tuple<CommitID, ICommitable> e;
				//while (dispatchQueue.TryDequeue(out e))
				//	sender.Dispatch(new CommitEntry(e.Item1, p.Term, e.Item2));
			}
			else
			{
				var ld = leader;
				if (ld != sender)
				{
					LogEvent("Rejecting append entries because of leader mismatch of same term. Known=" + (ld?.ToString() ??"null"));
					sender.Dispatch(new AppendEntriesConfirmation(this, false,true));
					return;
				}
			}

			if (p.SkipTo >= 0)
				log.SetOffset(p.SkipTo,true);
			var t = GetLogTerm(p.PrevLogLength);
			if (t == -1 || t != p.PrevLogTerm)    //don't have (yet)
			{
				LogEvent("Rejecting append entries because don't have prev log index " + p.PrevLogLength + ", term " + p.Term+", lcom "+CommitIndex);
				sender.Dispatch(new AppendEntriesConfirmation(this, false,false));
			}
			else
			{
				if (p.Entries != null)
					for (int i = 0; i < p.Entries.Length; i++)
					{
						int at = i + 1 + p.PrevLogLength;
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
							if (at <= commitCount)
								throw new InvalidOperationException(state + ": overwrite consistency failure. my term " + myTerm + " != " + e.Term
										+ ". Replacing " + log[at - 1] + " with " + e);
							TruncateTo(at - 1);
						}
						if (log.Count != at - 1)
							throw new InvalidOperationException(log.Count + " != " + (at - 1));
						log.Add(new PrivateEntry(e));
					}
				CommitTo(Math.Min(p.LeaderCommit, log.Count));
				sender.Dispatch(new AppendEntriesConfirmation(this, true,false));
			}
			nextActionAt = GetElectionTimeout();
			if (p.SkipTo > 0 && !log.IsWellFormed)
				throw new InvalidOperationException("AppendEntries() left internal log in an invalid state");
		}


		internal void ReCheckCommitment()
		{
			for (int j = log.Count; j > commitCount; j--)
			{
				int threshold = j;
				int cnt = 1;    //self
				ForeachConnection(c => {if (c.ConsensusState.MatchIndex >= threshold) cnt++; });
				if (cnt >= config.Majority)
				{
					CommitTo(j);
					return;
				}
			}
		}

		internal void ProcessVoteRequest(Connection sender, int term, bool upToDate)
		{
			serialLock.AssertIsLockedByMe();

			if ((/*votedFor == null ||*/ votedFor == sender || term > currentTerm) && upToDate)	//already voted: all good. 
			{
				Yield();
				//state = State.Follower;
				//leader = null;	leave old leader in place
				LogMinorEvent("Recognized vote request for term " + term + " from " + sender);
				//nextActionAt = GetElectionTimeout();
				votedFor = sender;
				currentTerm = term;
				//currentTerm = term;


				//!!!!we voted: we cannot accept any other appendentries from our last leader!!!!



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
			{
				LogMinorEvent("Rejected vote request for term " + term + " (at term " + currentTerm + ", upToDate=" + upToDate + ") from " + sender);
				if (term > currentTerm)
				{
					bool now = state == State.Candidate && !upToDate;	//preemt
					currentTerm = term;
					Yield();
					nextActionAt = now ? PreciseTime.Now : GetElectionTimeout();
				}
			}

		}

		internal void ProcessVoteConfirmation(Connection sender, int term)
		{
			serialLock.AssertIsLockedByMe();
			LogMinorEvent("Processing vote confirmation for term " + term+" from "+sender);
			if (state == State.Candidate && term == currentTerm)
			{
				nextActionAt = GetElectionTimeout();
				numVotes++;
				LogMinorEvent("Num Votes now " + numVotes);
				if (numVotes >= config.Majority)
				{
					LogEvent("Elected leader of term " + currentTerm);
					ForeachConnection(c => c.SignalIncoming());	//prevent disconnection
					votedFor = null;
					state = State.Leader;
					cfgChangeAt = DateTime.Now;
					lastCommit = DateTime.Now;
					leader = this;
					ClearRemoteInfo();
					Broadcast(new AppendEntries(this));
					nextActionAt = PreciseTime.Now + HEART_BEAT_TIMEOUT_NS;

					if (DebugState != null)
						DebugState.AssertLeaderMatch(log.Select(p => p.Entry), log.Offset);

					if (commitCount != LogSize)
					{
						Broadcast(new AppendEntries(this, commitCount + 1));
						ForeachConnection(c => c.ConsensusState.AppendTimeout = GetAppendMessageTimeout());
					}
				}
			}
			else
				LogMinorEvent("Confirmation rejected: " + state + ", t" + term);

		}

		internal void Yield()
		{
			serialLock.AssertIsLockedByMe();
			LogEvent("Yielding...");
			state = State.Follower;
			leader = null;
			votedFor = null;
			//votedInTerm = -1;
			nextActionAt = GetElectionTimeout();
			if (cfgChange != null)
			{
				Join(cfgChange.NewCFG);
				if (cfgChange != null)
					throw new IntegrityViolation("Consensus: New CFG should have been unset by Join()");
			}
		}

		internal void SignalVoteRejectedBadTerm(int term, Connection sender)
		{
			LogMinorEvent("Vote rejected by " + sender + ". Remote term reported as " + term+", local is "+currentTerm);
			currentTerm = Math.Max(currentTerm,term);   //try better next time
			if (state == State.Candidate)
			{
				nextActionAt = PreciseTime.Now;
				votedFor = null;
			}
		}

		internal LogEntry[] LogSubSet(object p)
		{
			throw new NotImplementedException();
		}
	}
}
