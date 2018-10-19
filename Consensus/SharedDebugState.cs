﻿using Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Consensus
{
	public class SharedDebugState
	{
		private struct Execution
		{
			public Node first;
			public Node.State firstState;
			public DateTime stamp;
			public int firstTerm;
			public int term;

			public Execution(Node actor, int term) : this()
			{
				this.first = actor;
				firstState = actor.CurrentState;
				firstTerm = actor.CurrentTerm;
				stamp = DateTime.Now;
				this.term = term;
			}
		}

		private readonly ConcurrentDictionary<int, Execution> history = new ConcurrentDictionary<int, Execution>();		//base0Index -> term


		internal void SignalExecution(int base0LogIndex, int term, Node actor)
		{
			var old = history.GetOrAdd(base0LogIndex, new Execution(actor,term));
			if (old.term != term)
				throw new IntegrityViolation("Attempting to execute different term at #"+base0LogIndex+" of shared history (length "+history.Count+")");
		}

		internal void SignalSignalAppendAttempt(int base0LogIndex, int term)
		{
			Execution old;
			if (history.TryGetValue(base0LogIndex, out old))
			{
				if (term > old.term)
					throw new IntegrityViolation("Attempting to append entry of newer term "+term+" in existing location #"+base0LogIndex+"/"+history.Count+". Previously committed term is "+old);
			}
		}

		internal void SignalLogRemoval(int base0LogIndex, int term)
		{
			Execution old;
			if (history.TryGetValue(base0LogIndex, out old) && old.term == term)
				throw new IntegrityViolation("Trying to remove entry #"+base0LogIndex+"/"+history.Count+", which has been committed in term "+term);
		}

	
		internal void AssertLeaderMatch(IEnumerable<LogEntry> entries)
		{
			int g = 0;
			foreach (var e in entries)
			{
				Execution exec;
				if (!history.TryGetValue(g, out exec))
					return; //all done
				if (e.Term != exec.term)
					throw new IntegrityViolation("Log mismatch at #"+g+"/"+history.Count+". Expected term "+exec.term+", got "+e.Term);
				g++;
			}
			if (g < history.Count)
				throw new IntegrityViolation("Log mismatch: Expected at least "+history.Count+" entries, got "+g);
		}
	}
}