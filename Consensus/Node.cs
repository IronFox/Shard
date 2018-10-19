using Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Consensus
{

	public class Node : Identity, IDisposable
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
		private Thread listenThread,consensusThread;
		private volatile bool disposing = false;


		private Configuration config;
		protected Configuration Configuration => config;
		private int myIndex;
		public int Index => myIndex;

		public enum State
		{
			Follower,
			Leader,
			Candidate
		}


		private readonly ConcurrentQueue<string> eventLog = new ConcurrentQueue<string>();
		internal void LogError(object error, Identity sender=null)
		{
			Console.Error.WriteLine((sender??this) + ": " + error);
			LogMinorEvent("ERROR: " + error,sender);
		}
		internal void LogError(Exception ex, Identity sender=null)
		{
			//LogEvent(ex.Message);
			LogError(ex.Message + " [" + ex.StackTrace[0].ToString() + "]", sender);
		}
		internal void LogMinorEvent(object ev, Identity sender=null)
		{
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
		}
		internal void LogEvent(object ev, Identity sender=null)
		{
			//string str = ev.ToString();
			//if (str.StartsWith("Unable to write data"))
			//{
			//	bool brk = true;
			//}
			LogMinorEvent(ev,sender);
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
		private readonly List<PrivateEntry> log = new List<PrivateEntry>();
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
			LogMinorEvent("Election timeout at "+(DateTime.Now + delta.TimeSpan).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
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
				return log[idx - 1].Entry.Term;
			}
		}

		private void CommitTo(int newCommitCount)
		{
			serialLock.AssertIsLockedByMe();
			if (commitCount < newCommitCount)
			{
				LogMinorEvent("Committing " + commitCount + ".." + newCommitCount + ", history length " + log.Count);
				for (int i = commitCount; i < newCommitCount; i++)
				{
					PrivateEntry e = log[i];
					if (DebugState != null)
						DebugState.SignalExecution(i, e.Entry.Term,this);
					LogEvent("Executing " + e.Entry);
					e.Execute(this);
					committed.TryRemove(e.Entry.CommitID);
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


		public Node(Configuration config, int myIndex):base(null,config.Addresses[myIndex])
		{
			serialLock = new DebugMutex("Consensus.Node[" + myIndex + "]");
			IPAddress filter = IPAddress.Any;
			int port = config.Addresses[myIndex]() != null ? config.Addresses[myIndex]().Port : 0;
			listener = new TcpListener(filter,port);
			listener.Start();
			if (port == 0)
			{
				port = ((IPEndPoint)listener.LocalEndpoint).Port;
				Address = () => new Address(port);
			}
			LogMinorEvent("Started to listening on port " + port);
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

					var conn = new Connection(this, config.Addresses[remoteID], client);
					DoSerialized(() =>
					{
						if (remoteMembers[remoteID] != null)
							Dispose(remoteMembers[remoteID]);
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

		public int CommitIndex => commitCount;

		public int LogSize => log.Count;

		public State CurrentState => state;

		private void Broadcast(IDispatchable p)
		{
			LogMinorEvent("Broadcasting " + p);
			ForeachConnection(c => c.Dispatch(p));
		}


		//private SpinLock serialLock = new SpinLock();

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
				if (IsLeader && !c.IsAlive && !(c is ActiveConnection))
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
							DoSerialized(() =>
							{
								if (PreciseTime.Now < nextActionAt)
									return;
								LogMinorEvent("Timeout " + PreciseTime.Now + "/" + nextActionAt + " reached. Reverting to follower (for now)");
								votedFor = null;
								//votedInTerm = -1;
								state = State.Follower;
								leader = null;
								nextActionAt = GetCandidateFailedElectionTimeout();
							});
							break;
						case State.Follower:
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

								if (PreciseTime.Now < dl)
									throw new IntegrityViolation("Timing issue");
								LogMinorEvent("Timeout "+PreciseTime.Now+"/"+ nextActionAt + " reached. Starting new election at log term "+GetLogTerm(LogSize));

								nextActionAt = GetElectionTimeout();

								Broadcast(new RequestVote(this));
							});
							break;
					}
				}
				catch
				{ }
			}

	
		}

		private void Dispose(Connection c)
		{
			LogMinorEvent("Scheduling unresponsive connection for disposal: " + c);
			garbage.Add(c);
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
						bool found = false;
						foreach (var l in log)
							if (l.Entry.CommitID == c.Key)
							{
								found = true;
								c.Value.lastAttempt = DateTime.Now;
								break; ;
							}
						if (!found)
						{
							LogMinorEvent("Trying to recommit " + c.Value.comm);
							c.Value.lastAttempt = DateTime.Now;
							Commit(c.Key, c.Value.comm);
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
		public bool KnowsRemoteLeader => state == State.Follower && leader != null;

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

		internal static PreciseTime NextHeartbeat => PreciseTime.Now + PreciseTimeSpan.FromMilliseconds(100);


		private int commitSerial = 0;


		private class Committed
		{
			public ICommitable comm;
			public DateTime lastAttempt;
		}
		private ConcurrentDictionary<CommitID, Committed> committed = new ConcurrentDictionary<CommitID, Committed>();

		internal void Commit(CommitID cID, ICommitable entry)
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

				//lock (log)

					log.Add(p);
				Broadcast(new AppendEntries(this, p.Entry));
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
		public void Commit(ICommitable entry)
		{
			if (entry == null)
				return;
			DoSerialized(() =>
			{
				var cID = new CommitID(myIndex, commitSerial++);
				Commit(cID, entry);
				committed.GetOrAdd(cID, new Committed() { comm = entry, lastAttempt = DateTime.Now });
			});
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
		}


		internal void ReCheckCommitment()
		{
			for (int j = log.Count; j > commitCount; j--)
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
				if (numVotes >= config.Addresses.Length / 2 + 1)
				{
					LogEvent("Elected leader of term " + currentTerm);
					ForeachConnection(c => c.SignalIncoming());	//prevent disconnection
					votedFor = null;
					state = State.Leader;
					leader = this;
					ClearRemoteInfo();
					Broadcast(new AppendEntries(this));
					nextActionAt = PreciseTime.Now + HEART_BEAT_TIMEOUT_NS;

					if (DebugState != null)
						DebugState.AssertLeaderMatch(log.Select(p => p.Entry));

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
		}

		internal void SignalVoteRejectedBadTerm(int term, Connection sender)
		{
			LogMinorEvent("Vote rejected by " + sender + ". Remote term reported as " + term+", local is "+currentTerm);
			currentTerm = Math.Max(currentTerm,term);	//try better next time
		}
	}
}
